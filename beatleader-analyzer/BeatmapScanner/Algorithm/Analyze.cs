using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;
using System.Linq;
using beatleader_analyzer.BeatmapScanner.Data;
using System.Runtime.InteropServices;

namespace Analyzer.BeatmapScanner.Algorithm
{
    internal class Analyze
    {
        public static (List<double>, List<PerSwing>) UseLackWizAlgorithm(List<Cube> red, List<Cube> blue, float bpm)
        {
            double tech = 0;
            List<double> value = [];
            List<SwingData> redSwingData = [];
            List<SwingData> blueSwingData = [];
            List<List<SwingData>> redPatternData = [];
            List<List<SwingData>> bluePatternData = [];
            List<PerSwing> redPerSwing = [];
            List<PerSwing> bluePerSwing = [];
            List<SwingData> combinedSwingData = [];

            if (red.Count > 2)
            {
                FlowDetector.Detect(red, bpm, false);
                redSwingData = SwingProcesser.Process(red);
                if (redSwingData != null)
                {
                    redPatternData = PatternSplitter.Split(redSwingData);
                }
                if (redSwingData != null && redPatternData != null)
                {
                    redSwingData = ParityPredictor.Predict(redPatternData, false);
                }
                if (redSwingData != null)
                {
                    SwingCurve.Calc(redSwingData, false);
                }
                if (redSwingData != null)
                {
                    combinedSwingData.AddRange(redSwingData);
                }
            }

            if (blue.Count > 2)
            {
                FlowDetector.Detect(blue, bpm, true);
                blueSwingData = SwingProcesser.Process(blue);
                if (blueSwingData != null)
                {
                    bluePatternData = PatternSplitter.Split(blueSwingData);
                }
                if (blueSwingData != null && bluePatternData != null)
                {
                    blueSwingData = ParityPredictor.Predict(bluePatternData, true);
                }
                if (blueSwingData != null)
                {
                    SwingCurve.Calc(blueSwingData, true);
                }
                if (blueSwingData != null)
                {
                    combinedSwingData.AddRange(blueSwingData);
                }
            }

            combinedSwingData.Sort((a, b) => a.Time.CompareTo(b.Time));
            double balanced_pass = 0.0;

            if (combinedSwingData.Count > 0)
            {
                const double oneSaberNerf = 0.35;

                combinedSwingData = DiffToPass.CalcSwingDiff(combinedSwingData, bpm);
                redSwingData = combinedSwingData.Where(x => x.Start.Type == 0).ToList();
                blueSwingData = combinedSwingData.Where(x => x.Start.Type == 1).ToList();

                var windowSizes = new HashSet<int> { 8, 16, 32, 64, 128 };
                var passDiffRed = 0.0;
                var passDiffBlue = 0.0;
                var passDiffCombined = 0.0;

                foreach (var windowSize in windowSizes)
                {
                    if (redSwingData.Count > 1) passDiffRed += DiffToPass.CalcAverage(redSwingData, windowSize / 2).Select(x => x.Pass).Max();
                    if (blueSwingData.Count > 1) passDiffBlue += DiffToPass.CalcAverage(blueSwingData, windowSize / 2).Select(x => x.Pass).Max();
                    passDiffCombined += DiffToPass.CalcAverage(combinedSwingData, windowSize).Select(x => x.Pass).Max();
                }
                passDiffRed /= windowSizes.Count;
                passDiffBlue /= windowSizes.Count;
                passDiffCombined /= windowSizes.Count;

                // a bit pepega, but to explain:
                // - easiest separate hand divided by the easiest of:
                // -- hardest separate hand
                // -- combined hands
                // - the result of which is then capped to 1
                var handsRatio = Math.Min(Math.Min(passDiffRed, passDiffBlue) / Math.Min(Math.Max(passDiffRed, passDiffBlue), passDiffCombined), 1.0);
                var nerfMult = 1 - (1 - handsRatio) * oneSaberNerf;

                balanced_pass = passDiffCombined * nerfMult * 0.752;
            }

            if (combinedSwingData.Count > 2)
            {
                foreach (var item in combinedSwingData)
                {
                    var buff = NjsBuff.CalculateNjsBuff(item.Start.Njs);
                    item.AngleStrain *= buff;
                    item.PathStrain *= buff;
                }

                // We can sort the original list here, as only count and average is accessed after this line
                combinedSwingData.Sort(CompareAngleAndPathStrain);
                tech = AverageAnglePath(CollectionsMarshal.AsSpan(combinedSwingData)[(int)(combinedSwingData.Count * 0.25)..]);
            }

            value.Add(balanced_pass);
            double balanced_tech = tech * (-(Math.Pow(1.4, -balanced_pass)) + 1);
            value.Add(balanced_tech);
            double low_note_nerf = 1 / (1 + Math.Pow(Math.E, -1.4 - (combinedSwingData.Count / 50)));
            value.Add(low_note_nerf);

            List<PerSwing> perSwing = [];
            perSwing.AddRange(redPerSwing);
            perSwing.AddRange(bluePerSwing);
            perSwing = [.. perSwing.OrderBy(x => x.Time)];
            perSwing.ForEach(x => { x.Pass /= 5;
                x.Tech /= 5;
            });

            return (value, perSwing);
        }

        private static readonly Comparer<SwingData> CompareAngleAndPathStrain = Comparer<SwingData>.Create((a, b) => (a.AngleStrain + a.PathStrain).CompareTo(b.AngleStrain + b.PathStrain));

        public static double AverageAnglePath(Span<SwingData> list)
        {
            double sum = 0;
            foreach(SwingData val in list)
            {
                sum += val.AngleStrain + val.PathStrain;
            }
            return sum / list.Length;
        }

        public static double AveragePattern(Span<SwingData> list)
        {
            double sum = 0;
            foreach(SwingData val in list)
            {
                sum += val.Pattern;
            }
            return sum / list.Length;
        }

        public static List<PerSwing> AddList(List<PerSwing> list, List<PerSwing> list2)
        {
            List<PerSwing> newList = [];
            List<double> Pass = [];
            List<double> Tech = [];

            foreach (var val in list)
            {
                Pass.Add(val.Pass);
                Tech.Add(val.Tech);
            }
            for (int i = 0; i < list2.Count; i++)
            {
                if (Pass.Count <= i) return newList;
                Pass[i] += list2[i].Pass;
                Tech[i] += list2[i].Tech;
                newList.Add(new(list2[i].Time, Pass[i], Tech[i]));
            }

            return newList;
        }
    }
}
