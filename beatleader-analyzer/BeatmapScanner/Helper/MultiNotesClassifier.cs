using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using static Analyzer.BeatmapScanner.Helper.MultiNoteHitDetector;

namespace Analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Classifies multi-note hits into specific categories (Stack, Tower, Slider, Window, etc.)
    /// for statistical tracking and analysis.
    /// </summary>
    internal class MultiNotesClassifier
    {
        /// <summary>
        /// Time tolerance (in beats) for considering notes "simultaneous".
        /// Notes within this time window are considered to occur at the same time.
        /// </summary>
        private const float SIMULTANEOUS_TIME_TOLERANCE = 0.001f;

        /// <summary>
        /// Analyzes a list of cubes and counts specific multi-note hit types.
        /// </summary>
        /// <param name="cubes">List of notes (cubes) for a single hand, sorted by time.</param>
        /// <param name="bpm">Beats per minute of the song.</param>
        /// <returns>PatternStatistics with counts of each multi-note hit type.</returns>
        public static Statistics AnalyzeMultiNotes(List<Cube> cubes, float bpm)
        {
            var stats = new Statistics();

            if (cubes.Count < 2)
            {
                return stats;
            }

            // Track which notes have been processed to avoid double-counting
            var processed = new HashSet<int>();

            for (int i = 0; i < cubes.Count; i++)
            {
                if (processed.Contains(i) || !cubes[i].Head)
                {
                    continue; // Skip if already processed or not a multi-note hit head
                }

                // Collect all notes in this multi-note hit
                var patternNotes = new List<int> { i };
                for (int j = i + 1; j < cubes.Count; j++)
                {
                    if (cubes[j].Pattern && !cubes[j].Head)
                    {
                        patternNotes.Add(j);
                        if (cubes[j].Tail)
                        {
                            break; // End of multi-note hit
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // Need at least 2 notes for a multi-note hit
                if (patternNotes.Count < 2)
                {
                    continue;
                }

                // Mark all notes as processed
                foreach (int idx in patternNotes)
                {
                    processed.Add(idx);
                }

                // Classify the multi-note hit
                ClassifyPattern(cubes, patternNotes, stats, bpm);
            }

            return stats;
        }

        /// <summary>
        /// Classifies a specific multi-note hit and updates statistics.
        /// </summary>
        private static void ClassifyPattern(List<Cube> cubes, List<int> patternIndices, Statistics stats, float bpm)
        {
            int noteCount = patternIndices.Count;
            
            // Check if all notes are simultaneous (within tolerance)
            bool isSimultaneous = AreNotesSimultaneous(cubes, patternIndices);

            if (isSimultaneous)
            {
                // Simultaneous multi-note hits: Stack, Tower, Window, or Loloppe
                if (noteCount == 2)
                {
                    // Could be Stack, Window, or Loloppe
                    if (IsStack(cubes, patternIndices))
                    {
                        stats.Stacks++;
                    }
                    else if (IsWindow(cubes, patternIndices))
                    {
                        stats.Windows++;
                    }
                    else if (IsLoloppe(cubes, patternIndices))
                    {
                        stats.Loloppes++;
                    }
                }
                else if (noteCount == 3)
                {
                    // Could be Tower
                    if (IsTower(cubes, patternIndices))
                    {
                        stats.Towers++;
                    }
                }
            }
            else
            {
                // Non-simultaneous multi-note hits: Slider or Curved Slider
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
        /// Checks if all notes in a multi-note hit occur at the same time (within tolerance).
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
        /// Checks if two notes have the same CutDirection (ignoring AngleOffset).
        /// In Beat Saber, notes with the same CutDirection snap to align based on their positions,
        /// creating a "slanted window" effect where the actual swing angle is calculated from positions.
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
        /// Calculates the snap angle for notes based on their position vector.
        /// When two notes have the same CutDirection, Beat Saber snaps them to align
        /// so a smooth linear swing can pass through both notes.
        /// </summary>
        private static double CalculateSnapAngle(Cube note1, Cube note2)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            // Calculate angle from note1 to note2
            double angleRadians = Math.Atan2(layerDiff, lineDiff);
            double angleDegrees = angleRadians * 180.0 / Math.PI;

            // Normalize to 0-360 range
            return (angleDegrees + 360.0) % 360.0;
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

            bool isAdjacent = (lineDiff == 1 && layerDiff == 0) || (lineDiff == 0 && layerDiff == 1);
            
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

            bool verticalLine = (note1.Line == note2.Line && note2.Line == note3.Line);
            bool horizontalLine = (note1.Layer == note2.Layer && note2.Layer == note3.Layer);

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

            // Check if vectors are collinear (cross product == 0) and each step is diagonal (distance = sqrt(2))
            bool isCollinear = (vec1X * vec2Y - vec1Y * vec2X) == 0;
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

            double direction = note1.Direction != 8 ? note1.Direction : note2.Direction;
            if (direction == 8) return false;

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            bool isAdjacent = (lineDiff == 1 && layerDiff == 0) || (lineDiff == 0 && layerDiff == 1);
            if (!isAdjacent) return false;

            return IsPerpendicularToSwingDirection(note1, note2, direction);
        }

        /// <summary>
        /// Checks if two notes form a Window (notes with gaps, aligned in swing direction).
        /// Supports "slanted windows" where notes with same CutDirection snap to position-based angles.
        /// </summary>
        private static bool IsWindow(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 2) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            bool hasGap = (lineDiff >= 2 && layerDiff == 0) || (lineDiff == 0 && layerDiff >= 2);
            if (!hasGap) return false;

            if (HaveSameCutDirection(note1, note2))
            {
                double snapAngle = CalculateSnapAngle(note1, note2);
                return IsAlignedInSwingDirection(note1, note2, snapAngle);
            }

            double direction = note1.Direction != 8 ? note1.Direction : note2.Direction;
            if (direction == 8) return false;

            return IsAlignedInSwingDirection(note1, note2, direction);
        }

        /// <summary>
        /// Checks if a slider is curved (not all notes are collinear).
        /// </summary>
        private static bool IsCurvedSlider(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count < 3) return false;

            bool allLinear = true;
            
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
                    allLinear = false;
                    break;
                }
            }

            return !allLinear;
        }

        private static bool IsAlignedInSwingDirection(Cube note1, Cube note2)
        {
            double direction = note1.Direction != 8 ? note1.Direction : note2.Direction;
            if (direction == 8) return true;

            return IsAlignedInSwingDirection(note1, note2, direction);
        }

        private static bool IsAlignedInSwingDirection(Cube note1, Cube note2, double direction)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            return direction switch
            {
                >= 67.5 and <= 112.5 => layerDiff > 0,
                >= 247.5 and <= 292.5 => layerDiff < 0,
                >= 157.5 and <= 202.5 => lineDiff < 0,
                <= 22.5 or >= 337.5 => lineDiff > 0,
                >= 112.5 and <= 157.5 => layerDiff >= 0 || lineDiff <= 0,
                >= 22.5 and <= 67.5 => layerDiff >= 0 || lineDiff >= 0,
                >= 202.5 and <= 247.5 => layerDiff <= 0 || lineDiff <= 0,
                >= 292.5 and <= 337.5 => layerDiff <= 0 || lineDiff >= 0,
                _ => false
            };
        }

        /// <summary>
        /// Checks if note2 is perpendicular to the swing direction (dot product near zero).
        /// </summary>
        private static bool IsPerpendicularToSwingDirection(Cube note1, Cube note2, double direction)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            double radians = direction * Math.PI / 180.0;
            double swingDirX = Math.Cos(radians);
            double swingDirY = Math.Sin(radians);

            double posVectorX = lineDiff;
            double posVectorY = layerDiff;

            double dotProduct = swingDirX * posVectorX + swingDirY * posVectorY;

            const double tolerance = 0.5;
            return Math.Abs(dotProduct) < tolerance;
        }
    }
}
