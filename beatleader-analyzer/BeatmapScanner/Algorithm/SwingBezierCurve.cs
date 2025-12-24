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

                // Single swing perpendicular repositioning
                if (i > 0)
                {
                    currentSwingPosition = swingData[i].EntryPosition;
                    previousSwingPosition = swingData[i - 1].ExitPosition;

                    (double x, double y) posChangeVector = (currentSwingPosition.x - previousSwingPosition.x, currentSwingPosition.y - previousSwingPosition.y);
                    (double x, double y) projectionVector = (Math.Cos((swingData[i].Direction + 90) * Math.PI / 180), Math.Sin((swingData[i].Direction + 90) * Math.PI / 180));
                    repositioningDistance = Math.Abs(posChangeVector.x * projectionVector.x + posChangeVector.y * projectionVector.y) * 1.0;
                }

                // 2-swing average total repositioning
                if (i > 1)
                {
                    (double x, double y) swingAPos = ((swingData[i].EntryPosition.x + swingData[i].ExitPosition.x) / 2, (swingData[i].EntryPosition.y + swingData[i].ExitPosition.y) / 2);
                    (double x, double y) swingBPos = ((swingData[i - 1].EntryPosition.x + swingData[i - 1].ExitPosition.x) / 2, (swingData[i - 1].EntryPosition.y + swingData[i - 1].ExitPosition.y) / 2);
                    (double x, double y) swingCPos = ((swingData[i - 2].EntryPosition.x + swingData[i - 2].ExitPosition.x) / 2, (swingData[i - 2].EntryPosition.y + swingData[i - 2].ExitPosition.y) / 2);

                    (double x, double y) avgAB = ((swingAPos.x + swingBPos.x) / 2, (swingAPos.y + swingBPos.y) / 2);
                    (double x, double y) avgBC = ((swingBPos.x + swingCPos.x) / 2, (swingBPos.y + swingCPos.y) / 2);

                    (double x, double y) avgDelta = (avgAB.x - avgBC.x, avgAB.y - avgBC.y);
                    double distance = Math.Sqrt(avgDelta.x * avgDelta.x + avgDelta.y * avgDelta.y);

                    repositioningDistance += distance * 0.5;
                }

                // Rotation
                double rotationAmount = 0.0;
                if (i > 0)
                {
                    double angleDifference = AngleDifference(swingData[i - 1].Direction, swingData[i].Direction);
                    rotationAmount = Math.Abs(angleDifference);
                    if (!swingData[i].ParityErrors) rotationAmount = Math.Abs(rotationAmount - 180);

                    rotationAmount /= 180;
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

                double pathAngleStrain = 0;
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
                swingData[i].RotationAmount = rotationAmount;
                swingData[i].AnglePathStrain = pathAngleStrain;
                swingData[i].PathStrain = rotationAmount + pathAngleStrain + repositioningDistance;
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
