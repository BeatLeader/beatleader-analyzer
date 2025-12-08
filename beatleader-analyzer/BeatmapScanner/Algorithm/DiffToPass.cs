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
        private const double PARITY_ERROR_MULTIPLIER = 2.0;
        
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
        private const double CROUCH_WALL_DURING_BUFF = 1.05;

        public static void CalcSwingDiff(List<SwingData> swingData, double bpm, List<Wall> dodgeWalls = null, List<Wall> crouchWalls = null)
        {
            if (swingData.Count == 0)
            {
                return;
            }

            // Calculate swing frequency per hand (red and blue separately)
            for (int i = 0; i < swingData.Count; i++)
            {
                int currentHand = swingData[i].Start.Type;
                
                // Find previous swing of the same hand
                int prevSameHandIndex = -1;
                for (int j = i - 1; j >= 0; j--)
                {
                    if (swingData[j].Start.Type == currentHand)
                    {
                        prevSameHandIndex = j;
                        break;
                    }
                }
                
                // Find next swing of the same hand
                int nextSameHandIndex = -1;
                for (int j = i + 1; j < swingData.Count; j++)
                {
                    if (swingData[j].Start.Type == currentHand)
                    {
                        nextSameHandIndex = j;
                        break;
                    }
                }
                
                // Calculate frequency using only same-hand swings
                if (prevSameHandIndex >= 0 && nextSameHandIndex >= 0)
                {
                    swingData[i].SwingFrequency = 2 / (swingData[nextSameHandIndex].Beat - swingData[prevSameHandIndex].Beat);
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
                if (swing.ParityErrors)
                {
                    swingSpeed *= PARITY_ERROR_MULTIPLIER;
                }
                
                double xHitDist = swing.EntryPosition.x - swing.ExitPosition.x;
                double yHitDist = swing.EntryPosition.y - swing.ExitPosition.y;
                double hitDistance = Math.Sqrt(xHitDist * xHitDist + yHitDist * yHitDist);
                double hitDiff = hitDistance / (hitDistance + HIT_DISTANCE_FALLOFF) + 1.0;
                
                double stress = (swing.AngleStrain * ANGLE_STRAIN_WEIGHT + swing.PathStrain) * hitDiff;
                
                double speedFalloff = 1.0 - Math.Pow(SPEED_FALLOFF_BASE, -swingSpeed);
                double stressMultiplier = stress / (stress + STRESS_FALLOFF) + 1.0;
                // Store intermediate values on the swing for debugging/export
                swing.DistanceDiff = distanceDiff;
                swing.SwingSpeed = swingSpeed;
                swing.HitDistance = hitDistance;
                swing.HitDiff = hitDiff;
                swing.Stress = stress;
                swing.SpeedFalloff = speedFalloff;
                swing.StressMultiplier = stressMultiplier;

                swing.SwingDiff = swingSpeed * speedFalloff * stressMultiplier;

                double njsBuff = NjsBuff.CalculateNjsBuff(swing.Start.Njs);
                swing.NjsBuff = njsBuff;
                swing.SwingDiff *= njsBuff;

                int currentHand = swing.Start.Type;
                if (previousHand.HasValue && previousHand.Value != currentHand)
                {
                    swing.SwingDiff *= STREAM_BONUS;
                    swing.StreamBonusApplied = true;
                }
                previousHand = currentHand;
                double wallBuffUsed = 1.0;
                if (wallBuffs.TryGetValue(i, out double wallBuff))
                {
                    wallBuffUsed = wallBuff;
                    swing.SwingDiff *= wallBuffUsed;
                }
                swing.WallBuff = wallBuffUsed;
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
                    float wallStart = wall.BpmTime;
                    float wallDuration = wall.DurationInBeats;
                    float wallEnd = wallStart + wallDuration;

                    if (swing.Beat >= wallStart && swing.Beat <= wallEnd)
                    {
                        maxBuff = Math.Max(maxBuff, CROUCH_WALL_DURING_BUFF);
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
            float wallEnd = wall.BpmTime + wallDuration;
            return swing.Beat >= wall.BpmTime && swing.Beat <= wallEnd;
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
