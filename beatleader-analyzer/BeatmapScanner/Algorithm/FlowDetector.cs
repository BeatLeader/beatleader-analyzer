using static Analyzer.BeatmapScanner.Helper.HandleMultiOrdering;
using static Analyzer.BeatmapScanner.Helper.Helper;
using static Analyzer.BeatmapScanner.Helper.FindAngleViaPosition;
using static Analyzer.BeatmapScanner.Helper.IsSameDirection;
using static Analyzer.BeatmapScanner.Helper.MultiNoteHitDetector;
using Analyzer.BeatmapScanner.Data;
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
        /// Updates pattern flags for multi-note hit continuation.
        /// </summary>
        private static void ProcessMultiNoteHitContinuation(List<Cube> cubes, int currentIndex, int prevIndex)
        {
            cubes[currentIndex].Pattern = true;
            
            if (!cubes[prevIndex].Pattern)
            {
                cubes[prevIndex].Pattern = true;
                cubes[prevIndex].Head = true;
            }
            else
            {
                cubes[prevIndex].Tail = false;
            }
            
            cubes[currentIndex].Tail = true;
        }

        /// <summary>
        /// Analyzes a sequence of notes and determines their swing directions and pattern relationships.
        /// </summary>
        public static void Detect(List<Cube> cubes, float bpm, bool isRightHand)
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
                bool isMultiHit = IsMultiNoteHit(cubes[0], cubes[1], bpm);
                cubes[1].Direction = FindAngleViaPos(cubes[1], cubes[0], cubes[0].Direction, isMultiHit);
                
                if (isMultiHit)
                {
                    if (cubes[0].CutDirection == 8)
                    {
                        cubes[0].Direction = cubes[1].Direction;
                    }
                    ProcessMultiNoteHitContinuation(cubes, 1, 0);
                }
            }
            else
            {
                cubes[1].Direction = Mod(DirectionToDegree[cubes[1].CutDirection] + cubes[1].AngleOffset, 360);
                
                if (IsMultiNoteHit(cubes[0], cubes[1], bpm))
                {
                    ProcessMultiNoteHitContinuation(cubes, 1, 0);
                }
            }
            
            
            
            for (int i = 2; i < cubes.Count - 1; i++)
            {
                if (cubes[i].CutDirection == 8)
                {
                    bool isMultiHit = IsMultiNoteHit(cubes[i - 1], cubes[i], bpm);
                    cubes[i].Direction = FindAngleViaPos(cubes[i], cubes[i - 1], cubes[i - 1].Direction, isMultiHit);
                    
                    if (isMultiHit)
                    {
                        if (cubes[i - 1].CutDirection == 8)
                        {
                            cubes[i - 1].Direction = cubes[i].Direction;
                        }
                        ProcessMultiNoteHitContinuation(cubes, i, i - 1);
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
                    
                    if (IsMultiNoteHit(cubes[i - 1], cubes[i], bpm))
                    {
                        ProcessMultiNoteHitContinuation(cubes, i, i - 1);
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
                bool isMultiHit = IsMultiNoteHit(cubes[lastIndex - 1], cubes[lastIndex], bpm);
                cubes[lastIndex].Direction = FindAngleViaPos(cubes[lastIndex], cubes[lastIndex - 1], cubes[lastIndex - 1].Direction, isMultiHit);
                
                if (isMultiHit)
                {
                    if (cubes[lastIndex - 1].CutDirection == 8)
                    {
                        cubes[lastIndex - 1].Direction = cubes[lastIndex].Direction;
                    }
                    ProcessMultiNoteHitContinuation(cubes, lastIndex, lastIndex - 1);
                }
            }
            else
            {
                cubes[lastIndex].Direction = Mod(DirectionToDegree[cubes[lastIndex].CutDirection] + cubes[lastIndex].AngleOffset, 360);
                
                if (IsMultiNoteHit(cubes[lastIndex - 1], cubes[lastIndex], bpm))
                {
                    ProcessMultiNoteHitContinuation(cubes, lastIndex, lastIndex - 1);
                }
            }
        }
    }
}
