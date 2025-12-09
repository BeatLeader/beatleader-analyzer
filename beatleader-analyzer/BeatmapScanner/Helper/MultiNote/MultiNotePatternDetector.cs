using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNoteHitDetector;

namespace beatleader_analyzer.BeatmapScanner.Helper.MultiNote
{
    /// <summary>
    /// Detects and processes multi-note hit patterns in a sequence of notes.
    /// Handles simultaneous notes ordering and pattern flag updates.
    /// </summary>
    internal class MultiNotePatternDetector
    {
        /// <summary>
        /// Detects if two notes form a multi-note hit pattern and ensures they are in correct order.
        /// For simultaneous notes, orders them by their alignment with the swing direction.
        /// </summary>
        public static bool DetectAndOrderMultiNoteHit(List<Cube> cubes, int prevIndex, int currentIndex, float bpm)
        {
            bool needsReorder = false;
            
            if (prevIndex < 0 || currentIndex >= cubes.Count)
            {
                return false;
            }

            Cube prev = cubes[prevIndex];
            Cube current = cubes[currentIndex];

            // Check if this is a multi-note hit
            bool isMultiHit = IsMultiNoteHit(prev, current, bpm);
            
            if (!isMultiHit)
            {
                return false;
            }

            // Check if notes are simultaneous
            bool isSimultaneous = Math.Abs(prev.Beat - current.Beat) < 0.001f;
            
            if (isSimultaneous)
            {
                // For simultaneous notes, ensure they're in the correct order based on swing direction
                // The "first" note should be the one closer to where the swing starts
                
                // Determine swing direction with different strategies:
                // 1. One note has arrow: Blend arrow direction with geometric angle
                // 2. Both are dots: Infer from geometry and previous swing
                double swingDirection;
                
                bool prevHasArrow = prev.CutDirection != 8;
                bool currentHasArrow = current.CutDirection != 8;
                
                if (prevHasArrow || currentHasArrow)
                {
                    // At least one note has an arrow - blend arrow with geometry
                    double arrowDirection = prevHasArrow ? prev.Direction : current.Direction;
                    double geometricDirection = CalculateGeometricAngle(prev, current);
                    
                    // Blend the arrow direction with geometric direction (50/50)
                    swingDirection = BlendAngles(arrowDirection, geometricDirection, 0.5);
                }
                else
                {
                    // Both are dot notes - infer from position and flow
                    swingDirection = InferSwingDirectionForDots(cubes, prevIndex, currentIndex);
                }
                
                // Check if notes need to be reordered
                needsReorder = ShouldReorderSimultaneousNotes(prev, current, swingDirection);
                
                if (needsReorder)
                {
                    // Swap the notes in the list
                    cubes[prevIndex] = current;
                    cubes[currentIndex] = prev;
                }
            }

            return true;
        }

        /// <summary>
        /// Calculates the geometric angle between two notes based on their positions.
        /// </summary>
        private static double CalculateGeometricAngle(Cube first, Cube second)
        {
            int lineDiff = second.X - first.X;
            int layerDiff = second.Y - first.Y;
            
            double angleRad = Math.Atan2(layerDiff, lineDiff);
            double angleDeg = angleRad * 180.0 / Math.PI;
            
            // Normalize to [0, 360)
            if (angleDeg < 0) angleDeg += 360;
            
            return angleDeg;
        }

        /// <summary>
        /// Blends two angles, taking the shortest angular path between them.
        /// </summary>
        /// <param name="angle1">First angle in degrees</param>
        /// <param name="angle2">Second angle in degrees</param>
        /// <param name="weight">Weight for angle2 (0.0 = all angle1, 1.0 = all angle2, 0.5 = middle)</param>
        /// <returns>Blended angle in degrees [0, 360)</returns>
        private static double BlendAngles(double angle1, double angle2, double weight)
        {
            // Normalize both angles to [0, 360)
            angle1 = MathHelper.Helper.Mod(angle1, 360);
            angle2 = MathHelper.Helper.Mod(angle2, 360);
            
            // Calculate the shortest angular difference
            double diff = MathHelper.Helper.Mod(angle2 - angle1 + 180, 360) - 180;
            
            // Blend along the shortest path
            double blended = angle1 + diff * weight;
            
            // Normalize result to [0, 360)
            return MathHelper.Helper.Mod(blended, 360);
        }

