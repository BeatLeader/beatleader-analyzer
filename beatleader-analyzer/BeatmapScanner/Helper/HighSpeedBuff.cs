using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Near the hands' speed limit, extra speed costs more than linearly (the swing difficulty is linear in the swing
    /// frequency). Each swing gets up to x(1 + Buff) on its pass difficulty by its hand's sustained EBPM around it (mean
    /// swing frequency of WindowSwings consecutive swings of that hand, centered on it, as AnalyzeMap's peak sustained EBPM
    /// counts them), in real time: smoothstep from StartEbpm to FullEbpm, flat past it. Sustained, so a single fast double
    /// in a slower map doesn't count.
    /// </summary>
    public static class HighSpeedBuff
    {
        public static double StartEbpm { get; set; } = 350;
        public static double FullEbpm { get; set; } = 390;
        /// <summary>Extra pass difficulty at FullEbpm and over (0.1 = x1.1); 0 turns it off. 0.15: maps at 350+ EBPM (nomod)
        /// get 26 plays at 750pp+ (13 without), the best at 800.1 and 799.9 (0.3 made 13 plays at 800+)</summary>
        public static double Buff { get; set; } = 0.15;
        public static int WindowSwings { get; set; } = 4;

        /// <summary>Sets a setting by name, for experiments (rerate HighSpeed.Buff=0.1)</summary>
        public static void Set(string name, double value)
        {
            var prop = typeof(HighSpeedBuff).GetProperty(name) ?? throw new ArgumentException(name);
            prop.SetValue(null, prop.PropertyType == typeof(int) ? (int)value : value);
        }

        /// <summary>Each swing's multiplier; swingData: both hands, in time order</summary>
        public static double[] Calculate(List<SwingData> swingData, double speedMult)
        {
            var buffs = new double[swingData.Count];
            Array.Fill(buffs, 1.0);
            if (Buff <= 0 || FullEbpm <= StartEbpm) return buffs;

            foreach (int hand in new[] { 0, 1 })
            {
                var indices = new List<int>();
                for (int i = 0; i < swingData.Count; i++)
                {
                    if (swingData[i].Cubes[0].Type == hand) indices.Add(i);
                }
                int window = Math.Min(WindowSwings, indices.Count);
                if (window < 2) continue;

                for (int k = 0; k < indices.Count; k++)
                {
                    int start = Math.Clamp(k - (window - 1) / 2, 0, indices.Count - window);
                    double sum = 0;
                    for (int j = start; j < start + window; j++)
                    {
                        sum += swingData[indices[j]].SwingFrequency;
                    }
                    // Hz to EBPM (one swing per half beat), song time to real time
                    double ebpm = sum / window * 60.0 / 2 * speedMult;
                    double t = Math.Clamp((ebpm - StartEbpm) / (FullEbpm - StartEbpm), 0, 1);
                    buffs[indices[k]] = 1 + Buff * t * t * (3 - 2 * t);
                }
            }
            return buffs;
        }
    }
}
