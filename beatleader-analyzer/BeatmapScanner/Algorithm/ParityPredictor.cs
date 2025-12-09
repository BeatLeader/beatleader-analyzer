using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.IsSameDirection;
using static beatleader_parser.VNJS.Easings;

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

        public static void Predict(List<Cube> cubes, bool isRightHand, List<Bomb> bombs = null)
        {
            if (cubes == null || cubes.Count == 0)
            {
                return;
            }

            OptimizeParityDynamic(cubes, isRightHand, bombs);
        }

        private static void OptimizeParityDynamic(List<Cube> cubes, bool isRightHand, List<Bomb> bombs)
        {
            int n = cubes.Count;
            if (n == 0) return;

            // Build list of pattern group head indices (includes single notes and head notes of patterns)
            var swingIndices = new List<int>();
            for (int i = 0; i < n; i++)
            {
                // Include note if it's NOT part of a pattern, OR if it's the HEAD of a pattern
                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    swingIndices.Add(i);
                }
            }

            int numSwings = swingIndices.Count;
            if (numSwings == 0) return;

            var dpCost = new double[numSwings, 2];
            var dpPrev = new int[numSwings, 2];
            var bombInfluences = new (bool hasBombs, bool parityFlip, double playerX, double playerY)[numSwings];

            int firstIdx = swingIndices[0];
            dpCost[0, 0] = CalculateAngleStrain(cubes[firstIdx].Direction, false, isRightHand);
            dpCost[0, 1] = CalculateAngleStrain(cubes[firstIdx].Direction, true, isRightHand);
            dpPrev[0, 0] = -1;
            dpPrev[0, 1] = -1;
            bombInfluences[0] = (false, false, -1, -1);

            for (int swingIdx = 1; swingIdx < numSwings; swingIdx++)
            {
                int currNoteIdx = swingIndices[swingIdx];
                int prevNoteIdx = swingIndices[swingIdx - 1];
                
                bool sameDirection = IsSameDir(cubes[prevNoteIdx].Direction, cubes[currNoteIdx].Direction);
                var bombInfluence = bombs != null ? AnalyzeBombInfluence(cubes, prevNoteIdx, currNoteIdx, bombs) : (false, false, -1, -1);
                bombInfluences[swingIdx] = bombInfluence;

                for (int currParity = 0; currParity <= 1; currParity++)
                {
                    bool isForehand = currParity == 1;
                    double angleCost = CalculateAngleStrain(cubes[currNoteIdx].Direction, isForehand, isRightHand);

                    double minCost = double.MaxValue;
                    int bestPrevParity = -1;

                    for (int prevParity = 0; prevParity <= 1; prevParity++)
                    {
                        double transitionCost = CalculateTransitionCost(
                            prevParity, currParity, sameDirection, (bombInfluence.hasBombs, bombInfluence.parityFlip));

                        double totalCost = dpCost[swingIdx - 1, prevParity] + angleCost + transitionCost;

                        if (totalCost < minCost)
                        {
                            minCost = totalCost;
                            bestPrevParity = prevParity;
                        }
                    }

                    dpCost[swingIdx, currParity] = minCost;
                    dpPrev[swingIdx, currParity] = bestPrevParity;
                }
            }

            int finalParity = dpCost[numSwings - 1, 0] <= dpCost[numSwings - 1, 1] ? 0 : 1;

            var swingParityPath = new int[numSwings];
            swingParityPath[numSwings - 1] = finalParity;

            for (int swingIdx = numSwings - 1; swingIdx > 0; swingIdx--)
            {
                swingParityPath[swingIdx - 1] = dpPrev[swingIdx, swingParityPath[swingIdx]];
            }

            // Apply parity to all notes based on their swing group
            int firstNoteIdx = swingIndices[0];
            cubes[firstNoteIdx].Forehand = swingParityPath[0] == 1;
            cubes[firstNoteIdx].ParityErrors = false;
            cubes[firstNoteIdx].BombAvoidance = false;

            // Apply parity from first swing to all notes in its pattern group
            if (cubes[firstNoteIdx].Pattern && cubes[firstNoteIdx].Head)
            {
                for (int i = firstNoteIdx + 1; i < n && cubes[i].Pattern && !cubes[i].Head; i++)
                {
                    cubes[i].Forehand = cubes[firstNoteIdx].Forehand;
                    cubes[i].ParityErrors = false;
                    cubes[i].BombAvoidance = false;
                }
            }

            for (int swingIdx = 1; swingIdx < numSwings; swingIdx++)
            {
                int currNoteIdx = swingIndices[swingIdx];
                int prevSwingHeadIdx = swingIndices[swingIdx - 1];
                
                // Get the last note of the previous swing (either single note or tail of pattern)
                int prevSwingLastIdx = prevSwingHeadIdx;
                if (cubes[prevSwingHeadIdx].Pattern && cubes[prevSwingHeadIdx].Head)
                {
                    // Find the tail of the previous pattern
                    for (int i = prevSwingHeadIdx + 1; i < n && cubes[i].Pattern && !cubes[i].Head; i++)
                    {
                        if (cubes[i].Tail)
                        {
                            prevSwingLastIdx = i;
                            break;
                        }
                    }
                }
                
                cubes[currNoteIdx].Forehand = swingParityPath[swingIdx] == 1;
                
                bool sameDirection = IsSameDir(cubes[prevSwingLastIdx].Direction, cubes[currNoteIdx].Direction);
                bool sameParity = cubes[currNoteIdx].Forehand == cubes[prevSwingHeadIdx].Forehand;

                cubes[currNoteIdx].ParityErrors = sameDirection && sameParity;
                cubes[currNoteIdx].BombAvoidance = bombInfluences[swingIdx].hasBombs;

                // Apply same parity to all notes in this pattern group
                if (cubes[currNoteIdx].Pattern && cubes[currNoteIdx].Head)
                {
                    for (int i = currNoteIdx + 1; i < n && cubes[i].Pattern && !cubes[i].Head; i++)
                    {
                        cubes[i].Forehand = cubes[currNoteIdx].Forehand;
                        cubes[i].ParityErrors = cubes[currNoteIdx].ParityErrors;
                        cubes[i].BombAvoidance = cubes[currNoteIdx].BombAvoidance;
                        
                        if (cubes[i].Tail) break;
                    }
                }
            }
        }

        public static (bool hasBombs, bool parityFlip, double playerX, double playerY) AnalyzeBombInfluence(List<Cube> cubes, int prevSwingHeadIndex, int currentSwingHeadIndex, List<Bomb> bombs)
        {
            if (bombs == null || bombs.Count == 0)
            {
                return (false, false, -1, -1);
            }

            // Get the tail of the previous swing (for patterns) or the head itself (for single notes)
            var prevSwingTailCube = cubes[prevSwingHeadIndex];
            if (cubes[prevSwingHeadIndex].Pattern && cubes[prevSwingHeadIndex].Head)
            {
                // Find the tail of the pattern
                for (int i = prevSwingHeadIndex + 1; i < cubes.Count && cubes[i].Pattern && !cubes[i].Head; i++)
                {
                    if (cubes[i].Tail)
                    {
                        prevSwingTailCube = cubes[i];
                        break;
                    }
                }
            }
            
            var currentCube = cubes[currentSwingHeadIndex];

            // Get ALL bombs between previous swing's tail and current swing's head
            var bombsBetween = bombs
                .Where(b => b.BpmTime > prevSwingTailCube.Beat && b.BpmTime < currentCube.Beat)
                .OrderBy(b => b.BpmTime)
                .ToList();

            var allRelevantBombs = bombsBetween.ToList();

            if (allRelevantBombs.Count == 0)
            {
                return (false, false, -1, -1);
            }

            // Calculate player's position after previous swing
            // Player swings to maximum extent in that direction, clamped to grid bounds
            double prevAngleRadians = prevSwingTailCube.Direction * Math.PI / 180.0;
            double prevDirX = Math.Cos(prevAngleRadians);
            double prevDirY = Math.Sin(prevAngleRadians);

            // Calculate target position in swing direction (large distance to ensure grid edge)
            double targetX = prevSwingTailCube.X + prevDirX * 10.0;
            double targetY = prevSwingTailCube.Y + prevDirY * 10.0;

            // Clamp to grid bounds: X [0, 3], Y [0, 2]
            double playerX = Math.Clamp(targetX, 0, 3);
            double playerY = Math.Clamp(targetY, 0, 2);

            bool encounteredBomb = false;
            int reversalCount = 0;
            bool parityFlip = false;

            // Simulate player trying to recover from swing and encountering bombs
            foreach (var bomb in allRelevantBombs)
            {
                // Check if bomb is at or very close to player's current position (within 0.8 units)
                double toBombX = bomb.x - playerX;
                double toBombY = bomb.y - playerY;
                double distToBomb = Math.Sqrt(toBombX * toBombX + toBombY * toBombY);

                if (distToBomb < 0.8)
                {
                    // Bomb is at player's position! Player must avoid it
                    reversalCount++;
                    encounteredBomb = true;
                    parityFlip = !parityFlip;

                    // Player moves maximum 2 grid spaces away from bomb
                    // Determine if bomb is in a corner or edge
                    bool bombInHorizontalEdge = bomb.x <= 0 || bomb.x >= 3;
                    bool bombInVerticalEdge = bomb.y <= 0 || bomb.y >= 2;
                    bool bombInCorner = bombInHorizontalEdge && bombInVerticalEdge;

                    if (bombInCorner)
                    {
                        // Bomb in corner: move 2 spaces in both directions
                        // (0,0) -> (2,2), (3,0) -> (1,2), (0,2) -> (2,0), (3,2) -> (1,0)
                        playerX = bomb.x <= 1.5 ? bomb.x + 2 : bomb.x - 2;
                        playerY = bomb.y <= 1.0 ? bomb.y + 2 : bomb.y - 2;
                    }
                    else if (bombInHorizontalEdge)
                    {
                        // Bomb at left/right edge: move 2 spaces horizontally only
                        // (0,y) -> (2,y), (3,y) -> (1,y)
                        playerX = bomb.x <= 1.5 ? bomb.x + 2 : bomb.x - 2;
                        // Keep Y position (stay at same row)
                    }
                    else if (bombInVerticalEdge)
                    {
                        // Bomb at top/bottom edge: move 2 spaces vertically only
                        // (x,0) -> (x,2), (x,2) -> (x,0)
                        playerY = bomb.y <= 1.0 ? bomb.y + 2 : bomb.y - 2;
                        // Keep X position (stay at same column)
                    }
                    
                    // Clamp to valid grid bounds
                    playerX = Math.Clamp(playerX, 0, 3);
                    playerY = Math.Clamp(playerY, 0, 2);
                }
            }

            // Return player position after bomb avoidance (or -1,-1 if no bombs encountered)
            return (encounteredBomb, parityFlip, encounteredBomb ? playerX : -1, encounteredBomb ? playerY : -1);
        }

        private static double CalculateTransitionCost(int fromParity, int toParity, bool sameDirection, (bool hasBombs, bool parityFlip) bombInfluence)
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
                if (bombInfluence.parityFlip)
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
