using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static Analyzer.BeatmapScanner.Helper.Helper;

namespace Analyzer.BeatmapScanner.Algorithm
{
    internal class DiffToPass
    {
        public static List<SwingData> CalcSwingDiff(List<SwingData> swingData, List<SwingData> otherHand, double bpm)
        {
            if (swingData.Count() == 0)
            {
                return swingData;
            }
            double bps = bpm / 60;
            var data = new List<SData>();
            swingData[0].SwingDiff = 0;
            for (int i = 1; i < swingData.Count(); i++)
            {
                double distanceDiff = swingData[i].PreviousDistance / (swingData[i].PreviousDistance + 3) + 1;
                data.Add(new SData(swingData[i].SwingFrequency * distanceDiff * bps));
                if (swingData[i].Reset)
                {
                    data.Last().SwingSpeed *= 2;
                }
                double xHitDist = swingData[i].EntryPosition.x - swingData[i].ExitPosition.x;
                double yHitDist = swingData[i].EntryPosition.y - swingData[i].ExitPosition.y;
                data.Last().HitDistance = Math.Sqrt(Math.Pow(xHitDist, 2) + Math.Pow(yHitDist, 2));
                data.Last().HitDiff = data.Last().HitDistance / (data.Last().HitDistance + 2) + 1;
                data.Last().Stress = (swingData[i].AngleStrain + swingData[i].PathStrain) * data.Last().HitDiff;
                swingData[i].SwingDiff = data.Last().SwingSpeed * (-Math.Pow(1.4, -data.Last().SwingSpeed) + 1) * (data.Last().Stress / (data.Last().Stress + 2) + 1);
                var close = otherHand.Where(x => swingData[i].Time - 0.6 <= x.Time && swingData[i].Time + 0.6 >= x.Time).ToList();
                SwingData? closest = null;
                double buff = 1;
                if (close.Count > 0)
                {
                    if (close.Count > 1)
                    {
                        closest = Closest(close, swingData[i].Time);
                    }
                    else if (close.Count == 1)
                    {
                        closest = close[0];
                    }
                    if(closest != null)
                    {
                        if (closest.SwingFrequency >= 0.5)
                        {
                            if(closest.SwingFrequency >= 2)
                            {
                                buff = 1.05;
                            }
                            else
                            {
                                buff = 1 + Math.Pow(0.25 * closest.SwingFrequency, 3) / 4;
                            }
                            swingData.Last().SwingDiff *= buff;
                        }
                    }
                }
            }

            return swingData;
        }


        public static double CalcAverage(List<SwingData> swingData, int WINDOW)
        {
            if (swingData.Count() < 2)
            {
                return 0;
            }

            var qDiff = new Queue<double>();
            var difficultyIndex = new List<double>();

            for (int i = 1; i < swingData.Count(); i++)
            {
                if (i > WINDOW)
                {
                    qDiff.Dequeue();
                }
                qDiff.Enqueue(swingData[i].SwingDiff);
                List<double> tempList = qDiff.ToList();
                tempList.Sort();
                tempList.Reverse();
                if (i >= WINDOW)
                {
                    var windowDiff = tempList.Average() * 0.8;
                    difficultyIndex.Add(windowDiff);
                }
            }

            if (difficultyIndex.Count > 0)
            {
                return difficultyIndex.Max();
            }
            else
            {
                return 0;
            }
        }
    }
}