        /// <summary>
        /// Infers the swing direction for dot notes based on:
        /// 1. The geometric angle needed to hit both notes smoothly
        /// 2. The previous swing direction (should flow naturally, often opposite)
        /// Used when both notes in a multi-hit are dot notes (cut direction 8).
        /// </summary>
        private static double InferSwingDirectionForDots(List<Cube> cubes, int firstIndex, int secondIndex)
        {
            Cube first = cubes[firstIndex];
            Cube second = cubes[secondIndex];
            
            // Calculate geometric angle from first to second note
            double geometricAngleDeg = CalculateGeometricAngle(first, second);
            
            // Try to find previous swing direction to maintain flow
            double? previousSwingDirection = null;
            
            // Look backwards for the last arrow note or established direction
            for (int i = firstIndex - 1; i >= 0; i--)
            {
                if (cubes[i].Direction != 8)
                {
                    previousSwingDirection = cubes[i].Direction;
                    break;
                }
            }
            
            // If we have a previous swing, the current swing should flow naturally
            // Often this means swinging in roughly the opposite direction
            if (previousSwingDirection.HasValue)
            {
                // Calculate the opposite direction (reverse flow)
                double oppositeDirection = MathHelper.Helper.Mod(previousSwingDirection.Value + 180, 360);
                
                // Check if geometric angle is reasonably close to the opposite direction
                // Allow ±90° tolerance for natural flow
                double angleDiff = Math.Abs(MathHelper.Helper.Mod(geometricAngleDeg - oppositeDirection + 180, 360) - 180);
                
                if (angleDiff <= 90)
                {
                    // Geometric angle aligns with natural flow, use it
                    return geometricAngleDeg;
                }
                else
                {
                    // Geometric angle doesn't align well, prefer flow direction
                    // But still influenced by geometry using proper angle blending
                    return BlendAngles(oppositeDirection, geometricAngleDeg, 0.3);
                }
            }
            
            // No previous direction available, use pure geometric angle
            return geometricAngleDeg;
        }

        /// <summary>
        /// Determines if two simultaneous notes should be reordered based on swing direction.
        /// The note that should be hit first (closer to swing entry) should come first in the list.
        /// </summary>
        private static bool ShouldReorderSimultaneousNotes(Cube first, Cube second, double swingDirection)
        {
            // Calculate which note is "earlier" in the swing path based on direction
            int lineDiff = second.X - first.X;
            int layerDiff = second.Y - first.Y;

            // Determine if the second note is "before" the first note in the swing direction
            // If so, they need to be swapped
            bool secondIsEarlier = swingDirection switch
            {
                // Up (90°): lower layer comes first
                >= 67.5 and <= 112.5 => layerDiff < 0,
                
                // Down (270°): higher layer comes first
                >= 247.5 and <= 292.5 => layerDiff > 0,
                
                // Left (180°): right line comes first
                >= 157.5 and <= 202.5 => lineDiff > 0,
                
                // Right (0°): left line comes first
                <= 22.5 or >= 337.5 => lineDiff < 0,
                
                // Up-Left (135°): lower-right comes first
                >= 112.5 and <= 157.5 => layerDiff < 0 || layerDiff == 0 && lineDiff > 0,
                
                // Up-Right (45°): lower-left comes first
                >= 22.5 and <= 67.5 => layerDiff < 0 || layerDiff == 0 && lineDiff < 0,
                
                // Down-Left (225°): upper-right comes first
                >= 202.5 and <= 247.5 => layerDiff > 0 || layerDiff == 0 && lineDiff > 0,
                
                // Down-Right (315°): upper-left comes first
                >= 292.5 and <= 337.5 => layerDiff > 0 || layerDiff == 0 && lineDiff < 0,
                
                _ => false
            };

            return secondIsEarlier;
        }

