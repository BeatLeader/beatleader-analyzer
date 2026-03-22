using beatleader_parser.Timescale;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal class WallClassifier
    {
        private const float RESET_TO_NEUTRAL_DODGE = 0.1f;
        private const float RESET_TO_NEUTRAL_CROUCH = 0.5f;

        public static (List<Wall> dodgeWallsAll, List<Wall> crouchWallsAll, int dodgeWallsCount, int crouchWallsCount) ClassifyWalls(List<Wall> walls)
        {
            var dodgeWallsList = new List<Wall>();
            var crouchWallsList = new List<Wall>();

            if (walls == null || walls.Count == 0)
            {
                return (dodgeWallsList, crouchWallsList, 0, 0);
            }

            // Sort walls by time
            var wallsByTime = walls.OrderBy(w => w.Seconds).ToList();

            Wall lastDodgeWall = null;
            Wall lastCrouchWall = null;

            bool isX1Blocked = false;
            bool X1Type = false; // If true = crouch wall, if false = dodge wall
            Wall X1Wall = null;
            bool isX2Blocked = false;
            bool X2Type = false; // If true = crouch wall, if false = dodge wall
            Wall X2Wall = null;

            foreach (var wall in wallsByTime)
            {
                // Classify wall based on what it blocks
                bool coverX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                bool coverX2 = (wall.x <= 1 && wall.x + wall.Width > 2) || (wall.x == 2 && wall.Width >= 1);

                float X1ExtraDuration = RESET_TO_NEUTRAL_DODGE;
                float X2ExtraDuration = RESET_TO_NEUTRAL_DODGE;

                if (X1Type) X1ExtraDuration = RESET_TO_NEUTRAL_CROUCH;
                if (X2Type) X2ExtraDuration = RESET_TO_NEUTRAL_CROUCH;

                // Clear blocked wall
                if (X1Wall != null && !coverX1 && isX1Blocked && wall.Seconds > X1Wall.Seconds + X1Wall.DurationInSeconds + X1ExtraDuration)
                {
                    isX1Blocked = false;
                    X1Wall = null;
                }

                if (X2Wall != null && !coverX2 && isX2Blocked && wall.Seconds > X2Wall.Seconds + X2Wall.DurationInSeconds + X2ExtraDuration)
                {
                    isX2Blocked = false;
                    X2Wall = null;
                }

                if (!coverX1 && !coverX2)
                {
                    // Wall doesn't affect center lanes, skip
                    continue;
                }

                // Check if this is an overhead wall (crouch wall)
                bool isOverhead = wall.y + wall.Height > 2 && wall.y == 2;

                // Check if this blocks at standing height (dodge wall)
                bool blocksStanding = wall.Height + wall.y >= 3 && wall.y <= 0;

                // The player will only crouch if both side are covered
                if (isOverhead && !blocksStanding && ((coverX1 && isX2Blocked) || (isX1Blocked && coverX2) || (coverX1 && coverX2)))
                {
                    // Crouch wall (overhead)
                    if (lastCrouchWall != null && wall.Seconds - (lastCrouchWall.Seconds + lastCrouchWall.DurationInSeconds) < RESET_TO_NEUTRAL_CROUCH)
                    {
                        // Extend previous crouch by creating a new wall with extended duration
                        float wallEnd = wall.Seconds + wall.DurationInSeconds;
                        float newDuration = wallEnd - lastCrouchWall.Seconds;
                        
                        // Create a new wall object with the extended duration
                        var mergedWall = new Wall
                        {
                            Beats = lastCrouchWall.Beats,
                            Seconds = lastCrouchWall.Seconds,
                            BpmTime = lastCrouchWall.BpmTime,
                            DurationInSeconds = newDuration,
                            x = lastCrouchWall.x,
                            y = lastCrouchWall.y,
                            Width = lastCrouchWall.Width,
                            Height = lastCrouchWall.Height
                        };
                        
                        // Replace the last crouch wall with the merged one
                        crouchWallsList[crouchWallsList.Count - 1] = mergedWall;
                        lastCrouchWall = mergedWall;
                    }
                    else if (lastDodgeWall != null && wall.Seconds - (lastDodgeWall.Seconds + lastDodgeWall.DurationInSeconds) < RESET_TO_NEUTRAL_DODGE)
                    {
                        // Extend previous dodge by creating a new wall with extended duration
                        float wallEnd = wall.Seconds + wall.DurationInSeconds;
                        float newDuration = wallEnd - lastDodgeWall.Seconds;

                        // Create a new wall object with the extended duration
                        var mergedWall = new Wall
                        {
                            Beats = lastDodgeWall.Beats,
                            Seconds = lastDodgeWall.Seconds,
                            BpmTime = lastDodgeWall.BpmTime,
                            DurationInSeconds = newDuration,
                            x = lastDodgeWall.x,
                            y = lastDodgeWall.y,
                            Width = lastDodgeWall.Width,
                            Height = lastDodgeWall.Height
                        };

                        // Replace the last dodge wall into a crouch wall instead
                        dodgeWallsList.RemoveAt(dodgeWallsList.Count - 1);
                        crouchWallsList.Add(mergedWall);
                        lastCrouchWall = mergedWall;
                    }
                    else
                    {
                        // New crouch action
                        crouchWallsList.Add(wall);
                        lastCrouchWall = wall;
                    }

                    if (coverX1)
                    {
                        isX1Blocked = true;
                        X1Type = true;
                        X1Wall = lastCrouchWall;
                    }
                    if (coverX2)
                    {
                        isX2Blocked = true;
                        X2Type = true;
                        X2Wall = lastCrouchWall;
                    }
                }
                else if (isOverhead || blocksStanding)
                {
                    // Extend crouch, otherwise extend dodge, otherwise create new dodge
                    if (lastCrouchWall != null && wall.Seconds - (lastCrouchWall.Seconds + lastCrouchWall.DurationInSeconds) < RESET_TO_NEUTRAL_CROUCH)
                    {
                        // Extend previous crouch by creating a new wall with extended duration
                        float wallEnd = wall.Seconds + wall.DurationInSeconds;
                        float newDuration = wallEnd - lastCrouchWall.Seconds;

                        // Create a new wall object with the extended duration
                        var mergedWall = new Wall
                        {
                            Beats = lastCrouchWall.Beats,
                            Seconds = lastCrouchWall.Seconds,
                            BpmTime = lastCrouchWall.BpmTime,
                            DurationInSeconds = newDuration,
                            x = lastCrouchWall.x,
                            y = lastCrouchWall.y,
                            Width = lastCrouchWall.Width,
                            Height = lastCrouchWall.Height
                        };

                        // Replace the last crouch wall with the merged one
                        crouchWallsList[crouchWallsList.Count - 1] = mergedWall;
                        lastCrouchWall = mergedWall;
                    }
                    else if (lastDodgeWall != null && wall.Seconds - (lastDodgeWall.Seconds + lastDodgeWall.DurationInSeconds) < RESET_TO_NEUTRAL_DODGE)
                    {
                        // Extend previous dodge by creating a new wall with extended duration
                        float wallEnd = wall.Seconds + wall.DurationInSeconds;
                        float newDuration = wallEnd - lastDodgeWall.Seconds;
                        
                        // Create a new wall object with the extended duration
                        var mergedWall = new Wall
                        {
                            Beats = lastDodgeWall.Beats,
                            Seconds = lastDodgeWall.Seconds,
                            BpmTime = lastDodgeWall.BpmTime,
                            DurationInSeconds = newDuration,
                            x = lastDodgeWall.x,
                            y = lastDodgeWall.y,
                            Width = lastDodgeWall.Width,
                            Height = lastDodgeWall.Height
                        };
                        
                        // Replace the last dodge wall with the merged one
                        dodgeWallsList[dodgeWallsList.Count - 1] = mergedWall;
                        lastDodgeWall = mergedWall;
                    }
                    else
                    {
                        // New dodge action
                        dodgeWallsList.Add(wall);
                        lastDodgeWall = wall;
                    }

                    if (coverX1)
                    {
                        isX1Blocked = true;
                        X1Type = false;
                        X1Wall = lastDodgeWall;
                    }
                    if (coverX2)
                    {
                        isX2Blocked = true;
                        X2Type = false;
                        X2Wall = lastDodgeWall;
                    }
                }
            }

            return (dodgeWallsList, crouchWallsList, dodgeWallsList.Count, crouchWallsList.Count);
        }
    }
}
