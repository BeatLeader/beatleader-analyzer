using beatleader_parser.Timescale;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper.WallHelper
{
    internal class WallClassifier
    {
        private const float TIME_TOLERANCE = 0.1f; // Walls within 0.1s are truly simultaneous
        private const float DODGE_COOLDOWN_SECONDS = 1.0f;

        public static (List<Wall> dodgeWallsAll, List<Wall> crouchWallsAll, int dodgeWallsCount, int crouchWallsCount) ClassifyWalls(List<Wall> walls, Timescale timescale)
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

            foreach (var wall in wallsByTime)
            {
                // Classify wall based on what it blocks
                bool coversCenter = (wall.x <= 1 && wall.x + wall.Width > 1) || (wall.x <= 2 && wall.x + wall.Width > 2);
                
                if (!coversCenter)
                {
                    // Wall doesn't affect center lanes, skip
                    continue;
                }

                // Check if this is an overhead wall (crouch wall)
                bool isOverhead = wall.y + wall.Height > 2;
                
                // Check if this blocks at standing height (dodge wall)
                bool blocksStanding = (wall.y <= 1 && wall.Height >= 2) || (wall.y == 0 && wall.Height >= 3);

                if (isOverhead && !blocksStanding)
                {
                    // Pure crouch wall (overhead but doesn't block standing)
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
                            DurationInSeconds = lastDodgeWall.DurationInSeconds + newDuration,
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
                }
                else if (blocksStanding)
                {
                    // Dodge wall (blocks at standing height)
                    if (lastDodgeWall != null && wall.Seconds - lastDodgeWall.Seconds < TIME_TOLERANCE)
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
                            DurationInSeconds = lastDodgeWall.DurationInSeconds + newDuration,
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
                }
            }

            return (dodgeWallsList, crouchWallsList, dodgeWallsList.Count, crouchWallsList.Count);
        }
    }
}
