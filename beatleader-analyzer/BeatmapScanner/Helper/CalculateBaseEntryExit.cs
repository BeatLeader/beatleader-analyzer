using System;
using static Analyzer.BeatmapScanner.Helper.Helper;
using static Analyzer.BeatmapScanner.Helper.GridPositionHelper;

namespace Analyzer.BeatmapScanner.Helper
{
    internal class CalculateBaseEntryExit
    {
        public static ((double x, double y) entry, (double x, double y) exit) CalcBaseEntryExit((double x, double y) position, double angle)
        {
            double angleInRadians = ConvertDegreesToRadians(angle);
            double cosAngle = Math.Cos(angleInRadians);
            double sinAngle = Math.Sin(angleInRadians);

            // Convert grid position to meters using centered grid (0,0) system
            (double posX, double posY) = GridToMeters(position.x, position.y);

            // Entry point: position minus half note size in swing direction
            (double, double) entry = (
                posX - cosAngle * NOTE_SIZE,
                posY - sinAngle * NOTE_SIZE
            );

            // Exit point: position plus half note size in swing direction
            (double, double) exit = (
                posX + cosAngle * NOTE_SIZE,
                posY + sinAngle * NOTE_SIZE
            );

            return (entry, exit);
        }
    }
}
