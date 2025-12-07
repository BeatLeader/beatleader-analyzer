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
            
            // Get bombs between previous and current swing, sorted by time
            var relevantBombs = bombs.Where(b => 
                b.Beats > prevSwing.Beat && 
                b.Beats < currentSwing.Beat
            ).OrderBy(b => b.Beats).ToList();

            if (relevantBombs.Count == 0)
            {
                return (false, 0, 0);
            }

            // Simulate player hand position over time
            // Player continues swinging in the cut direction until reaching grid boundary
            var handX = prevSwing.Start.Line;
            var handY = prevSwing.Start.Layer;
            
            // Calculate swing direction vector
            double angleRadians = prevSwing.Angle * Math.PI / 180.0;
            double dirX = Math.Cos(angleRadians);
            double dirY = Math.Sin(angleRadians);
            
            // Project hand position to grid boundary in swing direction
            // Move hand as far as possible in the swing direction
            const double swingExtent = 3.0; // Maximum swing distance
            double finalX = handX + dirX * swingExtent;
            double finalY = handY + dirY * swingExtent;
            
            // Clamp to grid boundaries
            handX = Math.Clamp((int)Math.Round(finalX), 0, 3);
            handY = Math.Clamp((int)Math.Round(finalY), 0, 2);

            bool hadToReposition = false;
            Bomb firstBlockingBomb = default;

            // Simulate each bomb encounter
            foreach (var bomb in relevantBombs)
            {
                // Check if bomb is at current hand position
                if (bomb.x == handX && bomb.y == handY)
                {
                    // Bomb forces repositioning - move to opposite side of grid
                    // Move to the position furthest from the bomb
                    handX = 3 - bomb.x;  // Flip horizontally
                    handY = 2 - bomb.y;  // Flip vertically
                    
                    if (!hadToReposition)
                    {
                        firstBlockingBomb = bomb;
                    }
                    hadToReposition = true;
                }
                // No repositioning needed if bomb is not at hand position
            }

            if (hadToReposition)
            {
                return (true, firstBlockingBomb.y, firstBlockingBomb.x);
            }

            return (false, 0, 0);
        }

        private static bool IsBombBlockingPath(Bomb bomb, int prevX, int prevY, int currX, int currY)
        {
            // Check if bomb is BETWEEN the previous exit and current entry positions
            // A bomb along the travel path IS blocking - you must deviate around it
            
            // Bomb at starting position - blocks leaving that position
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

            int bombDeltaX = bomb.x - prevX;
            int bombDeltaY = bomb.y - prevY;

            // Vertical movement
            if (deltaX == 0)
            {
                if (bomb.x == prevX)
                {
                    int minY = Math.Min(prevY, currY);
                    int maxY = Math.Max(prevY, currY);
                    // Bomb must be BETWEEN start and end (exclusive of destination)
                    return bomb.y > minY && bomb.y < maxY;
                }
            }
            // Horizontal movement
            else if (deltaY == 0)
            {
                if (bomb.y == prevY)
                {
                    int minX = Math.Min(prevX, currX);
                    int maxX = Math.Max(prevX, currX);
                    // Bomb must be BETWEEN start and end (exclusive of destination)
                    return bomb.x > minX && bomb.x < maxX;
                }
            }
            // Diagonal or complex movement
            else
            {
                if (Math.Abs(deltaX) == Math.Abs(deltaY))
                {
                    // Perfect diagonal
                    if (Math.Abs(bombDeltaX) == Math.Abs(bombDeltaY))
                    {
                        double ratio = (double)bombDeltaX / deltaX;
                        // Bomb is on the path BETWEEN start and end (not at destination)
                        if (ratio > 0 && ratio < 1.0 && bombDeltaY == (int)(deltaY * ratio))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    // Non-diagonal movement - check proximity to path
                    double crossProduct = deltaX * bombDeltaY - deltaY * bombDeltaX;
                    double distance = Math.Abs(crossProduct) / Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                    
                    if (distance < 0.7)
                    {
                        double dotProduct = bombDeltaX * deltaX + bombDeltaY * deltaY;
                        double lengthSquared = deltaX * deltaX + deltaY * deltaY;
                        double t = dotProduct / lengthSquared;
                        
                        // Bomb is on/near the path BETWEEN start and end (not at destination)
                        if (t > 0 && t < 1.0)
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
