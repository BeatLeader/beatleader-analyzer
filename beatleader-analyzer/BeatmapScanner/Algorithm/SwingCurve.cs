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
using beatleader_analyzer.BeatmapScanner.Data;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Calculates swing path complexity using Bezier curve analysis.
    /// </summary>
    public class SwingCurve
    {
        public static bool UseParallel { get; set; } = true;
        public static List<SwingCurveDebugData> DebugData { get; private set; } = new List<SwingCurveDebugData>();

        // Maximum reasonable distance in meters squared (full grid diagonal ~3m, squared = 9m²)
        // Using 4.68m² (sqrt(4.68) ≈ 2.16m) as practical maximum for position complexity
        private const double MAX_GRID_DISTANCE_SQUARED = 4.68;

        public static void Calc(List<SwingData> swingData, bool isRightHand)
        {
            CalcInternal(swingData, isRightHand, captureDebugData: false);
        }

        public static void CalcWithDebug(List<SwingData> swingData, bool isRightHand)
        {
            CalcInternal(swingData, isRightHand, captureDebugData: true);
        }

        private static void CalcInternal(List<SwingData> swingData, bool isRightHand, bool captureDebugData)
        {
            if (swingData.Count < 2)
            {
                return;
            }

            if (captureDebugData)
            {
                DebugData = new List<SwingCurveDebugData>(swingData.Count);
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
                SwingCurveDebugData debugData = captureDebugData ? new SwingCurveDebugData 
                { 
                    SwingIndex = i,
                    Beat = swingData[i].Beat,
                    Hand = swingData[i].Start.Type == 0 ? "Red" : "Blue",
                    IsReset = swingData[i].ParityErrors,
                    EntryX = swingData[i].EntryPosition.x,
                    EntryY = swingData[i].EntryPosition.y,
                    ExitX = swingData[i].ExitPosition.x,
                    ExitY = swingData[i].ExitPosition.y
                } : null;

                Point point0 = new(swingData[i - 1].ExitPosition.x, swingData[i - 1].ExitPosition.y);
                Point point1 = new(point0.X + cosValues[i - 1], point0.Y + sinValues[i - 1]);
                Point point3 = new(swingData[i].EntryPosition.x, swingData[i].EntryPosition.y);
                Point point2 = new(point3.X - cosValues[i], point3.Y - sinValues[i]);

                if (captureDebugData)
                {
                    debugData.Point0X = point0.X;
                    debugData.Point0Y = point0.Y;
                    debugData.Point1X = point1.X;
                    debugData.Point1Y = point1.Y;
                    debugData.Point2X = point2.X;
                    debugData.Point2Y = point2.Y;
                    debugData.Point3X = point3.X;
                    debugData.Point3Y = point3.Y;
                }

                Span<Point> controlPoints = stackalloc Point[4] { point0, point1, point2, point3 };
                var point = BezierCurveDirect(controlPoints[0], controlPoints[1], controlPoints[2], controlPoints[3]);
                
                if (captureDebugData)
                {
                    for (int p = 0; p < point.Length; p++)
                    {
                        debugData.BezierPoints.Add((point[p].X, point[p].Y));
                    }
                }
                
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
                    
                    if (captureDebugData)
                    {
                        debugData.AngleList.Add(angle);
                    }
                    
                    distance += Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

                    if (angleCount > 0)
                    {
                        double angleDiff = Math.Abs(angleList[angleCount] - angleList[angleCount - 1]);
                        double angleChange = 180 - Math.Abs(angleDiff - 180);
                        angleChangeList[angleChangeCount++] = angleChange;
                        
                        if (captureDebugData)
                        {
                            debugData.AngleChangeList.Add(angleChange);
                        }
                    }
                    
                    angleCount++;
                }

                double distanceBeforeOffset = distance;
                distance -= 0.75;

                if (captureDebugData)
                {
                    debugData.Distance = distanceBeforeOffset;
                    debugData.DistanceMinusOffset = distance;
                    debugData.AngleCount = angleCount;
                    debugData.AngleChangeCount = angleChangeCount;
                }

                double rawPositionComplexity = 0;
                (double x, double y) simHandCurPos = (0, 0);
                (double x, double y) simHandPrePos = (0, 0);

                if (i > 1)
                {
                    simHandCurPos = swingData[i].EntryPosition;

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
                    rawPositionComplexity = deltaX * deltaX + deltaY * deltaY;

                    if (rawPositionComplexity > MAX_GRID_DISTANCE_SQUARED)
                    {
                        rawPositionComplexity = MAX_GRID_DISTANCE_SQUARED;
                    }
                    
                    positionComplexity = rawPositionComplexity;
                }

                if (captureDebugData)
                {
                    debugData.SimHandCurPosX = simHandCurPos.x;
                    debugData.SimHandCurPosY = simHandCurPos.y;
                    debugData.SimHandPrePosX = simHandPrePos.x;
                    debugData.SimHandPrePosY = simHandPrePos.y;
                    debugData.RawPositionComplexity = rawPositionComplexity;
                    debugData.PositionComplexity = positionComplexity;
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

                if (captureDebugData)
                {
                    debugData.First = first;
                    debugData.Last = last;
                    debugData.PathLookback = pathLookback;
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

                        var pathAngleSlice = angleList.Slice(pathLookbackIndex, angleCount - pathLookbackIndex);
                        pathAngleStrain = BezierAngleTotalStrain(pathAngleSlice, swingData[i].Forehand, isRightHand)
                                                 / pathAngleSlice.Length * 4;
                    }
                }

                if (captureDebugData)
                {
                    debugData.FirstIndex = firstIndex;
                    debugData.LastIndex = lastIndex;
                    debugData.PathLookbackIndex = pathLookbackIndex;
                    debugData.AvgAngleChange = avgAngleChange;
                    debugData.CurveComplexity = curveComplexity;
                    debugData.PathAngleStrain = pathAngleStrain;
                    debugData.PathStrain = curveComplexity + pathAngleStrain + positionComplexity;
                }

                swingData[i].PreviousDistance = distance;
                swingData[i].PositionComplexity = positionComplexity;
                swingData[i].CurveComplexity = curveComplexity;
                swingData[i].AnglePathStrain = pathAngleStrain;
                swingData[i].PathStrain = curveComplexity + pathAngleStrain + positionComplexity;

                if (captureDebugData)
                {
                    DebugData.Add(debugData);
                }
            }

            // Disable parallel processing when capturing debug data to maintain correct order
            if (UseParallel && !captureDebugData)
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
