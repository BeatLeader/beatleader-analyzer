using Analyzer.BeatmapScanner.Data;
using beatleader_parser.Timescale;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.SwingSimulation;
using static beatleader_analyzer.BeatmapScanner.Helper.GridPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;

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
        public static void Detect(List<Cube> cubes, List<Bomb> bombs, bool isRightHand)
        {
            // Step 1: Create groups of notes that are close together in time (can be singular too)
            var groups = CreateNoteGroups(cubes);
            
            // Step 2: Set initial direction for all HEAD notes only
            SetInitialDirections(cubes, groups, bombs, isRightHand);
            
            // Step 3: For each group, validate and correct angle based on geometry
            ValidateAndCorrectGroupAngles(cubes, groups);
        }
        
        /// <summary>
        /// Creates groups of notes that are close together in time.
        /// Groups can contain simultaneous notes or notes very close in depth.
        /// </summary>
        private static List<List<int>> CreateNoteGroups(List<Cube> cubes)
        {
            var groups = new List<List<int>>();
            int i = 0;

            // We use seconds, so we don't need to use bpm changes.
            while (i < cubes.Count)
            {
                var group = new List<int> { i };

                // Add notes that are at the same time or very close in depth
                for (int j = i + 1; j < cubes.Count; j++)
                {
                    // Get the previous note in the group (last added note)
                    int prevIndex = group[group.Count - 1];
                    float prevTime = cubes[prevIndex].Seconds;
                    float prevNjs = cubes[prevIndex].Njs;
                    float timeDiff = Math.Abs(cubes[j].Seconds - prevTime);
                    
                    // Check if simultaneous (same time)
                    if (timeDiff < 0.001f)
                    {
                        group.Add(j);
                        continue;
                    }

                    float distance = timeDiff * cubes[j].Njs;

                    // Some maps have reversed sliders, so distance <= 0.1f is a catch-all.
                    // Example map: Meowchine, Break, Grave of the Fireflies.
                    // 1.3f is for G1ll35 d3 R415 specifically (0.070s dot inline at 18NJS).
                    // Might be unnecessary, but it's overweighted otherwise.
                    // Example map with slow sliders: Alone intelligence, Lost It (require 1.2f), etc.
                    if ((distance < 1.3f && ValidateSliders(cubes[prevIndex], cubes[j])) || distance <= 0.1f)
                    {
                        group.Add(j);
                    }
                    else
                    {
                        // Too far apart, stop checking
                        break;
                    }
                }
                
                // Reorder group if notes are out of chronological order (incorrect slider mapping)
                if (group.Count > 1)
                {
                    ReorderGroupByTime(group, cubes);
                }
                
                groups.Add(group);
                i += group.Count;
            }
            
            return groups;
        }
        
        /// <summary>
        /// Reorders notes within a group by their beat time.
        /// Handles incorrectly mapped sliders where head appears after tail.
        /// </summary>
        private static void ReorderGroupByTime(List<int> group, List<Cube> cubes)
        {
            // Check if notes are already in order
            bool needsReordering = false;
            for (int i = 1; i < group.Count; i++)
            {
                if (cubes[group[i]].BpmTime < cubes[group[i - 1]].BpmTime)
                {
                    needsReordering = true;
                    break;
                }
            }
            
            if (!needsReordering)
            {
                return;
            }
            
            // Create list of (index, time) pairs and sort by time
            var indexedNotes = group.Select(idx => new { Index = idx, Time = cubes[idx].BpmTime }).ToList();
            indexedNotes.Sort((a, b) => a.Time.CompareTo(b.Time));
            
            // Update group with sorted indices
            for (int i = 0; i < group.Count; i++)
            {
                group[i] = indexedNotes[i].Index;
            }
        }

        private static bool ValidateSliders(Cube previous, Cube current)
        {
            // We want to consider dot spam as sliders
            if (current.CutDirection == 8 && 
                previous.X == current.X && previous.Y == current.Y)
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

            // Case 0: Both are dots and no direction is set - we store the direction for potential upcoming dot notes
            if (previous.Direction == 8 && previous.CutDirection == 8 && current.CutDirection == 8)
            {
                previous.Direction = geometricAngle;
                current.Direction = geometricAngle;
                return true;
            }
            else if (previous.CutDirection == 8 && current.CutDirection == 8)
            {
                double angleDiff = Math.Abs(Mod(geometricAngle - previous.Direction + 180, 360) - 180);

                return angleDiff <= DIRECTION_TOLERANCE;
            }

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
                bool groupHasBombAvoidance = false;
                
                if (headCube.CutDirection != 8)
                {
                    // Arrow: use literal direction
                    headCube.Direction = Mod(DirectionToDegree[headCube.CutDirection] + headCube.AngleOffset, 360);

                    // Check for bomb avoidance
                    var bombInfluence = FlagBombAvoidance(cubes, previousCubeIndex, headIndex, bombs);
                    groupHasBombAvoidance = bombInfluence.hasBombs && (bombInfluence.playerX >= 0 || bombInfluence.playerY >= 0);
                }
                else
                {
                    // Dot: calculate direction based on previous swing and bomb avoidance
                    if (group == groups[0])
                    {
                        // First note: use position-based
                        headCube.Direction = GetInitialPosition(headCube.X, headCube.Y, isRightHand);

                        // Check for bomb avoidance
                        var bombInfluence = FlagBombAvoidance(cubes, previousCubeIndex, headIndex, bombs);
                        groupHasBombAvoidance = bombInfluence.hasBombs && (bombInfluence.playerX >= 0 || bombInfluence.playerY >= 0);
                    }
                    else
                    {
                        // Check for bomb avoidance
                        var bombInfluence = FlagBombAvoidance(cubes, previousCubeIndex, headIndex, bombs);
                        groupHasBombAvoidance = bombInfluence.hasBombs && (bombInfluence.playerX >= 0 || bombInfluence.playerY >= 0);

                        // If bomb avoidance occurred, calculate direction from player's new position
                        if (groupHasBombAvoidance)
                        {
                            // Convert both positions to meters for consistent calculation
                            var (headMeterX, headMeterY) = GridToMeters(headCube.X, headCube.Y);
                            double deltaX = headMeterX - bombInfluence.playerX;
                            double deltaY = headMeterY - bombInfluence.playerY;
                            
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
                    cubes[group[^1]].Tail = true;
                }

                // If bomb avoidance was detected, apply it to all notes in the group
                if (groupHasBombAvoidance)
                {
                    foreach (int idx in group)
                    {
                        cubes[idx].BombAvoidance = true;
                    }
                }
            }
        }
        
        private static (bool hasBombs, bool parityFlip, double playerX, double playerY) FlagBombAvoidance(List<Cube> cubes, int previousIndex, int currentIndex, List<Bomb> bombs)
        {
            // Check for bomb avoidance
            var bombInfluence = AnalyzeBombInfluence(cubes, previousIndex, currentIndex, bombs);

            // If bomb avoidance occurred
            if (bombInfluence.hasBombs && (bombInfluence.playerX >= 0 || bombInfluence.playerY >= 0))
            {
                // Mark bomb avoidance
                cubes[currentIndex].BombAvoidance = true;
            }

            return bombInfluence;
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

        public static (bool hasBombs, bool parityFlip, double playerX, double playerY) AnalyzeBombInfluence(List<Cube> cubes, int prevSwingHeadIndex, int currentSwingHeadIndex, List<Bomb> bombs)
        {
            if (bombs == null || bombs.Count == 0)
            {
                return (false, false, -1, -1);
            }

            // Get the tail of the previous swing (for patterns) or the head itself (for single notes)
            var prevSwingTailCube = cubes[prevSwingHeadIndex];
            if (cubes[prevSwingHeadIndex].Pattern && cubes[prevSwingHeadIndex].Head)
            {
                // Find the tail of the pattern
                for (int i = prevSwingHeadIndex + 1; i < cubes.Count && cubes[i].Pattern && !cubes[i].Head; i++)
                {
                    if (cubes[i].Tail)
                    {
                        prevSwingTailCube = cubes[i];
                        break;
                    }
                }
            }

            var currentCube = cubes[currentSwingHeadIndex];

            // Get ALL bombs between previous swing's tail and current swing's head
            var bombsBetween = bombs
                .Where(b => b.BpmTime > prevSwingTailCube.BpmTime && b.BpmTime < currentCube.BpmTime)
                .OrderBy(b => b.BpmTime)
                .ToList();

            var allRelevantBombs = bombsBetween.ToList();

            if (allRelevantBombs.Count == 0)
            {
                return (false, false, -1, -1);
            }

            // Calculate player's position after previous swing in meter-based coordinates
            // Convert previous swing tail position to meters
            var (prevMeterX, prevMeterY) = GridToMeters(prevSwingTailCube.X, prevSwingTailCube.Y);
            
            double prevAngleRadians = prevSwingTailCube.Direction * Math.PI / 180.0;
            double prevDirX = Math.Cos(prevAngleRadians);
            double prevDirY = Math.Sin(prevAngleRadians);

            // Calculate target position in swing direction (large distance to ensure beyond grid edge)
            // Using 6 meters (10 grid units * 0.6m) to ensure we reach the edge
            double targetX = prevMeterX + prevDirX * 6.0;
            double targetY = prevMeterY + prevDirY * 6.0;

            // Clamp to grid bounds in meters: X [-0.9, 0.9], Y [0, 1.05]
            double playerX = Math.Clamp(targetX, GridXToMeters(0), GridXToMeters(3));
            double playerY = Math.Clamp(targetY, GridYToMeters(0), GridYToMeters(2));

            bool encounteredBomb = false;
            int reversalCount = 0;
            bool parityFlip = false;

            // Simulate player trying to recover from swing and encountering bombs
            foreach (var bomb in allRelevantBombs)
            {
                // Convert bomb position to meters for consistent comparison
                var (bombMeterX, bombMeterY) = GridToMeters(bomb.x, bomb.y);
                
                // Check if bomb is at or very close to player's current position (within 0.48 meters = 0.8 grid units)
                double toBombX = bombMeterX - playerX;
                double toBombY = bombMeterY - playerY;
                double distToBomb = Math.Sqrt(toBombX * toBombX + toBombY * toBombY);

                if (distToBomb < 0.48)
                {
                    // Bomb is at player's position! Player must avoid it
                    reversalCount++;
                    encounteredBomb = true;
                    parityFlip = !parityFlip;

                    // Player moves maximum 2 grid spaces away from bomb (1.2 meters)
                    // Determine if bomb is in a corner or edge
                    double leftEdge = GridXToMeters(0);
                    double rightEdge = GridXToMeters(3);
                    double bottomEdge = GridYToMeters(0);
                    double topEdge = GridYToMeters(2);
                    double centerX = GridXToMeters(1.5);
                    double centerY = GridYToMeters(1.0);
                    
                    bool bombInHorizontalEdge = bombMeterX <= leftEdge + 0.01 || bombMeterX >= rightEdge - 0.01;
                    bool bombInVerticalEdge = bombMeterY <= bottomEdge + 0.01 || bombMeterY >= topEdge - 0.01;
                    bool bombInCorner = bombInHorizontalEdge && bombInVerticalEdge;

                    if (bombInCorner)
                    {
                        // Bomb in corner: move 2 spaces (1.2m) in both directions
                        playerX = bombMeterX <= centerX ? bombMeterX + 1.2 : bombMeterX - 1.2;
                        playerY = bombMeterY <= centerY ? bombMeterY + 1.1 : bombMeterY - 1.1; // Y spacing varies slightly
                    }
                    else if (bombInHorizontalEdge)
                    {
                        // Bomb at left/right edge: move 2 spaces horizontally only
                        playerX = bombMeterX <= centerX ? bombMeterX + 1.2 : bombMeterX - 1.2;
                        // Keep Y position (stay at same row)
                    }
                    else if (bombInVerticalEdge)
                    {
                        // Bomb at top/bottom edge: move 2 spaces vertically only
                        playerY = bombMeterY <= centerY ? bombMeterY + 1.1 : bombMeterY - 1.1;
                        // Keep X position (stay at same column)
                    }

                    // Clamp to valid grid bounds in meters
                    playerX = Math.Clamp(playerX, leftEdge, rightEdge);
                    playerY = Math.Clamp(playerY, bottomEdge, topEdge);
                }
            }

            // Return player position after bomb avoidance (or -1,-1 if no bombs encountered)
            return (encounteredBomb, parityFlip, encounteredBomb ? playerX : -1, encounteredBomb ? playerY : -1);
        }
    }
}
