using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Helper.Debug;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.BombPathSimulator;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.FindAngleViaPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.IsSameDirection;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.HandleMultiOrdering;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNoteHitDetector;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNotePatternDetector;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Detects flow and patterns in beatmap sequences.
    /// Determines swing directions for dot notes and identifies multi-note hits.
    /// </summary>
    internal class FlowDetector
    {
        /// <summary>
        /// Marks groups of 3+ simultaneous notes as patterns (towers).
        /// This ensures towers are properly detected before pairwise processing.
        /// Validates that notes can be swung through in a single motion by checking:
        /// 1. Notes are roughly collinear in the swing direction
        /// 2. Each consecutive pair (when ordered along swing path) forms a valid multi-note hit
        /// </summary>
        private static void MarkSimultaneousGroupsAsPatterns(List<Cube> cubes, float bpm)
        {
            int i = 0;
            while (i < cubes.Count)
            {
                float currentTime = cubes[i].Time;
                var simultaneousGroup = new List<int> { i };
                
                // Collect all notes at this time
                for (int j = i + 1; j < cubes.Count; j++)
                {
                    if (Math.Abs(cubes[j].Time - currentTime) < 0.001f)
                    {
                        simultaneousGroup.Add(j);
                    }
                    else
                    {
                        break;
                    }
                }
                
                // If we have 3+ simultaneous notes, check if they form a swingable pattern
                if (simultaneousGroup.Count >= 3)
                {
                    // Infer swing direction from the group
                    // Priority: arrow note direction > geometric direction from positions
                    double swingDirection = 270.0; // Default down
                    
                    // Check if any note has an arrow
                    int? arrowIdx = null;
                    foreach (int idx in simultaneousGroup)
                    {
                        if (cubes[idx].CutDirection != 8)
                        {
                            arrowIdx = idx;
                            break;
                        }
                    }
                    
                    if (arrowIdx.HasValue)
                    {
                        // Use arrow direction
                        swingDirection = Mod(DirectionToDegree[cubes[arrowIdx.Value].CutDirection] + 
                                            cubes[arrowIdx.Value].AngleOffset, 360);
                    }
                    else
                    {
                        // All dots - calculate geometric direction from first to last note
                        Cube first = cubes[simultaneousGroup[0]];
                        Cube last = cubes[simultaneousGroup[simultaneousGroup.Count - 1]];
                        
                        int lineDiff = last.Line - first.Line;
                        int layerDiff = last.Layer - first.Layer;
                        
                        if (lineDiff != 0 || layerDiff != 0)
                        {
                            double angleRad = Math.Atan2(layerDiff, lineDiff);
                            double angleDeg = angleRad * 180.0 / Math.PI;
                            swingDirection = (angleDeg + 360.0) % 360.0;
                        }
                    }
                    
                    // Order notes by their position along the swing direction
                    var orderedIndices = simultaneousGroup
                        .Select(idx => new { 
                            Index = idx, 
                            Cube = cubes[idx],
                            Order = GetSwingOrderValue(cubes[idx], swingDirection)
                        })
                        .OrderBy(x => x.Order)
                        .Select(x => x.Index)
                        .ToList();
                    
                    // Check if all consecutive pairs form valid multi-note hits
                    bool allPairsValid = true;
                    for (int p = 0; p < orderedIndices.Count - 1; p++)
                    {
                        if (!IsMultiNoteHit(cubes[orderedIndices[p]], cubes[orderedIndices[p + 1]], bpm))
                        {
                            allPairsValid = false;
                            break;
                        }
                    }
                    
                    // Additionally check collinearity: all notes should be roughly aligned in swing direction
                    // This prevents patterns like squares or scattered notes from being marked as towers
                    bool isCollinear = true;
                    if (allPairsValid && orderedIndices.Count >= 3)
                    {
                        // Calculate deviation of each note from the straight line between first and last
                        Cube first = cubes[orderedIndices[0]];
                        Cube last = cubes[orderedIndices[orderedIndices.Count - 1]];
                        
                        double lineVecX = last.Line - first.Line;
                        double lineVecY = last.Layer - first.Layer;
                        double lineLength = Math.Sqrt(lineVecX * lineVecX + lineVecY * lineVecY);
                        
                        if (lineLength > 0.01) // Avoid division by zero
                        {
                            // Normalize line vector
                            lineVecX /= lineLength;
                            lineVecY /= lineLength;
                            
                            // Check each middle note's perpendicular distance from the line
                            for (int p = 1; p < orderedIndices.Count - 1; p++)
                            {
                                Cube middle = cubes[orderedIndices[p]];
                                
                                // Vector from first to middle
                                double toMiddleX = middle.Line - first.Line;
                                double toMiddleY = middle.Layer - first.Layer;
                                
                                // Perpendicular distance = |cross product| / line length (already normalized)
                                double perpDistance = Math.Abs(toMiddleX * lineVecY - toMiddleY * lineVecX);
                                
                                // Allow small deviation (0.5 grid units) for near-collinear patterns
                                if (perpDistance > 0.5)
                                {
                                    isCollinear = false;
                                    break;
                                }
                            }
                        }
                    }
                    
                    // Mark as pattern if all consecutive pairs are valid and notes are collinear
                    if (allPairsValid && isCollinear)
                    {
                        // Mark all notes in the ordered group as pattern AND set their direction
                        for (int idx = 0; idx < orderedIndices.Count; idx++)
                        {
                            int cubeIdx = orderedIndices[idx];
                            cubes[cubeIdx].Pattern = true;
                            cubes[cubeIdx].Direction = swingDirection; // Set direction for all notes in the pattern
                            
                            if (idx == 0)
                            {
                                cubes[cubeIdx].Head = true;
                            }
                            else if (idx == orderedIndices.Count - 1)
                            {
                                cubes[cubeIdx].Tail = true;
                            }
                        }
                    }
                }
                
                // Move to next time group
                i += simultaneousGroup.Count;
            }
        }
        
        /// <summary>
        /// Helper method to calculate ordering value along swing direction.
        /// </summary>
        private static double GetSwingOrderValue(Cube cube, double swingDirection)
        {
            double radians = swingDirection * Math.PI / 180.0;
            double swingX = Math.Cos(radians);
            double swingY = Math.Sin(radians);
            
            // Project the note position onto the swing direction vector
            double projection = cube.Line * swingX + cube.Layer * swingY;
            
            return projection;
        }

        /// <summary>
        /// Adjusts dot note direction based on bomb-forced relocations.
        /// Bombs at player's standing position force them to relocate, changing where they end up.
        /// The final swing direction is calculated from the final position to the target note.
        /// </summary>
        private static (double angle, bool bombAffected) AdjustDirectionForBombs(Cube prevNote, Cube currentNote, double expectedDirection, List<Bomb> bombs)
        {
            // Calculate player position after previous swing
            var (startX, startY) = CalculatePlayerPositionAfterSwing(
                prevNote.Line, prevNote.Layer, prevNote.Direction);

            // Check if bombs force player to relocate (also returns relocation count)
            var (finalX, finalY, lastDirection, encounteredBombs, relocationCount) = SimulateBombForcedRelocations(
                startX, startY,
                prevNote.Direction,
                bombs,
                prevNote.Time, currentNote.Time);

            // Calculate angle from player's final position to the note
            double angle;

            // Special case: If player ends up at the same position as the note
            if (finalX == currentNote.Line && finalY == currentNote.Layer)
            {
                // Each bomb relocation reverses direction by 180°
                // Apply the reversals to the expected direction
                angle = expectedDirection;
                for (int i = 0; i < relocationCount; i++)
                {
                    angle = (angle + 180.0) % 360.0;
                }
            }
            else
            {
                // Calculate angle from player's final position to the note
                double dx = currentNote.Line - finalX;
                double dy = currentNote.Layer - finalY;
                angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

                // Normalize to [0, 360)
                if (angle < 0) angle += 360;
            }

            // DEBUG: Log adjustments for beats around 203
            if (Math.Abs(currentNote.Time - 203.0f) < 2.0f)
            {
                BombDebugLogger.LogBombAdjustment(
                    prevNote, currentNote, expectedDirection,
                    startX, startY, finalX, finalY,
                    encounteredBombs, relocationCount, angle, bombs);
            }

            bool bombAffected = encounteredBombs || relocationCount > 0;
            return (angle, bombAffected);
        }

        /// <summary>
        /// Infers direction for a dot note by finding the next arrow and working backwards.
        /// </summary>
        public static double InferDirectionFromFutureArrow(List<Cube> cubes, int startIndex, float bpm)
        {
            var firstArrowNote = cubes.Skip(startIndex).FirstOrDefault(ca => ca.CutDirection != 8);
            
            if (firstArrowNote == null)
            {
                return cubes[startIndex].Layer >= 2 ? 90 : 270;
            }

            double direction = DirectionToDegree[firstArrowNote.CutDirection] + firstArrowNote.AngleOffset;
            
            int arrowIndex = cubes.IndexOf(firstArrowNote);
            for (int i = arrowIndex; i > startIndex; i--)
            {
                if (!AreNotesCloseInDepth(cubes[i - 1], cubes[i], bpm))
                {
                    direction = ReverseCutDirection(direction);
                }
            }
            
            return direction;
        }

        /// <summary>
        /// Attempts to adjust a dot note's direction to create proper alternating flow.
        /// </summary>
        private static bool TryAdjustDirectionForFlow(List<Cube> cubes, int currentIndex, double testValue)
        {
            double currentDir = cubes[currentIndex].Direction;
            double prevDir = cubes[currentIndex - 1].Direction;
            
            double adjustedDir = Mod(currentDir + testValue, 360);
            if (!IsSameDir(prevDir, adjustedDir))
            {
                if (currentIndex < cubes.Count - 1 && cubes[currentIndex + 1].CutDirection != 8)
                {
                    double nextDir = Mod(DirectionToDegree[cubes[currentIndex + 1].CutDirection] + 
                                       cubes[currentIndex + 1].AngleOffset, 360);
                    
                    if (IsSameDir(adjustedDir, nextDir))
                    {
                        if (!IsSameDir(adjustedDir + testValue, nextDir))
                        {
                            cubes[currentIndex].Direction = Mod(adjustedDir + testValue, 360);
                            return true;
                        }
                        else if (!IsSameDir(adjustedDir - testValue, nextDir))
                        {
                            cubes[currentIndex].Direction = Mod(adjustedDir - testValue, 360);
                            return true;
                        }
                        return false;
                    }
                }
                
                cubes[currentIndex].Direction = adjustedDir;
                return true;
            }

            adjustedDir = Mod(currentDir - testValue, 360);
            if (!IsSameDir(prevDir, adjustedDir))
            {
                if (currentIndex < cubes.Count - 1 && cubes[currentIndex + 1].CutDirection != 8)
                {
                    double nextDir = Mod(DirectionToDegree[cubes[currentIndex + 1].CutDirection] + 
                                       cubes[currentIndex + 1].AngleOffset, 360);
                    
                    if (IsSameDir(adjustedDir, nextDir))
                    {
                        if (!IsSameDir(adjustedDir + testValue, nextDir))
                        {
                            cubes[currentIndex].Direction = Mod(adjustedDir + testValue, 360);
                            return true;
                        }
                        else if (!IsSameDir(adjustedDir - testValue, nextDir))
                        {
                            cubes[currentIndex].Direction = Mod(adjustedDir - testValue, 360);
                            return true;
                        }
                        return false;
                    }
                }
                
                cubes[currentIndex].Direction = adjustedDir;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to fix bad flow by adjusting both the previous and current dot notes.
        /// </summary>
        private static bool TryAdjustBothPreviousAndCurrent(List<Cube> cubes, int currentIndex, double testValue)
        {
            if (cubes[currentIndex - 1].CutDirection != 8 || currentIndex < 2)
            {
                return false;
            }

            double prevPrevDir = cubes[currentIndex - 2].Direction;
            double prevDir = cubes[currentIndex - 1].Direction;
            double currentDir = cubes[currentIndex].Direction;

            double adjustedPrevDir = Mod(prevDir + testValue, 360);
            if (!IsSameDir(prevPrevDir, adjustedPrevDir))
            {
                double adjustedCurrentDir = Mod(currentDir + testValue * 2, 360);
                if (!IsSameDir(adjustedPrevDir, adjustedCurrentDir))
                {
                    cubes[currentIndex - 1].Direction = adjustedPrevDir;
                    cubes[currentIndex].Direction = adjustedCurrentDir;
                    return true;
                }
            }

            adjustedPrevDir = Mod(prevDir - testValue, 360);
            if (!IsSameDir(prevPrevDir, adjustedPrevDir))
            {
                double adjustedCurrentDir = Mod(currentDir - testValue * 2, 360);
                if (!IsSameDir(adjustedPrevDir, adjustedCurrentDir))
                {
                    cubes[currentIndex - 1].Direction = adjustedPrevDir;
                    cubes[currentIndex].Direction = adjustedCurrentDir;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Analyzes a sequence of notes and determines their swing directions and pattern relationships.
        /// </summary>
        public static void Detect(List<Cube> cubes, float bpm, bool isRightHand, List<Bomb> bombs = null)
        {
            if (cubes.Count < 2)
            {
                return;
            }

            double testValue = isRightHand ? -45 : 45;

            HandleSimultaneousNotes(cubes, bpm);
            
            // Pre-process: Mark groups of 3+ simultaneous notes as patterns (towers)
            MarkSimultaneousGroupsAsPatterns(cubes, bpm);
            
            if (cubes[0].CutDirection == 8)
            {
                if (cubes[1].CutDirection != 8 && AreNotesCloseInDepth(cubes[0], cubes[1], bpm))
                {
                    cubes[0].Direction = Mod(DirectionToDegree[cubes[1].CutDirection] + cubes[1].AngleOffset, 360);
                }
                else
                {
                    cubes[0].Direction = InferDirectionFromFutureArrow(cubes, 0, bpm);
                }
            }
            else
            {
                cubes[0].Direction = Mod(DirectionToDegree[cubes[0].CutDirection] + cubes[0].AngleOffset, 360);
            }
            
            
            
            if (cubes[1].CutDirection == 8)
            {
                bool isMultiHit = DetectAndOrderMultiNoteHit(cubes, 0, 1, bpm, out bool needsReorder);
                double inferredDirection = FindAngleViaPos(cubes[1], cubes[0], cubes[0].Direction, isMultiHit);

                // Adjust direction based on bombs between previous and current note
                var (adjustedDirection1, bombAffected1) = AdjustDirectionForBombs(cubes[0], cubes[1], inferredDirection, bombs);
                cubes[1].Direction = adjustedDirection1;

                if (isMultiHit)
                {
                    // Synchronize directions for simultaneous dot notes
                    // If prev is already in a pattern, sync curr to prev (extending existing pattern)
                    // Otherwise, sync prev to curr (starting new pattern)
                    if (cubes[0].Pattern && cubes[1].CutDirection == 8)
                    {
                        cubes[1].Direction = cubes[0].Direction;
                    }
                    else if (cubes[0].CutDirection == 8)
                    {
                        cubes[0].Direction = cubes[1].Direction;
                    }
                    MarkAsMultiNotePattern(cubes, 1, 0);
                }
            }
            else
            {
                cubes[1].Direction = Mod(DirectionToDegree[cubes[1].CutDirection] + cubes[1].AngleOffset, 360);
                
                if (DetectAndOrderMultiNoteHit(cubes, 0, 1, bpm, out bool needsReorder))
                {
                    MarkAsMultiNotePattern(cubes, 1, 0);
                }
            }
            
            
            
            
            
            for (int i = 2; i < cubes.Count - 1; i++)
            {
                if (cubes[i].CutDirection == 8)
                {
                    // Skip if already marked as part of a pattern group (e.g., tower)
                    if (cubes[i].Pattern)
                    {
                        // Still need to set direction for pattern notes
                        if (!cubes[i - 1].Pattern || cubes[i - 1].Tail)
                        {
                            // Previous note is not in pattern or is tail, infer direction normally
                            bool isMultiHit2 = DetectAndOrderMultiNoteHit(cubes, i - 1, i, bpm, out bool needsReorder2);
                            int prevIdx2 = needsReorder2 ? i : i - 1;
                            int currIdx2 = needsReorder2 ? i - 1 : i;
                            double inferredDirection2 = FindAngleViaPos(cubes[currIdx2], cubes[prevIdx2], cubes[prevIdx2].Direction, isMultiHit2);
                            var (adjustedDirection2, bombAffected2) = AdjustDirectionForBombs(cubes[prevIdx2], cubes[currIdx2], inferredDirection2, bombs);
                            cubes[currIdx2].Direction = adjustedDirection2;
                        }
                        else
                        {
                            // Part of same pattern group, synchronize direction
                            cubes[i].Direction = cubes[i - 1].Direction;
                        }
                        continue;
                    }
                    
                    bool isMultiHit = DetectAndOrderMultiNoteHit(cubes, i - 1, i, bpm, out bool needsReorder);
                    
                    // After potential reordering, indices might have swapped
                    int prevIdx = needsReorder ? i : i - 1;
                    int currIdx = needsReorder ? i - 1 : i;
                    
                    double inferredDirection = FindAngleViaPos(cubes[currIdx], cubes[prevIdx], cubes[prevIdx].Direction, isMultiHit);

                    // Adjust direction based on bombs between previous and current note
                    var (adjustedDirection, bombAffected) = AdjustDirectionForBombs(cubes[prevIdx], cubes[currIdx], inferredDirection, bombs);
                    cubes[currIdx].Direction = adjustedDirection;

                if (isMultiHit)
                {
                    // Synchronize directions for simultaneous dot notes
                    // If prev is already in a pattern, sync curr to prev (extending existing pattern)
                    // Otherwise, sync prev to curr (starting new pattern)
                    if (cubes[prevIdx].Pattern && cubes[currIdx].CutDirection == 8)
                    {
                        cubes[currIdx].Direction = cubes[prevIdx].Direction;
                    }
                    else if (cubes[prevIdx].CutDirection == 8)
                    {
                        cubes[prevIdx].Direction = cubes[currIdx].Direction;
                    }
                    
                    MarkAsMultiNotePattern(cubes, currIdx, prevIdx);
                    continue;
                }

                    // Skip flow adjustments if bombs dictated the direction
                    if (bombAffected)
                    {
                        continue;
                    }

                    if (!IsSameDir(cubes[i - 1].Direction, cubes[i].Direction))
                    {
                        continue;
                    }
                    
                    if (TryAdjustDirectionForFlow(cubes, i, testValue))
                    {
                        continue;
                    }
                    
                    TryAdjustBothPreviousAndCurrent(cubes, i, testValue);
                }
                else
                {
                    cubes[i].Direction = Mod(DirectionToDegree[cubes[i].CutDirection] + cubes[i].AngleOffset, 360);
                    
                    // Skip if already marked as part of a pattern group
                    if (!cubes[i].Pattern && DetectAndOrderMultiNoteHit(cubes, i - 1, i, bpm, out bool needsReorder))
                    {
                        int prevIdx = needsReorder ? i : i - 1;
                        int currIdx = needsReorder ? i - 1 : i;
                        MarkAsMultiNotePattern(cubes, currIdx, prevIdx);
                    }
                }
            }
            
            
            
            for (int i = 2; i < cubes.Count - 2; i++)
            {
                if (cubes[i].CutDirection != 8 || cubes[i].Pattern)
                {
                    continue;
                }
                
                bool flowsWithPrev = IsSameDir(cubes[i].Direction, cubes[i - 1].Direction);
                bool flowsWithNext = IsSameDir(cubes[i].Direction, cubes[i + 1].Direction);
                
                if (flowsWithPrev != flowsWithNext)
                {
                    double adjusted = Mod(cubes[i].Direction + testValue, 360);
                    if (!IsSameDir(adjusted, cubes[i - 1].Direction) && !IsSameDir(adjusted, cubes[i + 1].Direction))
                    {
                        cubes[i].Direction = adjusted;
                        continue;
                    }
                    
                    adjusted = Mod(cubes[i].Direction - testValue, 360);
                    if (!IsSameDir(adjusted, cubes[i - 1].Direction) && !IsSameDir(adjusted, cubes[i + 1].Direction))
                    {
                        cubes[i].Direction = adjusted;
                    }
                }
            }
            
            int lastIndex = cubes.Count - 1;
            
            if (cubes[lastIndex].CutDirection == 8)
            {
                bool isMultiHit = DetectAndOrderMultiNoteHit(cubes, lastIndex - 1, lastIndex, bpm, out bool needsReorder);
                double inferredDirection = FindAngleViaPos(cubes[lastIndex], cubes[lastIndex - 1], cubes[lastIndex - 1].Direction, isMultiHit);

                // Adjust direction based on bombs between previous and current note
                var (adjustedDirectionLast, bombAffectedLast) = AdjustDirectionForBombs(cubes[lastIndex - 1], cubes[lastIndex], inferredDirection, bombs);
                cubes[lastIndex].Direction = adjustedDirectionLast;

                if (isMultiHit)
                {
                    // Synchronize directions for simultaneous dot notes
                    // If prev is already in a pattern, sync curr to prev (extending existing pattern)
                    // Otherwise, sync prev to curr (starting new pattern)
                    if (cubes[lastIndex - 1].Pattern && cubes[lastIndex].CutDirection == 8)
                    {
                        cubes[lastIndex].Direction = cubes[lastIndex - 1].Direction;
                    }
                    else if (cubes[lastIndex - 1].CutDirection == 8)
                    {
                        cubes[lastIndex - 1].Direction = cubes[lastIndex].Direction;
                    }
                    MarkAsMultiNotePattern(cubes, lastIndex, lastIndex - 1);
                }
            }
            else
            {
                cubes[lastIndex].Direction = Mod(DirectionToDegree[cubes[lastIndex].CutDirection] + cubes[lastIndex].AngleOffset, 360);
                
                if (DetectAndOrderMultiNoteHit(cubes, lastIndex - 1, lastIndex, bpm, out bool needsReorder))
                {
                    MarkAsMultiNotePattern(cubes, lastIndex, lastIndex - 1);
                }
            }
            
            // After all direction assignments, apply snap angles for slanted windows
            // (simultaneous notes with same CutDirection)
            ApplySnapAnglesForSlantedWindows(cubes);
        }
    }
}
