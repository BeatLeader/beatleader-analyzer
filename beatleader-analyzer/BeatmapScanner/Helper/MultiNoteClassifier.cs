using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper
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
        /// Analyzes swing data and returns statistics with counts of each multi-note hit type.
        /// Also labels each swing with its pattern type.
        /// </summary>
        public static Statistics CountMultiNoteHits(List<SwingData> swingData)
        {
            var stats = new Statistics();

            if (swingData == null || swingData.Count == 0)
            {
                return stats;
            }

            foreach (var swing in swingData)
            {
                if (swing.Cubes.Count < 2)
                {
                    continue;
                }

                ClassifyAndCountMultiNoteHit(swing, stats);
            }

            return stats;
        }

        /// <summary>
        /// Assigns multi-note hit type labels to swings based on their constituent notes.
        /// </summary>
        public static void LabelSwingMultiNoteHits(List<SwingData> swingData)
        {
            if (swingData == null || swingData.Count == 0)
            {
                return;
            }

            foreach (var swing in swingData)
            {
                if (swing.Cubes.Count < 2)
                {
                    swing.PatternType = "Single";
                    continue;
                }

                swing.PatternType = DetermineMultiNoteHitType(swing);
            }
        }

        /// <summary>
        /// Classifies a multi-note hit and increments the appropriate statistics counter.
        /// </summary>
        private static void ClassifyAndCountMultiNoteHit(SwingData swing, Statistics stats)
        {
            int noteCount = swing.Cubes.Count;
            bool isSimultaneous = AreNotesSimultaneous(swing);

            if (isSimultaneous)
            {
                bool isWindow = IsWindow(swing, out bool isSlanted);
                bool isTower = IsTower(swing);
                bool isStack = IsStack(swing);
                
                if (isWindow)
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
                else if (isTower)
                {
                    stats.Towers++;
                }
                else if (isStack)
                {
                    stats.Stacks++;
                }
            }
            else
            {
                if (noteCount >= 2)
                {
                    if (IsCurvedSlider(swing))
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
        private static string DetermineMultiNoteHitType(SwingData swing)
        {
            int noteCount = swing.Cubes.Count;
            bool isSimultaneous = AreNotesSimultaneous(swing);

            if (isSimultaneous)
            {
                if (noteCount == 2)
                {
                    if (IsStack(swing))
                    {
                        return "Stack";
                    }
                    else if (IsWindow(swing, out bool isSlanted))
                    {
                        return isSlanted ? "Slanted Window" : "Window";
                    }
                }
                else if (noteCount >= 3)
                {
                    if (IsTower(swing))
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
                    if (IsCurvedSlider(swing))
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
        /// Checks if all notes in a swing occur at the same time (within tolerance).
        /// </summary>
        private static bool AreNotesSimultaneous(SwingData swing)
        {
            if (swing.Cubes.Count < 2) return false;

            float firstTime = swing.Cubes[0].BpmTime;
            for (int i = 1; i < swing.Cubes.Count; i++)
            {
                if (Math.Abs(swing.Cubes[i].BpmTime - firstTime) > SIMULTANEOUS_TIME_TOLERANCE)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Checks if two adjacent notes form a Stack (aligned in swing direction).
        /// Includes orthogonally adjacent (horizontal/vertical) and diagonally adjacent notes.
        /// </summary>
        private static bool IsStack(SwingData swing)
        {
            if (swing.Cubes.Count != 2) return false;

            Cube note1 = swing.Cubes[0];
            Cube note2 = swing.Cubes[1];

            int lineDiff = Math.Abs(note1.X - note2.X);
            int layerDiff = Math.Abs(note1.Y - note2.Y);

            // Check for adjacency: orthogonal (h/v) or diagonal
            bool isOrthogonallyAdjacent = lineDiff == 1 && layerDiff == 0 || lineDiff == 0 && layerDiff == 1;
            bool isDiagonallyAdjacent = lineDiff == 1 && layerDiff == 1;
            bool isAdjacent = isOrthogonallyAdjacent || isDiagonallyAdjacent;

            if (!isAdjacent) return false;

            return IsAlignedInSwingDirection(note1, note2, swing.Direction);
        }

        /// <summary>
        /// Checks if three consecutive notes form a Tower (vertical, horizontal, or diagonal line).
        /// </summary>
        private static bool IsTower(SwingData swing)
        {
            if (swing.Cubes.Count != 3) return false;

            Cube note1 = swing.Cubes[0];
            Cube note2 = swing.Cubes[1];
            Cube note3 = swing.Cubes[2];

            bool verticalLine = note1.X == note2.X && note2.X == note3.X;
            bool horizontalLine = note1.Y == note2.Y && note2.Y == note3.Y;

            if (verticalLine)
            {
                var layers = new[] { note1.Y, note2.Y, note3.Y };
                Array.Sort(layers);
                return layers[1] - layers[0] == 1 && layers[2] - layers[1] == 1;
            }

            if (horizontalLine)
            {
                var lines = new[] { note1.X, note2.X, note3.X };
                Array.Sort(lines);
                return lines[1] - lines[0] == 1 && lines[2] - lines[1] == 1;
            }

            // Check for diagonal tower
            int vec1X = note2.X - note1.X;
            int vec1Y = note2.Y - note1.Y;
            int vec2X = note3.X - note2.X;
            int vec2Y = note3.Y - note2.Y;

            bool isCollinear = vec1X * vec2Y - vec1Y * vec2X == 0;
            bool isDiagonal1 = Math.Abs(vec1X) == 1 && Math.Abs(vec1Y) == 1;
            bool isDiagonal2 = Math.Abs(vec2X) == 1 && Math.Abs(vec2Y) == 1;
            bool sameDirection = vec1X == vec2X && vec1Y == vec2Y;

            return isCollinear && isDiagonal1 && isDiagonal2 && sameDirection;
        }

        /// <summary>
        /// Checks if two notes form a Window (notes with gaps, aligned in swing direction).
        /// Regular windows: Notes directly aligned with their CutDirection or with similar directions.
        /// Slanted windows: Notes with same CutDirection but snap angle differs from CutDirection.
        /// </summary>
        private static bool IsWindow(SwingData swing, out bool isSlanted)
        {
            isSlanted = false;

            if (swing.Cubes.Count != 2) return false;

            Cube note1 = swing.Cubes[0];
            Cube note2 = swing.Cubes[1];

            int lineDiff = Math.Abs(note1.X - note2.X);
            int layerDiff = Math.Abs(note1.Y - note2.Y);

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
                double cutDirectionAngle = Common.DirectionToDegree[note1.CutDirection] + note1.AngleOffset;
                cutDirectionAngle = (cutDirectionAngle + 360.0) % 360.0;

                // Calculate angular difference (shortest path)
                double angleDiff = Math.Abs(snapAngle - cutDirectionAngle);
                if (angleDiff > 180) angleDiff = 360 - angleDiff;

                // Check if snap angle is directly aligned with CutDirection
                bool isDirectlyAligned = angleDiff < 5.0; // Small tolerance for floating point
                
                // Check if notes are at pure cardinal or diagonal positions
                // Pure cardinal: exactly horizontal or vertical (one diff is 0)
                // Pure diagonal: line diff equals layer diff (e.g., (0,0) to (2,2))
                bool isCardinalOrPureDiagonal = lineDiff == 0 || layerDiff == 0 ||  // Cardinal
                                                lineDiff == layerDiff;                // Pure diagonal

                // Slanted window: BOTH conditions must be true:
                // 1. Snap angle differs from CutDirection (not directly aligned)
                // 2. Notes are NOT at cardinal/pure diagonal positions
                if (!isDirectlyAligned && !isCardinalOrPureDiagonal && IsAlignedInSwingDirection(note1, note2, swing.Direction))
                {
                    isSlanted = true;
                    return true;
                }
                
                // If at cardinal/diagonal positions OR directly aligned, it's a regular window (if aligned)
                if (IsAlignedInSwingDirection(note1, note2, swing.Direction))
                {
                    isSlanted = false;
                    return true;
                }
            }

            // Regular window - check if directions are similar (within tolerance)
            isSlanted = false;
            return IsAlignedInSwingDirection(note1, note2, swing.Direction);
        }

        /// <summary>
        /// Checks if a slider is curved (not all notes are collinear).
        /// </summary>
        private static bool IsCurvedSlider(SwingData swing)
        {
            if (swing.Cubes.Count < 3) return false;

            for (int i = 1; i < swing.Cubes.Count - 1; i++)
            {
                Cube prev = swing.Cubes[i - 1];
                Cube curr = swing.Cubes[i];
                Cube next = swing.Cubes[i + 1];

                int vec1X = curr.X - prev.X;
                int vec1Y = curr.Y - prev.Y;
                int vec2X = next.X - curr.X;
                int vec2Y = next.Y - curr.Y;

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
            int lineDiff = note2.X - note1.X;
            int layerDiff = note2.Y - note1.Y;

            double angleRadians = Math.Atan2(layerDiff, lineDiff);
            double angleDegrees = angleRadians * 180.0 / Math.PI;

            return (angleDegrees + 360.0) % 360.0;
        }

        /// <summary>
        /// Checks if notes are aligned in the given swing direction (order-independent).
        /// For dot notes or uncertain cases, returns true (lenient).
        /// </summary>
        private static bool IsAlignedInSwingDirection(Cube note1, Cube note2, double direction)
        {
            int lineDiff = note2.X - note1.X;
            int layerDiff = note2.Y - note1.Y;

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
                >= 112.5 and <= 157.5 => layerDiff > 0 && lineDiff < 0 || layerDiff < 0 && lineDiff > 0,

                // Up-Right (45°)
                >= 22.5 and <= 67.5 => layerDiff > 0 && lineDiff > 0 || layerDiff < 0 && lineDiff < 0,

                // Down-Left (225°)
                >= 202.5 and <= 247.5 => layerDiff < 0 && lineDiff < 0 || layerDiff > 0 && lineDiff > 0,

                // Down-Right (315°)
                >= 292.5 and <= 337.5 => layerDiff < 0 && lineDiff > 0 || layerDiff > 0 && lineDiff < 0,

                _ => false
            };
        }

        #endregion
    }
}
