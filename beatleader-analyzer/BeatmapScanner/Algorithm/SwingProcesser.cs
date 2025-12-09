using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.CalculateEntryExit;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.SwingAngleStrain;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Converts analyzed cubes into swing data with entry/exit positions.
    /// Multi-note hits are combined into single swings.
    /// </summary>
    internal class SwingProcesser
    {
        public static List<SwingData> Process(List<Cube> cubes, bool isRightHand, bool strictAngles = false)
        {
            if (cubes.Count == 0)
            {
                return new List<SwingData>();
            }

            var headIndices = new List<int>(cubes.Count / 10);
            for (int i = 0; i < cubes.Count; i++)
            {
                if (cubes[i].Head)
                {
                    headIndices.Add(i);
                }
            }
            int[] headCubes = headIndices.ToArray();
            
            var swingData = new List<SwingData>(cubes.Count)
            {
                new SwingData(cubes[0])
            };

            // Calculate entry and exit positions for first note
            CalcEntryExitWithMemory(null, swingData[^1]);

            for (int i = 1; i < cubes.Count; i++)
            {
                var currentBeat = cubes[i].Beat;

                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    swingData.Add(new SwingData(cubes[i]));

                    // Calculate entry and exit positions
                    CalcEntryExitWithMemory(swingData[^2], swingData[^1]);

                    if (cubes[i].Chain)
                    {
                        // Override exit position for chains
                        CalcChainExit(swingData[^1], cubes[i]);
                    }
                }
                else
                {
                    // Multi-note pattern: find the head note index
                    int headIndex = -1;
                    for (int h = headCubes.Length - 1; h >= 0; h--)
                    {
                        if (headCubes[h] < i)
                        {
                            headIndex = headCubes[h];
                            break;
                        }
                    }

                    // Calculate multi-note exit position with averaged angle
                    Cube headCube = headIndex >= 0 ? cubes[headIndex] : null;
                    CalcMultiNoteExit(swingData[^1], cubes[i], headCube, strictAngles);
                }
            }

            for (int i = 1; i < swingData.Count; i++)
            {
                NormalizeAngle(swingData[i - 1], swingData[i], strictAngles);
            }

            for (int i = 0; i < swingData.Count; i++)
            {
                swingData[i].AngleStrain = SwingAngleStrainCalc(new List<SwingData> { swingData[i] }, isRightHand);
            }

            return swingData;
        }
    }
}
