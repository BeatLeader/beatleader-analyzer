using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Helper;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Converts swing metrics into pass difficulty ratings.
    /// </summary>
    internal class Difficulty
    {
        // Prod values, except DISTANCE_FALLOFF. Resets have no multiplier here: SwingCreation already doubles their SwingFrequency
        // (except false positives).
        private const double STRESS_FALLOFF = 2.0;
        // Prod 2.668 made the distance between swings almost irrelevant (0.65 vs 0.74 m: +1.7%), and maps with longer hit
        // distances were worth less than others of their stars. 1.0: closer worth across map kinds (with the 16-128 s windows
        // then used: pass share gap 0.052 -> 0.042, worth spread 0.4937 -> 0.4919). 0.5 went further on pass but left
        // tech-heavy maps overvalued.
        private const double DISTANCE_FALLOFF = 1.0;
        private const double SPEED_FALLOFF_BASE = 1.4;
        private const double ANGLE_STRAIN_STRESS = 0.05;
        private const double REPOSITIONING_STRESS = 0.3;
        private const double ROTATION_STRESS = 0.2;
        // Stream bonus
        private const double STREAM_BONUS = 1.05;
        // Wall bonus. Checked on ~3400 ranked maps against fail difficulty from attempts: dodges fit best at 1.1, crouches
        // at ~2.0 (bootstrap 1.8-2.4; prod has 1.2). Head tracking of top replays confirmed the classification.
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
                    swing.SwingDiff *= STREAM_BONUS;
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
        /// When the map is shorter than the window, the rest of the window counts as rest: the whole map's average times
        /// its length / the window (whole-map averages made very short maps outliers).
        /// </summary>
        /// With densityWeight > 0, each window's average is multiplied by (swings per second / densityReference)^weight, so a
        /// stretch with more swings counts more than one with the same swings and rests in between. Swings per second of song
        /// time: the swing difficulty already includes the speed modifier, real time would count it twice.
        // Highest density counted, in reference densities: denser stretches made the hardest maps worth too much for their
        // stars (Megametric75 +0.19 above their star range for the 5% that gained most from density). 1: sparse stretches
        // still count less, dense ones no more than the reference: with 2, long dense streams were overvalued (with the 16-128 s
        // windows: Ascension to Heaven Expert Megametric75 +2.04 above its star range, 1.81 with 1; maps with the densest peaks
        // systematically above theirs). Keeping the bonus and discounting short bursts instead (vibro) hurt the balance and
        // the maps players find hardest (The Purple Dimension has more short vibro bursts than any).
        public const double DENSITY_CAP = 1.0;

        public static double MaxAverageOverSeconds(List<SwingData> swingData, double seconds, Modifiers modifiers,
            double densityReference = 0, double densityWeight = 0)
        {
            double Density(double swingsPerSecond) => densityWeight > 0 && densityReference > 0
                ? Math.Pow(Math.Min(swingsPerSecond / densityReference, DENSITY_CAP), densityWeight)
                : 1;

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
                    best = Math.Max(best, sum / (end - start) * Density((end - start) / window));
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
                // The map is shorter than the window: the rest of the window counts as rest, so a 23-second map isn't rated
                // as if it lasted the whole window
                double length = last - swingData[0].Cubes[0].Seconds;
                return total / count * Math.Min(1, length / window) * Density(count / Math.Max(length, 1));
            }
            return Math.Max(best, 0);
        }

        /// <summary>
        /// Highest average swing difficulty over any <paramref name="swings"/> consecutive swings (0 when the map has no more
        /// swings than that, as in prod).
        /// </summary>
        public static double MaxAverageOverSwings(List<SwingData> swingData, int swings)
        {
            return CalcAverage(swingData, swings).Select(x => x.Pass).DefaultIfEmpty(0).Max();
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
