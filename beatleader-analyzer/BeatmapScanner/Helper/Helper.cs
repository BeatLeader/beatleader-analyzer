using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Analyzer.BeatmapScanner.Helper
{
    internal class Helper
    {
        // Maps cut direction indices to angles: 0=Up, 1=Down, 2=Left, 3=Right, 4=UpLeft, 5=UpRight, 6=DownLeft, 7=DownRight, 8=Dot
        public static int[] DirectionToDegree = { 90, 270, 180, 0, 135, 45, 225, 315, 270 };

        public static double ConvertDegreesToRadians(double degrees)
        {
            return degrees * (Math.PI / 180f);
        }

        public static double ConvertRadiansToDegrees(double radians)
        {
            return radians * (180f / Math.PI);
        }

        /// <summary>
        /// Modulo operation that always returns positive results (handles negative numbers correctly for angle wrapping).
        /// </summary>
        public static double Mod(double x, double m)
        {
            return (x % m + m) % m;
        }

        public static void Swap<T>(IList<T> list, int indexA, int indexB)
        {
            (list[indexB], list[indexA]) = (list[indexA], list[indexB]);
        }

        /// <summary>
        /// Reverses a cut direction by 180 degrees.
        /// </summary>
        public static double ReverseCutDirection(double direction)
        {
            return direction >= 180 ? direction - 180 : direction + 180;
        }
    }
}
