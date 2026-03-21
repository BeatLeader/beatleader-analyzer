using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.EntryExit;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using static beatleader_analyzer.BeatmapScanner.Helper.AngleStrain;
using beatleader_analyzer.BeatmapScanner.Data;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Converts analyzed cubes into swing data with entry/exit positions.
    /// Multi-note hits are combined into single swings.
    /// </summary>
    internal class SwingCreation
    {
        public static List<SwingData> Process(List<Cube> cubes, bool isRightHand, Modifiers modifiers)
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

            int groupIndex = 0;

            for (int i = 0; i < cubes.Count; i++)
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
                    // This also take into account individual note with a chain.
                    CalcEntryExit(swingData[^1]);
                }
                else
                {
                    // Calculate multi-note exit position with averaged angle
                    Cube headCube = groupIndex >= 0 ? groups[groupIndex][0] : null;
                    CalcMultiNoteExit(swingData[^1], modifiers.strictAngles);
                }
            }

            // Second pass: verify that direction match geometry for multi-note swings
            VerifyMultiNotes(swingData, modifiers.strictAngles);

            // Calculate swing frequency
            SwingData previousSwing = null;
            foreach (SwingData swing in swingData)
            {
                if (previousSwing != null)
                {
                    float deltaTime = swing.Cubes[0].Seconds - previousSwing.Cubes[0].Seconds;
                    if (deltaTime != 0)
                    {
                        swing.SwingFrequency = 1 / deltaTime;
                        if (swing.ParityErrors) swing.SwingFrequency *= 2;
                    }
                    else // Error
                    {
                        swing.SwingFrequency = 64;
                    }
                }

                previousSwing = swing;
            }

            // Normalize angles between swings based on angle tolerance
            // Recalculate entry and exit position
            // Skip notes with bomb avoidance as they have special direction calculation
            for (int i = 1; i < swingData.Count; i++)
            {
                if (swingData[i].Cubes[0].BombAvoidance)
                {
                    continue;
                }

                NormalizeAngle(swingData[i - 1], swingData[i], modifiers);
            }

            // Calculate hit distance
            previousSwing = null;
            foreach (SwingData swing in swingData)
            {
                if (previousSwing != null)
                {
                    // Calculate straight-line distance between positions
                    double dx = swing.EntryPosition.x - previousSwing.EntryPosition.x;
                    double dy = swing.EntryPosition.y - previousSwing.EntryPosition.y;
                    swing.HitDistance = Math.Sqrt(dx * dx + dy * dy);
                }

                previousSwing = swing;
            }

            swingData[0].AngleStrain = SwingAngleStrainCalc(swingData[0], null, isRightHand) * 4;
            bool isLinear = true;

            for (int i = 1; i < swingData.Count; i++)
            {
                swingData[i].AngleStrain = SwingAngleStrainCalc(swingData[i], swingData[i - 1], isRightHand) * 4;

                // Check if it's linear
                // First we check if the expected direction matches the reverse diretion of the previous swing
                double target = ReverseCutDirection(swingData[i - 1].Direction);
                bool directionMatches = IsSameDir(target, swingData[i].Direction, 22.5);
                // Then we check if the note placement matches the expected geometric direction
                var prevPos = swingData[i - 1].Cubes[^1];
                var currPos = swingData[i].Cubes[0];
                double dx = currPos.X - prevPos.X;
                double dy = currPos.Y - prevPos.Y;
                double geometricAngle = Mod(Math.Atan2(dy, dx) * 180.0 / Math.PI, 360);
                bool movementMatchesDirection = IsSameDir(target, geometricAngle, 22.5);
                if (directionMatches && (movementMatchesDirection || (dx == 0 && dy == 0))) // We can't deduce anything from same position
                {
                    // Can be considered linear movement, reduce strain
                    if (isLinear)
                    {
                        swingData[i].IsLinear = true;
                    }
                    isLinear = true;
                }
                else isLinear = false;
            }

            return swingData;
        }

        public static void VerifyMultiNotes(List<SwingData> swingData, bool strictAngles)
        {
            // Calculate geometric direction for multi-note swings
            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                if (swing.Cubes.Count <= 1)
                {
                    continue;
                }

                if(swing.Cubes.All(x => x.CutDirection == 8))
                {
                    // Compute geometric angle based on head → tail
                    var entry = swing.Cubes[0];
                    var exit = swing.Cubes[^1];
                    var deltaX = exit.X - entry.X;
                    var deltaY = exit.Y - entry.Y;

                    var geometricAngle = Math.Atan2(deltaY, deltaX) * (180.0 / Math.PI);
                    if (geometricAngle < 0) geometricAngle += 360.0;

                    // Calculate the reverse of the geometric angle
                    var reverseGeometricAngle = (geometricAngle + 180.0) % 360.0;

                    // Use angle strain based on parity to decide
                    bool isRightHand = entry.Type == 1; // 0 = red/left, 1 = blue/right
                    bool currentParity = swing.Forehand;

                    SwingData previousSwing = i > 0 ? swingData[i - 1] : null;

                    // Test geometric angle
                    var tempSwingGeometric = new SwingData(swing.Cubes, geometricAngle, currentParity);
                    double strainGeometric = SwingAngleStrainCalc(tempSwingGeometric, previousSwing, isRightHand);

                    // Test reverse angle
                    var tempSwingReverse = new SwingData(swing.Cubes, reverseGeometricAngle, currentParity);
                    double strainReverse= SwingAngleStrainCalc(tempSwingReverse, previousSwing, isRightHand);

                    // Find which combination has the lowest strain
                    double minStrain = Math.Min(strainGeometric, strainReverse);

                    double selectedAngle;

                    if (minStrain == strainGeometric) selectedAngle = geometricAngle;
                    else selectedAngle = reverseGeometricAngle;

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
                    swing.Cubes.ForEach(n => n.Direction = swing.Direction);
                }

                // Reorder notes based on their projection along the swing direction
                // Calculate direction vector
                double directionRadians = swing.Direction * Math.PI / 180.0;
                double dirX = Math.Cos(directionRadians);
                double dirY = Math.Sin(directionRadians);

                // Calculate projection of each note onto the direction vector
                var notesWithProjection = swing.Cubes.Select(note => new
                {
                    Note = note,
                    Projection = note.X * dirX + note.Y * dirY
                }).OrderBy(x => x.Projection).ToList();

                // Reorder the notes list
                swing.Cubes.Clear();
                swing.Cubes.AddRange(notesWithProjection.Select(x => x.Note));

                // Update Head and Tail markers
                for (int j = 0; j < swing.Cubes.Count; j++)
                {
                    swing.Cubes[j].Head = j == 0;
                    swing.Cubes[j].Tail = j == swing.Cubes.Count - 1;
                }
            }
        }
    }
}
