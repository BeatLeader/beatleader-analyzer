using Analyzer.BeatmapScanner.Data;
using System;
using static Analyzer.BeatmapScanner.Helper.IsSameDirection;

namespace Analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Detects Multi-Note Hits using NJS-based 3D distance calculations.
    /// </summary>
    internal class MultiNoteHitDetector
    {
        private const double GRID_SPACING = 0.6;
        private const double MAX_Z_DISTANCE = 1.2;

        public static double CalculateZPosition(float time, float njs, float bpm)
        {
            double timeInSeconds = time * (60.0 / bpm);
            return njs * timeInSeconds;
        }

        public static double Calculate3DDistance(Cube prev, Cube next, float bpm)
        {
            double xDistance = (next.Line - prev.Line) * GRID_SPACING;
            double yDistance = (next.Layer - prev.Layer) * GRID_SPACING;

            double prevZ = CalculateZPosition(prev.Time, prev.Njs, bpm);
            double nextZ = CalculateZPosition(next.Time, next.Njs, bpm);
            double zDistance = Math.Abs(nextZ - prevZ);

            return Math.Sqrt(xDistance * xDistance + yDistance * yDistance + zDistance * zDistance);
        }

        public static bool IsPositionAlignedWithDirection(Cube prev, Cube next, double direction)
        {
            int xDiff = next.Line - prev.Line;
            int yDiff = next.Layer - prev.Layer;

            if (xDiff == 0 && yDiff == 0)
            {
                if (direction == 8) return true;
                return false;
            }

            switch (direction)
            {
                case double d when d > 67.5 && d <= 112.5:
                    return yDiff > 0;
                case double d when d > 247.5 && d <= 292.5:
                    return yDiff < 0;
                case double d when d > 157.5 && d <= 202.5:
                    return xDiff < 0;
                case double d when d <= 22.5 && d >= 0 || d > 337.5 && d < 360:
                    return xDiff > 0;
                case double d when d > 112.5 && d <= 157.5:
                    return yDiff >= 0 || xDiff <= 0;
                case double d when d > 22.5 && d <= 67.5:
                    return yDiff >= 0 || xDiff >= 0;
                case double d when d > 202.5 && d <= 247.5:
                    return yDiff <= 0 || xDiff <= 0;
                case double d when d > 292.5 && d <= 337.5:
                    return yDiff <= 0 || xDiff >= 0;
            }

            return false;
        }

        public static bool IsMultiNoteHit(Cube prev, Cube next, float bpm)
        {
            // Check if notes are simultaneous (same time)
            bool isSimultaneous = Math.Abs(prev.Time - next.Time) < 0.001f;
            
            if (isSimultaneous)
            {
                // For simultaneous notes (stacks, windows, towers, etc.),
                // there's no X/Y distance limit - they can be anywhere on the grid
                // No distance check needed for simultaneous multi-note patterns
            }
            else
            {
                // For sequential notes (sliders, curved sliders),
                // only check Z-distance (depth/time) to ensure they're close enough in time
                double prevZ = CalculateZPosition(prev.Time, prev.Njs, bpm);
                double nextZ = CalculateZPosition(next.Time, next.Njs, bpm);
                double zDistance = Math.Abs(nextZ - prevZ);
                
                if (zDistance > MAX_Z_DISTANCE)
                {
                    return false;
                }
            }

            if (next.CutDirection != 8)
            {
                if (!IsSameDir(prev.Direction, next.Direction))
                {
                    return false;
                }
            }

            if (!IsPositionAlignedWithDirection(prev, next, prev.Direction))
            {
                return false;
            }

            return true;
        }

        public static bool AreNotesCloseInDepth(Cube prev, Cube next, float bpm)
        {
            double prevZ = CalculateZPosition(prev.Time, prev.Njs, bpm);
            double nextZ = CalculateZPosition(next.Time, next.Njs, bpm);
            double zDistance = Math.Abs(nextZ - prevZ);

            return zDistance <= MAX_Z_DISTANCE;
        }
    }
}