        /// <summary>
        /// Detects all multi-note hits in a group of simultaneous notes and orders them correctly.
        /// Returns indices of notes that form multi-note patterns.
        /// </summary>
        public static List<int> DetectMultiNoteGroup(List<Cube> cubes, int startIndex, float bpm)
        {
            var groupIndices = new List<int> { startIndex };
            
            // Find all simultaneous notes
            float baseTime = cubes[startIndex].Beat;
            for (int i = startIndex + 1; i < cubes.Count; i++)
            {
                if (Math.Abs(cubes[i].Beat - baseTime) < 0.001f)
                {
                    groupIndices.Add(i);
                }
                else
                {
                    break;
                }
            }

            if (groupIndices.Count < 2)
            {
                return new List<int>();
            }

            // Determine swing direction from the group
            double swingDirection = 270; // Default down
            int? arrowNoteIndex = null;
            
            // First, try to find an arrow note in the group
            foreach (int idx in groupIndices)
            {
                if (cubes[idx].CutDirection != 8)
                {
                    arrowNoteIndex = idx;
                    break;
                }
            }
            
            if (arrowNoteIndex.HasValue)
            {
                // Found an arrow - blend with geometric angle
                double arrowDirection = MathHelper.Helper.DirectionToDegree[cubes[arrowNoteIndex.Value].CutDirection] + 
                                       cubes[arrowNoteIndex.Value].AngleOffset;
                arrowDirection = MathHelper.Helper.Mod(arrowDirection, 360);
                
                // Calculate geometric angle across the group (first to last note)
                double geometricDirection = CalculateGeometricAngle(
                    cubes[groupIndices[0]], 
                    cubes[groupIndices[groupIndices.Count - 1]]
                );
                
                // Blend arrow direction with geometric direction (50/50)
                swingDirection = BlendAngles(arrowDirection, geometricDirection, 0.5);
            }
            else
            {
                // All notes are dots - infer direction from geometry and flow
                swingDirection = InferSwingDirectionForDots(cubes, groupIndices[0], groupIndices[groupIndices.Count - 1]);
            }

            // Check which notes form valid multi-note hits with each other
            var validPairs = new HashSet<(int, int)>();
            
            for (int i = 0; i < groupIndices.Count; i++)
            {
                for (int j = i + 1; j < groupIndices.Count; j++)
                {
                    int idx1 = groupIndices[i];
                    int idx2 = groupIndices[j];
                    
                    if (IsMultiNoteHit(cubes[idx1], cubes[idx2], bpm))
                    {
                        validPairs.Add((idx1, idx2));
                    }
                }
            }

            if (validPairs.Count == 0)
            {
                return new List<int>();
            }

            // Order the group based on swing direction
            var orderedGroup = groupIndices
                .Select(idx => new { Index = idx, Cube = cubes[idx] })
                .OrderBy(item => GetSwingOrderValue(item.Cube, swingDirection))
                .Select(item => item.Index)
                .ToList();

            return orderedGroup;
        }

        /// <summary>
        /// Calculates a sort value for ordering notes in swing direction.
        /// Lower values mean the note is earlier in the swing path.
        /// </summary>
        private static double GetSwingOrderValue(Cube cube, double swingDirection)
        {
            double radians = swingDirection * Math.PI / 180.0;
            double swingX = Math.Cos(radians);
            double swingY = Math.Sin(radians);
            
            // Project the note position onto the swing direction vector
            // This gives us how far along the swing path the note is
            double projection = cube.X * swingX + cube.Y * swingY;
            
            // Negate because we want notes "before" in the swing to sort first
            return -projection;
        }

        /// <summary>
        /// Updates pattern flags for a multi-note hit continuation.
        /// </summary>
        public static void MarkAsMultiNotePattern(List<Cube> cubes, int currentIndex, int prevIndex)
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
            
            
            // Only mark as Tail if we're sure this is the last note in the pattern
            // Check if there are more simultaneous notes that could be part of this pattern
            bool isLastInPattern = true;
            if (currentIndex + 1 < cubes.Count)
            {
                float currentTime = cubes[currentIndex].Beat;
                int currentType = cubes[currentIndex].Type;
                
                // Find all remaining simultaneous notes of the same type
                for (int i = currentIndex + 1; i < cubes.Count; i++)
                {
                    if (Math.Abs(cubes[i].Beat - currentTime) >= 0.001f)
                    {
                        // No more simultaneous notes
                        break;
                    }
                    
                    if (cubes[i].Type != currentType)
                    {
                        // Different hand
                        continue;
                    }
                    
                    // Check if this note forms a valid multi-note hit with ANY note in the current pattern
                    // Multi-note hits don't require adjacency (windows can have gaps, for example)
                    bool isPartOfPattern = false;
                    
                    for (int j = prevIndex; j <= currentIndex; j++)
                    {
                        if (!cubes[j].Pattern || cubes[j].Type != currentType)
                        {
                            continue;
                        }
                        
                        // Use the same multi-note hit detection logic used elsewhere
                        // Notes are simultaneous (already verified above) and same type
                        // So we just need to check if they could form a valid multi-note hit
                        if (IsMultiNoteHit(cubes[j], cubes[i], 0)) // BPM doesn't matter for simultaneous notes
                        {
                            isPartOfPattern = true;
                            break;
                        }
                    }
                    
                    if (isPartOfPattern)
                    {
                        isLastInPattern = false;
                        break;
                    }
                }
            }
            
            if (isLastInPattern)
            {
                cubes[currentIndex].Tail = true;
            }
        }
    }
}
