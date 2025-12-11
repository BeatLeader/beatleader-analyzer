using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Helper.MathHelper;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.IsSameDirection;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Predicts swing parity (forehand vs backhand) by alternating on direction changes.
    /// Detects parity errors when consecutive swings have the same direction.
    /// </summary>
    internal class ParityPredictor
    {
        public static void Predict(List<Cube> cubes, bool isRightHand, List<Bomb> bombs = null)
        {
            int n = cubes.Count;
            if (n == 0) return;

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

            // DP arrays: cost[i][parity] = minimum cost to reach swing i with given parity (false=backhand, true=forehand)
            double[,] cost = new double[numSwings, 2];
            bool[,] parentParity = new bool[numSwings, 2];

            // Initialize first swing (start with forehand)
            cost[0, 0] = double.MaxValue; // backhand start is not preferred
            cost[0, 1] = 0; // forehand start

            // Forward pass: calculate minimum cost for each swing with each parity
            for (int i = 1; i < numSwings; i++)
            {
                int currIdx = swingIndices[i];
                int prevIdx = swingIndices[i - 1];

                // Get tail of previous swing for direction
                int prevTailIdx = GetTailIndex(cubes, prevIdx, n);

                // Try both parities for current swing
                for (int currParity = 0; currParity <= 1; currParity++)
                {
                    bool isForehand = currParity == 1;
                    double minCost = double.MaxValue;
                    bool bestPrevParity = false;

                    // Try both previous parities
                    for (int prevParity = 0; prevParity <= 1; prevParity++)
                    {
                        if (cost[i - 1, prevParity] == double.MaxValue) continue;

                        // Create SwingData objects for strain calculation
                        var prevSwing = CreateSwingData(cubes, prevIdx, prevTailIdx, prevParity == 1);
                        var currSwing = CreateSwingData(cubes, currIdx, currIdx, isForehand);

                        // Calculate angle strain cost
                        double strainCost = SwingAngleStrain.ParityAngleStrainCalc(currSwing, prevSwing, isRightHand);


                        double totalCost = cost[i - 1, prevParity] + strainCost;

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

            // Backward pass: reconstruct optimal path
            bool[] optimalParity = new bool[numSwings];
            
            // Find best final parity
            bool lastParity = cost[numSwings - 1, 1] <= cost[numSwings - 1, 0];
            optimalParity[numSwings - 1] = lastParity;

            // Trace back
            for (int i = numSwings - 1; i > 0; i--)
            {
                int parityIdx = optimalParity[i] ? 1 : 0;
                optimalParity[i - 1] = parentParity[i, parityIdx];
            }

            // Apply optimal parity to cubes
            for (int i = 0; i < numSwings; i++)
            {
                int noteIdx = swingIndices[i];
                bool forehand = optimalParity[i];

                // Determine if this is a parity error
                bool parityError = false;
                if (i > 0)
                {
                    int prevIdx = swingIndices[i - 1];
                    int prevTailIdx = GetTailIndex(cubes, prevIdx, n);

                    // Mark as parity error if same parity is kept
                    if (optimalParity[i] == optimalParity[i - 1])
                    {
                        parityError = true;
                    }
                }

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

            var swing = new SwingData(swingCubes)
            {
                Forehand = forehand
            };

            return swing;
        }
    }
}
