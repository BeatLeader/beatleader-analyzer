using static Analyzer.BeatmapScanner.Helper.Helper;
using static Analyzer.BeatmapScanner.Helper.FindAngleViaPosition;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Analyzer.BeatmapScanner.Helper
{
    internal class HandlePatternOrdering
    {
        public static void HandlePattern(List<Cube> cubes)
        {
            var length = 0;
            var timeGroupedCubes = cubes.GroupBy(x => x.Time).ToDictionary(x => x.Key, x => x.ToArray());

#if NET9_0_OR_GREATER
            var timeGroupedCubeIndizes = new OrderedDictionary<float, int[]>(
                cubes
                    .Select((val, index) => (index, val))
                    .Where(x => x.val.CutDirection != 8)
                    .GroupBy(x => x.val.Time, x => x.index)
                    .Select(x => new KeyValuePair<float, int[]>(x.Key, [.. x]))
            );
#endif
            for (int n = 0; n < cubes.Count - 2; n++)
            {
                if (length > 0)
                {
                    length--;
                    continue;
                }

                Cube cube = cubes[n];
                if (cube.Time == cubes[n + 1].Time)
                {
                    // Pattern found
                    Cube[] cubesAtCurrentTime = timeGroupedCubes[cube.Time];
                    length = cubesAtCurrentTime.Length - 1;
                    Cube arrowLastElement = cubesAtCurrentTime.LastOrDefault(c => c.CutDirection != 8);
                    double direction = 0;
                    if (arrowLastElement is null)
                    {
                        // Pattern got no arrow
                        #if NET9_0_OR_GREATER
                        var timeIndex = timeGroupedCubeIndizes.IndexOf(cube.Time);
                        if (timeIndex != -1)
                        {
                            // If we found something at the current time its simply the next element
                            timeIndex++;
                        }
                        else
                        {
                            // If we dont then we have to try to find the first time after our current time
                            for (int i = 0; i < timeGroupedCubeIndizes.Count; i++)
                            {
                                if (timeGroupedCubeIndizes.GetAt(i).Key > cube.Time)
                                {
                                    timeIndex = i;
                                    break;
                                }
                            }
                        }
                        var foundArrowIndex = timeIndex == -1 ? -1 : timeGroupedCubeIndizes.GetAt(timeIndex).Value[0];
                        #else
                        var foundArrowIndex = cubes.FindIndex(c => c.CutDirection != 8 && c.Time > cube.Time);
                        #endif
                        if (foundArrowIndex != -1)
                        {
                            var foundArrow = cubes[foundArrowIndex];
                            // An arrow note is found after the note
                            direction = ReverseCutDirection(Mod(DirectionToDegree[foundArrow.CutDirection] + foundArrow.AngleOffset, 360));
                            for (int i = foundArrowIndex - 1; i > n; i--)
                            {
                                // Reverse for every dot note in between
                                if (cubes[i + 1].Time - cubes[i].Time >= 0.25)
                                {
                                    direction = ReverseCutDirection(direction);
                                }
                            }
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Use the arrow to determine the direction
                        direction = ReverseCutDirection(Mod(DirectionToDegree[arrowLastElement.CutDirection] + arrowLastElement.AngleOffset, 360));
                    }
                    // Simulate a swing to determine the entry point of the pattern
                    (double x, double y) pos;
                    if (n > 0)
                    {
                        pos = SimSwingPos(cubes[n - 1].Line, cubes[n - 1].Layer, direction);
                    }
                    else
                    {
                        pos = SimSwingPos(cubes[0].Line, cubes[0].Layer, direction);
                    }
                    // Calculate the distance of each note based on the new position
                    List<double> distance = new();
                    for (int i = n; i < n + length + 1; i++)
                    {
                        distance.Add(Math.Sqrt(Math.Pow(pos.y - cubes[i].Layer, 2) + Math.Pow(pos.x - cubes[i].Line, 2)));
                    }
                    // Re-order the notes in the proper order
                    for (int i = 0; i < distance.Count; i++)
                    {
                        for (int j = n; j < n + length; j++)
                        {
                            if (distance[j - n + 1] < distance[j - n])
                            {
                                Swap(cubes, j, j + 1);
                                Swap(distance, j - n + 1, j - n);
                            }
                        }
                    }
                }
            }
        }
    }
}
