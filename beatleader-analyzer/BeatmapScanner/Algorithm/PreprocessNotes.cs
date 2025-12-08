using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.FindAngleViaPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.HandleMultiOrdering;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNoteHitDetector;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNotePatternDetector;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Detects flow and patterns in beatmap sequences.
    /// Determines swing directions for dot notes and identifies multi-note hits.
    /// </summary>
    internal class PreprocessNotes
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
        /// Detects multi-note patterns and sets initial direction for the first two notes.
        /// Direction for subsequent notes is calculated by SwingProcesser.
        /// </summary>
        public static void Detect(List<Cube> cubes, float bpm, bool isRightHand, List<Bomb> bombs = null)
        {
            if (cubes.Count < 2)
            {
                return;
            }

            // Detect and mark simultaneous groups (towers, etc.)
            HandleSimultaneousNotes(cubes, bpm);
            MarkSimultaneousGroupsAsPatterns(cubes, bpm);
            
            // Set direction for first note (needed by SwingProcesser as starting point)
            SetInitialDirection(cubes, 0, bpm, isRightHand);
            
            // Set direction for second note if it's a dot (for multi-note detection)
            if (cubes[1].CutDirection == 8)
            {
                // If part of multi-note with first note, infer direction
                if (DetectAndOrderMultiNoteHit(cubes, 0, 1, bpm))
                {
                    if (cubes[0].CutDirection == 8)
                    {
                        cubes[0].Direction = FindAngleViaPos(cubes[1], cubes[0], cubes[0].Direction, true);
                    }
                    MarkAsMultiNotePattern(cubes, 1, 0);
                }
                else
                {
                    // Not a multi-note, infer direction independently
                    SetInitialDirection(cubes, 1, bpm, isRightHand);
                }
            }
            else
            {
                // Arrow note - set direction directly
                cubes[1].Direction = Mod(DirectionToDegree[cubes[1].CutDirection] + cubes[1].AngleOffset, 360);
                
                // Check if it forms multi-note with first note
                if (DetectAndOrderMultiNoteHit(cubes, 0, 1, bpm))
                {
                    MarkAsMultiNotePattern(cubes, 1, 0);
                }
            }

            // Detect and mark multi-note patterns for remaining notes
            for (int i = 2; i < cubes.Count; i++)
            {
                // Skip if already marked as part of a pattern group (e.g., tower)
                if (cubes[i].Pattern)
                {
                    continue;
                }

                // Check if this note forms a multi-note pattern with the previous note
                if (DetectAndOrderMultiNoteHit(cubes, i - 1, i, bpm))
                {
                    MarkAsMultiNotePattern(cubes, i, i - 1);
                }
                
                // Set direction for arrow notes (dots will be handled by SwingProcesser)
                if (cubes[i].CutDirection != 8)
                {
                    cubes[i].Direction = Mod(DirectionToDegree[cubes[i].CutDirection] + cubes[i].AngleOffset, 360);
                }
            }
            
            // After all pattern detection, apply snap angles for slanted windows
            ApplySnapAnglesForSlantedWindows(cubes);
        }

        /// <summary>
        /// Sets the initial direction for a note by simulating a natural starting position.
        /// For left hand (red, type 0): starting position is (1, 1)
        /// For right hand (blue, type 1): starting position is (2, 1)
        /// </summary>
        private static void SetInitialDirection(List<Cube> cubes, int index, float bpm, bool isRightHand)
        {
            if (cubes[index].CutDirection != 8)
            {
                // Arrow note - set direction directly
                cubes[index].Direction = Mod(DirectionToDegree[cubes[index].CutDirection] + cubes[index].AngleOffset, 360);
                return;
            }

            // Dot note - infer direction from simulated starting position
            // Natural resting position: left hand (1,1), right hand (2,1)
            int startLine = isRightHand ? 2 : 1;
            int startLayer = 1;
            
            // Calculate angle from starting position to the note
            int deltaLine = cubes[index].Line - startLine;
            int deltaLayer = cubes[index].Layer - startLayer;
            
            if (deltaLine == 0 && deltaLayer == 0)
            {
                // Note is at the starting position, default to down
                cubes[index].Direction = 270.0;
            }
            else
            {
                // Calculate angle from starting position to note
                double angleRad = Math.Atan2(deltaLayer, deltaLine);
                double angleDeg = angleRad * 180.0 / Math.PI;
                cubes[index].Direction = Mod(angleDeg, 360);
            }
        }
    }
}
