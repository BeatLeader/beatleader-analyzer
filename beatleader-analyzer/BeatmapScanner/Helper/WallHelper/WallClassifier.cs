using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper.WallHelper
{
    internal class WallClassifier
    {
        private const float TIME_TOLERANCE = 0.1f;
        private const float DODGE_COOLDOWN_SECONDS = 1.0f; // Minimum time between dodges at same position

        public static (List<Wall> dodgeWallsAll, List<Wall> crouchWallsAll, int dodgeWallsCount, int crouchWallsCount) ClassifyWalls(List<Wall> walls, float bpm = 120f)
        {
            var dodgeWallsList = new List<Wall>();
            var crouchWallsList = new List<Wall>();
            int dodgeWallsCount = 0;
            int crouchWallsCount = 0;

            if (walls == null || walls.Count == 0)
            {
                return (dodgeWallsList, crouchWallsList, 0, 0);
            }

            // Convert cooldown from seconds to beats
            float cooldownBeats = DODGE_COOLDOWN_SECONDS * (bpm / 60f);

            // Sort walls by time to group simultaneous walls
            var wallsByTime = walls
                .OrderBy(w => w.Beats)
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
                    if (Math.Abs(wallsByTime[j].Beats - currentWall.Beats) <= TIME_TOLERANCE)
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
                    // Check if enough time has passed since player could stand up from last crouch
                    float timeSinceLastCrouch = currentWall.Beats - lastCrouchEndTime;
                    
                    if (timeSinceLastCrouch >= cooldownBeats)
                    {
                        // This is a new crouch action - add only the walls at y=2 that force crouching
                        var crouchWalls = simultaneousWalls.Where(w => w.y == 2).ToList();
                        if (crouchWalls.Count > 0)
                        {
                            crouchWallsList.AddRange(crouchWalls);
                            crouchWallsCount++;
                            
                            // Track the wall with longest duration for potential extension
                            lastCrouchWall = crouchWalls.OrderByDescending(w => w.DurationInBeats).First();
                            lastCrouchEndTime = lastCrouchWall.Beats + lastCrouchWall.DurationInBeats;
                        }
                    }
                    else if (lastCrouchWall != null)
                    {
                        // Player is still crouched - extend the duration of the previous wall
                        var crouchWalls = simultaneousWalls.Where(w => w.y == 2).ToList();
                        if (crouchWalls.Count > 0)
                        {
                            float currentWallEnd = crouchWalls.Max(w => w.Beats + w.DurationInBeats);
                            float newDuration = currentWallEnd - lastCrouchWall.Beats;
                            lastCrouchWall.DurationInBeats = newDuration;
                            lastCrouchEndTime = currentWallEnd;
                        }
                        
                        // Don't add this wall separately - it's merged with the previous one
                    }
                }
                else if (IsDodgeWall(simultaneousWalls))
                {
                    // Determine which side(s) this dodge wall covers
                    bool coversLeft = false;
                    bool coversRight = false;
                    
                    foreach (var wall in simultaneousWalls)
                    {
                        bool coversX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                        bool coversX2 = wall.x <= 2 && wall.x + wall.Width > 2;
                        
                        if (coversX1) coversLeft = true;
                        if (coversX2) coversRight = true;
                    }

                    string coverageKey;
                    if (coversLeft && coversRight)
                    {
                        coverageKey = "both";
                    }
                    else if (coversLeft)
                    {
                        coverageKey = "left";
                    }
                    else
                    {
                        coverageKey = "right";
                    }

                    // Check if enough time has passed since last dodge in this area
                    float timeSinceLastDodge = currentWall.Beats - lastDodgeTime[coverageKey];
                    
                    if (timeSinceLastDodge >= cooldownBeats)
                    {
                        // This is a new dodge action - add it as a separate wall
                        dodgeWallsList.AddRange(simultaneousWalls);
                        dodgeWallsCount++;
                        
                        // Track this wall for potential duration extension
                        lastDodgeWall[coverageKey] = currentWall;
                        lastDodgeTime[coverageKey] = currentWall.Beats;
                        
                        // Also update "both" if we covered a specific side
                        if (coverageKey != "both")
                        {
                            // If covering both sides separately in quick succession, track that too
                            float otherSideTime = coverageKey == "left" ? lastDodgeTime["right"] : lastDodgeTime["left"];
                            if (currentWall.Beats - otherSideTime < cooldownBeats)
                            {
                                lastDodgeTime["both"] = currentWall.Beats;
                            }
                        }
                    }
                    else if (lastDodgeWall[coverageKey] != null)
                    {
                        // Player is already dodged - extend the duration of the previous wall
                        float currentWallEnd = currentWall.Beats + currentWall.DurationInBeats;
                        float newDuration = currentWallEnd - lastDodgeWall[coverageKey].Beats;
                        lastDodgeWall[coverageKey].DurationInBeats = newDuration;
                        lastDodgeTime[coverageKey] = currentWallEnd;
                        
                        // Don't add this wall separately - it's merged with the previous one
                    }
                }
            }

            return (dodgeWallsList, crouchWallsList, dodgeWallsCount, crouchWallsCount);
        }

        private static bool IsDodgeWall(List<Wall> walls)
        {
            foreach (var wall in walls)
            {
                bool coversX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                bool coversX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                if (coversX1 || coversX2)
                {
                    // Walls at y=0 or y=1 with sufficient height are dodge walls
                    // Walls at y=2 (crouch height) are also dodge walls if they don't fully cover both center columns
                    // (partial width high walls are dodged, not crouched)
                    bool validHeight = wall.y <= 0 && wall.Height >= 3 || 
                                      wall.y == 1 && wall.Height >= 2 ||
                                      wall.y == 2 && wall.Height >= 3;

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
            // A crouch wall must have a wall at y=2 that covers both center positions (x=1 and x=2)
            // Either a single wall covers both, or multiple walls combine to cover both
            bool hasTopWallCoveringBothCenters = false;

            foreach (var wall in walls)
            {
                if (wall.y == 2)
                {
                    // Check if this top wall covers both center positions x=1 and x=2
                    bool coversX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                    bool coversX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                    if (coversX1 && coversX2)
                    {
                        hasTopWallCoveringBothCenters = true;
                        break;
                    }
                }
            }

            // If there's no top wall covering both centers, it's not a crouch wall
            if (!hasTopWallCoveringBothCenters)
            {
                return false;
            }

            // Now check if there are any lower walls that would make dodging impossible
            // (i.e., walls that block the side positions where player could dodge instead of crouch)
            bool hasBlockingWallAtX1 = false;
            bool hasBlockingWallAtX2 = false;

            foreach (var wall in walls)
            {
                // Only walls from ground level (y=0 or y=1) that are tall enough block dodging
                bool isTallEnough = wall.y <= 0 && wall.Height >= 3 || wall.y == 1 && wall.Height >= 2;
                
                if (isTallEnough)
                {
                    bool blocksX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                    bool blocksX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                    if (blocksX1) hasBlockingWallAtX1 = true;
                    if (blocksX2) hasBlockingWallAtX2 = true;
                }
            }

            // If both center positions have blocking walls from below, it's definitely a crouch
            if (hasBlockingWallAtX1 && hasBlockingWallAtX2)
            {
                return true;
            }

            // If only the top wall exists (no blocking walls below), it's still a crouch
            // because the player must crouch to avoid it
            if (!hasBlockingWallAtX1 && !hasBlockingWallAtX2)
            {
                return true;
            }

            // If there's only one blocking wall below and one side is free,
            // the player could potentially dodge instead of crouch
            // However, if the top wall is present, crouching is still the primary action
            // so we consider it a crouch wall
            return true;
        }
    }
}
