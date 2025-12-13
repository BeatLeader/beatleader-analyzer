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
        private const double DISTANCE_FALLOFF = 4.68;
        private const double ANGLE_STRAIN_WEIGHT = 0.1;
        private const double SPEED_FALLOFF_BASE = 1.4;
        // Parity Reset bonus
        private const double PARITY_ERROR_MULTIPLIER = 2.0;
        // Stream bonus
        private const double STREAM_BONUS = 1.05;
        // Wall bonus
        private const double WALL_EXTRA_DURATION = 0.5f;
        private const double DODGE_WALL_BUFF = 1.1;
        private const double CROUCH_WALL_BUFF = 1.2;

        public static void CalcSwingDiff(List<SwingData> swingData, float bpm, List<Wall> dodgeWalls = null, List<Wall> crouchWalls = null)
        {
            if (swingData.Count == 0)
            {
                return;
            }

            SwingData previousRed = null;
            SwingData previousBlue = null;
            SwingData previousSwing = null;

            // Calculate swing frequency and transition distance per hand (red and blue separately)
            for (int i = 0; i < swingData.Count; i++)
            {
                int currentHand = swingData[i].Notes[0].Type;

                if (currentHand == 0)
                {
                    previousSwing = previousRed;
                }
                else
                {
                    previousSwing = previousBlue;
                }

                // Calculate frequency using only same-hand swings
                if (previousSwing != null)
                {
                    swingData[i].SwingFrequency = 1 / (swingData[i].BpmTime - previousSwing.BpmTime);
                    
                    // Calculate transition distance from previous exit to current entry
                    swingData[i].HitDistance = CalculateTransitionDistance(
                        previousSwing.ExitPosition,
                        previousSwing.Direction,
                        swingData[i].EntryPosition,
                        swingData[i].Direction
                    );
                }
                else
                {
                    swingData[i].SwingFrequency = 0;
                    swingData[i].HitDistance = 0;
                }

                if (currentHand == 0)
                {
                    previousRed = swingData[i];
                }
                else
                {
                    previousBlue = swingData[i];
                }
            }

            // Swing is in BpmTime, so we don't have to worry about BPM changes here
            double bps = bpm / 60.0;
            int? previousHand = null;

            var wallBuffs = (dodgeWalls != null || crouchWalls != null) ? AnalyzeWallInfluence(swingData, dodgeWalls, crouchWalls) : new Dictionary<int, double>();

            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                // https://www.desmos.com/calculator/mshzoffzgs
                double distanceDiff = swing.BezierCurveDistance / (swing.BezierCurveDistance + DISTANCE_FALLOFF) + 1;
                
                double swingSpeed = swing.SwingFrequency * distanceDiff * bps;
                if (swing.ParityErrors)
                {
                    swingSpeed *= PARITY_ERROR_MULTIPLIER;
                }

                // https://www.desmos.com/calculator/mshzoffzgs
                double hitDiff = swingData[i].HitDistance / (swingData[i].HitDistance + DISTANCE_FALLOFF) + 1.0;
                
                double stress = (swing.AngleStrain * ANGLE_STRAIN_WEIGHT + swing.PathStrain) * hitDiff;
                // https://www.desmos.com/calculator/nl9wpe3fdo
                double speedFalloff = 1.0 - Math.Pow(SPEED_FALLOFF_BASE, -swingSpeed);
                // https://www.desmos.com/calculator/lcpwvisblz
                double stressMultiplier = stress / (stress + STRESS_FALLOFF) + 1.0;

                // Store intermediate values on the swing for debugging/export
                swing.DistanceDiff = distanceDiff;
                swing.SwingSpeed = swingSpeed;
                swing.HitDistance = swingData[i].HitDistance;
                swing.HitDiff = hitDiff;
                swing.Stress = stress;
                swing.SpeedFalloff = speedFalloff;
                swing.StressMultiplier = stressMultiplier;

                swing.SwingDiff = swingSpeed * speedFalloff * stressMultiplier;

                double njsBuff = NjsBuff.CalculateNjsBuff(swing.Notes[0].Njs);
                swing.NjsBuff = njsBuff;
                swing.SwingDiff *= njsBuff;

                int currentHand = swing.Notes[0].Type;
                if (previousHand.HasValue && previousHand.Value != currentHand)
                {
                    swing.SwingDiff *= STREAM_BONUS;
                    swing.StreamBonusApplied = true;
                }
                previousHand = currentHand;

                if (wallBuffs.TryGetValue(i, out double wallBuff))
                {
                    swing.SwingDiff *= wallBuff;
                    swing.WallBuff = wallBuff;
                }
            }
        }

        private static double CalculateTransitionDistance(
            (double x, double y) prevExitPos,
            double prevExitAngle,
            (double x, double y) currentEntryPos,
            double currentEntryAngle)
        {
            // Calculate straight-line distance between positions
            double dx = currentEntryPos.x - prevExitPos.x;
            double dy = currentEntryPos.y - prevExitPos.y;
            double straightDistance = Math.Sqrt(dx * dx + dy * dy);

            // Convert angles from degrees to radians
            double prevExitAngleRad = prevExitAngle * Math.PI / 180.0;
            double currentEntryAngleRad = currentEntryAngle * Math.PI / 180.0;
            
            // Get direction vectors (normalized)
            double prevExitDirX = Math.Cos(prevExitAngleRad);
            double prevExitDirY = Math.Sin(prevExitAngleRad);
            double currentEntryDirX = Math.Cos(currentEntryAngleRad);
            double currentEntryDirY = Math.Sin(currentEntryAngleRad);

            // Calculate how far you swing in the exit direction
            // Project the transition vector onto the exit direction
            double projectionOnExit = dx * prevExitDirX + dy * prevExitDirY;
            
            // If projection is positive, you're swinging away from the target
            // You need to travel that distance out, then come back to reach the target
            double effectiveDistance = straightDistance;
            
            if (projectionOnExit > 0)
            {
                // Swinging away from target: distance = go out + come back to target
                // Total = projectionOnExit (going out) + straightDistance (to reach target from start)
                effectiveDistance = straightDistance + projectionOnExit;
            }
            
            // Special case: same or very close position with opposite swing directions
            const double CLOSE_THRESHOLD = 0.1; // Grid units
            if (straightDistance < CLOSE_THRESHOLD)
            {
                // Calculate angle difference between the two swing directions
                double angleDiff = Math.Abs(currentEntryAngle - prevExitAngle);
                
                // Normalize to 0-180 range
                if (angleDiff > 180)
                {
                    angleDiff = 360 - angleDiff;
                }
                
                // For opposite directions (angle ~180), need to swing out and come back
                // Use a base distance of 1 for opposite swings at same position
                if (angleDiff > 135) // Near-opposite directions
                {
                    double resetDistance = 1;
                    effectiveDistance = Math.Max(effectiveDistance, resetDistance);
                }
            }

            return effectiveDistance;
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
            return swing.Notes[0].Seconds >= wallStart && swing.Notes[0].Seconds <= wallEnd;
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
                    difficultyIndex.Add(new(swingData[i].BpmTime, windowDiff, swingData[i].AngleStrain + swingData[i].PathStrain));
                }
                else
                {
                    difficultyIndex.Add(new(swingData[i].BpmTime, 0, swingData[i].AngleStrain + swingData[i].PathStrain));
                }
            }

            return difficultyIndex;
        }
    }
}
