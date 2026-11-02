using Analyzer.BeatmapScanner.Data;
using System;
using System.Threading;
using static beatleader_analyzer.BeatmapScanner.Helper.AngleTolerance;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using static beatleader_analyzer.BeatmapScanner.Helper.GridPosition;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Swing directions from the parity model (Algorithm/ParityModel.cs, Cube.ModelDirection): the direction top players cut
    /// the swing at, for its parity. When the model gives one, it replaces the Analyzer's own (arrow, bend towards the
    /// path, dot rules, multi-note geometry) and the entry / exit points follow it. Arrows stay within the cut tolerance of
    /// the head arrow. A single dot keeps its per-position arc offset (DotDirections), so angle strain and rotation still
    /// leave the arm's arc out.
    /// </summary>
    public static class ModelDirections
    {
        /// <summary>Swings given a model direction, and how many of them were clamped to their arrow's tolerance</summary>
        public static long Applied, Clamped;

        public static void Apply(SwingData swing, bool strictAngles)
        {
            var head = swing.Cubes[0];
            double direction = head.ModelDirection;
            if (direction < 0) return;
            if (head.CutDirection != 8)
            {
                double tolerance = GetTolerance(strictAngles);
                double delta = AngleDifference(head.Direction, direction);
                if (Math.Abs(delta) > tolerance)
                {
                    direction = Mod(head.Direction + Math.Sign(delta) * tolerance, 360);
                    Interlocked.Increment(ref Clamped);
                }
            }
            Interlocked.Increment(ref Applied);
            swing.Direction = direction;

            double rad = ConvertDegreesToRadians(direction);
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            // Entry before the first note along the direction, exit after the last one (the notes of a multi-note swing
            // aren't always in that order)
            double first = double.MaxValue, last = double.MinValue;
            (double x, double y) entry = default, exit = default;
            foreach (var cube in swing.Cubes)
            {
                (double cx, double cy) = GridToMeters(cube.X, cube.Y);
                double along = cx * cos + cy * sin;
                if (along < first) { first = along; entry = (cx, cy); }
                if (along > last) { last = along; exit = (cx, cy); }
            }
            swing.EntryPosition = (entry.x - cos * NOTE_SIZE, entry.y - sin * NOTE_SIZE);
            // A chain keeps its exit at the chain's tail (EntryExit.CalcEntryExit)
            if (!swing.Cubes.Exists(c => c.Chain)) swing.ExitPosition = (exit.x + cos * NOTE_SIZE, exit.y + sin * NOTE_SIZE);
        }
    }
}
