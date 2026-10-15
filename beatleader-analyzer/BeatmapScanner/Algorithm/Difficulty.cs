using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Helper;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Converts swing metrics into pass difficulty ratings.
    /// </summary>
    /// The constants were fitted on ~11M clear / fail attempts on ranked maps (how hard each map is to pass, accounting for
    /// the players who tried it): ranking of maps by fail difficulty 0.904 -> 0.917, better for weak and strong players and
    /// on both halves of the attempt history. Stress (angle strain, repositioning) weighs much more than before; alternating
    /// hands (streams) showed no extra difficulty.
    internal class Difficulty
    {
        private const double STRESS_FALLOFF = 10.0;
        private const double DISTANCE_FALLOFF = 0.79;
        private const double SPEED_FALLOFF_BASE = 2.1;
        private const double ANGLE_STRAIN_STRESS = 0.375;
        private const double REPOSITIONING_STRESS = 0.45;
        private const double ROTATION_STRESS = 0.1;
        // Wall bonus. Checked on ~3400 ranked maps against fail difficulty from attempts (and against accuracy loss):
        // dodges fit best at 1.1; crouches at ~2.0 (bootstrap 1.8-2.4). Head tracking of top replays confirmed the
        // classification (97% of dodge walls dodged, 96% of crouch walls crouched).
        private const double WALL_EXTRA_DURATION = 0.5f;
        private const double DODGE_WALL_BUFF = 1.1;
        private const double CROUCH_WALL_BUFF = 2.0;

        public static void CalcSwingDiff(List<SwingData> swingData, Modifiers modifiers, List<Wall> dodgeWalls = null, List<Wall> crouchWalls = null)
        {
            if (swingData.Count == 0)
            {
                return;
            }

            int? previousHand = null;

            var wallBuffs = (dodgeWalls != null || crouchWalls != null) ? AnalyzeWallInfluence(swingData, dodgeWalls, crouchWalls) : new Dictionary<int, double>();

            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];

                // https://www.desmos.com/calculator/mshzoffzgs
                double distanceDiff = swing.HitDistance / (swing.HitDistance + DISTANCE_FALLOFF) + 1;

                double swingSpeed = swing.SwingFrequency * distanceDiff * modifiers.speedMult;

                double stress = (swing.AngleStrain * ANGLE_STRAIN_STRESS + swing.RepositioningDistance * REPOSITIONING_STRESS + swing.RotationAmount * ROTATION_STRESS) * distanceDiff;

                // https://www.desmos.com/calculator/nl9wpe3fdo
                double lowSpeedFalloff = 1.0 - Math.Pow(SPEED_FALLOFF_BASE, -swingSpeed);
                // https://www.desmos.com/calculator/ikntxtr1mz
                double stressMultiplier = STRESS_FALLOFF * stress / (stress + STRESS_FALLOFF) + 1.0;

                // Store intermediate values on the swing for debugging/export
                swing.DistanceDiff = distanceDiff;
                swing.SwingSpeed = swingSpeed;
                swing.Stress = stress;
                swing.LowSpeedFalloff = lowSpeedFalloff;
                swing.StressMultiplier = stressMultiplier;

                double swingDiff = swingSpeed * lowSpeedFalloff * stressMultiplier;

                swing.SwingDiff = swingDiff;

                double njsBuff = NjsBuff.CalculateNjsBuff(swing.Cubes[0].Njs, modifiers);
                swing.NjsBuff = njsBuff;
                swing.SwingDiff *= njsBuff;

                int currentHand = swing.Cubes[0].Type;
                if (previousHand.HasValue && previousHand.Value != currentHand)
                {
                    swing.IsStream = true;
                }
                previousHand = currentHand;

                if (wallBuffs.TryGetValue(i, out double wallBuff))
                {
                    swing.SwingDiff *= wallBuff;
                    swing.WallBuff = wallBuff;
                }
            }
        }
        private static Dictionary<int, double> AnalyzeWallInfluence(List<SwingData> swingData, List<Wall> dodgeWalls, List<Wall> crouchWalls)
        {
            var wallBuffs = new Dictionary<int, double>();

            if (swingData.Count == 0)
            {
                return wallBuffs;
            }

            dodgeWalls ??= new List<Wall>();
            crouchWalls ??= new List<Wall>();

            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                double maxBuff = 1.0;

                foreach (var wall in dodgeWalls)
                {
                    if (IsSwingDuringWall(swing, wall))
                    {
                        maxBuff = Math.Max(maxBuff, DODGE_WALL_BUFF);
                    }
                }

                foreach (var wall in crouchWalls)
                {
                    float wallStart = wall.Seconds;
                    float wallDuration = wall.DurationInSeconds;
                    float wallEnd = wallStart + wallDuration;

                    if (IsSwingDuringWall(swing, wall))
                    {
                        maxBuff = Math.Max(maxBuff, CROUCH_WALL_BUFF);
                    }
                }

                if (maxBuff > 1.0)
                {
                    wallBuffs[i] = maxBuff;
                }
            }

            return wallBuffs;
        }

        private static bool IsSwingDuringWall(SwingData swing, Wall wall)
        {
            double wallStart = wall.Seconds - WALL_EXTRA_DURATION;
            double wallDuration = wall.DurationInSeconds + WALL_EXTRA_DURATION;
            double wallEnd = wall.Seconds + wallDuration;
            return swing.Cubes[0].Seconds >= wallStart && swing.Cubes[0].Seconds <= wallEnd;
        }

        /// <summary>
        /// Highest average swing difficulty over any stretch of the map lasting <paramref name="seconds"/> (real time, so
        /// speed modifiers shorten the map). Windows used to be counts of swings, which made them much shorter in time on
        /// very fast maps: a 10-second burst filled every window (The Purple Dimension, Feral). In seconds, the fit to fail
        /// difficulty from attempts improved (out-of-fold R2 0.628 -> 0.664, ranking of maps above 10 pass 0.567 -> 0.615,
        /// overall ranking unchanged at 0.916), and short maps are no longer underrated.
        /// When the map is shorter than the window, the whole map is averaged.
        /// </summary>
        public static double MaxAverageOverSeconds(List<SwingData> swingData, double seconds, Modifiers modifiers)
        {
            int count = swingData.Count;
            if (count < 2)
            {
                return 0;
            }

            // Note times are song time; with a speed modifier the same real-time window covers more of the song
            double window = seconds * Math.Max(modifiers.speedMult, 0.01);
            double last = swingData[count - 1].Cubes[0].Seconds;

            double best = double.MinValue;
            double sum = 0;
            int end = 0;
            for (int start = 0; start < count; start++)
            {
                double windowEnd = swingData[start].Cubes[0].Seconds + window;
                if (windowEnd > last)
                {
                    break;
                }
                if (end < start)
                {
                    end = start;
                    sum = 0;
                }
                while (end < count && swingData[end].Cubes[0].Seconds < windowEnd)
                {
                    sum += swingData[end].SwingDiff;
                    end++;
                }
                if (end > start)
                {
                    best = Math.Max(best, sum / (end - start));
                }
                sum -= swingData[start].SwingDiff;
            }

            if (best == double.MinValue)
            {
                double total = 0;
                foreach (var swing in swingData)
                {
                    total += swing.SwingDiff;
                }
                return total / count;
            }
            return Math.Max(best, 0);
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
                    difficultyIndex.Add(new(swingData[i].BpmTime, windowDiff, swingData[i].SwingTech));
                }
                else
                {
                    difficultyIndex.Add(new(swingData[i].BpmTime, 0, swingData[i].SwingTech));
                }
            }

            return difficultyIndex;
        }
    }
}
