using System;
using static Analyzer.BeatmapScanner.Helper.Helper;

namespace Analyzer.BeatmapScanner.Helper
{
    internal class CalculateBaseEntryExit
    {
        private const double GRID_CELL_SIZE = 1.0 / 3.0;
        private const double HALF_GRID_CELL = 1.0 / 6.0;

        public static ((double x, double y) entry, (double x, double y) exit) CalcBaseEntryExit((double x, double y) position, double angle)
        {
            double angleInRadians = ConvertDegreesToRadians(angle);
            double cosAngle = Math.Cos(angleInRadians);
            double sinAngle = Math.Sin(angleInRadians);

            double normalizedX = position.x * GRID_CELL_SIZE;
            double normalizedY = position.y * GRID_CELL_SIZE;

            (double, double) entry = (
                normalizedX - cosAngle * HALF_GRID_CELL + HALF_GRID_CELL,
                normalizedY - sinAngle * HALF_GRID_CELL + HALF_GRID_CELL
            );

            (double, double) exit = (
                normalizedX + cosAngle * HALF_GRID_CELL + HALF_GRID_CELL,
                normalizedY + sinAngle * HALF_GRID_CELL + HALF_GRID_CELL
            );

            return (entry, exit);
        }
    }
}
