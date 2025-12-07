using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.HandleMultiOrdering;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.FindAngleViaPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.IsSameDirection;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNoteHitDetector;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNotePatternDetector;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.BombPathSimulator;
using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Detects flow and patterns in beatmap sequences.
    /// Determines swing directions for dot notes and identifies multi-note hits.
    /// </summary>
    internal class FlowDetector
    {
        /// <summary>
        /// Adjusts dot note direction based on bomb-forced relocations.
        /// Bombs at player's standing position force them to relocate, changing where they end up.
        /// The final swing direction is calculated from the final position to the target note.
        /// </summary>
        private static double AdjustDirectionForBombs(Cube prevNote, Cube currentNote, double expectedDirection, List<Bomb> bombs)
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

            // Special case: If player ends up at the same position as the note
            if (finalX == currentNote.Line && finalY == currentNote.Layer)
            {
                // Each bomb relocation reverses direction by 180°
                // Apply the reversals to the expected direction
                double adjustedDirection = expectedDirection;
                for (int i = 0; i < relocationCount; i++)
                {
                    adjustedDirection = (adjustedDirection + 180.0) % 360.0;
                }
                return adjustedDirection;
            }

            // Calculate angle from player's final position to the note
            double dx = currentNote.Line - finalX;
            double dy = currentNote.Layer - finalY;
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            
            // Normalize to [0, 360)
            if (angle < 0) angle += 360;

            return angle;
        }

        /// <summary>
        /// Infers direction for a dot note by finding the next arrow and working backwards.
        /// </summary>
        private static double InferDirectionFromFutureArrow(List<Cube> cubes, int startIndex, float bpm)
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
                cubes[1].Direction = AdjustDirectionForBombs(cubes[0], cubes[1], inferredDirection, bombs);
                
                if (isMultiHit)
                {
                    if (cubes[0].CutDirection == 8)
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
                    bool isMultiHit = DetectAndOrderMultiNoteHit(cubes, i - 1, i, bpm, out bool needsReorder);
                    
                    // After potential reordering, indices might have swapped
                    int prevIdx = needsReorder ? i : i - 1;
                    int currIdx = needsReorder ? i - 1 : i;
                    
                    double inferredDirection = FindAngleViaPos(cubes[currIdx], cubes[prevIdx], cubes[prevIdx].Direction, isMultiHit);
                    
                    // Adjust direction based on bombs between previous and current note
                    cubes[currIdx].Direction = AdjustDirectionForBombs(cubes[prevIdx], cubes[currIdx], inferredDirection, bombs);
                    
                    if (isMultiHit)
                    {
                        if (cubes[prevIdx].CutDirection == 8)
                        {
                            cubes[prevIdx].Direction = cubes[currIdx].Direction;
                        }
                        MarkAsMultiNotePattern(cubes, currIdx, prevIdx);
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
                    
                    if (DetectAndOrderMultiNoteHit(cubes, i - 1, i, bpm, out bool needsReorder))
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
                cubes[lastIndex].Direction = AdjustDirectionForBombs(cubes[lastIndex - 1], cubes[lastIndex], inferredDirection, bombs);
                
                if (isMultiHit)
                {
                    if (cubes[lastIndex - 1].CutDirection == 8)
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
