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

        public static (List<Wall> dodgeWallsAll, List<Wall> crouchWallsAll, int dodgeWallsCount, int crouchWallsCount, float dodgeDuration, float crouchDuration) ClassifyWalls(List<Wall> walls)
        {
            var dodgeWallsList = new List<Wall>();
            var crouchWallsList = new List<Wall>();

            if (walls == null || walls.Count == 0)
            {
                return (dodgeWallsList, crouchWallsList, 0, 0, 0, 0);
            }

            // Sort walls by time, then prioritize non-crouch walls
            var wallsByTime = walls.OrderBy(w => w.Seconds).ThenBy(x => x.y).ToList();

            Wall lastDodgeWall = null;
            Wall lastCrouchWall = null;

            bool isX1Blocked = false;
            Wall x1Wall = null;
            bool isX2Blocked = false;
            Wall x2Wall = null;
            int xPosition = 0; // -1 left, 0 neutral, 1 right
            int yPosition = 1; // 0 crouch, 1 standing

            foreach (var wall in wallsByTime)
            {
                // Classify wall based on what it blocks
                bool coverX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                bool coverX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                float X1ExtraDuration = RESET_TO_NEUTRAL_DODGE;
                float X2ExtraDuration = RESET_TO_NEUTRAL_DODGE;

                if (yPosition == 0) X1ExtraDuration = RESET_TO_NEUTRAL_CROUCH;
                if (yPosition == 0) X2ExtraDuration = RESET_TO_NEUTRAL_CROUCH;

                if (x1Wall != null && wall.Seconds > x1Wall.Seconds + x1Wall.DurationInSeconds + X1ExtraDuration)
                {
                    if (xPosition == 1) xPosition = 0;
                    if (X1ExtraDuration == RESET_TO_NEUTRAL_CROUCH) yPosition = 1;
                }

                if (x2Wall != null && wall.Seconds > x2Wall.Seconds + x2Wall.DurationInSeconds + X2ExtraDuration)
                {
                    if (xPosition == -1) xPosition = 0;
                    if (X2ExtraDuration == RESET_TO_NEUTRAL_CROUCH) yPosition = 1;
                }

                // Clear blocked wall
                if (x1Wall != null && !coverX1 && isX1Blocked && wall.Seconds > x1Wall.Seconds + x1Wall.DurationInSeconds + X1ExtraDuration)
                {
                    isX1Blocked = false;
                    x1Wall = null;
                }

                if (x2Wall != null && !coverX2 && isX2Blocked && wall.Seconds > x2Wall.Seconds + x2Wall.DurationInSeconds + X2ExtraDuration)
                {
                    isX2Blocked = false;
                    x2Wall = null;
                }

                if (!isX1Blocked && !isX2Blocked)
                {
                    xPosition = 0;
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
                    if (lastDodgeWall != null && lastDodgeWall.y == 2 &&
                        wall.Seconds - (lastDodgeWall.Seconds + lastDodgeWall.DurationInSeconds) < 0.5)
                    {
                        // Convert previous overhead dodge by creating a new wall with extended duration
                        float wallEnd = wall.Seconds + wall.DurationInSeconds;
                        float newDuration = lastDodgeWall.DurationInSeconds;
                        if (wallEnd > lastDodgeWall.Seconds + lastDodgeWall.DurationInSeconds)
                        {
                            newDuration = wallEnd - lastDodgeWall.Seconds;
                        }

                        var oldDodgeWall = lastDodgeWall;

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

                        // Replace the last crouch wall with the merged one
                        crouchWallsList.Add(mergedWall);
                        lastCrouchWall = mergedWall;
                        dodgeWallsList.Remove(lastDodgeWall);
                        lastDodgeWall = dodgeWallsList.LastOrDefault();

                        // Update lane trackers that pointed at the replaced wall
                        if (x1Wall == oldDodgeWall) x1Wall = mergedWall;
                        if (x2Wall == oldDodgeWall) x2Wall = mergedWall;
                    }
                    else if (lastCrouchWall != null && wall.Seconds - (lastCrouchWall.Seconds + lastCrouchWall.DurationInSeconds) < RESET_TO_NEUTRAL_CROUCH)
                    {
                        // Extend previous crouch by creating a new wall with extended duration
                        float wallEnd = wall.Seconds + wall.DurationInSeconds;
                        float newDuration = lastCrouchWall.DurationInSeconds;
                        if (wallEnd > lastCrouchWall.Seconds + lastCrouchWall.DurationInSeconds)
                        {
                            newDuration = wallEnd - lastCrouchWall.Seconds;
                        }

                        var oldCrouchWall = lastCrouchWall;

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

                        // Update lane trackers that pointed at the replaced wall
                        if (x1Wall == oldCrouchWall) x1Wall = mergedWall;
                        if (x2Wall == oldCrouchWall) x2Wall = mergedWall;
                    }
                    else
                    {
                        // New crouch action
                        crouchWallsList.Add(wall);
                        lastCrouchWall = wall;
                        yPosition = 0;
                    }

                    if (coverX1)
                    {
                        isX1Blocked = true;
                        x1Wall = lastCrouchWall;
                    }
                    if (coverX2)
                    {
                        isX2Blocked = true;
                        x2Wall = lastCrouchWall;
                    }
                }
                else // Potential dodge action
                {
                    // Extend dodge, otherwise create new dodge
                    if (lastDodgeWall != null && wall.Seconds - (lastDodgeWall.Seconds + lastDodgeWall.DurationInSeconds) < 0.5
                        && ((coverX1 && (xPosition == 1 || xPosition == 2)) || (coverX2 && (xPosition == -1 || xPosition == 2))))
                    {
                        // Extend previous dodge by creating a new wall with extended duration
                        float wallEnd = wall.Seconds + wall.DurationInSeconds;
                        float newDuration = lastDodgeWall.DurationInSeconds;
                        if (wallEnd > lastDodgeWall.Seconds + lastDodgeWall.DurationInSeconds)
                        {
                            newDuration = wallEnd - lastDodgeWall.Seconds;
                        }

                        var oldDodgeWall = lastDodgeWall;

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

                        // Update lane trackers that pointed at the replaced wall
                        if (x1Wall == oldDodgeWall) x1Wall = mergedWall;
                        if (x2Wall == oldDodgeWall) x2Wall = mergedWall;
                    }
                    else
                    {
                        if (xPosition == 0 || (xPosition == 1 && coverX2) || (xPosition == -1 && coverX1))
                        {
                            // New dodge action
                            dodgeWallsList.Add(wall);
                            lastDodgeWall = wall;

                            if (coverX1)
                            {
                                xPosition = 1;
                            }
                            if (coverX2)
                            {
                                xPosition = -1;
                            }
                            if (coverX1 && coverX2)
                            {
                                xPosition = 2;
                            }
                        }
                    }
                    
                    if (coverX1)
                    {
                        isX1Blocked = true;
                        x1Wall = lastDodgeWall;
                    }
                    if (coverX2)
                    {
                        isX2Blocked = true;
                        x2Wall = lastDodgeWall;
                    }
                }
            }

            float dodgeDuration = 0;
            float crouchDuration = 0;

            foreach (var wall in dodgeWallsList)
            {
                dodgeDuration += wall.DurationInSeconds + 0.1f;
            }

            foreach (var wall in crouchWallsList)
            {
                crouchDuration += wall.DurationInSeconds + 0.5f;
            }

            return (dodgeWallsList, crouchWallsList, dodgeWallsList.Count, crouchWallsList.Count, dodgeDuration, crouchDuration);
        }
    }
}
