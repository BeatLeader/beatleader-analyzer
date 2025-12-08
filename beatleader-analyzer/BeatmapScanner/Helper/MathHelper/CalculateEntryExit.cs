using System;
using Analyzer.BeatmapScanner.Data;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.GridPositionHelper;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.AngleTolerance;
using static beatleader_analyzer.BeatmapScanner.Helper.Grid.FindAngleViaPosition;

namespace beatleader_analyzer.BeatmapScanner.Helper.MathHelper
{
    internal class CalculateEntryExit
    {
        /// <summary>
        /// Calculates optimal entry and exit positions for a note based on the previous swing's extended position.
        /// The target is the center of the note, with entry/exit points determined by angle tolerance.
        /// The previous swing position is extended to account for swing momentum.
        /// Overwrites the Angle, EntryPosition, and ExitPosition of the current SwingData.
        /// </summary>
        /// <param name="previous">Previous swing data</param>
        /// <param name="current">Current swing data</param>
        /// <param name="strictAngles">Strict Angle modifier</param>
        /// <returns>Entry and exit positions in meters</returns>
        public static void CalcEntryExit(SwingData previous, SwingData current, bool strictAngles = false)
        {
            Cube cube = current.Start;
            // Setting it here only matter for the first note (if it's a dot)
            double noteAngle = cube.Direction;
            if (cube.CutDirection != 8)
                noteAngle = Mod(DirectionToDegree[cube.CutDirection] + cube.AngleOffset, 360);

            (double x, double y) position = (cube.Line, cube.Layer);

            // Convert grid position to meters (this is the note center)
            (double centerX, double centerY) = GridToMeters(position.x, position.y);

            double tolerance = GetTolerance(strictAngles);
            double actualSwingAngle = noteAngle;

            // Dot notes (cut direction 8) can be hit from any direction
            bool isDotNote = cube.CutDirection == 8;

            // If there's a previous swing, calculate the optimal angle from the extended swing position
            if (previous != null)
            {
                var prevExit = previous.ExitPosition;
                var previousSwingAngle = previous.Angle;

                // Extend the previous exit position to account for swing momentum
                // Using LANE_SPACING (0.6m) as the extension distance
                double extendedX = prevExit.x + Math.Cos(ConvertDegreesToRadians(previousSwingAngle)) * LANE_SPACING;
                double extendedY = prevExit.y + Math.Sin(ConvertDegreesToRadians(previousSwingAngle)) * LANE_SPACING;
                
                // Calculate the angle from the extended position to the current note center
                double dx = centerX - extendedX;
                double dy = centerY - extendedY;
                double directAngle = Mod(ConvertRadiansToDegrees(Math.Atan2(dy, dx)), 360);

                if (isDotNote)
                {
                    // Dot notes have no angle restriction - always use the direct line
                    actualSwingAngle = directAngle;
                }
                else
                {
                    // Check if the direct line from extended position to note center is within tolerance
                    if (IsAngleWithinTolerance(directAngle, noteAngle, tolerance))
                    {
                        // Use the direct angle as it's more natural and within tolerance
                        actualSwingAngle = directAngle;
                    }
                    else
                    {
                        // Direct line is outside tolerance - clamp to the closest allowed angle
                        actualSwingAngle = ClampAngleToTolerance(directAngle, noteAngle, tolerance);
                    }
                }
            }

            // Calculate entry and exit points based on the swing angle
            double angleInRadians = ConvertDegreesToRadians(actualSwingAngle);
            double cosAngle = Math.Cos(angleInRadians);
            double sinAngle = Math.Sin(angleInRadians);

            // Entry point: note center minus note radius in swing direction
            (double, double) entry = (
                centerX - cosAngle * NOTE_SIZE,
                centerY - sinAngle * NOTE_SIZE
            );

            // Exit point: note center plus note radius in swing direction
            (double, double) exit = (
                centerX + cosAngle * NOTE_SIZE,
                centerY + sinAngle * NOTE_SIZE
            );

            // Apply the new values
            current.Angle = actualSwingAngle;
            current.EntryPosition = entry;
            current.ExitPosition = exit;
        }

