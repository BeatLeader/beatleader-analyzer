using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;

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
        public static void Detect(List<Cube> cubes, float bpm, bool isRightHand)
        {
            if (cubes.Count == 0)
            {
                return;
            }

            // Handle single note case
            if (cubes.Count == 1)
            {
                Cube cube = cubes[0];
                
                if (cube.CutDirection != 8)
                {
                    // Arrow: use literal direction
                    cube.Direction = Mod(DirectionToDegree[cube.CutDirection] + cube.AngleOffset, 360);
                }
                else
                {
                    // Dot: use position-based direction
                    cube.Direction = GetInitialPosition(cube.X, cube.Y, isRightHand);
                }
                
                return;
            }

            // Step 1: Create groups of notes that are close together in time
            var groups = CreateNoteGroups(cubes, bpm);
            
            // Step 2: Set initial direction for all HEAD notes only
            SetInitialDirections(cubes, groups, isRightHand);
            
            // Step 3: For each group, validate and correct angle based on geometry
            ValidateAndCorrectGroupAngles(cubes, groups);
        }
        
        /// <summary>
        /// Creates groups of notes that are close together in time.
        /// Groups can contain simultaneous notes or notes very close in depth.
        /// </summary>
        private static List<List<int>> CreateNoteGroups(List<Cube> cubes, float bpm)
        {
            var groups = new List<List<int>>();
            int i = 0;
            
            while (i < cubes.Count)
            {
                var group = new List<int> { i };
                float currentTime = cubes[i].Beat;
                float currentNjs = cubes[i].Njs;
                
                // Add notes that are at the same time or very close in depth
                for (int j = i + 1; j < cubes.Count; j++)
                {
                    float timeDiff = cubes[j].Beat - currentTime;
                    
                    // Check if simultaneous (same time)
                    if (Math.Abs(timeDiff) < 0.001f)
                    {
                        group.Add(j);
                        continue;
                    }
                    
                    // Check if very close in depth (multi-note hit range)
                    float z1 = CalculateZPosition(currentTime, currentNjs, bpm);
                    float z2 = CalculateZPosition(cubes[j].Beat, cubes[j].Njs, bpm);
                    float depthDiff = Math.Abs(z2 - z1);
                    
                    // If within multi-note hit range (~0.3 meters), add to group
                    if (depthDiff < 0.5f)
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
        
        /// <summary>
        /// Calculates Z position (depth) of a note based on time, NJS, and BPM.
        /// </summary>
        private static float CalculateZPosition(float time, float njs, float bpm)
        {
            // Convert beat time to seconds
            float timeInSeconds = time * 60f / bpm;
            // Z position = NJS * time (simplified, actual game has more complex formula)
            return njs * timeInSeconds;
        }
        
        /// <summary>
        /// Sets initial direction for HEAD notes only (first note in each group).
        /// Non-head notes inherit from head during validation phase.
        /// </summary>
        private static void SetInitialDirections(List<Cube> cubes, List<List<int>> groups, bool isRightHand)
        {
            double previousDirection = 270.0; // Start with forehand down
            
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
                    // Dot: reverse previous direction
                    if (group == groups[0])
                    {
                        // First note: use position-based
                        headCube.Direction = GetInitialPosition(headCube.X, headCube.Y, isRightHand);
                    }
                    else
                    {
                        // Subsequent: reverse previous
                        headCube.Direction = Mod(previousDirection + 180, 360);
                    }
                }
                
                previousDirection = headCube.Direction;
                
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
        /// For each group with 2+ notes, calculate geometric angle and validate against head direction.
        /// If angle doesn't match, reverse it and apply to all notes in group.
        /// </summary>
        private static void ValidateAndCorrectGroupAngles(List<Cube> cubes, List<List<int>> groups)
        {
            foreach (var group in groups)
            {
                if (group.Count < 2)
                {
                    // Single note: just copy head direction to itself (already set)
                    continue;
                }
                
                // Get head note direction
                int headIndex = group[0];
                double headDirection = cubes[headIndex].Direction;
                
                // Calculate geometric angle from first to last note position
                Cube firstNote = cubes[group[0]];
                Cube lastNote = cubes[group[group.Count - 1]];
                
                int lineDiff = lastNote.X - firstNote.X;
                int layerDiff = lastNote.Y - firstNote.Y;
                
                // If notes are at same position, skip geometric correction
                if (lineDiff == 0 && layerDiff == 0)
                {
                    // Apply head direction to all notes
                    foreach (int idx in group)
                    {
                        cubes[idx].Direction = headDirection;
                        cubes[idx].Pattern = true;
                    }
                    continue;
                }
                
                // Calculate geometric angle
                double angleRad = Math.Atan2(layerDiff, lineDiff);
                double geometricAngle = (angleRad * 180.0 / Math.PI + 360.0) % 360.0;
                
                // Calculate reverse of geometric angle
                double reverseAngle = Mod(geometricAngle + 180, 360);
                
                // Determine which angle is closer to head direction
                double diffGeometric = Math.Abs(Mod(geometricAngle - headDirection + 180, 360) - 180);
                double diffReverse = Math.Abs(Mod(reverseAngle - headDirection + 180, 360) - 180);
                
                // Choose the angle that's closer to head direction
                double finalAngle = diffGeometric < diffReverse ? geometricAngle : reverseAngle;
                
                // Apply final angle to ALL notes in the group
                foreach (int idx in group)
                {
                    cubes[idx].Direction = finalAngle;
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
