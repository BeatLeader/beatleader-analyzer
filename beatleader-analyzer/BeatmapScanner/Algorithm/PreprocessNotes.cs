using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.FindAngleViaPosition;
using beatleader_parser.Timescale;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Detects flow and patterns in beatmap sequences.
    /// Determines swing directions for dot notes and identifies multi-note hits.
    /// </summary>
    internal class PreprocessNotes
    {
        /// <summary>
        /// Detects multi-note patterns and sets swing directions.
        /// Groups notes by proximity and validates/corrects angles based on geometry.
        /// </summary>
        public static void Detect(List<Cube> cubes, List<Bomb> bombs, bool isRightHand, Timescale timescale)
        {
            // Step 1: Create groups of notes that are close together in time (can be singular too)
            var groups = CreateNoteGroups(cubes, timescale);
            
            // Step 2: Set initial direction for all HEAD notes only
            SetInitialDirections(cubes, groups, bombs, isRightHand);
            
            // Step 3: For each group, validate and correct angle based on geometry
            ValidateAndCorrectGroupAngles(cubes, groups);
        }
        
        /// <summary>
        /// Creates groups of notes that are close together in time.
        /// Groups can contain simultaneous notes or notes very close in depth.
        /// </summary>
        private static List<List<int>> CreateNoteGroups(List<Cube> cubes, Timescale timescale)
        {
            var groups = new List<List<int>>();
            int i = 0;
            
            while (i < cubes.Count)
            {
                var group = new List<int> { i };
                
                // Add notes that are at the same time or very close in depth
                for (int j = i + 1; j < cubes.Count; j++)
                {
                    // Get the previous note in the group (last added note)
                    int prevIndex = group[group.Count - 1];
                    float prevTime = cubes[prevIndex].BpmTime;
                    float timeDiff = cubes[j].BpmTime - prevTime;
                    
                    // Check if simultaneous (same time)
                    if (Math.Abs(timeDiff) < 0.001f)
                    {
                        group.Add(j);
                        continue;
                    }

                    // Check if very close using beat difference (need to take into account BPM changes)
                    // 1/5 of a beat is a good value to not catch 1/4 jumps.
                    // Will create false positive if there's sliders slower than this, but it's not rankable.
                    // Initial idea was to use NJS and BPM to calculate meters, but for some cases it would completely fail
                    // We need a catch-all, even if it creates some false positives in rare cases.
                    float prevBeat = timescale.ToBeatTime(cubes[prevIndex].Seconds, true);
                    float currBeat = timescale.ToBeatTime(cubes[j].Seconds, true);
                    float beatDiff = Math.Abs(currBeat - prevBeat);
                    if (beatDiff <= 0.2f && ValidateSliders(cubes[prevIndex], cubes[j]))
                    {
                        group.Add(j);
                    }
                    else
                    {
                        // Too far apart, stop checking
                        break;
                    }
                }
                
                groups.Add(group);
                i += group.Count;
            }
            
            return groups;
        }

        private static bool ValidateSliders(Cube previous, Cube current)
        {
            // Both are dots - always valid, direction will be calculated from geometry
            // This might allow some false positives, such as inline dots, but those are rare and acceptable
            if (previous.CutDirection == 8 && current.CutDirection == 8)
            {
                return true;
            }

            // Calculate geometric angle from previous to current note position
            int lineDiff = current.X - previous.X;
            int layerDiff = current.Y - previous.Y;
            
            // If notes are at the same position, not a valid slider
            if (lineDiff == 0 && layerDiff == 0)
            {
                return false;
            }

            double geometricAngle = Math.Atan2(layerDiff, lineDiff) * (180.0 / Math.PI);
            if (geometricAngle < 0)
            {
                geometricAngle += 360.0;
            }

            // Threshold for "similar direction" - within 67.5 degrees (matching IsSameDir threshold)
            const double DIRECTION_TOLERANCE = 67.5;

            // Case 1: Previous is arrow, current is dot
            // Verify current's position is in a similar direction to previous arrow
            if (previous.CutDirection != 8 && current.CutDirection == 8)
            {
                double previousArrowAngle = Mod(DirectionToDegree[previous.CutDirection] + previous.AngleOffset, 360);
                double angleDiff = Math.Abs(Mod(geometricAngle - previousArrowAngle + 180, 360) - 180);
                
                return angleDiff <= DIRECTION_TOLERANCE;
            }

            // Case 2: Previous is dot, current is arrow
            // Verify current arrow is in a similar direction to the geometric path
            if (previous.CutDirection == 8 && current.CutDirection != 8)
            {
                double currentArrowAngle = Mod(DirectionToDegree[current.CutDirection] + current.AngleOffset, 360);
                double angleDiff = Math.Abs(Mod(geometricAngle - currentArrowAngle + 180, 360) - 180);
                
                return angleDiff <= DIRECTION_TOLERANCE;
            }

            // Case 3: Both are arrows
            // Verify both arrows are similar to each other AND to the geometric path
            if (previous.CutDirection != 8 && current.CutDirection != 8)
            {
                double previousArrowAngle = Mod(DirectionToDegree[previous.CutDirection] + previous.AngleOffset, 360);
                double currentArrowAngle = Mod(DirectionToDegree[current.CutDirection] + current.AngleOffset, 360);
                
                // Check if arrows are similar to each other
                double arrowDiff = Math.Abs(Mod(currentArrowAngle - previousArrowAngle + 180, 360) - 180);
                if (arrowDiff > DIRECTION_TOLERANCE)
                {
                    return false;
                }
                
                // Check if geometric path is similar to the arrow directions
                double geomToPrevDiff = Math.Abs(Mod(geometricAngle - previousArrowAngle + 180, 360) - 180);
                double geomToCurrDiff = Math.Abs(Mod(geometricAngle - currentArrowAngle + 180, 360) - 180);
                
                // At least one arrow should align with the geometric path
                return geomToPrevDiff <= DIRECTION_TOLERANCE || geomToCurrDiff <= DIRECTION_TOLERANCE;
            }

            // Fallback - should not reach here
            return true;
        }

        /// <summary>
        /// Sets initial direction for HEAD notes only (first note in each group).
        /// Non-head notes inherit from head during validation phase.
        /// </summary>
        private static void SetInitialDirections(List<Cube> cubes, List<List<int>> groups, List<Bomb> bombs, bool isRightHand)
        {
            double previousDirection = 270.0; // Start with forehand down
            int previousCubeIndex = 0;
            
            foreach (var group in groups)
            {
                // Only set direction for the HEAD note (first in group)
                int headIndex = group[0];
                Cube headCube = cubes[headIndex];
                
                if (headCube.CutDirection != 8)
                {
                    // Arrow: use literal direction
                    headCube.Direction = Mod(DirectionToDegree[headCube.CutDirection] + headCube.AngleOffset, 360);
                }
                else
                {
                    // Dot: calculate direction based on previous swing and bomb avoidance
                    if (group == groups[0])
                    {
                        // First note: use position-based
                        headCube.Direction = GetInitialPosition(headCube.X, headCube.Y, isRightHand);
                    }
                    else
                    {
                        // Check for bomb avoidance
                        var bombInfluence = ParityPredictor.AnalyzeBombInfluence(cubes, previousCubeIndex, headIndex, bombs);
                        
                        // If bomb avoidance occurred, calculate direction from player's new position
                        if (bombInfluence.hasBombs && bombInfluence.playerX >= 0)
                        {
                            double deltaX = headCube.X - bombInfluence.playerX;
                            double deltaY = headCube.Y - bombInfluence.playerY;
                            
                            if (Math.Abs(deltaX) > 0.01 || Math.Abs(deltaY) > 0.01)
                            {
                                // Calculate angle from player position (after bomb avoidance) to current dot note
                                double angleRad = Math.Atan2(deltaY, deltaX);
                                headCube.Direction = (angleRad * 180.0 / Math.PI + 360.0) % 360.0;
                            }
                            else
                            {
                                // Same position, use previous direction reversed
                                headCube.Direction = FindAngleViaPos(headCube, cubes[previousCubeIndex], previousDirection, false);
                            }
                        }
                        else
                        {
                            // No bomb avoidance: normal flow
                            // If bomb avoidance suggests parity flip, keep previous direction
                            // Otherwise, reverse direction
                            if (bombInfluence.parityFlip)
                            {
                                headCube.Direction = FindAngleViaPos(headCube, cubes[previousCubeIndex], Mod(previousDirection + 180, 360), false);
                            }
                            else
                            {
                                headCube.Direction = FindAngleViaPos(headCube, cubes[previousCubeIndex], previousDirection, false);
                            }
                        }
                    }
                }
                
                previousDirection = headCube.Direction;
                previousCubeIndex = headIndex;

                // Mark head
                headCube.Head = true;
                
                // Mark tail if group has multiple notes
                if (group.Count > 1)
                {
                    cubes[group[group.Count - 1]].Tail = true;
                }
            }
        }
        
        /// <summary>
        /// For each group with 2+ notes, calculate geometric angle from head to tail.
        /// For sliders and patterns, the direction is always from the lowest beat (head) to highest beat (tail).
        /// When both geometric and reverse angles are roughly equidistant from the previous swing,
        /// prefer the angle that would naturally alternate parity.
        /// </summary>
        private static void ValidateAndCorrectGroupAngles(List<Cube> cubes, List<List<int>> groups)
        {
            for (int groupIdx = 0; groupIdx < groups.Count; groupIdx++)
            {
                var group = groups[groupIdx];
                
                if (group.Count < 2)
                {
                    // Single note: just copy head direction to itself (already set)
                    continue;
                }
                
                // Get head note direction (initial guess, will be overridden for patterns)
                int headIndex = group[0];
                double headDirection = cubes[headIndex].Direction;
                
                // Find the actual head (lowest beat) and tail (highest beat)
                // This handles cases where notes might not be in perfect chronological order
                int actualHeadIndex = group[0];
                int actualTailIndex = group[0];
                double minBeat = cubes[group[0]].BpmTime;
                double maxBeat = cubes[group[0]].BpmTime;
                
                for (int i = 1; i < group.Count; i++)
                {
                    int idx = group[i];
                    if (cubes[idx].BpmTime < minBeat)
                    {
                        minBeat = cubes[idx].BpmTime;
                        actualHeadIndex = idx;
                    }
                    if (cubes[idx].BpmTime > maxBeat)
                    {
                        maxBeat = cubes[idx].BpmTime;
                        actualTailIndex = idx;
                    }
                }
                
                Cube headNote = cubes[actualHeadIndex];
                Cube tailNote = cubes[actualTailIndex];
                
                int lineDiff = tailNote.X - headNote.X;
                int layerDiff = tailNote.Y - headNote.Y;
                
                // Check if this is a simultaneous pattern (all notes at same beat time)
                bool isSimultaneous = Math.Abs(maxBeat - minBeat) < 0.001;
                
                // If notes are at the exact same position (shouldn't happen in practice), use flow direction
                if (lineDiff == 0 && layerDiff == 0)
                {
                    // No spatial movement between notes - use flow-based direction
                    foreach (int idx in group)
                    {
                        cubes[idx].Direction = headDirection;
                        cubes[idx].Pattern = true;
                    }
                    continue;
                }
                
                // Calculate geometric angle based on spatial positions
                double angleRad = Math.Atan2(layerDiff, lineDiff);
                double geometricAngle = (angleRad * 180.0 / Math.PI + 360.0) % 360.0;
                
                // Calculate reverse angle
                double reverseAngle = Mod(geometricAngle + 180, 360);
                
                // Default to geometric angle (head to tail)
                double chosenAngle = geometricAngle;
                
                // For both simultaneous and sequential patterns, consider parity alternation
                if (groupIdx > 0)
                {
                    // Find the previous group's last note (tail of previous pattern or single note)
                    var prevGroup = groups[groupIdx - 1];
                    int prevTailIndex = prevGroup[prevGroup.Count - 1];
                    double prevDirection = cubes[prevTailIndex].Direction;
                    
                    // Calculate angular distance from previous direction to both options
                    double diffGeometric = Math.Abs(Mod(geometricAngle - prevDirection + 180, 360) - 180);
                    double diffReverse = Math.Abs(Mod(reverseAngle - prevDirection + 180, 360) - 180);
                    
                    // Check if angles are roughly equidistant (within 30 degrees difference)
                    double equidistanceThreshold = 30;
                    bool roughlyEquidistant = Math.Abs(diffGeometric - diffReverse) < equidistanceThreshold;
                    
                    if (roughlyEquidistant)
                    {
                        // When equidistant, prefer the angle that alternates parity (different direction)
                        // "Same direction" means within 67.5 degrees (matching IsSameDir threshold)
                        bool geometricSameDir = diffGeometric < 67.5;
                        bool reverseSameDir = diffReverse < 67.5;
                        
                        if (geometricSameDir && !reverseSameDir)
                        {
                            // Geometric is same direction, reverse is different -> prefer reverse for parity alternation
                            chosenAngle = reverseAngle;
                        }
                        else if (!geometricSameDir && reverseSameDir)
                        {
                            // Reverse is same direction, geometric is different -> keep geometric for parity alternation
                            chosenAngle = geometricAngle;
                        }
                        // If both same or both different, keep geometric (default)
                    }
                    // If not equidistant, keep geometric angle (head to tail is always primary)
                }
                
                // Apply chosen angle to ALL notes in the group
                foreach (int idx in group)
                {
                    cubes[idx].Direction = chosenAngle;
                    cubes[idx].Pattern = true;
                }
            }
        }
        
        /// <summary>
        /// Gets initial angle for first dot note based on position.
        /// </summary>
        private static double GetInitialPosition(int line, int layer, bool isRightHand)
        {
            if (isRightHand)
            {
                // Blue hand
                return (line, layer) switch
                {
                    (0, 0) => 225.0,  // DOWN-LEFT
                    (0, 1) => 180.0,  // LEFT
                    (0, 2) => 135.0,  // UP-LEFT
                    (1, 0) => 225.0,  // DOWN-LEFT
                    (1, 1) => 225.0,  // DOWN-LEFT
                    (1, 2) => 135.0,  // UP-LEFT
                    (2, 0) => 270.0,  // DOWN
                    (2, 1) => 270.0,  // DOWN
                    (2, 2) => 90.0,   // UP
                    (3, 0) => 315.0,  // DOWN-RIGHT
                    (3, 1) => 315.0,  // DOWN-RIGHT
                    (3, 2) => 45.0,   // UP-RIGHT
                    _ => 270.0        // Default DOWN
                };
            }
            else
            {
                // Red hand
                return (line, layer) switch
                {
                    (0, 0) => 225.0,  // DOWN-LEFT
                    (0, 1) => 225.0,  // DOWN-LEFT
                    (0, 2) => 135.0,  // UP-LEFT
                    (1, 0) => 270.0,  // DOWN
                    (1, 1) => 270.0,  // DOWN
                    (1, 2) => 90.0,   // UP
                    (2, 0) => 315.0,  // DOWN-RIGHT
                    (2, 1) => 315.0,  // DOWN-RIGHT
                    (2, 2) => 45.0,   // UP-RIGHT
                    (3, 0) => 315.0,  // DOWN-RIGHT
                    (3, 1) => 0.0,    // RIGHT
                    (3, 2) => 45.0,   // UP-RIGHT
                    _ => 270.0        // Default DOWN
                };
            }
        }
    }
}
