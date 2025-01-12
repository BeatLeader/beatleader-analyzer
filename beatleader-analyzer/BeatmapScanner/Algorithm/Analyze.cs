using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;
using System.Linq;
using beatleader_analyzer.BeatmapScanner.Data;

namespace Analyzer.BeatmapScanner.Algorithm
{
    internal class Analyze
    {
        public static (List<double>, List<PerSwing>) UseLackWizAlgorithm(List<Cube> red, List<Cube> blue, float bpm, float njs)
        {
            double leftDiff = 0;
            double rightDiff = 0;
            double tech = 0;
            List<double> value = [];
            List<SwingData> redSwingData = [];
            List<SwingData> blueSwingData = [];
            List<List<SwingData>> redPatternData = [];
            List<List<SwingData>> bluePatternData = [];
            List<PerSwing> redPerSwing = [];
            List<PerSwing> bluePerSwing = [];
            List<SwingData> data = [];

            if (red.Count > 2)
            {
                FlowDetector.Detect(red, bpm, njs, false);
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
                    data.AddRange(redSwingData);
                }
            }

            if (blue.Count > 2)
            {
                FlowDetector.Detect(blue, bpm, njs, true);
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
                    data.AddRange(blueSwingData);
                }
            }

            if(redSwingData != null)
            {
                redSwingData = DiffToPass.CalcSwingDiff(redSwingData, bpm);
                redPerSwing = DiffToPass.CalcAverage(redSwingData, 8);
                leftDiff = redPerSwing.Select(x => x.Pass).Max();
                var temp = DiffToPass.CalcAverage(redSwingData, 16);
                leftDiff += temp.Select(x => x.Pass).Max();
                redPerSwing = AddList(redPerSwing, temp);
                temp = DiffToPass.CalcAverage(redSwingData, 32);
                leftDiff += temp.Select(x => x.Pass).Max();
                redPerSwing = AddList(redPerSwing, temp);
                temp = DiffToPass.CalcAverage(redSwingData, 48);
                leftDiff += temp.Select(x => x.Pass).Max();
                redPerSwing = AddList(redPerSwing, temp);
                temp = DiffToPass.CalcAverage(redSwingData, 96);
                leftDiff += temp.Select(x => x.Pass).Max();
                redPerSwing = AddList(redPerSwing, temp);
                leftDiff /= 5;
            }
            if(blueSwingData != null)
            {
                blueSwingData = DiffToPass.CalcSwingDiff(blueSwingData, bpm);
                bluePerSwing = DiffToPass.CalcAverage(blueSwingData, 8);
                rightDiff = bluePerSwing.Select(x => x.Pass).Max();
                var temp = DiffToPass.CalcAverage(blueSwingData, 16);
                rightDiff += temp.Select(x => x.Pass).Max();
                bluePerSwing = AddList(bluePerSwing, temp);
                temp = DiffToPass.CalcAverage(blueSwingData, 32);
                rightDiff += temp.Select(x => x.Pass).Max();
                bluePerSwing = AddList(bluePerSwing, temp);
                temp = DiffToPass.CalcAverage(blueSwingData, 48);
                rightDiff += temp.Select(x => x.Pass).Max();
                bluePerSwing = AddList(bluePerSwing, temp);
                temp = DiffToPass.CalcAverage(blueSwingData, 96);
                rightDiff += temp.Select(x => x.Pass).Max();
                bluePerSwing = AddList(bluePerSwing, temp);
                rightDiff /= 5;
            }

            if (data.Count > 2)
            {
                // We can sort the original list here, as only count and average is accessed after this line
                data.Sort(CompareAngleAndPathStrain);
                tech = AverageAnglePath(CollectionsMarshal.AsSpan(data)[(int)(data.Count * 0.25)..]);
            }

            double balanced_pass = leftDiff * 0.5 + rightDiff * 0.5;

            value.Add(balanced_pass);
            double balanced_tech = tech * (-(Math.Pow(1.4, -balanced_pass)) + 1);
            value.Add(balanced_tech);
            double low_note_nerf = 1 / (1 + Math.Pow(Math.E, -0.6 * (data.Count / 100 + 1.5)));
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
