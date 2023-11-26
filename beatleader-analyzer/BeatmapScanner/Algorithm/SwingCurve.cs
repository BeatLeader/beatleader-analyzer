using static Analyzer.BeatmapScanner.Helper.Helper;
using static Analyzer.BeatmapScanner.Helper.Curve;
using static Analyzer.BeatmapScanner.Helper.SwingAngleStrain;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;

namespace Analyzer.BeatmapScanner.Algorithm
{
    public class SwingCurve
    {
        public static bool UseParallel { get; set; } = true;
        public static void Calc(List<SwingData> swingData, bool leftOrRight)
        {
            if (swingData.Count < 2)
            {
                return;
            }

            double pathLookback;
            (double x, double y) simHandCurPos;
            (double x, double y) simHandPrePos;
            double curveComplexity;
            double pathAngleStrain;
            double positionComplexity;

            swingData[0].PathStrain = 0;
            swingData[0].PositionComplexity = 0;
            swingData[0].PreviousDistance = 0;
            swingData[0].CurveComplexity = 0;
            swingData[0].AnglePathStrain = 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void ForContent(int i)
            {
                Point point0 = new(swingData[i - 1].ExitPosition.x, swingData[i - 1].ExitPosition.y);
                Point point1 = new(point0.X + 1 * Math.Cos(ConvertDegreesToRadians(swingData[i - 1].Angle)),
                    point0.Y + 1 * Math.Sin(ConvertDegreesToRadians(swingData[i - 1].Angle)));
                Point point3 = new(swingData[i].EntryPosition.x, swingData[i].EntryPosition.y);
                Point point2 = new(point3.X - 1 * Math.Cos(ConvertDegreesToRadians(swingData[i].Angle)),
                    point3.Y - 1 * Math.Sin(ConvertDegreesToRadians(swingData[i].Angle)));

                List<Point> points = new()
                {
                    point0,
                    point1,
                    point2,
                    point3
                };

                var point = BezierCurve(points);

                positionComplexity = 0;
                List<double> angleChangeList = new(point.Count);
                List<double> angleList = new(point.Count);
                double distance = 0;
                for (int f = point.Count - 1; f >= 1; f--)
                {
                    angleList.Add(Mod(ConvertRadiansToDegrees(Math.Atan2(point[f].Y - point[f - 1].Y, point[f].X - point[f - 1].X)), 360));
                    distance += Math.Sqrt(Math.Pow(point[f].Y - point[f - 1].Y, 2) + Math.Pow(point[f].X - point[f - 1].X, 2));
                    if (f < point.Count - 2)
                    {
                        angleChangeList.Add(180 - Math.Abs(Math.Abs(angleList[^1] - angleList[^2]) - 180));
                    }
                }
                distance -= 0.75;
                if (i > 1)
                {
                    simHandCurPos = swingData[i].EntryPosition;
                    if (!swingData[i].Reset && !swingData[i - 1].Reset)
                    {
                        simHandPrePos = swingData[i - 2].EntryPosition;
                    }
                    else if (!swingData[i].Reset && swingData[i - 1].Reset)
                    {
                        simHandPrePos = swingData[i - 1].EntryPosition;

                    }
                    else if (swingData[i].Reset)
                    {
                        simHandPrePos = swingData[i - 1].EntryPosition;
                    }
                    else
                    {
                        simHandPrePos = simHandCurPos;
                    }
                    positionComplexity = Math.Pow(Math.Sqrt(Math.Pow(simHandCurPos.y - simHandPrePos.y, 2) + Math.Pow(simHandCurPos.x - simHandPrePos.x, 2)), 2);
                    if (positionComplexity > 10)
                    {
                        positionComplexity = 10;
                    }
                }

                double lengthOfList = angleChangeList.Count * 0.6;
                double first;
                double last;

                if (swingData[i].Reset)
                {
                    pathLookback = 0.9;
                    first = 0.5;
                    last = 1;
                }
                else
                {
                    pathLookback = 0.5;
                    first = 0.2;
                    last = 0.8;
                }
                int pathLookbackIndex = (int)(angleList.Count * pathLookback);
                int firstIndex = (int)(angleChangeList.Count * first) - 1;
                int lastIndex = (int)(angleChangeList.Count * last) - 1;

                // Not sure if the +1 is necessary
                curveComplexity = Math.Abs((lengthOfList * Average(CollectionsMarshal.AsSpan(angleChangeList).Slice(firstIndex, lastIndex - firstIndex + 1)) - 180) / 180);
                pathAngleStrain = BezierAngleStrainCalc(CollectionsMarshal.AsSpan(angleList)[pathLookbackIndex..angleList.Count], swingData[i].Forehand, leftOrRight) / angleList.Count * 2;

                swingData[i].PositionComplexity = positionComplexity;
                swingData[i].PreviousDistance = distance;
                swingData[i].CurveComplexity = curveComplexity;
                swingData[i].AnglePathStrain = pathAngleStrain;
                swingData[i].PathStrain = curveComplexity + pathAngleStrain + positionComplexity;
            }

            if (UseParallel)
            {
                Parallel.For(1, swingData.Count, ForContent);
            }
            else
            {
                for (int i = 1; i < swingData.Count; i++)
                {
                    ForContent(i);
                }
            }
        }

        public static double Average(Span<double> list)
        {
            double sum = 0;
            foreach(double val in list)
            {
                sum += val;
            }
            return sum / list.Length;
        }
    }
}
