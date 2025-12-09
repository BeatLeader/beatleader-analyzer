using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.FindAngleViaPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNoteHitDetector;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper.MultiNote
{
    /// <summary>
    /// Orders simultaneous notes by distance from swing entry point.
    /// </summary>
    internal class HandleMultiOrdering
    {
        public static void HandleSimultaneousNotes(List<Cube> cubes, float bpm)
        {
            if (cubes.Count < 2)
            {
                return;
            }

            var timeGroupedCubes = cubes.GroupBy(x => x.Beat).ToDictionary(x => x.Key, x => x.ToArray());

            int skipCount = 0;

            for (int n = 0; n < cubes.Count - 1; n++)
            {
                if (skipCount > 0)
                {
                    skipCount--;
                    continue;
                }

                Cube currentCube = cubes[n];
                
                if (currentCube.Beat == cubes[n + 1].Beat)
                {
                    Cube[] simultaneousNotes = timeGroupedCubes[currentCube.Beat];
                    skipCount = simultaneousNotes.Length - 1;

                    double entryDirection = DetermineSwingDirection(cubes, currentCube, n, bpm);

                    if (entryDirection == -1)
                    {
                        continue;
                    }

                    // The entry direction is the reverse of the swing direction
                    // Calculate the actual swing direction for snap angle calculation
                    double inferredSwingDirection = ReverseCutDirection(entryDirection);
                    
                    // Before ordering, calculate snap angles for slanted windows
                    // This ensures ordering uses the correct geometric angle
                    double actualSwingDirection = CalculateSnapAngleForGroup(cubes, n, simultaneousNotes.Length, inferredSwingDirection);
                    
                    (double x, double y) entryPoint = CalculateEntryPoint(cubes, n, actualSwingDirection);
                    OrderSimultaneousNotesByDistance(cubes, n, simultaneousNotes.Length, entryPoint, actualSwingDirection);
                }
            }
        }

        private static double DetermineSwingDirection(List<Cube> cubes, Cube currentCube, int currentIndex, float bpm)
        {
            var timeGroupedCubes = cubes.Where(c => c.Beat == currentCube.Beat).ToArray();
            Cube arrowNote = timeGroupedCubes.LastOrDefault(c => c.CutDirection != 8);

            if (arrowNote != null)
            {
                return ReverseCutDirection(Mod(DirectionToDegree[arrowNote.CutDirection] + arrowNote.AngleOffset, 360));
            }

            // All notes in the group are dots - calculate geometric direction and validate with flow
            // For dots, infer direction from the geometric angle between first and last note
            var first = timeGroupedCubes[0];
            var last = timeGroupedCubes[timeGroupedCubes.Length - 1];

            int lineDiff = last.X - first.X;
            int layerDiff = last.Y - first.Y;

            if (lineDiff != 0 || layerDiff != 0)
            {
                double angleRad = Math.Atan2(layerDiff, lineDiff);
                double angleDeg = angleRad * 180.0 / Math.PI;
                if (angleDeg < 0) angleDeg += 360;

                double inferredSwingDirection = angleDeg;

                // Validate the inferred direction by checking flow consistency with the last direction
                // Find the previous direction note before this group
                int prevNoteIndex = -1;
                for (int i = currentIndex - 1; i >= 0; i--)
                {
                    if (cubes[i].Direction != 8)
                    {
                        prevNoteIndex = i;
                        break;
                    }
                }

                if (prevNoteIndex >= 0)
                {
                    // Reverse the direction for each note between the arrow and current group
                    // to predict what direction we should have at this group
                    double predictedDirection = cubes[prevNoteIndex].Direction;
                    for (int i = prevNoteIndex; i < currentIndex; i++)
                    {
                        // Check if notes are close enough in time to maintain flow direction
                        if (i + 1 < cubes.Count && !AreNotesCloseInDepth(cubes[i], cubes[i + 1], bpm))
                        {
                            predictedDirection = ReverseCutDirection(predictedDirection);
                        }
                    }

                    // Compare predicted direction with inferred direction
                    // Allow ±90° tolerance for natural flow variation
                    double angleDiff = Math.Abs(Mod(inferredSwingDirection - predictedDirection + 180, 360) - 180);

                    // If the inferred direction doesn't align with flow, use the reverse direction
                    if (angleDiff > 90)
                    {
                        inferredSwingDirection = Mod(inferredSwingDirection + 180, 360);
                    }
                }

                // Return the entry direction (reverse of swing direction)
                return ReverseCutDirection(inferredSwingDirection);
            }

            // No direction available - use default based on position
            // If no previous notes, assume entry from bottom-center
            return cubes[currentIndex].Y >= 2 ? 270 : 90; // Default to down if high, up if low
        }

        private static (double x, double y) CalculateEntryPoint(List<Cube> cubes, int startIndex, double swingDirection)
        {
            if (startIndex > 0)
            {
                return SimSwingPos(cubes[startIndex - 1].X, cubes[startIndex - 1].Y, swingDirection);
            }
            else
            {
                return SimSwingPos(1.5, 1.0, swingDirection);
            }
        }

        private static void OrderSimultaneousNotesByDistance(List<Cube> cubes, int startIndex, int count, (double x, double y) entryPoint, double swingDirection)
        {
            if (count < 2)
            {
                return;
            }

            // Use the provided swing direction (which may be a snap angle for slanted windows)
            var notesWithOrder = new List<(Cube cube, double orderValue, int index)>();
            
            for (int i = 0; i < count; i++)
            {
                int cubeIndex = startIndex + i;
                Cube cube = cubes[cubeIndex];
                
                // Calculate distance from entry point along swing direction
                // Notes closer to entry point get lower values (hit first)
                double dx = cube.X - entryPoint.x;
                double dy = cube.Y - entryPoint.y;
                
                double radians = swingDirection * Math.PI / 180.0;
                double swingX = Math.Cos(radians);
                double swingY = Math.Sin(radians);
                
                // Project relative position onto swing direction
                double swingOrderValue = dx * swingX + dy * swingY;
                
                notesWithOrder.Add((cube, swingOrderValue, cubeIndex));
            }

            // Sort by swing order (lower values = earlier in swing path = hit first)
            notesWithOrder.Sort((a, b) => a.orderValue.CompareTo(b.orderValue));

            for (int i = 0; i < count; i++)
            {
                cubes[startIndex + i] = notesWithOrder[i].cube;
            }
        }

        /// <summary>
        /// For simultaneous notes with the same CutDirection, calculates and applies the geometric snap angle.
        /// In Beat Saber, when notes have the same CutDirection, they snap to align so a smooth linear swing
        /// can pass through both notes. The actual swing angle is determined by the position vector between them.
        /// This should be called AFTER initial Direction values have been set from CutDirection.
        /// </summary>
        public static void ApplySnapAnglesForSlantedWindows(List<Cube> cubes)
        {
            if (cubes.Count < 2)
            {
                return;
            }

            int i = 0;
            while (i < cubes.Count)
            {
                // Find groups of simultaneous notes
                float currentTime = cubes[i].Beat;
                int groupStart = i;
                int groupCount = 1;
                
                while (i + 1 < cubes.Count && Math.Abs(cubes[i + 1].Beat - currentTime) < 0.001f)
                {
                    groupCount++;
                    i++;
                }
                
                if (groupCount >= 2)
                {
                    ApplySnapAngleForGroup(cubes, groupStart, groupCount);
                }
                
                i++;
            }
        }

        /// <summary>
        /// Calculates the snap angle for a group of simultaneous notes with the same CutDirection.
        /// Returns the snap angle if found, otherwise returns the provided default swing direction.
        /// This should be called BEFORE ordering to get the correct angle for ordering.
        /// Uses the defaultSwingDirection (inferred from context) to determine which geometric angle to use.
        /// </summary>
        private static double CalculateSnapAngleForGroup(List<Cube> cubes, int startIndex, int count, double defaultSwingDirection)
        {
            // Group notes by CutDirection
            var cutDirectionGroups = new Dictionary<int, List<int>>();
            
            for (int i = 0; i < count; i++)
            {
                int cubeIndex = startIndex + i;
                int cutDir = cubes[cubeIndex].CutDirection;
                
                if (!cutDirectionGroups.ContainsKey(cutDir))
                {
                    cutDirectionGroups[cutDir] = new List<int>();
                }
                cutDirectionGroups[cutDir].Add(cubeIndex);
            }

            // Find the largest group with matching CutDirection (including dots if that's the largest group)
            List<int> largestGroup = null;
            int maxCount = 0;
            
            foreach (var group in cutDirectionGroups.Values)
            {
                if (group.Count > maxCount)
                {
                    maxCount = group.Count;
                    largestGroup = group;
                }
            }

            // If we found a group with 2+ notes, calculate snap angle
            if (largestGroup != null && largestGroup.Count >= 2)
            {
                // Calculate geometric angle from first to last note's position
                int firstIdx = largestGroup[0];
                int lastIdx = largestGroup[largestGroup.Count - 1];
                
                int lineDiff = cubes[lastIdx].X - cubes[firstIdx].X;
                int layerDiff = cubes[lastIdx].Y - cubes[firstIdx].Y;
                
                // If notes are at the same position, use the default direction
                if (lineDiff == 0 && layerDiff == 0)
                {
                    return defaultSwingDirection;
                }
                
                // Calculate angle from first to last
                double angleRadians = Math.Atan2(layerDiff, lineDiff);
                double angleDegrees = angleRadians * 180.0 / Math.PI;
                double geometricAngle = (angleDegrees + 360.0) % 360.0;
                
                // Calculate the reverse angle (opposite direction)
                double reverseAngle = Mod(geometricAngle + 180, 360);
                
                // Determine reference direction: prefer CutDirection if arrows exist, otherwise use flow
                double referenceDirection;
                int cutDir = cubes[firstIdx].CutDirection;
                
                if (cutDir != 8)
                {
                    // Notes have arrows - use their CutDirection as reference
                    referenceDirection = Mod(DirectionToDegree[cutDir] + cubes[firstIdx].AngleOffset, 360);
                }
                else
                {
                    // Notes are dots - use inferred direction from flow
                    referenceDirection = defaultSwingDirection;
                }
                
                // Choose between geometric and reverse angle based on which is closer to reference
                double diff1 = Math.Abs(Mod(geometricAngle - referenceDirection + 180, 360) - 180);
                double diff2 = Math.Abs(Mod(reverseAngle - referenceDirection + 180, 360) - 180);
                
                return diff1 < diff2 ? geometricAngle : reverseAngle;
            }

            return defaultSwingDirection;
        }

        /// <summary>
        /// Applies snap angle for a single group of simultaneous notes AFTER they've been ordered.
        /// Only applies if notes are aligned with their CutDirection (a true slanted window).
        /// </summary>
        private static void ApplySnapAngleForGroup(List<Cube> cubes, int startIndex, int count)
        {
            // Group notes by CutDirection
            var cutDirectionGroups = new Dictionary<int, List<int>>();
            
            for (int i = 0; i < count; i++)
            {
                int cubeIndex = startIndex + i;
                int cutDir = cubes[cubeIndex].CutDirection;
                
                // Only process arrow notes (not dots)
                if (cutDir == 8)
                {
                    continue;
                }
                
                if (!cutDirectionGroups.ContainsKey(cutDir))
                {
                    cutDirectionGroups[cutDir] = new List<int>();
                }
                cutDirectionGroups[cutDir].Add(cubeIndex);
            }

            // For each group with 2+ notes, calculate and apply snap angle
            foreach (var group in cutDirectionGroups.Values)
            {
                if (group.Count < 2)
                {
                    continue;
                }

                // After ordering, notes are in swing order (earlier notes first)
                // Calculate snap angle from FIRST to LAST to get the swing direction
                int firstIdx = group[0];
                int lastIdx = group[group.Count - 1];
                
                // Calculate angle FROM first TO last (swing direction)
                int lineDiff = cubes[lastIdx].X - cubes[firstIdx].X;
                int layerDiff = cubes[lastIdx].Y - cubes[firstIdx].Y;
                
                // Calculate geometric angle
                double angleRadians = Math.Atan2(layerDiff, lineDiff);
                double angleDegrees = angleRadians * 180.0 / Math.PI;
                double snapAngle = (angleDegrees + 360.0) % 360.0;
                
                // Get the CutDirection's angle
                double cutDirectionAngle = Mod(DirectionToDegree[cubes[firstIdx].CutDirection] + cubes[firstIdx].AngleOffset, 360);
                
                // Only apply snap angle if notes are roughly aligned with their CutDirection
                double angleDiff = Math.Abs(Mod(snapAngle - cutDirectionAngle + 180, 360) - 180);
                
                // Allow up to 45° deviation for slanted windows
                if (angleDiff <= 45)
                {
                    // Apply snap angle to all notes in this group
                    foreach (int idx in group)
                    {
                        cubes[idx].Direction = snapAngle;
                    }
                }
            }
        }
    }
}
