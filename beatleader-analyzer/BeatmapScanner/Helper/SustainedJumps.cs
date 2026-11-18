using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using static beatleader_analyzer.BeatmapScanner.Helper.AngleTolerance;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using static beatleader_analyzer.BeatmapScanner.Helper.GridPosition;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Sustained jumps: a hand going back and forth fast between the same two positions, one arrow per swing, no reset.
    /// Players swing straight along the line between the two notes whatever the arrows (within their cut tolerance), so the
    /// arrows of such a run take that line as their direction, over the parity model's or the Analyzer's own. Without it, a
    /// DL / UR jump rated well over the same jump mapped D / UR (Godspeed Expert vs Expert+).
    /// Slow and short runs are left out (low diffs lost most of their tech), and so are jumps along a column or row by
    /// default: up / down streams in one column are cut at an angle, not straight up and down (their tech rose up to 44%).
    /// Arrows far from the line keep their direction (wide jumps with up arrows gained a lot of tech).
    /// </summary>
    public static class SustainedJumps
    {
        /// <summary>Fewest swings in a run (4 back and forths); 0 turns it off</summary>
        public static int MinSwings { get; set; } = 8;
        /// <summary>Longest time between two swings of the run (seconds)</summary>
        public static double MaxGapSeconds { get; set; } = 0.2;
        /// <summary>Only jumps that change both column and row</summary>
        public static bool DiagonalOnly { get; set; } = true;
        /// <summary>Most an arrow is bent to the line (degrees, within the cut tolerance)</summary>
        public static double MaxBendDegrees { get; set; } = 30;

        /// <summary>Sets a setting by name, for experiments (rerate Jumps.MaxGapSeconds=0.25)</summary>
        public static void Set(string name, double value)
        {
            var prop = typeof(SustainedJumps).GetProperty(name) ?? throw new ArgumentException(name);
            prop.SetValue(null, prop.PropertyType == typeof(bool) ? value != 0 : prop.PropertyType == typeof(int) ? (int)value : value);
        }

        // Applied after ModelDirections; swings: one hand's
        public static void Apply(List<SwingData> swings, bool strictAngles)
        {
            if (MinSwings <= 0) return;
            double maxBend = Math.Min(MaxBendDegrees, GetTolerance(strictAngles));
            int start = 0;
            for (int i = 1; i <= swings.Count; i++)
            {
                // The run goes on while the swing is a single note, alternates parity, jumps soon after the previous one, and
                // is back where it was two swings ago
                bool jumps = i < swings.Count && Jump(swings[i - 1], swings[i]);
                bool goesOn = jumps && (i - start < 2 || SamePosition(swings[i], swings[i - 2]));
                if (goesOn) continue;
                if (i - start >= MinSwings)
                {
                    // Not the first swing: it comes from elsewhere
                    for (int j = start + 1; j < i; j++) Straighten(swings[j], swings[j - 1], maxBend);
                }
                // A move to a new position starts the next run with the swing before it (A B A B C B C B: B C B C B)
                start = jumps ? i - 1 : i;
            }
        }

        private static bool IsSingle(SwingData swing) => swing.Cubes.Count == 1 && !swing.Cubes[0].BombAvoidance;

        private static bool Jump(SwingData previous, SwingData swing)
        {
            if (!IsSingle(previous) || !IsSingle(swing) || swing.ParityErrors) return false;
            if (swing.Cubes[0].Seconds - previous.Cubes[0].Seconds > MaxGapSeconds) return false;
            int dx = swing.Cubes[0].X - previous.Cubes[0].X, dy = swing.Cubes[0].Y - previous.Cubes[0].Y;
            return DiagonalOnly ? dx != 0 && dy != 0 : dx != 0 || dy != 0;
        }

        private static bool SamePosition(SwingData a, SwingData b) => a.Cubes[0].X == b.Cubes[0].X && a.Cubes[0].Y == b.Cubes[0].Y;

        // The arrow takes the line from the previous note to its own, when it's within maxBend of the arrow. Dots keep theirs
        // (DotDirections, ModelDirections).
        private static void Straighten(SwingData swing, SwingData previous, double maxBend)
        {
            var head = swing.Cubes[0];
            if (head.CutDirection == 8) return;
            var (x, y) = GridToMeters(head.X, head.Y);
            var (ox, oy) = GridToMeters(previous.Cubes[0].X, previous.Cubes[0].Y);
            double line = Mod(Math.Atan2(y - oy, x - ox) * 180.0 / Math.PI, 360);
            if (Math.Abs(AngleDifference(head.Direction, line)) > maxBend) return;
            ModelDirections.SetDirection(swing, line);
        }
    }
}
