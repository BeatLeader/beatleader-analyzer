using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;

namespace Analyzer.BeatmapScanner.Algorithm
{
    internal class Analyze
    {
        public static List<double> UseLackWizAlgorithm(List<Cube> red, List<Cube> blue, float bpm, float njs)
        {
            double tech = 0;
            List<double> value = new();
            List<SwingData> redSwingData = new();
            List<SwingData> blueSwingData = new();
            List<List<SwingData>> redPatternData = new();
            List<List<SwingData>> bluePatternData = new();
            List<SwingData> data = new();
            List<SwingData> compiled = new();

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
                compiled = DiffToPass.CalcSwingDiff(redSwingData, bpm);
            }
            if(blueSwingData != null)
            {
                compiled.AddRange(DiffToPass.CalcSwingDiff(blueSwingData, bpm));
            }

            double balanced_pass = DiffToPass.CalcRollingAverage(compiled, bpm / 60);

            if (data.Count > 2)
            {
                // We can sort the original list here, as only count and average is accessed after this line
                data.Sort(CompareAngleAndPathStrain);
                tech = AverageAnglePath(CollectionsMarshal.AsSpan(data)[(int)(data.Count * 0.25)..]);
            }

            value.Add(balanced_pass);
            double balanced_tech = tech * (-(Math.Pow(1.4, -balanced_pass)) + 1);
            value.Add(balanced_tech);
            double low_note_nerf = 1 / (1 + Math.Pow(Math.E, -0.6 * (data.Count / 100 + 1.5)));
            value.Add(low_note_nerf);

            return value;
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
    }
}
