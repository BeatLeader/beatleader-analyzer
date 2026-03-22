using beatleader_parser.Timescale;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal class WallClassifier
    {
        private const float TIME_TOLERANCE = 0.1f; // Walls within 0.1s are truly simultaneous
        private const float DODGE_COOLDOWN_SECONDS = 1.0f;

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
            Wall X1Wall = null;
            bool isX2Blocked = false;
            Wall X2Wall = null;

            foreach (var wall in wallsByTime)
            {
                // Classify wall based on what it blocks
                bool coverX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                bool coverX2 = (wall.x <= 1 && wall.x + wall.Width > 2) || (wall.x == 2 && wall.Width >= 1);

                // Clear blocked wall
                if (X1Wall != null && !coverX1 && isX1Blocked && wall.Seconds > X1Wall.Seconds)
                {
                    isX1Blocked = false;
                    X1Wall = null;
                }

                if (X2Wall != null && !coverX2 && isX2Blocked && wall.Seconds > X2Wall.Seconds)
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
                    if (lastCrouchWall != null && wall.Seconds - (lastCrouchWall.Seconds + lastCrouchWall.DurationInSeconds) < DODGE_COOLDOWN_SECONDS)
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
                    else
                    {
                        // New crouch action
                        crouchWallsList.Add(wall);
                        lastCrouchWall = wall;
                    }

                    if (coverX1)
                    {
                        isX1Blocked = true;
                        X1Wall = lastCrouchWall;
                    }
                    if (coverX2)
                    {
                        isX2Blocked = true;
                        X2Wall = lastCrouchWall;
                    }
                }
                else if (blocksStanding)
                {
                    // Dodge wall (blocks full height)
                    if (lastDodgeWall != null && wall.Seconds - (lastDodgeWall.Seconds + lastDodgeWall.DurationInSeconds) < TIME_TOLERANCE)
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
                        X1Wall = lastDodgeWall;
                    }
                    if (coverX2)
                    {
                        isX2Blocked = true;
                        X2Wall = lastDodgeWall;
                    }
                }
            }

            return (dodgeWallsList, crouchWallsList, dodgeWallsList.Count, crouchWallsList.Count);
        }
    }
}
