using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.EntryExit;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using static beatleader_analyzer.BeatmapScanner.Helper.AngleStrain;
using beatleader_analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Helper;

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
            
            // One swing per group, each with its own entry/exit; multi-note swings get their multi-note exit.
            var swingData = new List<SwingData>(groups.Count);
            foreach (var group in groups)
            {
                var swing = new SwingData(group);
                // Also handles a single note with a chain (exit at the chain's tail)
                CalcEntryExit(swing);
                if (group.Count > 1)
                {
                    CalcMultiNoteExit(swing, modifiers.strictAngles);
                }
                swingData.Add(swing);
            }

            // Second pass: dot-only multi-note swings take their direction from geometry, notes are reordered along it
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
                        // A reset adds a motion in the same time
                        if (swing.ParityErrors) swing.SwingFrequency *= 2;
                    }
                    else // Same time as the previous swing: fixed high frequency
                    {
                        swing.SwingFrequency = 64;
                    }
                }

                previousSwing = swing;
            }

            // Bend each swing towards the path from the previous one (within angle tolerance), updating entry/exit.
            // Bomb avoidance swings have their own direction logic; single dots are handled below.
            for (int i = 1; i < swingData.Count; i++)
            {
                if (swingData[i].Cubes[0].BombAvoidance || DotDirections.IsSingleDot(swingData[i]))
                {
                    continue;
                }

                NormalizeAngle(swingData[i - 1], swingData[i], modifiers);
            }

            // Single dots take the direction top players cut them at
            foreach (var swing in swingData)
            {
                if (DotDirections.IsSingleDot(swing)) DotDirections.Apply(swing);
            }

            // Hit distance: straight-line distance between consecutive entry positions
            previousSwing = null;
            foreach (SwingData swing in swingData)
            {
                if (previousSwing != null)
                {
                    double dx = swing.EntryPosition.x - previousSwing.EntryPosition.x;
                    double dy = swing.EntryPosition.y - previousSwing.EntryPosition.y;
                    swing.HitDistance = Math.Sqrt(dx * dx + dy * dy);
                }

                previousSwing = swing;
            }

            swingData[0].AngleStrain = SwingAngleStrainCalc(swingData[0], null, isRightHand) * 4;
            swingData[0].AngleStrainTech = SwingAngleStrainCalc(swingData[0], null, isRightHand, softCap: true) * 4;
            bool isLinear = true;

            for (int i = 1; i < swingData.Count; i++)
            {
                swingData[i].AngleStrain = SwingAngleStrainCalc(swingData[i], swingData[i - 1], isRightHand) * 4;
                swingData[i].AngleStrainTech = SwingAngleStrainCalc(swingData[i], swingData[i - 1], isRightHand, softCap: true) * 4;

                // Linear: the swing reverses the previous direction and the notes move along it.
                // IsLinear is only set when the previous swing was linear too.
                double target = ReverseCutDirection(swingData[i - 1].Direction);
                bool directionMatches = IsSameDir(target, swingData[i].Direction, 22.5);
                var prevPos = swingData[i - 1].Cubes[^1];
                var currPos = swingData[i].Cubes[0];
                double dx = currPos.X - prevPos.X;
                double dy = currPos.Y - prevPos.Y;
                double geometricAngle = Mod(Math.Atan2(dy, dx) * 180.0 / Math.PI, 360);
                bool movementMatchesDirection = IsSameDir(target, geometricAngle, 22.5);
                if (directionMatches && (movementMatchesDirection || (dx == 0 && dy == 0))) // We can't deduce anything from same position
                {
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
            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                if (swing.Cubes.Count <= 1)
                {
                    continue;
                }

                if(swing.Cubes.All(x => x.CutDirection == 8))
                {
                    // Dot-only swing: direction is the head -> tail line or its reverse, whichever strains less
                    var entry = swing.Cubes[0];
                    var exit = swing.Cubes[^1];
                    var deltaX = exit.X - entry.X;
                    var deltaY = exit.Y - entry.Y;

                    var geometricAngle = Math.Atan2(deltaY, deltaX) * (180.0 / Math.PI);
                    if (geometricAngle < 0) geometricAngle += 360.0;

                    var reverseGeometricAngle = (geometricAngle + 180.0) % 360.0;

                    bool isRightHand = entry.Type == 1; // 0 = red/left, 1 = blue/right
                    bool currentParity = swing.Forehand;

                    SwingData previousSwing = i > 0 ? swingData[i - 1] : null;

                    var tempSwingGeometric = new SwingData(swing.Cubes, geometricAngle, currentParity);
                    double strainGeometric = SwingAngleStrainCalc(tempSwingGeometric, previousSwing, isRightHand);

                    var tempSwingReverse = new SwingData(swing.Cubes, reverseGeometricAngle, currentParity);
                    double strainReverse= SwingAngleStrainCalc(tempSwingReverse, previousSwing, isRightHand);

                    double minStrain = Math.Min(strainGeometric, strainReverse);

                    double selectedAngle;

                    if (minStrain == strainGeometric) selectedAngle = geometricAngle;
                    else selectedAngle = reverseGeometricAngle;

                    // Within 45 degrees of the previous swing's direction: flip it (swings alternate)
                    if (i > 0)
                    {
                        double previousAngle = swingData[i - 1].Direction;
                        double angleDiffToPrevious = Math.Abs(selectedAngle - previousAngle);
                        if (angleDiffToPrevious > 180)
                        {
                            angleDiffToPrevious = 360 - angleDiffToPrevious;
                        }
                        
                        const double SIMILARITY_THRESHOLD = 45.0;
                        if (angleDiffToPrevious < SIMILARITY_THRESHOLD)
                        {
                            selectedAngle = selectedAngle == geometricAngle ? reverseGeometricAngle : geometricAngle;
                        }
                    }
                    
                    swing.Direction = selectedAngle;
                    swing.Cubes.ForEach(n => n.Direction = swing.Direction);
                }

                // Reorder notes by their projection along the swing direction
                double directionRadians = swing.Direction * Math.PI / 180.0;
                double dirX = Math.Cos(directionRadians);
                double dirY = Math.Sin(directionRadians);

                var notesWithProjection = swing.Cubes.Select(note => new
                {
                    Note = note,
                    Projection = note.X * dirX + note.Y * dirY
                }).OrderBy(x => x.Projection).ToList();

                swing.Cubes.Clear();
                swing.Cubes.AddRange(notesWithProjection.Select(x => x.Note));

                // Update Head and Tail markers
                for (int j = 0; j < swing.Cubes.Count; j++)
                {
                    swing.Cubes[j].Head = j == 0;
                    swing.Cubes[j].Tail = j == swing.Cubes.Count - 1;
                }

                // Keep SwingData.BpmTime in sync with the (possibly reordered) head cube
                swing.BpmTime = swing.Cubes[0].BpmTime;
            }
        }
    }
}
