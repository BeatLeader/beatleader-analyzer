using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;

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

            const double streamBuff = 1.05;

            double bps = bpm / 60;
            var buffNextRed = false;
            var buffNextBlue = false;

            foreach (var swing in swingData)
            {
                double distanceDiff = swing.PreviousDistance / (swing.PreviousDistance + 3) + 1;
                var swingSpeed = swing.SwingFrequency * distanceDiff * bps;
                if (swing.Reset)
                {
                    swingSpeed *= 2;
                }
                double xHitDist = swing.EntryPosition.x - swing.ExitPosition.x;
                double yHitDist = swing.EntryPosition.y - swing.ExitPosition.y;
                var hitDistance = Math.Sqrt(Math.Pow(xHitDist, 2) + Math.Pow(yHitDist, 2));
                var hitDiff = hitDistance / (hitDistance + 2) + 1;
                var stress = (swing.AngleStrain / 10 + swing.PathStrain) * hitDiff;
                swing.SwingDiff = swingSpeed * (-Math.Pow(1.4, -swingSpeed) + 1) * (stress / (stress + 2) + 1);
                swing.SwingDiff *= NjsBuff.CalculateNjsBuff(swing.Start.Njs);

                if (swing.Start.Type == 0)
                {
                    if (buffNextRed) swing.SwingDiff *= streamBuff;
                    buffNextRed = false;
                    buffNextBlue = true;
                }
                else
                {
                    if (buffNextBlue) swing.SwingDiff *= streamBuff;
                    buffNextBlue = false;
                    buffNextRed = true;
                }
            }

            return swingData;
        }


        public static List<PerSwing> CalcAverage(List<SwingData> swingData, int WINDOW)
        {
            if (swingData.Count < 2)
            {
                return [];
            }

            var qDiff = new CircularBuffer(stackalloc double[WINDOW]);
            var difficultyIndex = new List<PerSwing>();
            for (int i = 0; i < swingData.Count; i++)
            {
                qDiff.Enqueue(swingData[i].SwingDiff);
                if (i >= WINDOW)
                {
                    var windowDiff = Average(qDiff.Buffer);
                    difficultyIndex.Add(new(swingData[i].Time, windowDiff, swingData[i].AngleStrain + swingData[i].PathStrain));
                }
                else difficultyIndex.Add(new(swingData[i].Time, 0, swingData[i].AngleStrain + swingData[i].PathStrain));
            }

            if (difficultyIndex.Count > 0)
            {
                return difficultyIndex;
            }
            else
            {
                return [];
            }
        }
    }
}
