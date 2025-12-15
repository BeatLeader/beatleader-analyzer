using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using static beatleader_analyzer.BeatmapScanner.Helper.BezierCurve;
using static beatleader_analyzer.BeatmapScanner.Helper.AngleStrain;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;
using static Analyzer.BeatmapScanner.Data.SwingData;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Calculates swing path complexity using Bezier curve analysis.
    /// </summary>
    public class SwingBezierCurve
    {
        public static bool UseParallel { get; set; } = true;

        // Maximum reasonable distance in meters squared (full grid diagonal ~3m, squared = 9m²)
        private const double MAX_GRID_DISTANCE_SQUARED = 4.68;

        public static void Calc(List<SwingData> swingData, bool isRightHand)
        {
            CalcInternal(swingData, isRightHand);
        }

        private static void CalcInternal(List<SwingData> swingData, bool isRightHand)
        {
            if (swingData.Count < 2)
            {
                return;
            }

            var cosValues = new double[swingData.Count];
            var sinValues = new double[swingData.Count];
            
            for (int i = 0; i < swingData.Count; i++)
            {
                double radians = ConvertDegreesToRadians(swingData[i].Direction);
                cosValues[i] = Math.Cos(radians);
                sinValues[i] = Math.Sin(radians);
            }

            void ForContent(int i)
            {
                Point point0 = new(swingData[i - 1].ExitPosition.x, swingData[i - 1].ExitPosition.y);
                Point point1 = new(point0.X + cosValues[i - 1], point0.Y + sinValues[i - 1]);
                Point point3 = new(swingData[i].EntryPosition.x, swingData[i].EntryPosition.y);
                Point point2 = new(point3.X - cosValues[i], point3.Y - sinValues[i]);

                Span<Point> controlPoints = stackalloc Point[4] { point0, point1, point2, point3 };
                var point = BezierCurveDirect(controlPoints[0], controlPoints[1], controlPoints[2], controlPoints[3]);

                double repositioningDistance = 0;
                
                const int maxPoints = 25;
                Span<double> angleList = stackalloc double[maxPoints];
                Span<double> angleChangeList = stackalloc double[maxPoints];
                int angleCount = 0;
                int angleChangeCount = 0;

                for (int f = 1; f < point.Length; f++)
                {
                    double deltaX = point[f].X - point[f - 1].X;
                    double deltaY = point[f].Y - point[f - 1].Y;
                    
                    double angle = Mod(ConvertRadiansToDegrees(Math.Atan2(deltaY, deltaX)), 360);
                    angleList[angleCount] = angle;

                    if (angleCount > 0)
                    {
                        double angleDiff = Math.Abs(angleList[angleCount] - angleList[angleCount - 1]);
                        double angleChange = 180 - Math.Abs(angleDiff - 180);
                        angleChangeList[angleChangeCount++] = angleChange;
                    }
                    
                    angleCount++;
                }

                (double x, double y) currentSwingPosition = (0, 0);
                (double x, double y) previousSwingPosition = (0, 0);

                double timeDiff = 1;

                if (i > 1)
                {
                    currentSwingPosition = swingData[i].EntryPosition;

                    // Positional difference between 2 swings ago and now
                    // unless parity errors, then use previous swing instead
                    if (!swingData[i].ParityErrors)
                    {
                        previousSwingPosition = swingData[i - 2].EntryPosition;
                    }
                    else
                    {
                        previousSwingPosition = swingData[i - 1].EntryPosition;
                    }
                    
                    double deltaX = currentSwingPosition.x - previousSwingPosition.x;
                    double deltaY = currentSwingPosition.y - previousSwingPosition.y;
                    repositioningDistance = deltaX * deltaX + deltaY * deltaY;
                    // Decay over time
                    timeDiff = Math.Abs(swingData[i].Cubes[0].Seconds - swingData[i - 1].Cubes[^1].Seconds);
                    // https://www.desmos.com/calculator/5xlyaybnmt
                    repositioningDistance *= Math.Exp((0.25 - timeDiff) * Math.Log(4.0));
                    // Clamp to max grid distance squared
                    repositioningDistance = repositioningDistance / (repositioningDistance + MAX_GRID_DISTANCE_SQUARED);
                }

                double first, last, pathLookback;

                if (swingData[i].ParityErrors)
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

                double curveComplexity = 0;
                double pathAngleStrain = 0;
                double avgAngleChange = 0;
                int firstIndex = 0;
                int lastIndex = 0;
                int pathLookbackIndex = 0;

                if (angleChangeCount >= 2 && angleCount >= 2)
                {
                    firstIndex = Math.Max(0, (int)(angleChangeCount * first));
                    lastIndex = Math.Min(angleChangeCount, (int)(angleChangeCount * last));
                    pathLookbackIndex = (int)(angleCount * pathLookback);

                    if (lastIndex > firstIndex)
                    {
                        var angleSlice = angleChangeList.Slice(firstIndex, lastIndex - firstIndex);
                        avgAngleChange = Average(angleSlice);
                        curveComplexity = avgAngleChange / 180.0;
                        curveComplexity = curveComplexity * curveComplexity;
                        // Decay over time
                        curveComplexity *= Math.Exp((0.25 - timeDiff) * Math.Log(4.0));

                        if (i == 0)
                        {
                            pathAngleStrain = 0;
                        }
                        else
                        {
                            var pathAngleSlice = angleList.Slice(pathLookbackIndex, angleCount - pathLookbackIndex);
                            pathAngleStrain = BezierAngleTotalStrain(pathAngleSlice, swingData[i].Cubes[0].Seconds,
                                swingData[i - 1].Cubes[^1].Seconds, swingData[i].Forehand, isRightHand) / pathAngleSlice.Length * 4;
                        }
                    }
                }

                swingData[i].RepositioningDistance = repositioningDistance;
                swingData[i].CurveComplexity = curveComplexity;
                swingData[i].AnglePathStrain = pathAngleStrain;
                swingData[i].PathStrain = curveComplexity + pathAngleStrain + repositioningDistance;
            }

            // Disable parallel processing when capturing debug data to maintain correct order
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
    }
}
