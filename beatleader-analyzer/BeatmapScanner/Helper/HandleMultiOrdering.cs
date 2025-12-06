using static Analyzer.BeatmapScanner.Helper.Helper;
using static Analyzer.BeatmapScanner.Helper.FindAngleViaPosition;
using static Analyzer.BeatmapScanner.Helper.MultiNoteHitDetector;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Orders simultaneous notes by distance from swing entry point.
    /// </summary>
    internal class HandleMultiOrdering
    {
        public static void HandleSimultaneousNotes(List<Cube> cubes, float bpm)
        {
            if (cubes.Count < 2)
            {
                return;
            }

            var timeGroupedCubes = cubes.GroupBy(x => x.Time).ToDictionary(x => x.Key, x => x.ToArray());

#if NET9_0_OR_GREATER
            var timeGroupedCubeIndices = new OrderedDictionary<float, int[]>(
                cubes
                    .Select((val, index) => (index, val))
                    .Where(x => x.val.CutDirection != 8)
                    .GroupBy(x => x.val.Time, x => x.index)
                    .Select(x => new KeyValuePair<float, int[]>(x.Key, [.. x]))
            );
#endif

            int skipCount = 0;

            for (int n = 0; n < cubes.Count - 1; n++)
            {
                if (skipCount > 0)
                {
                    skipCount--;
                    continue;
                }

                Cube currentCube = cubes[n];
                
                if (currentCube.Time == cubes[n + 1].Time)
                {
                    Cube[] simultaneousNotes = timeGroupedCubes[currentCube.Time];
                    skipCount = simultaneousNotes.Length - 1;

                    double swingDirection = DetermineSwingDirection(cubes, currentCube, n, bpm
#if NET9_0_OR_GREATER
                        , timeGroupedCubeIndices
#endif
                    );

                    if (swingDirection == -1)
                    {
                        continue;
                    }

                    (double x, double y) entryPoint = CalculateEntryPoint(cubes, n, swingDirection);
                    OrderSimultaneousNotesByDistance(cubes, n, simultaneousNotes.Length, entryPoint);
                }
            }
        }

        private static double DetermineSwingDirection(List<Cube> cubes, Cube currentCube, int currentIndex, float bpm
#if NET9_0_OR_GREATER
            , OrderedDictionary<float, int[]> timeGroupedCubeIndices
#endif
        )
        {
            var timeGroupedCubes = cubes.Where(c => c.Time == currentCube.Time).ToArray();
            Cube arrowNote = timeGroupedCubes.LastOrDefault(c => c.CutDirection != 8);

            if (arrowNote != null)
            {
                return ReverseCutDirection(Mod(DirectionToDegree[arrowNote.CutDirection] + arrowNote.AngleOffset, 360));
            }

            int foundArrowIndex;

#if NET9_0_OR_GREATER
            var timeIndex = timeGroupedCubeIndices.IndexOf(currentCube.Time);
            if (timeIndex != -1)
            {
                timeIndex++;
            }
            else
            {
                timeIndex = -1;
                for (int i = 0; i < timeGroupedCubeIndices.Count; i++)
                {
                    if (timeGroupedCubeIndices.GetAt(i).Key > currentCube.Time)
                    {
                        timeIndex = i;
                        break;
                    }
                }
            }
            foundArrowIndex = timeIndex == -1 ? -1 : timeGroupedCubeIndices.GetAt(timeIndex).Value[0];
#else
            foundArrowIndex = cubes.FindIndex(c => c.CutDirection != 8 && c.Time > currentCube.Time);
#endif

            if (foundArrowIndex == -1)
            {
                return -1;
            }

            Cube futureArrow = cubes[foundArrowIndex];
            double direction = ReverseCutDirection(Mod(DirectionToDegree[futureArrow.CutDirection] + futureArrow.AngleOffset, 360));

            for (int i = foundArrowIndex - 1; i > currentIndex; i--)
            {
                if (!AreNotesCloseInDepth(cubes[i], cubes[i + 1], bpm))
                {
                    direction = ReverseCutDirection(direction);
                }
            }

            return direction;
        }

        private static (double x, double y) CalculateEntryPoint(List<Cube> cubes, int startIndex, double swingDirection)
        {
            if (startIndex > 0)
            {
                return SimSwingPos(cubes[startIndex - 1].Line, cubes[startIndex - 1].Layer, swingDirection);
            }
            else
            {
                return SimSwingPos(1.5, 1.0, swingDirection);
            }
        }

        private static void OrderSimultaneousNotesByDistance(List<Cube> cubes, int startIndex, int count, (double x, double y) entryPoint)
        {
            var notesWithDistances = new List<(Cube cube, double distance, int index)>();
            
            for (int i = 0; i < count; i++)
            {
                int cubeIndex = startIndex + i;
                Cube cube = cubes[cubeIndex];
                
                double distance = Math.Sqrt(
                    Math.Pow(entryPoint.x - cube.Line, 2) + 
                    Math.Pow(entryPoint.y - cube.Layer, 2)
                );
                
                notesWithDistances.Add((cube, distance, cubeIndex));
            }

            notesWithDistances.Sort((a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < count; i++)
            {
                cubes[startIndex + i] = notesWithDistances[i].cube;
            }
        }
    }
}
