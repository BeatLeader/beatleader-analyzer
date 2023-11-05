using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Analyzer.BeatmapScanner.Helper
{
    internal class Curve
    {
        public static double BernsteinPoly(int i, int n, double t)
        {
            return BinomialCoefficient(n, i) * Math.Pow(t, n - i) * Math.Pow(1 - t, i);
        }

        public static List<Point> BezierCurve(List<Point> points, int nTimes = 1000)
        {
            int nPoints = points.Count;
            List<double> xPoints = points.Select(p => p.X).ToList();
            List<double> yPoints = points.Select(p => p.Y).ToList();
            double[] t = Enumerable.Range(0, nTimes).Select(i => i / (double)(nTimes - 1)).ToArray();

            List<double> resultX = new();
            List<double> resultY = new();

            for (int i = 0; i < nTimes; i++)
            {
                double currentT = t[i];
                double x = 0;
                double y = 0;
                for (int j = 0; j < nPoints; j++)
                {
                    double poly = BernsteinPoly(j, nPoints - 1, currentT);
                    x += xPoints[j] * poly;
                    y += yPoints[j] * poly;
                }
                resultX.Add(x);
                resultY.Add(y);
            }

            return resultX.Zip(resultY, (x, y) => new Point(x, y)).ToList();
        }

        private static long BinomialCoefficient(int n, int k)
        {
            if (k < 0 || k > n)
            {
                return 0;
            }

            if (k == 0 || k == n)
            {
                return 1;
            }

            long result = 1;
            for (int i = 1; i <= k; i++)
            {
                result = result * (n - i + 1) / i;
            }

            return result;
        }
    }
}
