using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Curve;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.SwingAngleStrain;
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
    public class SwingCurve
    {
        public static bool UseParallel { get; set; } = true;

        // Maximum reasonable distance in meters squared (full grid diagonal ~3m, squared = 9m²)
        // Using 4.68m² (sqrt(4.68) ≈ 2.16m) as practical maximum for position complexity
        private const double MAX_GRID_DISTANCE_SQUARED = 4.68;

        public static void Calc(List<SwingData> swingData, bool isRightHand)
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            void ForContent(int i)
            {
                Point point0 = new(swingData[i - 1].ExitPosition.x, swingData[i - 1].ExitPosition.y);
                Point point1 = new(point0.X + cosValues[i - 1], point0.Y + sinValues[i - 1]);
                Point point3 = new(swingData[i].EntryPosition.x, swingData[i].EntryPosition.y);
                Point point2 = new(point3.X - cosValues[i], point3.Y - sinValues[i]);

                Span<Point> controlPoints = stackalloc Point[4] { point0, point1, point2, point3 };
                var point = BezierCurveDirect(controlPoints[0], controlPoints[1], controlPoints[2], controlPoints[3]);
                
                double positionComplexity = 0;
                
                const int maxPoints = 25;
                Span<double> angleList = stackalloc double[maxPoints];
                Span<double> angleChangeList = stackalloc double[maxPoints];
                int angleCount = 0;
                int angleChangeCount = 0;
                
                double distance = 0;

                double dx = point3.X - point0.X;
                double dy = point3.Y - point0.Y;

                for (int f = 1; f < point.Length; f++)
                {
                    double deltaX = point[f].X - point[f - 1].X;
                    double deltaY = point[f].Y - point[f - 1].Y;
                    
                    double angle = Mod(ConvertRadiansToDegrees(Math.Atan2(deltaY, deltaX)), 360);
                    angleList[angleCount] = angle;
                    
                    distance += Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

                    if (angleCount > 0)
                    {
                        double angleDiff = Math.Abs(angleList[angleCount] - angleList[angleCount - 1]);
                        angleChangeList[angleChangeCount++] = 180 - Math.Abs(angleDiff - 180);
                    }
                    
                    angleCount++;
                }

                distance -= 0.75;

                if (i > 1)
                {
                    (double x, double y) simHandCurPos = swingData[i].EntryPosition;
                    (double x, double y) simHandPrePos;

                    if (!swingData[i].ParityErrors && !swingData[i - 1].ParityErrors)
                    {
                        simHandPrePos = swingData[i - 2].EntryPosition;
                    }
                    else
                    {
                        simHandPrePos = swingData[i - 1].EntryPosition;
                    }

                    double deltaX = simHandCurPos.x - simHandPrePos.x;
                    double deltaY = simHandCurPos.y - simHandPrePos.y;
                    double rawPositionComplexity = deltaX * deltaX + deltaY * deltaY;

                    if (rawPositionComplexity > MAX_GRID_DISTANCE_SQUARED)
                    {
                        rawPositionComplexity = MAX_GRID_DISTANCE_SQUARED;
                    }   
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

                if (angleChangeCount >= 2 && angleCount >= 2)
                {
                    int firstIndex = Math.Max(0, (int)(angleChangeCount * first));
                    int lastIndex = Math.Min(angleChangeCount, (int)(angleChangeCount * last));
                    int pathLookbackIndex = (int)(angleCount * pathLookback);

                    if (lastIndex > firstIndex)
                    {
                        var angleSlice = angleChangeList.Slice(firstIndex, lastIndex - firstIndex);
                        double avgAngleChange = Average(angleSlice);
                        curveComplexity = avgAngleChange / 180.0;

                        var pathAngleSlice = angleList.Slice(pathLookbackIndex, angleCount - pathLookbackIndex);
                        pathAngleStrain = BezierAngleTotalStrain(pathAngleSlice, swingData[i].Forehand, isRightHand)
                                                 / pathAngleSlice.Length;
                    }
                }

                swingData[i].PreviousDistance = distance;

                swingData[i].PositionComplexity = positionComplexity;
                swingData[i].CurveComplexity = curveComplexity;
                swingData[i].AnglePathStrain = pathAngleStrain * 2;

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
    }
}
