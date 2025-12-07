using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static Analyzer.BeatmapScanner.Helper.IsSameDirection;
using static Analyzer.BeatmapScanner.Helper.SwingAngleStrain;
using Parser.Map.Difficulty.V3.Grid;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Predicts optimal swing parity (forehand vs backhand) using dynamic programming.
    /// </summary>
    internal class ParityPredictor
    {
        private const double RESET_PENALTY = 10.0;
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
            swings[0].Reset = false;
            swings[0].BombReset = false;

            for (int i = 1; i < n; i++)
            {
                swings[i].Forehand = parityPath[i] == 1;
                
                bool sameDirection = IsSameDir(swings[i - 1].Angle, swings[i].Angle);
                bool sameParity = swings[i].Forehand == swings[i - 1].Forehand;
                
                swings[i].Reset = sameDirection && sameParity;
                swings[i].BombReset = bombInfluences[i].hasBombs;
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

            // Get bombs between previous and current swing
            var relevantBombs = bombs.Where(b =>
                b.Beats > prevSwing.Beat &&
                b.Beats < currentSwing.Beat
            ).OrderBy(b => b.Beats).ToList();

            if (relevantBombs.Count == 0)
            {
                return (false, 0, 0);
            }

            // Calculate where hand is after previous swing
            var handX = prevSwing.Start.Line;
            var handY = prevSwing.Start.Layer;

            double prevAngleRadians = prevSwing.Angle * Math.PI / 180.0;
            double prevDirX = Math.Cos(prevAngleRadians);
            double prevDirY = Math.Sin(prevAngleRadians);

            const double swingExtent = 3.0;
            double prevFinalX = handX + prevDirX * swingExtent;
            double prevFinalY = handY + prevDirY * swingExtent;

            handX = Math.Clamp((int)Math.Round(prevFinalX), 0, 3);
            handY = Math.Clamp((int)Math.Round(prevFinalY), 0, 2);

            // Calculate where hand NEEDS to be for CURRENT swing
            // To swing in a direction, you need to start from the opposite side
            double currAngleRadians = currentSwing.Angle * Math.PI / 180.0;
            double currDirX = Math.Cos(currAngleRadians);
            double currDirY = Math.Sin(currAngleRadians);

            // Ideal preparation position is opposite to swing direction from note
            int prepX = currentSwing.Start.Line;
            int prepY = currentSwing.Start.Layer;

            double idealPrepX = prepX - currDirX * swingExtent;
            double idealPrepY = prepY - currDirY * swingExtent;

            int targetPrepX = Math.Clamp((int)Math.Round(idealPrepX), 0, 3);
            int targetPrepY = Math.Clamp((int)Math.Round(idealPrepY), 0, 2);

            // Check if we need significant movement to prepare for next swing
            int movementRequired = Math.Abs(handX - targetPrepX) + Math.Abs(handY - targetPrepY);

            // If no movement needed, bombs don't matter
            if (movementRequired <= 1)
            {
                return (false, 0, 0);
            }

            // Check if any bombs are adjacent to current position or prep position
            foreach (var bomb in relevantBombs)
            {
                // Check if bomb is adjacent to current hand position
                int distFromHand = Math.Abs(bomb.x - handX) + Math.Abs(bomb.y - handY);

                // Check if bomb is adjacent to target prep position  
                // int distFromPrep = Math.Abs(bomb.x - targetPrepX) + Math.Abs(bomb.y - targetPrepY);

                // If bomb is adjacent (distance <= 1) to either position
                // and we need to move, it's a bomb reset
                if (distFromHand <= 1) // || distFromPrep <= 1)
                {
                    return (true, bomb.y, bomb.x);
                }
            }

            return (false, 0, 0);
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
                    baseCost = RESET_PENALTY;
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
                    baseCost = RESET_PENALTY + BOMB_REPOSITIONING_COST;
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
