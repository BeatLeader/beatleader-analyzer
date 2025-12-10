using beatleader_parser.Timescale;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper.WallHelper
{
    internal class WallClassifier
    {
        private const float TIME_TOLERANCE = 1f;
        private const float DODGE_COOLDOWN_SECONDS = 1.0f; // Minimum time between dodges at same position

        public static (List<Wall> dodgeWallsAll, List<Wall> crouchWallsAll, int dodgeWallsCount, int crouchWallsCount) ClassifyWalls(List<Wall> walls, Timescale timescale)
        {
            var dodgeWallsList = new List<Wall>();
            var crouchWallsList = new List<Wall>();
            int dodgeWallsCount = 0;
            int crouchWallsCount = 0;

            if (walls == null || walls.Count == 0)
            {
                return (dodgeWallsList, crouchWallsList, 0, 0);
            }

            // Sort walls by time to group simultaneous walls
            var wallsByTime = walls
                .OrderBy(w => w.BpmTime)
                .ToList();

            var processed = new HashSet<int>();
            
            // Track last dodge wall by coverage area (X position)
            // Key: "left" (covers X1) or "right" (covers X2) or "both"
            var lastDodgeWall = new Dictionary<string, Wall>
            {
                { "left", null },
                { "right", null },
                { "both", null }
            };
            
            var lastDodgeTime = new Dictionary<string, float>
            {
                { "left", float.MinValue },
                { "right", float.MinValue },
                { "both", float.MinValue }
            };

            // Track last crouch wall and end time
            Wall lastCrouchWall = null;
            float lastCrouchEndTime = float.MinValue;

            for (int i = 0; i < wallsByTime.Count; i++)
            {
                if (processed.Contains(i))
                {
                    continue;
                }

                var currentWall = wallsByTime[i];
                var simultaneousWalls = new List<Wall> { currentWall };
                processed.Add(i);

                // Group walls that occur at the same time (within tolerance)
                for (int j = i + 1; j < wallsByTime.Count; j++)
                {
                    var start = wallsByTime[j].Seconds - (currentWall.Seconds);
                    var end = wallsByTime[j].Seconds - (currentWall.Seconds + currentWall.DurationInSeconds);
                    var duration = Math.Min(start, end);
                    if (duration <= TIME_TOLERANCE)
                    {
                        simultaneousWalls.Add(wallsByTime[j]);
                        processed.Add(j);
                    }
                    else
                    {
                        break;
                    }
                }

                // Classify the simultaneous wall group (crouch walls take priority)
                if (IsCrouchWall(simultaneousWalls))
                {
                    // Filter to only include walls at y=2 that actually cover the center columns
                    // This prevents walls at y=2 but outside center from being counted
                    var actualCrouchWalls = simultaneousWalls.Where(w =>
                    {
                        if (w.y != 2) return false;
                        bool coversX1 = w.x <= 1 && w.x + w.Width > 1;
                        bool coversX2 = w.x <= 2 && w.x + w.Width > 2;
                        return coversX1 || coversX2;
                    }).ToList();

                    if (actualCrouchWalls.Count == 0)
                    {
                        // No walls at y=2 actually cover the center - skip this group
                        continue;
                    }

                    // Check if this wall should be merged with the previous crouch wall
                    // Merge if the current wall starts before or shortly after the previous wall ends
                    float timeSinceLastCrouch = actualCrouchWalls[0].Seconds - lastCrouchEndTime;
                    
                    if (timeSinceLastCrouch < DODGE_COOLDOWN_SECONDS && lastCrouchWall != null)
                    {
                        // Player is still crouched - extend the duration of the previous wall
                        float currentWallEnd = actualCrouchWalls.Max(w => w.Seconds + w.DurationInSeconds);
                        float newDuration = currentWallEnd - lastCrouchWall.Seconds;
                        lastCrouchWall.DurationInSeconds = newDuration;
                        lastCrouchEndTime = currentWallEnd;
                        
                        // Don't add this wall separately - it's merged with the previous one
                    }
                    else
                    {
                        // This is a new crouch action - add only the first wall as representative
                        // (simultaneous walls are part of the same crouch action)
                        crouchWallsList.Add(actualCrouchWalls[0]);
                        crouchWallsCount++;
                        
                        // Track the wall with longest duration for potential extension
                        lastCrouchWall = actualCrouchWalls.OrderByDescending(w => w.Seconds + w.DurationInSeconds).First();
                        lastCrouchEndTime = lastCrouchWall.Seconds + lastCrouchWall.DurationInSeconds;
                    }
                }
                else if (IsDodgeWall(simultaneousWalls))
                {
                    // Filter to only include walls that actually cover the center columns (x=1 or x=2)
                    // This prevents non-dodge walls from being included just because they're simultaneous
                    var actualDodgeWalls = simultaneousWalls.Where(w =>
                    {
                        bool coversX1 = w.x <= 1 && w.x + w.Width > 1;
                        bool coversX2 = w.x <= 2 && w.x + w.Width > 2;
                        return coversX1 || coversX2;
                    }).ToList();

                    if (actualDodgeWalls.Count == 0)
                    {
                        // No walls actually cover the center - skip this group
                        continue;
                    }

                    // Determine which side(s) this dodge wall covers
                    bool coversLeft = false;
                    bool coversRight = false;
                    
                    foreach (var wall in actualDodgeWalls)
                    {
                        bool coversX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                        bool coversX2 = wall.x <= 2 && wall.x + wall.Width > 2;
                        
                        if (coversX1) coversLeft = true;
                        if (coversX2) coversRight = true;
                    }

                    // Check if this wall should be merged with any previous dodge wall
                    // We check all possible coverage areas and merge if ANY of them are within tolerance
                    bool merged = false;
                    Wall mergeTarget = null;
                    float currentWallEnd = actualDodgeWalls.Max(w => w.Seconds + w.DurationInSeconds);
                    var firstDodgeWall = actualDodgeWalls[0];
                    
                    // Priority order: check specific sides first, then "both"
                    var keysToCheck = new List<string>();
                    if (coversLeft && coversRight)
                    {
                        // This wall covers both sides - check if either side has a recent dodge
                        keysToCheck.Add("left");
                        keysToCheck.Add("right");
                        keysToCheck.Add("both");
                    }
                    else if (coversLeft)
                    {
                        keysToCheck.Add("left");
                        keysToCheck.Add("both"); // Left-only wall can extend a "both" wall
                    }
                    else if (coversRight)
                    {
                        keysToCheck.Add("right");
                        keysToCheck.Add("both"); // Right-only wall can extend a "both" wall
                    }

                    // Find the most recent previous wall that we should merge with
                    float mostRecentTime = float.MinValue;
                    foreach (var key in keysToCheck)
                    {
                        if (lastDodgeWall[key] == null)
                            continue;
                            
                        float timeSinceLastDodge = firstDodgeWall.Seconds - lastDodgeTime[key];
                        
                        if (timeSinceLastDodge < TIME_TOLERANCE)
                        {
                            // This previous wall is within merge range
                            // Choose the wall with the most recent end time
                            if (mergeTarget == null || lastDodgeTime[key] > mostRecentTime)
                            {
                                mergeTarget = lastDodgeWall[key];
                                mostRecentTime = lastDodgeTime[key];
                                merged = true;
                            }
                        }
                    }
                    
                    if (merged && mergeTarget != null)
                    {
                        // Extend the duration of the merge target wall
                        float newDuration = currentWallEnd - mergeTarget.Seconds;
                        mergeTarget.DurationInSeconds = newDuration;
                        
                        // Update all relevant tracking for the areas this wall covers
                        if (coversLeft && coversRight)
                        {
                            lastDodgeWall["both"] = mergeTarget;
                            lastDodgeTime["both"] = currentWallEnd;
                            lastDodgeWall["left"] = mergeTarget;
                            lastDodgeTime["left"] = currentWallEnd;
                            lastDodgeWall["right"] = mergeTarget;
                            lastDodgeTime["right"] = currentWallEnd;
                        }
                        else if (coversLeft)
                        {
                            lastDodgeWall["left"] = mergeTarget;
                            lastDodgeTime["left"] = currentWallEnd;
                        }
                        else if (coversRight)
                        {
                            lastDodgeWall["right"] = mergeTarget;
                            lastDodgeTime["right"] = currentWallEnd;
                        }
                        
                        // Don't add this wall separately - it's merged with the previous one
                    }
                    else
                    {
                        // This is a new dodge action - add only the first wall as representative
                        // (simultaneous walls are part of the same dodge action)
                        dodgeWallsList.Add(firstDodgeWall);
                        dodgeWallsCount++;
                        
                        // Track this wall for all affected coverage areas
                        if (coversLeft && coversRight)
                        {
                            lastDodgeWall["both"] = firstDodgeWall;
                            lastDodgeTime["both"] = currentWallEnd;
                            lastDodgeWall["left"] = firstDodgeWall;
                            lastDodgeTime["left"] = currentWallEnd;
                            lastDodgeWall["right"] = firstDodgeWall;
                            lastDodgeTime["right"] = currentWallEnd;
                        }
                        else if (coversLeft)
                        {
                            lastDodgeWall["left"] = firstDodgeWall;
                            lastDodgeTime["left"] = currentWallEnd;
                            
                            // Check if right side was recently covered - if so, update "both"
                            float otherSideTime = lastDodgeTime["right"];
                            if (firstDodgeWall.Seconds - otherSideTime < TIME_TOLERANCE)
                            {
                                lastDodgeTime["both"] = currentWallEnd;
                                lastDodgeWall["both"] = firstDodgeWall;
                            }
                        }
                        else if (coversRight)
                        {
                            lastDodgeWall["right"] = firstDodgeWall;
                            lastDodgeTime["right"] = currentWallEnd;
                            
                            // Check if left side was recently covered - if so, update "both"
                            float otherSideTime = lastDodgeTime["left"];
                            if (firstDodgeWall.Seconds - otherSideTime < TIME_TOLERANCE)
                            {
                                lastDodgeTime["both"] = currentWallEnd;
                                lastDodgeWall["both"] = firstDodgeWall;
                            }
                        }
                    }
                }
            }

            return (dodgeWallsList, crouchWallsList, dodgeWallsCount, crouchWallsCount);
        }

        private static bool IsDodgeWall(List<Wall> walls)
        {
            foreach (var wall in walls)
            {
                // Only check walls that cover the center columns (x=1 or x=2)
                bool coversX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                bool coversX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                if (coversX1 || coversX2)
                {
                    bool validHeight = (wall.y == 0 && wall.Height >= 3) || 
                                      (wall.y == 1 && wall.Height >= 2) ||
                                      (wall.y >= 2 && wall.Height >= 1);

                    if (validHeight)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsCrouchWall(List<Wall> walls)
        {
            // A crouch wall requires walls that COMBINED reach y=2 and cover both center positions (x=1 and x=2)
            // Check if ANY walls (at any height) that extend to y=2 cover both positions
            bool hasWallReachingTopAtX1 = false;
            bool hasWallReachingTopAtX2 = false;

            foreach (var wall in walls)
            {
                // Check if this wall reaches the top layer (y=2)
                // A wall at y position reaches y=2 if: y + height > 2
                bool reachesTop = wall.y + wall.Height > 2;
                
                if (reachesTop)
                {
                    // Check if this wall covers center position x=1
                    bool coversX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                    // Check if this wall covers center position x=2
                    bool coversX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                    if (coversX1) hasWallReachingTopAtX1 = true;
                    if (coversX2) hasWallReachingTopAtX2 = true;
                }
            }

            // If the walls don't reach the top at BOTH center positions, it's not a crouch wall
            if (!hasWallReachingTopAtX1 || !hasWallReachingTopAtX2)
            {
                return false;
            }

            // Now check if there are any lower walls that would make crouching impossible
            // The player can crouch if AT LEAST ONE center position is NOT blocked from below
            bool hasBlockingWallAtX1 = false;
            bool hasBlockingWallAtX2 = false;

            foreach (var wall in walls)
            {
                // Walls that start from ground level (y=0 or y=1) and are tall enough block crouching
                // We need to check if they fill the entire space from ground to top
                bool isTallEnough = (wall.y <= 0 && wall.Height >= 3) || (wall.y == 1 && wall.Height >= 2);
                
                if (isTallEnough)
                {
                    bool blocksX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                    bool blocksX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                    if (blocksX1) hasBlockingWallAtX1 = true;
                    if (blocksX2) hasBlockingWallAtX2 = true;
                }
            }

            // If BOTH center positions have blocking walls from below, crouching is impossible
            // If at least one position is free, the player can crouch there
            if (hasBlockingWallAtX1 && hasBlockingWallAtX2)
            {
                return false;
            }

            return true;
        }
    }
}
