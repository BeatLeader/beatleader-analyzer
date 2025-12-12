using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static Analyzer.BeatmapScanner.Data.SwingData;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Cubic Bezier curve calculations for swing path analysis.
    /// </summary>
    internal class BezierCurve
    {
        private const int nTimes = 25;
        private static readonly double[] tCached = Enumerable.Range(0, nTimes).Select(i => i / (double)(nTimes - 1)).ToArray();

        /// <summary>
        /// Generates interpolated points along a cubic Bezier curve.
        /// Formula: B(t) = (1-t)³P₀ + 3(1-t)²tP₁ + 3(1-t)t²P₂ + t³P₃
        /// </summary>
        public static Point[] BezierCurveDirect(Point p0, Point p1, Point p2, Point p3)
        {
            Point[] result = new Point[nTimes];

            for (int i = 0; i < nTimes; i++)
            {
                double t = tCached[i];
                
                double oneMinusT = 1 - t;
                double oneMinusT2 = oneMinusT * oneMinusT;
                double oneMinusT3 = oneMinusT2 * oneMinusT;
                double t2 = t * t;
                double t3 = t2 * t;

                double b0 = oneMinusT3;
                double b1 = 3 * oneMinusT2 * t;
                double b2 = 3 * oneMinusT * t2;
                double b3 = t3;

                double x = b0 * p0.X + b1 * p1.X + b2 * p2.X + b3 * p3.X;
                double y = b0 * p0.Y + b1 * p1.Y + b2 * p2.Y + b3 * p3.Y;
                
                result[i] = new(x, y);
            }

            return result;
        }
    }
}
