﻿using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static Analyzer.BeatmapScanner.Helper.CalculateBaseEntryExit;
using static Analyzer.BeatmapScanner.Helper.IsSameDirection;
using static Analyzer.BeatmapScanner.Helper.Helper;
using static Analyzer.BeatmapScanner.Helper.FindAngleViaPosition;

namespace Analyzer.BeatmapScanner.Algorithm
{
    internal class SwingProcesser
    {
        public static List<SwingData> Process(List<Cube> cubes)
        {
            var swingData = new List<SwingData>();
            (double x, double y) lastSimPos = (0, 0);
            if (cubes.Count == 0)
            {
                return swingData;
            }

            swingData.Add(new SwingData(cubes[0].Time, cubes[0].Direction, cubes[0]));
            (swingData[^1].EntryPosition, swingData[^1].ExitPosition) = CalcBaseEntryExit((cubes[0].Line, cubes[0].Layer), cubes[0].Direction);

            for (int i = 1; i < cubes.Count - 1; i++)
            {
                var previousAngle = swingData[^1].Angle;
                (double x, double y) previousPosition = (cubes[i - 1].Line, cubes[i - 1].Layer);
                var currentBeat = cubes[i].Time;
                var currentAngle = cubes[i].Direction;
                (double x, double y) currentPosition = (cubes[i].Line, cubes[i].Layer);

                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    // New swing
                    swingData.Add(new SwingData(currentBeat, currentAngle, cubes[i]));
                    (swingData[^1].EntryPosition, swingData[^1].ExitPosition) = CalcBaseEntryExit(currentPosition, currentAngle);
                    if (cubes[i].Chain)
                    {
                        var angleInRadians = ConvertDegreesToRadians(currentAngle);
                        swingData[^1].ExitPosition = ((cubes[i].TailLine * 0.333333 + Math.Cos(angleInRadians) * 0.166667 + 0.166667) * cubes[i].Squish, (cubes[i].TailLayer * 0.333333 + Math.Sin(angleInRadians) * 0.166667 + 0.166667) * cubes[i].Squish);
                    }
                }
                else // Pattern
                {
                    for (int f = i; f > 0; f--)
                    {
                        if (cubes[f].Head)
                        {
                            (currentAngle, lastSimPos) = FindAngleViaPos(cubes, i, f, previousAngle, true);
                            break;
                        }
                    }
                    if (!IsSameDir(currentAngle, previousAngle))
                    {
                        currentAngle = ReverseCutDirection(currentAngle);
                    }
                    swingData[^1].Angle = currentAngle;
                    var xtest = (swingData[^1].EntryPosition.x - (currentPosition.x * 0.333333 - Math.Cos(ConvertDegreesToRadians(currentAngle)) * 0.166667 + 0.166667)) * Math.Cos(ConvertDegreesToRadians(currentAngle));
                    var ytest = (swingData[^1].EntryPosition.y - (currentPosition.y * 0.333333 - Math.Sin(ConvertDegreesToRadians(currentAngle)) * 0.166667 + 0.166667)) * Math.Sin(ConvertDegreesToRadians(currentAngle));
                    if (xtest <= 0.001 && ytest >= 0.001)
                    {
                        swingData[^1].EntryPosition = (currentPosition.x * 0.333333 - Math.Cos(ConvertDegreesToRadians(currentAngle)) * 0.166667 + 0.166667, currentPosition.y * 0.333333 - Math.Sin(ConvertDegreesToRadians(currentAngle)) * 0.166667 + 0.166667);
                    }
                    else
                    {
                        swingData[^1].ExitPosition = (currentPosition.x * 0.333333 + Math.Cos(ConvertDegreesToRadians(currentAngle)) * 0.166667 + 0.166667, currentPosition.y * 0.333333 + Math.Sin(ConvertDegreesToRadians(currentAngle)) * 0.166667 + 0.166667);
                    }
                    var directionAngle = ReverseCutDirection(Mod(ConvertRadiansToDegrees(Math.Atan2(previousPosition.y - currentPosition.y, previousPosition.x - currentPosition.x)), 360));
                }
            }

            return swingData;
        }
    }
}
