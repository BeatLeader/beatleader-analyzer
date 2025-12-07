using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer.BeatmapScanner.Helper.Grid
{
    /// <summary>
    /// Handles bomb-forced relocations for player position tracking.
    /// Player stands at grid edge after swing; bombs force them to relocate to opposite edge.
    /// Shared logic used by both FlowDetector and ParityPredictor for consistent bomb handling.
    /// </summary>
    internal class BombPathSimulator
    {
        /// <summary>
        /// Calculates player's position after completing a swing.
        /// Player swings to maximum extent in the swing direction, clamped to grid bounds.
        /// </summary>
        /// <param name="noteX">Note's X position (Line)</param>
        /// <param name="noteY">Note's Y position (Layer)</param>
        /// <param name="swingAngle">Swing direction in degrees</param>
        /// <returns>Player's final position (X, Y) clamped to grid [0-3, 0-2]</returns>
        public static (double x, double y) CalculatePlayerPositionAfterSwing(int noteX, int noteY, double swingAngle)
        {
            double radians = swingAngle * Math.PI / 180.0;
            double dirX = Math.Cos(radians);
            double dirY = Math.Sin(radians);

            // Player swings to maximum extent (use large multiplier to reach grid edge)
            double targetX = noteX + dirX * 10.0;
            double targetY = noteY + dirY * 10.0;

            // Clamp to grid bounds: X [0, 3], Y [0, 2]
            return (Math.Clamp(targetX, 0, 3), Math.Clamp(targetY, 0, 2));
        }

        /// <summary>
        /// Simulates bomb-forced relocations for player position.
        /// Player stands still at grid edge; bombs spawning at that position force them to relocate.
        /// </summary>
        /// <param name="startX">Starting X position (after previous swing)</param>
        /// <param name="startY">Starting Y position (after previous swing)</param>
        /// <param name="lastSwingDirection">Direction of previous swing (used for reversals)</param>
        /// <param name="bombs">List of bombs to check</param>
        /// <param name="minTime">Minimum time (exclusive) for bomb consideration</param>
        /// <param name="maxTime">Maximum time (exclusive) for bomb consideration</param>
        /// <returns>(finalX, finalY, lastDirection, encounteredBombs, relocationCount)</returns>
        public static (int finalX, int finalY, double lastDirection, bool encounteredBombs, int relocationCount) 
            SimulateBombForcedRelocations(
                double startX, double startY,
                double lastSwingDirection,
                List<Bomb> bombs,
                float minTime, float maxTime)
        {
            if (bombs == null || bombs.Count == 0)
            {
                return ((int)Math.Round(startX), (int)Math.Round(startY), lastSwingDirection, false, 0);
            }

            // Find bombs strictly between min and max time
            var bombsBetween = bombs
                .Where(b => b.BpmTime > minTime && b.BpmTime < maxTime)
                .OrderBy(b => b.BpmTime)
                .ToList();

            if (bombsBetween.Count == 0)
            {
                return ((int)Math.Round(startX), (int)Math.Round(startY), lastSwingDirection, false, 0);
            }

            // Track player's current standing position
            int playerX = (int)Math.Round(startX);
            int playerY = (int)Math.Round(startY);
            double currentDirection = lastSwingDirection;
            bool hadBombReset = false;
            int relocationCount = 0;

            // Check each bomb in chronological order
            foreach (var bomb in bombsBetween)
            {
                // Simple check: Is bomb at exact player position?
                if (bomb.x == playerX && bomb.y == playerY)
                {
                    // Bomb spawns where player is standing!
                    // Player MUST relocate to opposite grid edge
                    hadBombReset = true;
                    relocationCount++;

                    // Reverse direction by 180°
                    currentDirection = (currentDirection + 180.0) % 360.0;

                    // Move to opposite grid edge
                    var (newX, newY) = CalculatePlayerPositionAfterSwing(
                        playerX, playerY, currentDirection);

                    playerX = (int)Math.Round(newX);
                    playerY = (int)Math.Round(newY);
                }
                // else: Bomb is elsewhere, player stays put
            }

            return (playerX, playerY, currentDirection, hadBombReset, relocationCount);
        }
    }
}
