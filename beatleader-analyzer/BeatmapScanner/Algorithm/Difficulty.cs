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
    internal class Difficulty
    {
        private const double STRESS_FALLOFF = 2.0;
        private const double DISTANCE_FALLOFF = 2.668;
        private const double SPEED_FALLOFF_BASE = 1.4;
        // Parity Reset bonus
        private const double PARITY_ERROR_MULTIPLIER = 2.0;
        // Stream bonus
        private const double STREAM_BONUS = 1.05;
        // Wall bonus
        private const double WALL_EXTRA_DURATION = 0.5f;
        private const double DODGE_WALL_BUFF = 1.1;
        private const double CROUCH_WALL_BUFF = 1.2;

        public static void CalcSwingDiff(List<SwingData> swingData, Modifiers modifiers, List<Wall> dodgeWalls = null, List<Wall> crouchWalls = null)
        {
            if (swingData.Count == 0)
            {
                return;
            }

            // Swing is in BpmTime, so we don't have to worry about BPM changes here
            double bps = modifiers.modifiedBPM / 60.0;
            int? previousHand = null;

            var wallBuffs = (dodgeWalls != null || crouchWalls != null) ? AnalyzeWallInfluence(swingData, dodgeWalls, crouchWalls) : new Dictionary<int, double>();

            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];

                // https://www.desmos.com/calculator/mshzoffzgs
                double distanceDiff = swing.HitDistance / (swing.HitDistance + DISTANCE_FALLOFF) + 1;

                double swingSpeed = swing.SwingFrequency * distanceDiff * bps;
                if (swing.ParityErrors)
                {
                    swingSpeed *= PARITY_ERROR_MULTIPLIER;
                }

                double stress;
                // Apply linear swing (3 swings in very similar direction in a row) penalty
                if (swing.IsLinear)
                {
                    stress = (swing.AngleStrain * 0.1 + swing.RepositioningDistance * 0.1 + swing.RotationAmount * 0.1) * distanceDiff;
                }
                else
                {
                    stress = (swing.AngleStrain * 1 + swing.RepositioningDistance * 1 + swing.RotationAmount * 1) * distanceDiff;
                }

                // https://www.desmos.com/calculator/nl9wpe3fdo
                double lowSpeedFalloff = 1.0 - Math.Pow(SPEED_FALLOFF_BASE, -swingSpeed);
                // https://www.desmos.com/calculator/lcpwvisblz
                double stressMultiplier = stress / (stress + STRESS_FALLOFF) + 1.0;

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
