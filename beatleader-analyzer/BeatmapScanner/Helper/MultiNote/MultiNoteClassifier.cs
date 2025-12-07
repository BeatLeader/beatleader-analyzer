using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Helper.MultiNote
{
    /// <summary>
    /// Unified classifier for multi-note hits that handles both counting (for statistics) and labeling (for swing data).
    /// Provides single source of truth for all multi-note hit type classification logic.
    /// </summary>
    internal class MultiNoteClassifier
    {
        /// <summary>
        /// Time tolerance (in beats) for considering notes "simultaneous".
        /// </summary>
        private const float SIMULTANEOUS_TIME_TOLERANCE = 0.001f;

        /// <summary>
        /// Analyzes cubes and returns statistics with counts of each multi-note hit type.
        /// </summary>
        public static Statistics CountMultiNoteHits(List<Cube> cubes, float bpm)
        {
            var stats = new Statistics();

            if (cubes.Count < 2)
            {
                return stats;
            }

            var processed = new HashSet<int>();

            for (int i = 0; i < cubes.Count; i++)
            {
                if (processed.Contains(i) || !cubes[i].Head)
                {
                    continue;
                }

                var patternNotes = CollectPatternNotes(cubes, i);

                if (patternNotes.Count < 2)
                {
                    continue;
                }

                foreach (int idx in patternNotes)
                {
                    processed.Add(idx);
                }

                ClassifyAndCountMultiNoteHit(cubes, patternNotes, stats);
            }

            return stats;
        }

        /// <summary>
        /// Assigns multi-note hit type labels to swings based on their constituent notes.
        /// </summary>
        public static void LabelSwingMultiNoteHits(List<Cube> cubes, List<SwingData> swingData, float bpm)
        {
            if (cubes.Count < 2 || swingData.Count == 0)
            {
                return;
            }

            var cubeToSwing = BuildCubeToSwingMap(cubes, swingData);
            var processed = new HashSet<int>();

            for (int i = 0; i < cubes.Count; i++)
            {
                if (processed.Contains(i) || !cubes[i].Head)
                {
                    continue;
                }

                var patternNotes = CollectPatternNotes(cubes, i);

                if (patternNotes.Count < 2)
                {
                    continue;
                }

                foreach (int idx in patternNotes)
                {
                    processed.Add(idx);
                }

                string patternType = DetermineMultiNoteHitType(cubes, patternNotes);

                if (cubeToSwing.TryGetValue(cubes[i].Time, out var swing))
                {
                    swing.PatternType = patternType;
                }
            }
        }

        /// <summary>
        /// Collects all notes in a multi-note hit pattern starting from the given index.
        /// </summary>
        private static List<int> CollectPatternNotes(List<Cube> cubes, int startIndex)
        {
            var patternNotes = new List<int> { startIndex };
            
            for (int j = startIndex + 1; j < cubes.Count; j++)
            {
                if (cubes[j].Pattern && !cubes[j].Head)
                {
                    patternNotes.Add(j);
                    if (cubes[j].Tail)
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            return patternNotes;
        }

        /// <summary>
        /// Builds a map from cube time to swing data for labeling.
        /// </summary>
        private static Dictionary<float, SwingData> BuildCubeToSwingMap(List<Cube> cubes, List<SwingData> swingData)
        {
            var cubeToSwing = new Dictionary<float, SwingData>();
            int swingIndex = 0;

            for (int i = 0; i < cubes.Count; i++)
            {
                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    if (swingIndex < swingData.Count)
                    {
                        cubeToSwing[cubes[i].Time] = swingData[swingIndex];
                        swingIndex++;
                    }
                }
            }

            return cubeToSwing;
        }

        /// <summary>
        /// Classifies a multi-note hit and increments the appropriate statistics counter.
        /// </summary>
        private static void ClassifyAndCountMultiNoteHit(List<Cube> cubes, List<int> patternIndices, Statistics stats)
        {
            int noteCount = patternIndices.Count;
            bool isSimultaneous = AreNotesSimultaneous(cubes, patternIndices);

            if (isSimultaneous)
            {
                if (IsWindow(cubes, patternIndices, out bool isSlanted))
                {
                    if (isSlanted)
                    {
                        stats.SlantedWindows++;
                    }
                    else
                    {
                        stats.Windows++;
                    }
                }
                else if (IsTower(cubes, patternIndices))
                {
                    stats.Towers++;
                }
                else if (IsStack(cubes, patternIndices))
                {
                    stats.Stacks++;
                }
                else if (IsLoloppe(cubes, patternIndices))
                {
                    stats.Loloppes++;
                }
            }
            else
            {
                if (noteCount >= 2)
                {
                    if (IsCurvedSlider(cubes, patternIndices))
                    {
                        stats.CurvedSliders++;
                    }
                    else
                    {
                        stats.Sliders++;
                    }
                }
            }
        }

        /// <summary>
        /// Determines the specific multi-note hit type and returns its string label.
        /// </summary>
        private static string DetermineMultiNoteHitType(List<Cube> cubes, List<int> patternIndices)
        {
            int noteCount = patternIndices.Count;
            bool isSimultaneous = AreNotesSimultaneous(cubes, patternIndices);

            if (isSimultaneous)
            {
                if (noteCount == 2)
                {
                    if (IsStack(cubes, patternIndices))
                    {
                        return "Stack";
                    }
                    else if (IsWindow(cubes, patternIndices, out bool isSlanted))
                    {
                        return isSlanted ? "Slanted Window" : "Window";
                    }
                    else if (IsLoloppe(cubes, patternIndices))
                    {
                        return "Loloppe";
                    }
                }
                else if (noteCount == 3)
                {
                    if (IsTower(cubes, patternIndices))
                    {
                        return "Tower";
                    }
                }
                return "Multi-Note";
            }
            else
            {
                if (noteCount >= 2)
                {
                    if (IsCurvedSlider(cubes, patternIndices))
                    {
                        return "Curved Slider";
                    }
                    else
                    {
                        return "Slider";
                    }
                }
            }

            return "Pattern";
        }

        #region Classification Methods

        /// <summary>
        /// Checks if all notes occur at the same time (within tolerance).
        /// </summary>
        private static bool AreNotesSimultaneous(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count < 2) return false;

            float firstTime = cubes[indices[0]].Time;
            for (int i = 1; i < indices.Count; i++)
            {
                if (Math.Abs(cubes[indices[i]].Time - firstTime) > SIMULTANEOUS_TIME_TOLERANCE)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks if two adjacent notes form a Stack (aligned in swing direction).
        /// </summary>
        private static bool IsStack(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 2) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            bool isAdjacent = lineDiff == 1 && layerDiff == 0 || lineDiff == 0 && layerDiff == 1;

            if (!isAdjacent) return false;

            return IsAlignedInSwingDirection(note1, note2);
        }

        /// <summary>
        /// Checks if three consecutive notes form a Tower (vertical, horizontal, or diagonal line).
        /// </summary>
        private static bool IsTower(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 3) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];
            Cube note3 = cubes[indices[2]];

            bool verticalLine = note1.Line == note2.Line && note2.Line == note3.Line;
            bool horizontalLine = note1.Layer == note2.Layer && note2.Layer == note3.Layer;

            if (verticalLine)
            {
                var layers = new[] { note1.Layer, note2.Layer, note3.Layer };
                Array.Sort(layers);
                return layers[1] - layers[0] == 1 && layers[2] - layers[1] == 1;
            }

            if (horizontalLine)
            {
                var lines = new[] { note1.Line, note2.Line, note3.Line };
                Array.Sort(lines);
                return lines[1] - lines[0] == 1 && lines[2] - lines[1] == 1;
            }

            // Check for diagonal tower
            int vec1X = note2.Line - note1.Line;
            int vec1Y = note2.Layer - note1.Layer;
            int vec2X = note3.Line - note2.Line;
            int vec2Y = note3.Layer - note2.Layer;

            bool isCollinear = vec1X * vec2Y - vec1Y * vec2X == 0;
            bool isDiagonal1 = Math.Abs(vec1X) == 1 && Math.Abs(vec1Y) == 1;
            bool isDiagonal2 = Math.Abs(vec2X) == 1 && Math.Abs(vec2Y) == 1;
            bool sameDirection = vec1X == vec2X && vec1Y == vec2Y;

            return isCollinear && isDiagonal1 && isDiagonal2 && sameDirection;
        }

        /// <summary>
        /// Checks if two adjacent notes form a Loloppe (perpendicular to swing direction).
        /// </summary>
        private static bool IsLoloppe(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 2) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];

            if (note1.CutDirection == 8 || note2.CutDirection == 8) return false;

            double direction = note1.Direction != 8 ? note1.Direction : note2.Direction;

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            bool isAdjacent = lineDiff == 1 && layerDiff == 0 || lineDiff == 0 && layerDiff == 1;
            if (!isAdjacent) return false;

            return IsPerpendicularToSwingDirection(note1, note2, direction);
        }

        /// <summary>
        /// Checks if two notes form a Window (notes with gaps, aligned in swing direction).
        /// Regular windows: Notes directly aligned with their CutDirection or with similar directions.
        /// Slanted windows: Notes with same CutDirection but snap angle differs from CutDirection.
        /// </summary>
        private static bool IsWindow(List<Cube> cubes, List<int> indices, out bool isSlanted)
        {
            isSlanted = false;

            if (indices.Count != 2) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            // Must have at least one gap (distance >= 2 in at least one direction)
            bool hasGap = lineDiff >= 2 && layerDiff == 0 ||
                         lineDiff == 0 && layerDiff >= 2 ||
                         lineDiff >= 1 && layerDiff >= 1 && lineDiff + layerDiff >= 3;

            if (!hasGap)
            {
                return false;
            }

            // Check if notes have same CutDirection (potential slanted window)
            if (HaveSameCutDirection(note1, note2))
            {
                double snapAngle = CalculateSnapAngle(note1, note2);

                // Get the CutDirection's intended angle
                double cutDirectionAngle = MathHelper.Helper.DirectionToDegree[note1.CutDirection] + note1.AngleOffset;
                cutDirectionAngle = (cutDirectionAngle + 360.0) % 360.0;

                // Calculate angular difference (shortest path)
                double angleDiff = Math.Abs(snapAngle - cutDirectionAngle);
                if (angleDiff > 180) angleDiff = 360 - angleDiff;

                // Slanted only if snap angle differs from CutDirection (not directly aligned)
                bool isDirectlyAligned = angleDiff == 0;

                if (IsAlignedInSwingDirection(note1, note2, snapAngle))
                {
                    if (!isDirectlyAligned)
                    {
                        isSlanted = true;
                        return true;
                    }
                }
            }

            // Regular window - check if directions are similar (within tolerance)
            isSlanted = false;
            return IsAlignedInSwingDirection(note1, note2);
        }

        /// <summary>
        /// Checks if a slider is curved (not all notes are collinear).
        /// </summary>
        private static bool IsCurvedSlider(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count < 3) return false;

            for (int i = 1; i < indices.Count - 1; i++)
            {
                Cube prev = cubes[indices[i - 1]];
                Cube curr = cubes[indices[i]];
                Cube next = cubes[indices[i + 1]];

                int vec1X = curr.Line - prev.Line;
                int vec1Y = curr.Layer - prev.Layer;
                int vec2X = next.Line - curr.Line;
                int vec2Y = next.Layer - curr.Layer;

                int crossProduct = vec1X * vec2Y - vec1Y * vec2X;
                if (crossProduct != 0)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Checks if two notes have the same CutDirection (ignoring AngleOffset).
        /// </summary>
        private static bool HaveSameCutDirection(Cube note1, Cube note2)
        {
            // Both must have arrow directions (not dots)
            if (note1.CutDirection == 8 || note2.CutDirection == 8)
            {
                return false;
            }

            return note1.CutDirection == note2.CutDirection;
        }

        /// <summary>
        /// Calculates the snap angle based on note positions.
        /// </summary>
        private static double CalculateSnapAngle(Cube note1, Cube note2)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            double angleRadians = Math.Atan2(layerDiff, lineDiff);
            double angleDegrees = angleRadians * 180.0 / Math.PI;

            return (angleDegrees + 360.0) % 360.0;
        }

        /// <summary>
        /// Checks if notes are aligned in swing direction (order-independent).
        /// </summary>
        private static bool IsAlignedInSwingDirection(Cube note1, Cube note2)
        {
            double direction = note1.CutDirection != 8 ? note1.Direction : note2.Direction;
            if (direction == 8) return true;

            return IsAlignedInSwingDirection(note1, note2, direction);
        }

        /// <summary>
        /// Checks if notes are aligned in the given swing direction (order-independent).
        /// </summary>
        private static bool IsAlignedInSwingDirection(Cube note1, Cube note2, double direction)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            return direction switch
            {
                // Up (90°): notes vertically aligned, layer difference exists
                >= 67.5 and <= 112.5 => lineDiff == 0 && layerDiff != 0,

                // Down (270°): notes vertically aligned, layer difference exists
                >= 247.5 and <= 292.5 => lineDiff == 0 && layerDiff != 0,

                // Left (180°): notes horizontally aligned, line difference exists
                >= 157.5 and <= 202.5 => layerDiff == 0 && lineDiff != 0,

                // Right (0°): notes horizontally aligned, line difference exists
                <= 22.5 or >= 337.5 => layerDiff == 0 && lineDiff != 0,

                // Diagonal directions: both line and layer must differ in correct quadrant
                // Up-Left (135°)
                >= 112.5 and <= 157.5 => (layerDiff > 0 && lineDiff < 0) || (layerDiff < 0 && lineDiff > 0),

                // Up-Right (45°)
                >= 22.5 and <= 67.5 => (layerDiff > 0 && lineDiff > 0) || (layerDiff < 0 && lineDiff < 0),

                // Down-Left (225°)
                >= 202.5 and <= 247.5 => (layerDiff < 0 && lineDiff < 0) || (layerDiff > 0 && lineDiff > 0),

                // Down-Right (315°)
                >= 292.5 and <= 337.5 => (layerDiff < 0 && lineDiff > 0) || (layerDiff > 0 && lineDiff < 0),

                _ => false
            };
        }

        /// <summary>
        /// Checks if note2 is perpendicular to the swing direction.
        /// </summary>
        private static bool IsPerpendicularToSwingDirection(Cube note1, Cube note2, double direction)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            double radians = direction * Math.PI / 180.0;
            double swingDirX = Math.Cos(radians);
            double swingDirY = Math.Sin(radians);

            double dotProduct = swingDirX * lineDiff + swingDirY * layerDiff;

            const double tolerance = 0.5;
            return Math.Abs(dotProduct) < tolerance;
        }

        #endregion
    }
}
