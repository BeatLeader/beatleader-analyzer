using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Analyzer.BeatmapScanner.Algorithm
{
    internal class DiffToPass
    {
        public static List<SwingData> CalcSwingDiff(List<SwingData> swingData, double bpm)
        {
            if (swingData.Count == 0)
            {
                return swingData;
            }
            double bps = bpm / 60;
            var data = new List<SData>();
            swingData[0].SwingDiff = 0;
            for (int i = 1; i < swingData.Count; i++)
            {
                double distanceDiff = swingData[i].PreviousDistance / (swingData[i].PreviousDistance + 3) + 1;
                data.Add(new SData(swingData[i].SwingFrequency * distanceDiff * bps));
                if (swingData[i].Reset)
                {
                    data[^1].SwingSpeed *= 2;
                }
                double xHitDist = swingData[i].EntryPosition.x - swingData[i].ExitPosition.x;
                double yHitDist = swingData[i].EntryPosition.y - swingData[i].ExitPosition.y;
                data[^1].HitDistance = Math.Sqrt(Math.Pow(xHitDist, 2) + Math.Pow(yHitDist, 2));
                data[^1].HitDiff = data[^1].HitDistance / (data[^1].HitDistance + 2) + 1;
                data[^1].Stress = (Math.Min(swingData[i].AngleStrain * 0.5, 0.5) + swingData[i].PathStrain) * data[^1].HitDiff;
                swingData[i].SwingDiff = data[^1].SwingSpeed * (-Math.Pow(1.4, -data[^1].SwingSpeed) + 1) * (data[^1].Stress / (data[^1].Stress + 2) + 1);
            }

            return swingData;
        }

        public static double CalcRollingAverage(List<SwingData> swingData, double bps)
        {
            if (swingData.Count < 2)
            {
                return 0;
            }

            var difficultyIndex = new List<double>();

            for (int i = 0; i < swingData.Count; i++)
            {
                var window = new List<double>
                {
                    swingData[i].SwingDiff
                };
                var limit = swingData[i].Time + bps;
                for (int j = i + 1; j < swingData.Count; j++)
                {
                    if (swingData[j].Time <= limit && swingData[j].Time >= swingData[i].Time) window.Add(swingData[j].SwingDiff);
                    else break; 
                }
                var windowDiff = window.Average() * 0.8;
                difficultyIndex.Add(windowDiff);
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
