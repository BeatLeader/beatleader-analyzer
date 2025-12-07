using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Analyzer.BeatmapScanner.Helper
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
                        // This is a new crouch action - add it as a separate wall
                        crouchWallsList.AddRange(simultaneousWalls);
                        crouchWallsCount++;
                        
                        // Track this wall for potential duration extension
                        lastCrouchWall = currentWall;
                        lastCrouchEndTime = currentWall.Beats + currentWall.DurationInBeats;
                    }
                    else if (lastCrouchWall != null)
                    {
                        // Player is still crouched - extend the duration of the previous wall
                        float currentWallEnd = currentWall.Beats + currentWall.DurationInBeats;
                        float newDuration = currentWallEnd - lastCrouchWall.Beats;
                        lastCrouchWall.DurationInBeats = newDuration;
                        lastCrouchEndTime = currentWallEnd;
                        
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
                    bool validHeight = (wall.y <= 0 && wall.Height >= 3) || 
                                      (wall.y == 1 && wall.Height >= 2) ||
                                      (wall.y == 2 && wall.Height >= 3);

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
            bool hasX1Covered = false;
            bool hasX2Covered = false;
            bool hasCrouchHeight = false;

            foreach (var wall in walls)
            {
                bool coversX1 = wall.x <= 1 && wall.x + wall.Width >= 2;
                bool coversX2 = wall.x <= 2 && wall.x + wall.Width >= 3;

                if (wall.y == 2)
                {
                    hasCrouchHeight = true;

                    if (coversX1)
                    {
                        hasX1Covered = true;
                    }
                    if (coversX2)
                    {
                        hasX2Covered = true;
                    }
                }
            }

            if (!hasCrouchHeight)
            {
                return false;
            }

            if (hasX1Covered && hasX2Covered)
            {
                return true;
            }

            if (hasX1Covered)
            {
                foreach (var wall in walls)
                {
                    bool coversX2 = wall.x <= 2 && wall.x + wall.Width >=3 ;
                    if (coversX2)
                    {
                        bool validHeight = (wall.y <= 0 && wall.Height >= 3) || (wall.y == 1 && wall.Height >= 2);
                        if (validHeight)
                        {
                            return true;
                        }
                    }
                }
            }

            if (hasX2Covered)
            {
                foreach (var wall in walls)
                {
                    bool coversX1 = wall.x <= 1 && wall.x + wall.Width >= 2;
                    if (coversX1)
                    {
                        bool validHeight = (wall.y <= 0 && wall.Height >= 3) || (wall.y == 1 && wall.Height >= 2);
                        if (validHeight)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
