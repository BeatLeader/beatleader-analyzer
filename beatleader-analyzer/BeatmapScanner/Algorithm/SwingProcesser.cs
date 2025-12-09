using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
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

            var groups = new List<List<Cube>>();
            List<Cube> current = null;
            
            for (int idx = 0; idx < cubes.Count; idx++)
            {
                var obj = cubes[idx];
                
                if (obj.Head)
                {
                    // Close previous group if exists
                    if (current != null)
                    {
                        groups.Add(current);
                    }
                    current = [obj];
                }
                else
                {
                    if (current == null)
                    {
                        // First cube doesn't have Head=true, treat it as a single-note group
                        current = [obj];
                        groups.Add(current);
                        current = null;
                    }
                    else
                    {
                        current.Add(obj);
                    }
                }
                
                // Check if this cube closes the group (Tail marker)
                if (obj.Tail && current != null)
                {
                    groups.Add(current);
                    current = null;
                }
            }
            
            // Don't forget the last group if it wasn't closed
            if (current != null)
            {
                groups.Add(current);
            }

            if (groups.Count == 0)
            {
                return new List<SwingData>();
            }
            
            var swingData = new List<SwingData>(groups.Count)
            {
                new SwingData(groups[0])
            };

            // Calculate entry and exit positions for first note
            CalcEntryExitWithMemory(null, swingData[^1]);

            int groupIndex = 0;

            for (int i = 1; i < cubes.Count; i++)
            {
                var currentBeat = cubes[i].Beat;

                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    groupIndex++;
                    
                    if (groupIndex >= groups.Count)
                    {
                        break;
                    }
                    
                    swingData.Add(new SwingData(groups[groupIndex]));

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
                    // Calculate multi-note exit position with averaged angle
                    Cube headCube = groupIndex >= 0 ? groups[groupIndex][0] : null;
                    CalcMultiNoteExit(swingData[^1], cubes[i], headCube, strictAngles);
                }
            }

            // Normalize angles between swings if within tolerance angle
            // Only for fast sections (< 1 beat) and single notes
            // Skip multi-note patterns as they are already geometrically normalized in preprocessing
            // Skip notes with bomb avoidance as they have special direction calculation
            for (int i = 1; i < swingData.Count; i++)
            {
                if (swingData[i].Beat - swingData[i - 1].Beat >= 1.0)
                {
                    continue;
                }

                if (swingData[i].Notes[0].Pattern && swingData[i].Notes[0].Head)
                {
                    continue;
                }

                if (swingData[i].Notes[0].BombAvoidance)
                {
                    continue;
                }

                NormalizeAngle(swingData[i - 1], swingData[i], strictAngles);
            }

            // Second pass: verify that direction match geometry for multi-note swings with only dots
            VerifyMultiNotes(swingData);

            for (int i = 0; i < swingData.Count; i++)
            {
                swingData[i].AngleStrain = SwingAngleStrainCalc(new List<SwingData> { swingData[i] }, isRightHand) * 4;
            }

            return swingData;
        }

        public static void VerifyMultiNotes(List<SwingData> swingData)
        {
            // Calculate geometric direction for multi-note swings
            for(int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                if(swing.Notes.Count <= 1 || !swing.Notes.All(x => x.CutDirection == 8))
                {
                    continue;
                }

                var entry = swing.Notes[0];
                var exit = swing.Notes.Last();
                var deltaX = exit.X - entry.X;
                var deltaY = exit.Y - entry.Y;
                var geometricAngle = Math.Atan2(deltaY, deltaX) * (180.0 / Math.PI);
                if (geometricAngle < 0)
                {
                    geometricAngle += 360.0;
                }

                // Calculate the reverse of the geometric angle
                var reverseGeometricAngle = (geometricAngle + 180.0) % 360.0;
                
                // Check if head note direction is closest to geometric angle or its reverse
                var headDirection = entry.Direction;
                
                // Calculate angular distance to geometric angle
                var diffToGeometric = Math.Abs(geometricAngle - headDirection);
                if (diffToGeometric > 180)
                {
                    diffToGeometric = 360 - diffToGeometric;
                }
                
                // Calculate angular distance to reverse geometric angle
                var diffToReverse = Math.Abs(reverseGeometricAngle - headDirection);
                if (diffToReverse > 180)
                {
                    diffToReverse = 360 - diffToReverse;
                }
                
                // Use the angle that is closest to the head direction
                double finalAngle = diffToGeometric <= diffToReverse ? geometricAngle : reverseGeometricAngle;

                swing.Notes.ForEach(n => n.Direction = finalAngle);
                swing.Direction = finalAngle;
            }
        }
    }
}
