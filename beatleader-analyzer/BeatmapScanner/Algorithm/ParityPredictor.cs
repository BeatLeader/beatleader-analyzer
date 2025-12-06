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
            
            var prevExitY = (int)Math.Round(prevSwing.ExitPosition.y * 3);
            var prevExitX = (int)Math.Round(prevSwing.ExitPosition.x * 3);
            var currentEntryY = currentSwing.Start.Layer;
            var currentEntryX = currentSwing.Start.Line;

            var relevantBombs = bombs.Where(b => 
                b.BpmTime >= prevSwing.Time && 
                b.BpmTime <= currentSwing.Time
            ).ToList();

            if (relevantBombs.Count == 0)
            {
                return (false, 0, 0);
            }

            foreach (var bomb in relevantBombs)
            {
                bool blocksPath = IsBombBlockingPath(bomb, prevExitX, prevExitY, currentEntryX, currentEntryY);
                if (blocksPath)
                {
                    return (true, bomb.y, bomb.x);
                }
            }

            return (false, 0, 0);
        }

        private static bool IsBombBlockingPath(Bomb bomb, int prevX, int prevY, int currX, int currY)
        {
            if (bomb.x == currX && bomb.y == currY)
            {
                return true;
            }

            if (bomb.x == prevX && bomb.y == prevY)
            {
                return true;
            }

            int deltaX = currX - prevX;
            int deltaY = currY - prevY;
            
            if (deltaX == 0 && deltaY == 0)
            {
                return false;
            }

            if (deltaX == 0)
            {
                if (bomb.x == prevX)
                {
                    int minY = Math.Min(prevY, currY);
                    int maxY = Math.Max(prevY, currY);
                    return bomb.y >= minY && bomb.y <= maxY;
                }
            }
            else if (deltaY == 0)
            {
                if (bomb.y == prevY)
                {
                    int minX = Math.Min(prevX, currX);
                    int maxX = Math.Max(prevX, currX);
                    return bomb.x >= minX && bomb.x <= maxX;
                }
            }
            else
            {
                int bombDeltaX = bomb.x - prevX;
                int bombDeltaY = bomb.y - prevY;
                
                if (Math.Abs(deltaX) == Math.Abs(deltaY))
                {
                    if (Math.Abs(bombDeltaX) == Math.Abs(bombDeltaY))
                    {
                        double ratio = (double)bombDeltaX / deltaX;
                        if (ratio >= 0 && ratio <= 1 && bombDeltaY == (int)(deltaY * ratio))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    double crossProduct = deltaX * bombDeltaY - deltaY * bombDeltaX;
                    double distance = Math.Abs(crossProduct) / Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                    
                    if (distance < 0.7)
                    {
                        double dotProduct = bombDeltaX * deltaX + bombDeltaY * deltaY;
                        double lengthSquared = deltaX * deltaX + deltaY * deltaY;
                        double t = dotProduct / lengthSquared;
                        
                        if (t >= 0 && t <= 1)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
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
                    baseCost += BOMB_REPOSITIONING_COST * 0.5;
                }
                else
                {
                    baseCost -= BOMB_REPOSITIONING_COST;
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
