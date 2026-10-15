using Analyzer.BeatmapScanner.Data;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Predicts the parity (forehand / backhand) of every swing of one hand, and so its resets (parity kept between two
    /// swings), with the cheapest path through all swings (dynamic programming over the two parities).
    ///
    /// The cost of going from one swing to the next adds up:
    /// - comfort: how far the swing is from the natural direction of its parity, COMFORT_WEIGHT * (degrees / 180)^2;
    /// - rotation: how far it is from the natural follow-up of the previous swing (the reverse direction when the parity
    ///   alternates, the same direction on a reset), ROTATION_WEIGHT * (degrees / 180)^ROTATION_EXPONENT;
    /// - reset (parity kept): (RESET_TIME / gap)^RESET_EXPONENT + RESET_CONSTANT (+ the pair adjustment, never below 0), so
    ///   a reset needs time: cheap after a long gap, very expensive on fast patterns. Multiplied by BOMB_RESET_FACTOR when
    ///   bombs push the hand away;
    /// - BACKHAND_COST on every backhand (players reset into forehands when they have the time).
    ///
    /// The weights were fitted on top players' replays (portaBLe ParityCheck, 198 maps). With pair resets (default,
    /// UsePairResets) the reset cost also depends on the two arrows (RESET_PAIR_ADJUSTMENT), fitted on every ranked map
    /// (3.88M swings): players almost never reset after "up then right" but nearly always after "down then down". On maps
    /// the fit didn't see: 99.75% of swings match the players (99.71% without the table, 14% fewer errors), reset-heavy
    /// maps 96.1% (95.6%). A version that also refitted the other weights matched wrist-roll maps better (Sanity Krush,
    /// SONG REQUEST) but missed many resets on speed reset maps (Speedcore Paradise), so it wasn't kept.
    /// UsePairResets = false: no pair table.
    /// </summary>
    public class ParityPredictor
    {
        /// <summary>true: pair resets (default). false: without the pair table. Set it before rating maps, e.g. from
        /// portaBLe: Analyzer.BeatmapScanner.Algorithm.ParityPredictor.UsePairResets = false;</summary>
        public static bool UsePairResets { get; set; } = true;

        private const double COMFORT_WEIGHT = 1.5;
        private const double ROTATION_WEIGHT = 1.5;
        private const double ROTATION_EXPONENT = 3.0;
        private const double RESET_TIME = 0.2;
        private const double RESET_EXPONENT = 1.5;
        private const double RESET_CONSTANT = 0.5;
        private const double BOMB_RESET_FACTOR = 0.25;
        private const double BACKHAND_COST = 0.5;

        // Added to the reset cost between two single arrows: [previous arrow][this arrow], each as 0 right, 1 up-right, 2 up,
        // 3 up-left, 4 left, 5 down-left, 6 down, 7 down-right for the right hand (the left hand uses the mirror image).
        // Negative: players reset there more than time alone says, positive: less.
        private static readonly double[,] RESET_PAIR_ADJUSTMENT =
        {
            { 0, 0.375, -2.625, 1.5, -1.3125, -0.9375, -0.375, 0 },
            { 0.5625, 0, 0, 0.75, 0.75, 0.75, 0.1875, 0.75 },
            { 1.5, 0, 0, 0, 0.375, 0.75, 0, 0.75 },
            { 1.5, -0.1875, 0, 0, 0, 0, -0.9375, -0.375 },
            { 0.375, 0.75, 1.125, 0, 0, 0, -2.625, 0.375 },
            { 2.25, -0.375, 0.75, 1.5, 0, -0.375, -3, 0.375 },
            { 1.5, 0, 0.75, 1.5, -1.5, -3, -3, -1.5 },
            { 0, -0.375, -1.3125, 1.125, -0.75, -0.375, 0, 0 },
        };

        // Arrow cut direction (0 up, 1 down, 2 left, 3 right, 4 up-left, 5 up-right, 6 down-left, 7 down-right) to the index
        // above, for the right hand and for the left hand (mirror image)
        private static readonly int[] RIGHT_ARROW_INDEX = { 2, 6, 4, 0, 3, 1, 5, 7 };
        private static readonly int[] LEFT_ARROW_INDEX = { 2, 6, 0, 4, 1, 3, 7, 5 };

        // Natural swing directions per hand and parity (same as AngleStrain)
        private const double LEFT_FOREHAND_NEUTRAL = 292.5;
        private const double RIGHT_FOREHAND_NEUTRAL = 247.5;
        private const double LEFT_BACKHAND_NEUTRAL = 112.5;
        private const double RIGHT_BACKHAND_NEUTRAL = 67.5;

        public static void Predict(List<Cube> cubes, bool isRightHand)
        {
            int n = cubes.Count;
            if (n == 0) return;
            // Kept for portaBLe's ParityCheck (swing creation can change Direction afterwards)
            foreach (var cube in cubes) cube.ParityDirection = cube.Direction;

            // Build list of swing indices
            var swingIndices = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    swingIndices.Add(i);
                }
            }

            int numSwings = swingIndices.Count;
            if (numSwings <= 1) return;

            // DP arrays: cost[i][parity] = minimum cost to reach swing i with given parity (0 backhand, 1 forehand)
            double[,] cost = new double[numSwings, 2];
            bool[,] parentParity = new bool[numSwings, 2];

            // First swing: backhand if it goes up, forehand otherwise
            if (IsSameDir(cubes[0].Direction, 90))
            {
                cost[0, 0] = 0;
                cost[0, 1] = double.MaxValue;
            }
            else
            {
                cost[0, 0] = double.MaxValue;
                cost[0, 1] = 0;
            }

            // Forward pass: minimum cost for each swing with each parity
            for (int i = 1; i < numSwings; i++)
            {
                int currIdx = swingIndices[i];
                int prevIdx = swingIndices[i - 1];
                int prevTailIdx = GetTailIndex(cubes, prevIdx, n);
                bool currSingle = !(cubes[currIdx].Pattern && cubes[currIdx].Head);

                for (int currParity = 0; currParity <= 1; currParity++)
                {
                    double minCost = double.MaxValue;
                    bool bestPrevParity = false;

                    for (int prevParity = 0; prevParity <= 1; prevParity++)
                    {
                        if (cost[i - 1, prevParity] == double.MaxValue) continue;

                        var prevSwing = CreateSwingData(cubes, prevIdx, prevTailIdx, prevParity == 1);
                        var currSwing = CreateSwingData(cubes, currIdx, currIdx, currParity == 1);
                        double totalCost = cost[i - 1, prevParity] + TransitionCost(currSwing, prevSwing, isRightHand, currSingle);

                        if (totalCost < minCost)
                        {
                            minCost = totalCost;
                            bestPrevParity = prevParity == 1;
                        }
                    }

                    cost[i, currParity] = minCost;
                    parentParity[i, currParity] = bestPrevParity;
                }
            }

            // Backward pass: reconstruct the cheapest path
            bool[] optimalParity = new bool[numSwings];
            optimalParity[numSwings - 1] = cost[numSwings - 1, 1] <= cost[numSwings - 1, 0];
            for (int i = numSwings - 1; i > 0; i--)
            {
                optimalParity[i - 1] = parentParity[i, optimalParity[i] ? 1 : 0];
            }

            // Apply to the cubes; keeping the parity of the previous swing is a reset (parity error)
            for (int i = 0; i < numSwings; i++)
            {
                int noteIdx = swingIndices[i];
                bool forehand = optimalParity[i];
                bool parityError = i > 0 && optimalParity[i] == optimalParity[i - 1];

                cubes[noteIdx].Forehand = forehand;
                cubes[noteIdx].ParityErrors = parityError;

                // Apply to pattern group
                if (cubes[noteIdx].Pattern && cubes[noteIdx].Head)
                {
                    for (int j = noteIdx + 1; j < n && cubes[j].Pattern && !cubes[j].Head; j++)
                    {
                        cubes[j].Forehand = forehand;
                        cubes[j].ParityErrors = parityError;
                        if (cubes[j].Tail) break;
                    }
                }
            }
        }

        private static double TransitionCost(SwingData current, SwingData previous, bool isRightHand, bool currentIsSingle)
        {
            double comfort = Math.Abs(AngleDifference(Neutral(current.Forehand, isRightHand), current.Direction)) / 180.0;
            double cost = COMFORT_WEIGHT * comfort * comfort;
            if (!current.Forehand) cost += BACKHAND_COST;

            bool reset = previous.Forehand == current.Forehand;
            double natural = reset ? previous.Direction : Mod(previous.Direction + 180, 360);
            double rotation = Math.Abs(AngleDifference(natural, current.Direction)) / 180.0;
            cost += ROTATION_WEIGHT * Math.Pow(rotation, ROTATION_EXPONENT);

            if (reset)
            {
                double gap = Math.Max(0.02, current.Cubes[0].Seconds - previous.Cubes[^1].Seconds);
                double resetCost = Math.Pow(RESET_TIME / gap, RESET_EXPONENT) + RESET_CONSTANT;
                if (UsePairResets)
                {
                    int prevCut = previous.Cubes[0].CutDirection, cut = current.Cubes[0].CutDirection;
                    if (currentIsSingle && previous.Cubes.Count == 1 && prevCut >= 0 && prevCut < 8 && cut >= 0 && cut < 8)
                    {
                        var index = isRightHand ? RIGHT_ARROW_INDEX : LEFT_ARROW_INDEX;
                        resetCost = Math.Max(0, resetCost + RESET_PAIR_ADJUSTMENT[index[prevCut], index[cut]]);
                    }
                }
                if (current.Cubes.Any(c => c.BombAvoidance)) resetCost *= BOMB_RESET_FACTOR;
                cost += resetCost;
            }

            return cost;
        }

        private static double Neutral(bool forehand, bool isRightHand) => forehand
            ? (isRightHand ? RIGHT_FOREHAND_NEUTRAL : LEFT_FOREHAND_NEUTRAL)
            : (isRightHand ? RIGHT_BACKHAND_NEUTRAL : LEFT_BACKHAND_NEUTRAL);

        private static int GetTailIndex(List<Cube> cubes, int headIdx, int maxIdx)
        {
            if (!cubes[headIdx].Pattern || !cubes[headIdx].Head)
                return headIdx;

            for (int i = headIdx + 1; i < maxIdx && cubes[i].Pattern && !cubes[i].Head; i++)
            {
                if (cubes[i].Tail)
                    return i;
            }
            return headIdx;
        }

        private static SwingData CreateSwingData(List<Cube> cubes, int headIdx, int tailIdx, bool forehand)
        {
            var swingCubes = new List<Cube>();

            if (headIdx == tailIdx)
            {
                swingCubes.Add(cubes[headIdx]);
            }
            else
            {
                for (int i = headIdx; i <= tailIdx && i < cubes.Count; i++)
                {
                    swingCubes.Add(cubes[i]);
                    if (cubes[i].Tail) break;
                }
            }

            return new SwingData(swingCubes)
            {
                Forehand = forehand
            };
        }
    }
}