        /// <summary>
        /// Calculates the exit position for a multi-note pattern (stacks, towers, windows).
        /// Averages the angle between the head note and tail note if within tolerance.
        /// Overwrites the Angle and ExitPosition of the current SwingData.
        /// </summary>
        /// <param name="current">Current swing data (head note of pattern)</param>
        /// <param name="tailCube">Tail note of the multi-note pattern</param>
        /// <param name="headCube">Head note for angle calculation (can be null)</param>
        /// <param name="strictAngles">Strict Angle modifier</param>
        public static void CalcMultiNoteExit(SwingData current, Cube tailCube, Cube headCube, bool strictAngles = false)
        {
            double currentAngle = current.Angle;
            
            // Find the angle between tail note and head note if available
            if (headCube != null)
            {
                currentAngle = FindAngleViaPos(tailCube, headCube, current.Angle, true);
            }

            // Calculate the exit position of the tail note using the found angle
            double angleInRadians = ConvertDegreesToRadians(currentAngle);
            double cosAngle = Math.Cos(angleInRadians);
            double sinAngle = Math.Sin(angleInRadians);

            (double tailX, double tailY) = GridToMeters(tailCube.Line, tailCube.Layer);
            double tailExitX = tailX + cosAngle * NOTE_SIZE;
            double tailExitY = tailY + sinAngle * NOTE_SIZE;

            // Calculate the angle from entry to tail exit (averaged angle)
            double dx = current.EntryPosition.x - tailExitX;
            double dy = current.EntryPosition.y - tailExitY;
            double averagedAngle = Mod(ConvertRadiansToDegrees(Math.Atan2(dy, dx)), 360);

            // Calculate the angular difference and average it
            double diff = ((averagedAngle - current.Angle + 540) % 360) - 180;
            double newAngle = current.Angle + diff * 0.5;

            // Validate the new angle against tolerance (if head note is not a dot)
            double tolerance = GetTolerance(strictAngles);
            bool canUseAveragedAngle = true;

            if (headCube != null && headCube.CutDirection != 8)
            {
                double noteAngle = Mod(DirectionToDegree[headCube.CutDirection] + headCube.AngleOffset, 360);
                canUseAveragedAngle = IsAngleWithinTolerance(newAngle, noteAngle, tolerance);
            }

            if (canUseAveragedAngle)
            {
                // Use the averaged angle and recalculate exit position
                current.Angle = newAngle;
                
                angleInRadians = ConvertDegreesToRadians(newAngle);
                cosAngle = Math.Cos(angleInRadians);
                sinAngle = Math.Sin(angleInRadians);

                tailExitX = tailX + cosAngle * NOTE_SIZE;
                tailExitY = tailY + sinAngle * NOTE_SIZE;
            }
            // else: keep the original angle and use the exit position calculated with currentAngle

            current.ExitPosition = (tailExitX, tailExitY);
        }

        /// <summary>
        /// Calculates the exit position for a chain note.
        /// Uses the chain's tail position and applies squish factor.
        /// Overwrites the ExitPosition of the current SwingData.
        /// </summary>
        /// <param name="current">Current swing data</param>
        /// <param name="chainCube">Chain cube with tail position and squish data</param>
        public static void CalcChainExit(SwingData current, Cube chainCube)
        {
            double tailDirection = current.Angle;
            if (chainCube.TailDirection != 8)
            {
                tailDirection = Mod(DirectionToDegree[chainCube.TailDirection], 360);
            }

            double angleInRadians = ConvertDegreesToRadians(tailDirection);
            double cosAngle = Math.Cos(angleInRadians);
            double sinAngle = Math.Sin(angleInRadians);

            // Chain tail position in meters using centered grid
            (double tailX, double tailY) = GridToMeters(chainCube.TailLine, chainCube.TailLayer);
            current.ExitPosition = (
                (tailX + cosAngle * NOTE_SIZE) * chainCube.Squish,
                (tailY + sinAngle * NOTE_SIZE) * chainCube.Squish
            );
        }

        /// <summary>
        /// Clamps an angle to the nearest edge of the allowed tolerance cone.
        /// </summary>
        /// <param name="angle">The angle to clamp (in degrees)</param>
        /// <param name="targetAngle">The center of the tolerance cone (expected cut direction)</param>
        /// <param name="tolerance">The angle tolerance (in degrees)</param>
        /// <returns>The clamped angle within the tolerance cone</returns>
        private static double ClampAngleToTolerance(double angle, double targetAngle, double tolerance)
        {
            // Calculate the angle difference, handling wrap-around
            double diff = angle - targetAngle;
            
            // Normalize to [-180, 180]
            while (diff > 180) diff -= 360;
            while (diff < -180) diff += 360;

            // If outside tolerance, clamp to the nearest boundary
            if (diff > tolerance)
            {
                return Mod(targetAngle + tolerance, 360);
            }
            else if (diff < -tolerance)
            {
                return Mod(targetAngle - tolerance, 360);
            }

            // Within tolerance (shouldn't happen if this function is called correctly)
            return angle;
        }
    }
}
