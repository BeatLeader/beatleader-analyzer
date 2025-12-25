using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Calculates movement between swings, measuring repositioning distance and rotation amount.
    /// </summary>
    public class SwingMovement
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
                double repositioningDistance = 0;

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

                // Since rotation and repositioning will be split between the real swing and the implied swing of the reset, the values should halved
                if (swingData[i].ParityErrors)
                {
                    repositioningDistance /= 2;
                    rotationAmount /= 2;
                }

                swingData[i].RepositioningDistance = repositioningDistance;
                swingData[i].RotationAmount = rotationAmount;
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
