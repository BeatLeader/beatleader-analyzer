using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.CalculateBaseEntryExit;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.IsSameDirection;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.FindAngleViaPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.GridPositionHelper;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Converts analyzed cubes into swing data with entry/exit positions.
    /// Multi-note hits are combined into single swings.
    /// </summary>
    internal class SwingProcesser
    {
        public static List<SwingData> Process(List<Cube> cubes)
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
            
            var swingData = new List<SwingData>(cubes.Count);

            swingData.Add(new SwingData(cubes[0].Time, cubes[0].Direction, cubes[0]));
            (swingData[^1].EntryPosition, swingData[^1].ExitPosition) = 
                CalcBaseEntryExit((cubes[0].Line, cubes[0].Layer), cubes[0].Direction);

            for (int i = 1; i < cubes.Count; i++)
            {
                var previousAngle = swingData[^1].Angle;
                var currentBeat = cubes[i].Time;
                var currentAngle = cubes[i].Direction;
                (double x, double y) currentPosition = (cubes[i].Line, cubes[i].Layer);

                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    swingData.Add(new SwingData(currentBeat, currentAngle, cubes[i]));
                    (swingData[^1].EntryPosition, swingData[^1].ExitPosition) = CalcBaseEntryExit(currentPosition, currentAngle);
                    
                    if (cubes[i].Chain)
                    {
                        double angleInRadians = ConvertDegreesToRadians(currentAngle);
                        double cosAngle = Math.Cos(angleInRadians);
                        double sinAngle = Math.Sin(angleInRadians);
                        
                        // Chain tail position in meters using centered grid
                        (double tailX, double tailY) = GridToMeters(cubes[i].TailLine, cubes[i].TailLayer);
                        swingData[^1].ExitPosition = (
                            (tailX + cosAngle * NOTE_SIZE) * cubes[i].Squish, 
                            (tailY + sinAngle * NOTE_SIZE) * cubes[i].Squish
                        );
                    }
                }
                else
                {
                    int headIndex = -1;
                    for (int h = headCubes.Length - 1; h >= 0; h--)
                    {
                        if (headCubes[h] < i)
                        {
                            headIndex = headCubes[h];
                            break;
                        }
                    }

                    if (headIndex >= 0)
                    {
                        currentAngle = FindAngleViaPos(cubes[i], cubes[headIndex], previousAngle, true);
                    }

                    swingData[^1].Angle = currentAngle;
                    
                    double angleInRadians = ConvertDegreesToRadians(currentAngle);
                    double cosAngle = Math.Cos(angleInRadians);
                    double sinAngle = Math.Sin(angleInRadians);
                    
                    // Multi-note exit position in meters using centered grid
                    (double noteX, double noteY) = GridToMeters(currentPosition.x, currentPosition.y);
                    double noteExitX = noteX + cosAngle * NOTE_SIZE;
                    double noteExitY = noteY + sinAngle * NOTE_SIZE;

                    swingData[^1].ExitPosition = (noteExitX, noteExitY);
                }
            }

            return swingData;
        }
    }
}
