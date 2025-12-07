using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Analyzer.BeatmapScanner.Helper
{
    internal class WallClassifier
    {
        private const float TIME_TOLERANCE = 0.1f;

        public static (List<Wall> dodgeWalls, List<Wall> crouchWalls) ClassifyWalls(List<Wall> walls)
        {
            var dodgeWallsList = new List<Wall>();
            var crouchWallsList = new List<Wall>();

            if (walls == null || walls.Count == 0)
            {
                return (dodgeWallsList, crouchWallsList);
            }

            // Sort walls by time to group simultaneous walls
            var wallsByTime = walls
                .OrderBy(w => w.Beats)
                .ToList();

            var processed = new HashSet<int>();

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

                // Classify the simultaneous wall group
                if (IsCrouchWall(simultaneousWalls))
                {
                    crouchWallsList.AddRange(simultaneousWalls);
                }
                else if (IsDodgeWall(simultaneousWalls))
                {
                    dodgeWallsList.AddRange(simultaneousWalls);
                }
            }

            return (dodgeWallsList, crouchWallsList);
        }

        private static bool IsDodgeWall(List<Wall> walls)
        {
            foreach (var wall in walls)
            {
                bool coversX1 = wall.x <= 1 && wall.x + wall.Width > 1;
                bool coversX2 = wall.x <= 2 && wall.x + wall.Width > 2;

                if (coversX1 || coversX2)
                {
                    bool validHeight = (wall.y <= 0 && wall.Height >= 3) || (wall.y == 1 && wall.Height >= 2);

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
