using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.IsSameDirection;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.SwingAngleStrain;
using Parser.Map.Difficulty.V3.Grid;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Predicts optimal swing parity (forehand vs backhand) using dynamic programming.
    /// </summary>
    internal class ParityPredictor
    {
        private const double MISMATCH_PENALTY = 10.0;
        private const double BOMB_REPOSITIONING_COST = 2.0;
        private const double LEFT_FOREHAND_NEUTRAL = 292.5;
        private const double RIGHT_FOREHAND_NEUTRAL = 247.5;
        private const double LEFT_BACKHAND_NEUTRAL = 112.5;
        private const double RIGHT_BACKHAND_NEUTRAL = 67.5;

        public static void Predict(List<SwingData> swingData, bool isRightHand, List<Bomb> bombs = null)
        {
            if (swingData == null || swingData.Count == 0)
            {
                return;
            }

            OptimizeParityDynamic(swingData, isRightHand, bombs);

            for (int i = 0; i < swingData.Count; i++)
            {
                swingData[i].AngleStrain = SwingAngleStrainCalc(new List<SwingData> { swingData[i] }, isRightHand);
            }
        }

        private static void OptimizeParityDynamic(List<SwingData> swings, bool isRightHand, List<Bomb> bombs)
        {
            int n = swings.Count;
            if (n == 0) return;

            var dpCost = new double[n, 2];
            var dpPrev = new int[n, 2];
            var bombInfluences = new (bool hasBombs, int blockingLayer, int blockingLine)[n];

            dpCost[0, 0] = CalculateAngleStrain(swings[0].Angle, false, isRightHand);
            dpCost[0, 1] = CalculateAngleStrain(swings[0].Angle, true, isRightHand);
            dpPrev[0, 0] = -1;
            dpPrev[0, 1] = -1;
            bombInfluences[0] = (false, 0, 0);

            for (int i = 1; i < n; i++)
            {
                bool sameDirection = IsSameDir(swings[i - 1].Angle, swings[i].Angle);
                var bombInfluence = bombs != null ? AnalyzeBombInfluence(swings, i, bombs) : (false, 0, 0);
                bombInfluences[i] = bombInfluence;

                for (int currParity = 0; currParity <= 1; currParity++)
                {
                    bool isForehand = currParity == 1;
                    double angleCost = CalculateAngleStrain(swings[i].Angle, isForehand, isRightHand);

                    double minCost = double.MaxValue;
                    int bestPrevParity = -1;

                    for (int prevParity = 0; prevParity <= 1; prevParity++)
                    {
                        double transitionCost = CalculateTransitionCost(
                            prevParity, currParity, sameDirection, bombInfluence);

                        double totalCost = dpCost[i - 1, prevParity] + angleCost + transitionCost;

                        if (totalCost < minCost)
                        {
                            minCost = totalCost;
                            bestPrevParity = prevParity;
                        }
                    }

                    dpCost[i, currParity] = minCost;
                    dpPrev[i, currParity] = bestPrevParity;
                }
            }

            int finalParity = dpCost[n - 1, 0] <= dpCost[n - 1, 1] ? 0 : 1;

            var parityPath = new int[n];
            parityPath[n - 1] = finalParity;

            for (int i = n - 1; i > 0; i--)
            {
                parityPath[i - 1] = dpPrev[i, parityPath[i]];
            }

            swings[0].Forehand = parityPath[0] == 1;
            swings[0].ParityErrors = false;
            swings[0].BombAvoidance = false;

            for (int i = 1; i < n; i++)
            {
                swings[i].Forehand = parityPath[i] == 1;
                
                bool sameDirection = IsSameDir(swings[i - 1].Angle, swings[i].Angle);
                bool sameParity = swings[i].Forehand == swings[i - 1].Forehand;
                
                swings[i].ParityErrors = sameDirection && sameParity;
                swings[i].BombAvoidance = bombInfluences[i].hasBombs;
            }
        }

        private static (bool hasBombs, int blockingLayer, int blockingLine) AnalyzeBombInfluence(List<SwingData> swings, int currentIndex, List<Bomb> bombs)
        {
            if (bombs == null || bombs.Count == 0 || currentIndex == 0)
            {
                return (false, 0, 0);
            }

            var prevSwing = swings[currentIndex - 1];
            var currentSwing = swings[currentIndex];

            // Get bombs between previous and current swing (strictly between)
            var bombsBetween = bombs
                .Where(b => b.Beats > prevSwing.Beat && b.Beats < currentSwing.Beat)
                .OrderBy(b => b.Beats)
                .ToList();

            if (bombsBetween.Count == 0)
            {
                return (false, 0, 0);
            }

            // Calculate player's position after previous swing using same logic as FlowDetector
            // Player swings to maximum extent in that direction, clamped to grid bounds
            double prevAngleRadians = prevSwing.Angle * Math.PI / 180.0;
            double prevDirX = Math.Cos(prevAngleRadians);
            double prevDirY = Math.Sin(prevAngleRadians);

            // Calculate target position in swing direction (large distance to ensure grid edge)
            double targetX = prevSwing.Start.Line + prevDirX * 10.0;
            double targetY = prevSwing.Start.Layer + prevDirY * 10.0;

            // Clamp to grid bounds: X [0, 3], Y [0, 2]
            double playerX = Math.Clamp(targetX, 0, 3);
            double playerY = Math.Clamp(targetY, 0, 2);

            // Calculate direction needed to reach current swing note
            double toNoteX = currentSwing.Start.Line - playerX;
            double toNoteY = currentSwing.Start.Layer - playerY;
            double angleToNote = Math.Atan2(toNoteY, toNoteX) * 180.0 / Math.PI;
            if (angleToNote < 0) angleToNote += 360;

            double currentDirection = angleToNote;
            bool encounteredBomb = false;
            int lastBombX = 0;
            int lastBombY = 0;

            // Simulate player movement through bomb field (same logic as FlowDetector)
            foreach (var bomb in bombsBetween)
            {
                // Calculate vector from player position to bomb
                double toBombX = bomb.x - playerX;
                double toBombY = bomb.y - playerY;
                double distanceToBomb = Math.Sqrt(toBombX * toBombX + toBombY * toBombY);

                // Calculate if bomb is in the current movement direction
                double currentRadians = currentDirection * Math.PI / 180.0;
                double currentDirX = Math.Cos(currentRadians);
                double currentDirY = Math.Sin(currentRadians);

                // Project bomb position onto current direction
                double projection = toBombX * currentDirX + toBombY * currentDirY;

                // Check perpendicular distance (how far off the line the bomb is)
                double perpDistance = Math.Abs(toBombX * currentDirY - toBombY * currentDirX);

                // Bomb blocks the path if it matches FlowDetector criteria
                if (projection > 0.2 && perpDistance < 1.2 && distanceToBomb < 2.5)
                {
                    // Bomb blocks the path! Player must reverse direction
                    encounteredBomb = true;
                    lastBombX = bomb.x;
                    lastBombY = bomb.y;

                    // Reverse direction (simplified - just flip 180°)
                    currentDirection = (currentDirection + 180.0) % 360.0;

                    // Update player's virtual position - they moved back in the new direction
                    currentRadians = currentDirection * Math.PI / 180.0;
                    currentDirX = Math.Cos(currentRadians);
                    currentDirY = Math.Sin(currentRadians);
                    playerX += currentDirX * 0.5;
                    playerY += currentDirY * 0.5;
                }
                else
                {
                    // Bomb doesn't block - player continues in current direction
                    playerX += currentDirX * 0.3;
                    playerY += currentDirY * 0.3;
                }
            }

            // Return whether any bombs were encountered during navigation
            return (encounteredBomb, lastBombY, lastBombX);
        }

        private static double CalculateTransitionCost(int fromParity, int toParity, bool sameDirection, (bool hasBombs, int blockingLayer, int blockingLine) bombInfluence)
        {
            double baseCost;
            
            if (!sameDirection)
            {
                baseCost = fromParity == toParity ? 0.1 : 0.0;
            }
            else
            {
                if (fromParity == toParity)
                {
                    baseCost = MISMATCH_PENALTY;
                }
                else
                {
                    baseCost = 0.0;
                }
            }

            if (bombInfluence.hasBombs)
            {
                if (fromParity != toParity)
                {
                    baseCost = MISMATCH_PENALTY + BOMB_REPOSITIONING_COST;
                }
                else
                {
                    baseCost = BOMB_REPOSITIONING_COST;
                }
            }

            return baseCost;
        }

        private static double CalculateAngleStrain(double angle, bool isForehand, bool isRightHand)
        {
            double neutralAngle;
            if (isForehand)
            {
                neutralAngle = isRightHand ? RIGHT_FOREHAND_NEUTRAL : LEFT_FOREHAND_NEUTRAL;
            }
            else
            {
                neutralAngle = isRightHand ? RIGHT_BACKHAND_NEUTRAL : LEFT_BACKHAND_NEUTRAL;
            }

            double diff = Math.Abs(angle - neutralAngle);
            double deviation = 180.0 - Math.Abs(diff - 180.0);

            double normalizedStrain = deviation / 180.0;
            return normalizedStrain * normalizedStrain;
        }
    }
}
