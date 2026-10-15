using Analyzer.BeatmapScanner.Data;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using static beatleader_analyzer.BeatmapScanner.Helper.GridPosition;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Direction of single dots as top players cut them, fitted on the cut directions recorded in their replays (portaBLe
    /// ParityCheck, about 16k single dots on 198 maps): mean error against the players 23.8 -> 12 degrees, dots cut the wrong
    /// way 3.7% -> 1.4%.
    /// A dot's direction (guessed by PreprocessNotes) is pulled towards the natural direction of its parity, then gets a
    /// per-position offset: the arc of the arm, which depends on where the note is. The offset changes where the saber goes
    /// (entry / exit points) but isn't wrist work, so angle strain and rotation leave it out (DifficultyDirection).
    /// Offsets are for the right hand, [parity: 0 backhand, 1 forehand][layer][line], in degrees counter-clockwise; the left
    /// hand uses the mirror image.
    /// </summary>
    internal static class DotDirections
    {
        // How far a dot's direction is pulled towards its parity's natural direction (0..1)
        private const double NEUTRAL_PULL = 0.6;

        private static readonly double[][][] Offset =
        {
            new[]
            {
                new[] { 0.0, 9.8, -11.1, -25.8 },
                new[] { 21.9, 12.4, 0.5, -14.0 },
                new[] { 0.0, 17.4, 6.7, -1.9 }
            },
            new[]
            {
                new[] { -19.0, -10.1, 11.8, 25.0 },
                new[] { -29.9, -17.8, 23.1, 38.5 },
                new[] { 0.0, -17.7, 36.1, 41.7 }
            }
        };

        private const double LEFT_FOREHAND_NEUTRAL = 292.5;
        private const double RIGHT_FOREHAND_NEUTRAL = 247.5;
        private const double LEFT_BACKHAND_NEUTRAL = 112.5;
        private const double RIGHT_BACKHAND_NEUTRAL = 67.5;

        public static bool IsSingleDot(SwingData swing) =>
            swing.Cubes.Count == 1 && !swing.Cubes[0].Chain && swing.Cubes[0].CutDirection == 8;

        /// <summary>Sets a single dot swing's direction, its arc offset and its entry / exit points.</summary>
        public static void Apply(SwingData swing)
        {
            var cube = swing.Cubes[0];
            bool right = cube.Type == 1;
            double neutral = swing.Forehand
                ? (right ? RIGHT_FOREHAND_NEUTRAL : LEFT_FOREHAND_NEUTRAL)
                : (right ? RIGHT_BACKHAND_NEUTRAL : LEFT_BACKHAND_NEUTRAL);
            double direction = Mod(swing.Direction + NEUTRAL_PULL * AngleDifference(swing.Direction, neutral), 360);

            double offset = 0;
            if (cube.X >= 0 && cube.X <= 3 && cube.Y >= 0 && cube.Y <= 2)
            {
                int parity = swing.Forehand ? 1 : 0;
                offset = right ? Offset[parity][cube.Y][cube.X] : -Offset[parity][cube.Y][3 - cube.X];
            }

            swing.ArcOffset = offset;
            swing.Direction = Mod(direction + offset, 360);

            (double x, double y) = GridToMeters(cube.X, cube.Y);
            double rad = ConvertDegreesToRadians(swing.Direction);
            swing.EntryPosition = (x - Math.Cos(rad) * NOTE_SIZE, y - Math.Sin(rad) * NOTE_SIZE);
            swing.ExitPosition = (x + Math.Cos(rad) * NOTE_SIZE, y + Math.Sin(rad) * NOTE_SIZE);
        }

        /// <summary>Direction used for angle strain and rotation: without the arc offset.</summary>
        public static double DifficultyDirection(SwingData swing) => Mod(swing.Direction - swing.ArcOffset, 360);
    }
}
