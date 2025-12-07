using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;
using Parser.Map.Difficulty.V3.Grid;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Converts swing metrics into pass difficulty ratings.
    /// </summary>
    internal class DiffToPass
    {
        private const double STREAM_BONUS = 1.05;
        private const double RESET_MULTIPLIER = 2.0;
        
        // Distance falloff constants adjusted for meter scale
        // Previously 3.0 in normalized units, now 1.8m for meter scale
        private const double DISTANCE_FALLOFF = 1.8;
        
        // Hit distance falloff adjusted for meter scale  
        // Previously 2.0 in normalized units, now 1.2m for meter scale
        private const double HIT_DISTANCE_FALLOFF = 1.2;
        
        private const double ANGLE_STRAIN_WEIGHT = 0.1;
        private const double SPEED_FALLOFF_BASE = 1.4;
        private const double STRESS_FALLOFF = 2.0;
        private const double DODGE_WALL_BUFF = 1.01;
        private const double CROUCH_WALL_INITIAL_BUFF = 1.10;
        private const double CROUCH_WALL_DURING_BUFF = 1.05;

        public static void CalcSwingDiff(List<SwingData> swingData, double bpm, List<Wall> dodgeWalls = null, List<Wall> crouchWalls = null)
        {
            if (swingData.Count == 0)
            {
                return;
            }

            for (int i = 0; i < swingData.Count; i++)
            {
                if (i > 0 && i + 1 < swingData.Count)
                {
                    swingData[i].SwingFrequency = 2 / (swingData[i + 1].Beat - swingData[i - 1].Beat);
                }
                else
                {
                    swingData[i].SwingFrequency = 0;
                }
            }

            double bps = bpm / 60.0;
            int? previousHand = null;

            var wallBuffs = (dodgeWalls != null || crouchWalls != null) ? AnalyzeWallInfluence(swingData, dodgeWalls, crouchWalls) : new Dictionary<int, double>();

            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                double distanceDiff = swing.ExcessDistance / (swing.ExcessDistance + DISTANCE_FALLOFF) + 1.0;
                
                double swingSpeed = swing.SwingFrequency * distanceDiff * bps;
                if (swing.Reset)
                {
                    swingSpeed *= RESET_MULTIPLIER;
                }
                
                double xHitDist = swing.EntryPosition.x - swing.ExitPosition.x;
                double yHitDist = swing.EntryPosition.y - swing.ExitPosition.y;
                double hitDistance = Math.Sqrt(xHitDist * xHitDist + yHitDist * yHitDist);
                double hitDiff = hitDistance / (hitDistance + HIT_DISTANCE_FALLOFF) + 1.0;
                
                double stress = (swing.AngleStrain * ANGLE_STRAIN_WEIGHT + swing.PathStrain) * hitDiff;
                
                double speedFalloff = 1.0 - Math.Pow(SPEED_FALLOFF_BASE, -swingSpeed);
                double stressMultiplier = stress / (stress + STRESS_FALLOFF) + 1.0;
                swing.SwingDiff = swingSpeed * speedFalloff * stressMultiplier;
                
                swing.SwingDiff *= NjsBuff.CalculateNjsBuff(swing.Start.Njs);

                int currentHand = swing.Start.Type;
                if (previousHand.HasValue && previousHand.Value != currentHand)
                {
                    swing.SwingDiff *= STREAM_BONUS;
                }
                previousHand = currentHand;

                if (wallBuffs.TryGetValue(i, out double wallBuff))
                {
                    swing.SwingDiff *= wallBuff;
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
                    float wallStart = wall.Beats;
                    float wallDuration = wall.DurationInBeats;
                    float wallEnd = wallStart + wallDuration;

                    if (swing.Beat >= wallStart && swing.Beat <= wallEnd)
                    {
                        bool isInitial = i > 0 && swingData[i - 1].Beat < wallStart;

                        if (isInitial)
                        {
                            maxBuff = Math.Max(maxBuff, CROUCH_WALL_INITIAL_BUFF);
                        }
                        else
                        {
                            maxBuff = Math.Max(maxBuff, CROUCH_WALL_DURING_BUFF);
                        }
                    }
                    else if (i < swingData.Count - 1)
                    {
                        var nextSwing = swingData[i + 1];
                        if (swing.Beat < wallStart && nextSwing.Beat >= wallStart)
                        {
                            maxBuff = Math.Max(maxBuff, CROUCH_WALL_INITIAL_BUFF);
                        }
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
            float wallDuration = wall.DurationInBeats;
            float wallEnd = wall.Beats + wallDuration;
            return swing.Beat >= wall.Beats && swing.Beat <= wallEnd;
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
                    difficultyIndex.Add(new(swingData[i].Beat, windowDiff, swingData[i].AngleStrain + swingData[i].PathStrain));
                }
                else
                {
                    difficultyIndex.Add(new(swingData[i].Beat, 0, swingData[i].AngleStrain + swingData[i].PathStrain));
                }
            }

            return difficultyIndex;
        }
    }
}
