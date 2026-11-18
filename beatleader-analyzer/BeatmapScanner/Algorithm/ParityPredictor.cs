using Analyzer.BeatmapScanner.Data;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Parser.Map.Difficulty.V3.Grid;

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
    /// The weights are fitted on top players' replays (portaBLe ParityCheck). With UsePairResets the reset cost also
    /// depends on the two arrows (RESET_PAIR_ADJUSTMENT): players almost never reset after "up then right" but nearly
    /// always after "down then down".
    /// </summary>
    public class ParityPredictor
    {
        /// <summary>true (default): reset cost includes RESET_PAIR_ADJUSTMENT. Set before rating maps.</summary>
        public static bool UsePairResets { get; set; } = true;

        /// <summary>The costs that change with the map style (Predict): flow maps, where players reset almost only for
        /// bombs; mixed maps, older maps with some resets anywhere; reset maps, played with resets for a big part</summary>
        public class StyleCosts
        {
            /// <summary>Added to the cost of every reset but bomb resets (and down -> down on flow hands)</summary>
            public double Extra { get; set; }
            /// <summary>Times RESET_PAIR_ADJUSTMENT</summary>
            public double PairScale { get; set; } = 1;
            public double ResetTime { get; set; } = RESET_TIME;
            public double ResetExponent { get; set; } = RESET_EXPONENT;
            public double ResetConstant { get; set; } = RESET_CONSTANT;
            public double BackhandCost { get; set; } = BACKHAND_COST;
            public double RotationWeight { get; set; } = ROTATION_WEIGHT;
            public double ComfortWeight { get; set; } = COMFORT_WEIGHT;
            public double BombResetFactor { get; set; } = BOMB_RESET_FACTOR;
        }

        // The first pass, which tells the style
        private static readonly StyleCosts PlainCosts = new();
        public static StyleCosts FlowCosts { get; } = new() { Extra = 3 };
        public static StyleCosts MixedCosts { get; } = new();
        public static StyleCosts ResetCosts { get; } = new() { Extra = -0.5 };

        /// <summary>Non-bomb resets per swing (first pass) from which a hand is a reset map</summary>
        public static double ResetMapRate { get; set; } = 0.15;
        /// <summary>From this non-bomb reset rate, a hand is a mixed map when the median gap before its non-bomb resets is at
        /// least MixedResetGapBeats and under MixedResetRunShare of them have another one within 2 swings</summary>
        public static double MixedResetRate { get; set; } = 0.04;
        public static double MixedResetGapBeats { get; set; } = 1;
        public static double MixedResetRunShare { get; set; } = 0.5;
        /// <summary>Down arrow after down arrow never charged FlowCosts.Extra</summary>
        public static bool DownDownFree { get; set; } = true;

        /// <summary>Sets a setting by name, for experiments: ResetMapRate, flow.Extra, mixed.PairScale, reset.ResetTime...</summary>
        public static void Set(string name, double value)
        {
            var dot = name.IndexOf('.');
            if (dot < 0)
            {
                var prop = typeof(ParityPredictor).GetProperty(name) ?? throw new ArgumentException(name);
                prop.SetValue(null, prop.PropertyType == typeof(bool) ? value != 0 : value);
                return;
            }
            var target = name[..dot] switch { "flow" => FlowCosts, "mixed" => MixedCosts, "reset" => ResetCosts, _ => throw new ArgumentException(name) };
            (typeof(StyleCosts).GetProperty(name[(dot + 1)..]) ?? throw new ArgumentException(name)).SetValue(target, value);
        }

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

        public static void Predict(List<Cube> cubes, bool isRightHand, List<Bomb> bombs = null)
        {
            int n = cubes.Count;
            if (n == 0) return;
            // Kept for portaBLe's ParityCheck (swing creation can change Direction afterwards)
            foreach (var cube in cubes) cube.ParityDirection = cube.Direction;

            // One swing per single note or pattern head
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

            // Swings with a bomb since the previous swing (any bomb: players also reset for bombs the Analyzer doesn't mark
            // as avoided) or a bomb avoidance: bomb resets, never charged StyleCosts.Extra
            var bombReset = new bool[numSwings];
            var bombTimes = bombs?.Select(b => b.Seconds).OrderBy(t => t).ToArray() ?? Array.Empty<float>();
            for (int i = 1; i < numSwings; i++)
            {
                float from = cubes[GetTailIndex(cubes, swingIndices[i - 1], n)].Seconds, to = cubes[swingIndices[i]].Seconds;
                int k = Array.BinarySearch(bombTimes, from);
                k = k < 0 ? ~k : k;
                while (k < bombTimes.Length && bombTimes[k] <= from) k++;
                bombReset[i] = (k < bombTimes.Length && bombTimes[k] <= to) || HasBombAvoidance(cubes, swingIndices[i]);
            }
            // Never charged FlowCosts.Extra either: down arrow after down arrow, which players reset on even in flow maps (84%
            // of the plain costs' down -> down resets there, all of them in the other maps; left -> left, right -> right: 0%)
            var flowFree = (bool[])bombReset.Clone();
            if (DownDownFree)
            {
                for (int i = 1; i < numSwings; i++)
                {
                    if (cubes[GetTailIndex(cubes, swingIndices[i - 1], n)].CutDirection == 1 && cubes[swingIndices[i]].CutDirection == 1) flowFree[i] = true;
                }
            }

            // First pass with the plain costs, then again with the costs of the hand's style, told from the first pass's
            // non-bomb resets:
            // - reset map: 15%+ of the swings;
            // - mixed map: 4%+, spaced out (median gap 1 beat or more) and mostly alone (players reset when they have the time),
            //   where a flow map's false ones come at its pace and in runs (one swing read the wrong way round flips its
            //   neighbours too);
            // - flow map: any other hand. The plain costs' few non-bomb resets there are mostly false (players reset on 19-42%
            //   of them under 2% of swings).
            bool[] optimalParity = CheapestPath(cubes, swingIndices, isRightHand, PlainCosts, bombReset);
            var gaps = new List<float>();
            var resetSwings = new List<int>();
            for (int i = 1; i < numSwings; i++)
            {
                if (optimalParity[i] == optimalParity[i - 1] && !bombReset[i])
                {
                    gaps.Add(cubes[swingIndices[i]].BpmTime - cubes[GetTailIndex(cubes, swingIndices[i - 1], n)].BpmTime);
                    resetSwings.Add(i);
                }
            }
            double rate = gaps.Count / (double)(numSwings - 1);
            gaps.Sort();
            int inRuns = 0;
            for (int k = 0; k < resetSwings.Count; k++)
            {
                if ((k > 0 && resetSwings[k] - resetSwings[k - 1] <= 2) || (k + 1 < resetSwings.Count && resetSwings[k + 1] - resetSwings[k] <= 2)) inRuns++;
            }
            if (rate >= ResetMapRate)
            {
                optimalParity = CheapestPath(cubes, swingIndices, isRightHand, ResetCosts, bombReset);
            }
            else if (rate >= MixedResetRate && gaps[gaps.Count / 2] >= MixedResetGapBeats && inRuns < MixedResetRunShare * gaps.Count)
            {
                optimalParity = CheapestPath(cubes, swingIndices, isRightHand, MixedCosts, bombReset);
            }
            else
            {
                optimalParity = CheapestPath(cubes, swingIndices, isRightHand, FlowCosts, flowFree);
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
                    for (int j = noteIdx + 1; j < cubes.Count && cubes[j].Pattern && !cubes[j].Head; j++)
                    {
                        cubes[j].Forehand = forehand;
                        cubes[j].ParityErrors = parityError;
                        if (cubes[j].Tail) break;
                    }
                }
            }
        }

        private static bool HasBombAvoidance(List<Cube> cubes, int headIdx)
        {
            int tailIdx = GetTailIndex(cubes, headIdx, cubes.Count);
            for (int i = headIdx; i <= tailIdx; i++)
            {
                if (cubes[i].BombAvoidance) return true;
            }
            return false;
        }

        // Cheapest parity of every swing (true forehand), costs.Extra added to every reset but the noExtra ones
        private static bool[] CheapestPath(List<Cube> cubes, List<int> swingIndices, bool isRightHand, StyleCosts costs, bool[] noExtra)
        {
            int n = cubes.Count;
            int numSwings = swingIndices.Count;

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
                        double totalCost = cost[i - 1, prevParity] + TransitionCost(currSwing, prevSwing, isRightHand, currSingle, costs, noExtra[i]);

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
            return optimalParity;
        }

        private static double TransitionCost(SwingData current, SwingData previous, bool isRightHand, bool currentIsSingle, StyleCosts c, bool noExtra)
        {
            double comfort = Math.Abs(AngleDifference(Neutral(current.Forehand, isRightHand), current.Direction)) / 180.0;
            double cost = c.ComfortWeight * comfort * comfort;
            if (!current.Forehand) cost += c.BackhandCost;

            bool reset = previous.Forehand == current.Forehand;
            double natural = reset ? previous.Direction : Mod(previous.Direction + 180, 360);
            double rotation = Math.Abs(AngleDifference(natural, current.Direction)) / 180.0;
            cost += c.RotationWeight * Math.Pow(rotation, ROTATION_EXPONENT);

            if (reset)
            {
                double gap = Math.Max(0.02, current.Cubes[0].Seconds - previous.Cubes[^1].Seconds);
                double resetCost = Math.Pow(c.ResetTime / gap, c.ResetExponent) + c.ResetConstant;
                if (UsePairResets)
                {
                    int prevCut = previous.Cubes[0].CutDirection, cut = current.Cubes[0].CutDirection;
                    if (currentIsSingle && previous.Cubes.Count == 1 && prevCut >= 0 && prevCut < 8 && cut >= 0 && cut < 8)
                    {
                        var index = isRightHand ? RIGHT_ARROW_INDEX : LEFT_ARROW_INDEX;
                        resetCost = Math.Max(0, resetCost + c.PairScale * RESET_PAIR_ADJUSTMENT[index[prevCut], index[cut]]);
                    }
                }
                if (current.Cubes.Any(cube => cube.BombAvoidance)) resetCost *= c.BombResetFactor;
                if (!noExtra) resetCost += c.Extra;
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
