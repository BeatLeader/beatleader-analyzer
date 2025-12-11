using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
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
            if (cubes == null || cubes.Count == 0)
            {
                return;
            }

            SimpleParity(cubes, isRightHand, bombs);
        }

        private static void SimpleParity(List<Cube> cubes, bool isRightHand, List<Bomb> bombs)
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

            // Start with forehand
            bool currentParity = true;

            // First swing
            int firstNoteIdx = swingIndices[0];
            cubes[firstNoteIdx].Forehand = currentParity;
            cubes[firstNoteIdx].ParityErrors = false;
            cubes[firstNoteIdx].BombAvoidance = false;

            // Apply parity to all notes in first pattern group
            if (cubes[firstNoteIdx].Pattern && cubes[firstNoteIdx].Head)
            {
                for (int i = firstNoteIdx + 1; i < n && cubes[i].Pattern && !cubes[i].Head; i++)
                {
                    cubes[i].Forehand = currentParity;
                    cubes[i].ParityErrors = false;
                    cubes[i].BombAvoidance = false;
                }
            }

            // Process remaining swings
            for (int swingIdx = 1; swingIdx < numSwings; swingIdx++)
            {
                int currNoteIdx = swingIndices[swingIdx];
                int prevSwingHeadIdx = swingIndices[swingIdx - 1];

                // Get the tail of previous swing for direction comparison
                int prevSwingLastIdx = prevSwingHeadIdx;
                if (cubes[prevSwingHeadIdx].Pattern && cubes[prevSwingHeadIdx].Head)
                {
                    for (int i = prevSwingHeadIdx + 1; i < n && cubes[i].Pattern && !cubes[i].Head; i++)
                    {
                        if (cubes[i].Tail)
                        {
                            prevSwingLastIdx = i;
                            break;
                        }
                    }
                }

                // Check for bomb avoidance using PreprocessNotes.AnalyzeBombInfluence
                (bool hasBombAvoidance, bool parityFlip, double playerX, double playerY) = bombs != null 
                    ? PreprocessNotes.AnalyzeBombInfluence(cubes, prevSwingHeadIdx, currNoteIdx, bombs) 
                    : (false, false, -1.0, -1.0);

                bool sameDirection = IsSameDir(cubes[prevSwingLastIdx].Direction, cubes[currNoteIdx].Direction);

                // Simple rule: if same direction, it's a parity error
                if (sameDirection || parityFlip)
                {
                    // Keep same parity (error!)
                    cubes[currNoteIdx].Forehand = currentParity;
                    cubes[currNoteIdx].ParityErrors = true;
                }
                else
                {
                    // Different direction: flip parity (normal alternation)
                    currentParity = !currentParity;
                    cubes[currNoteIdx].Forehand = currentParity;
                    cubes[currNoteIdx].ParityErrors = false;
                }

                cubes[currNoteIdx].BombAvoidance = hasBombAvoidance;

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
    }
}
