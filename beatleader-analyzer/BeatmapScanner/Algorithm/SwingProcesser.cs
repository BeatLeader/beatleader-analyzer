using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.CalculateEntryExit;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
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
            Cube firstTail = groups[0].Count > 1 ? groups[0].Find(c => c.Tail) : null;
            CalcEntryExit(swingData[^1], firstTail);

            int groupIndex = 0;

            for (int i = 1; i < cubes.Count; i++)
            {
                var currentBeat = cubes[i].BpmTime;

                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    groupIndex++;
                    
                    if (groupIndex >= groups.Count)
                    {
                        break;
                    }
                    
                    swingData.Add(new SwingData(groups[groupIndex]));

                    // Calculate entry and exit positions
                    Cube groupTail = groups[groupIndex].Count > 1 ? groups[groupIndex].Find(c => c.Tail) : null;
                    CalcEntryExit(swingData[^1], groupTail);

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

            // Second pass: verify that direction match geometry for multi-note swings
            VerifyMultiNotes(swingData);

            // Normalize angles between swings if within tolerance angle
            // Only for fast sections (< 1 beat) and single notes
            // Skip multi-note patterns as they are already geometrically normalized in preprocessing
            // Skip notes with bomb avoidance as they have special direction calculation
            for (int i = 1; i < swingData.Count; i++)
            {
                if (swingData[i].BpmTime - swingData[i - 1].BpmTime >= 1.0)
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

            swingData[0].AngleStrain = SwingAngleStrainCalc(swingData[0], null, isRightHand) * 4;
            bool isLinear = false;

            for (int i = 1; i < swingData.Count; i++)
            {
                swingData[i].AngleStrain = SwingAngleStrainCalc(swingData[i], swingData[i - 1], isRightHand) * 4;

                // Check if it's linear
                double target = ReverseCutDirection(swingData[i - 1].Direction);
                double dirDiff = Math.Abs(((target - swingData[i].Direction + 540) % 360) - 180);
                bool directionMatches = dirDiff < 22.5;
                var prevPos = swingData[i - 1].Notes[^1];
                var currPos = swingData[i].Notes[0];
                double dx = currPos.X - prevPos.X;
                double dy = currPos.Y - prevPos.Y;
                double geometricAngle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                if (geometricAngle < 0) geometricAngle += 360;
                double geoDiff = Math.Abs(((target - geometricAngle + 540) % 360) - 180);
                bool movementMatchesDirection = geoDiff < 22.5;
                if (directionMatches && movementMatchesDirection)
                {
                    // Can be considered linear movement, reduce strain
                    if (isLinear) swingData[i].AngleStrain *= 0.25;
                    isLinear = true;
                }
                else isLinear = false;
            }

            return swingData;
        }

        public static void VerifyMultiNotes(List<SwingData> swingData)
        {
            // Calculate geometric direction for multi-note swings
            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                if (swing.Notes.Count <= 1)
                {
                    continue;
                }

                if(swing.Notes.All(x => x.CutDirection == 8))
                {
                    var entry = swing.Notes[0];
                    var exit = swing.Notes[^1];
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
                    double selectedAngle = diffToGeometric <= diffToReverse ? geometricAngle : reverseGeometricAngle;
                    
                    // Check if this angle is too similar to the previous swing's angle
                    // If so, use the reverse angle instead
                    if (i > 0)
                    {
                        double previousAngle = swingData[i - 1].Direction;
                        double angleDiffToPrevious = Math.Abs(selectedAngle - previousAngle);
                        if (angleDiffToPrevious > 180)
                        {
                            angleDiffToPrevious = 360 - angleDiffToPrevious;
                        }
                        
                        // If the selected angle is within 45 degrees of the previous swing, use the reverse
                        const double SIMILARITY_THRESHOLD = 45.0;
                        if (angleDiffToPrevious < SIMILARITY_THRESHOLD)
                        {
                            // Use the opposite direction
                            selectedAngle = selectedAngle == geometricAngle ? reverseGeometricAngle : geometricAngle;
                        }
                    }
                    
                    swing.Direction = selectedAngle;
                    swing.Notes.ForEach(n => n.Direction = swing.Direction);
                }

                // Reorder notes based on their projection along the swing direction
                // Calculate direction vector
                double directionRadians = swing.Direction * Math.PI / 180.0;
                double dirX = Math.Cos(directionRadians);
                double dirY = Math.Sin(directionRadians);

                // Calculate projection of each note onto the direction vector
                var notesWithProjection = swing.Notes.Select(note => new
                {
                    Note = note,
                    Projection = note.X * dirX + note.Y * dirY
                }).OrderBy(x => x.Projection).ToList();

                // Reorder the notes list
                swing.Notes.Clear();
                swing.Notes.AddRange(notesWithProjection.Select(x => x.Note));

                // Update Head and Tail markers
                for (int j = 0; j < swing.Notes.Count; j++)
                {
                    swing.Notes[j].Head = j == 0;
                    swing.Notes[j].Tail = j == swing.Notes.Count - 1;
                }

                // Recalculate entry and exit positions based on swing direction and all notes of the group positions
                CalcEntryExit(swing, swing.Notes[^1]);
            }
        }
    }
}
