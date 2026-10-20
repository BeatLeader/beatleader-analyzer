using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Data;
using System;
using System.Linq;
using static beatleader_analyzer.BeatmapScanner.Helper.AngleTolerance;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using static beatleader_analyzer.BeatmapScanner.Helper.GridPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.SwingSimulation;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal class EntryExit
    {
        /// <summary>
        /// Determines what angle the swing should leave at.
        /// Calculates the world-space exit position from that angle.
        /// Blends the swing’s previous direction and positional geometry to keep the movement fluid.
        /// Respects cut-direction constraints if the next note has a fixed direction.
        /// </summary>
        public static void CalcMultiNoteExit(SwingData current, bool strictAngles = false)
        {
            Cube headCube = current.Cubes[0];
            Cube tailCube = current.Cubes[^1];

            // Calculate geometric angle based on entry position and tail cube position
            double currentAngle = FindAngleViaPos(tailCube, headCube, current.Direction, true);

            // Compute the geometric exit point
            (double tailX, double tailY) = GridToMeters(tailCube.X, tailCube.Y);

            // Blend the geometric angle with the current swing angle
            double diff = AngleDifference(current.Direction, currentAngle);
            double blendedAngle = Mod(current.Direction + diff * 0.5, 360);

            // Respect the head cube's cut direction tolerance
            double finalAngle = blendedAngle;

            if (headCube.CutDirection != 8)
            {
                double arrowAngle = headCube.Direction;
                double tolerance = GetTolerance(strictAngles);

                if (!IsAngleWithinTolerance(blendedAngle, arrowAngle, tolerance))
                {
                    // Outside tolerance → clamp to the nearest edge of allowed range
                    double delta = AngleDifference(arrowAngle, blendedAngle);

                    if (delta > 0)
                        finalAngle = Mod(arrowAngle + tolerance, 360);     // clamp high side
                    else
                        finalAngle = Mod(arrowAngle - tolerance, 360);     // clamp low side
                }
            }

            double rad = ConvertDegreesToRadians(finalAngle);
            double exitX = tailX + Math.Cos(rad) * NOTE_SIZE;
            double exitY = tailY + Math.Sin(rad) * NOTE_SIZE;

            current.Direction = finalAngle;
            current.ExitPosition = (exitX, exitY);
        }

        public static void CalcEntryExit(SwingData current)
        {
            Cube headCube = current.Cubes[0];
            (double headCenterX, double headCenterY) = GridToMeters(headCube.X, headCube.Y);

            double swingAngle = headCube.Direction;

            double angleRad = ConvertDegreesToRadians(swingAngle);
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            current.EntryPosition = (headCenterX - cos * NOTE_SIZE, headCenterY - sin * NOTE_SIZE);

            // If there's a chain note, use its tail for exit position
            Cube chainNote = current.Cubes.Where(x => x.Chain).FirstOrDefault();
            if (chainNote != null)
            {
                double angleInRadians = ConvertDegreesToRadians(chainNote.TailDirection);
                double cosAngle = Math.Cos(angleInRadians);
                double sinAngle = Math.Sin(angleInRadians);

                (double tailX, double tailY) = GridToMeters(chainNote.TailLine, chainNote.TailLayer);
                current.ExitPosition = (
                    (tailX + cosAngle * NOTE_SIZE) * chainNote.Squish,
                    (tailY + sinAngle * NOTE_SIZE) * chainNote.Squish
                );
            }
            else
            {
                current.ExitPosition = (headCenterX + cos * NOTE_SIZE, headCenterY + sin * NOTE_SIZE);
            }
        }

        // How much of the pull towards the path from the previous swing a single arrow gets, fitted on the cut directions
        // recorded in top players' replays (portaBLe ParityCheck, ~3.3M single arrows on 3886 maps): mean error against the
        // players 15.9 -> 10.2 degrees (within 30 degrees 85% -> 96%) against the full pull, and rotation / angle strain follow
        // the players' real wrist roll (controller roll in the replays) much better: R2 0.27 -> 0.50 / 0.52 -> 0.64.
        // Best scale by time to the previous swing on a row change: ~0.8 under 0.08 s, 0.7 under 0.11 s, 0.6 under 0.15 s, 0.5
        // under 0.2 s, 0.4 under 0.3 s, 0.3 above (the line below); on the same row 0.15 (0 when the note doesn't move).
        // Players cut arrows close to the way they point: they get to the next note with the arm, between the swings. The full
        // pull overshoots, more so when the swings are slow (more time to move the arm) and above all on the same row, where
        // the path from the previous exit runs sideways: on a bottom row stream, it bent every up and down arrow into ~50
        // degree diagonals, so the stream looked like constant wrist rotation and angle strain (too much tech).
        private const double SAME_ROW_ADJUST_SCALE = 0.15;
        private const double ROW_CHANGE_ADJUST_BASE = 0.25;
        private const double ROW_CHANGE_ADJUST_PER_SPS = 0.045;
        private const double ROW_CHANGE_ADJUST_MIN = 0.3;
        private const double ROW_CHANGE_ADJUST_MAX = 0.85;

        private static double SingleNoteAdjustScale(SwingData previous, SwingData current, double sps)
        {
            if (current.Cubes[0].Y == previous.Cubes[^1].Y) return SAME_ROW_ADJUST_SCALE;
            return Math.Clamp(ROW_CHANGE_ADJUST_BASE + ROW_CHANGE_ADJUST_PER_SPS * sps, ROW_CHANGE_ADJUST_MIN, ROW_CHANGE_ADJUST_MAX);
        }

        public static void NormalizeAngle(SwingData previous, SwingData current, Modifiers modifiers)
        {
            // Calculate the geometric angle from previous swing exit to current note center
            double deltaX = GridXToMeters(current.Cubes[0].X) - previous.ExitPosition.x;
            double deltaY = GridYToMeters(current.Cubes[0].Y) - previous.ExitPosition.y;

            // We can't deduce anything from same position
            if (deltaX == 0 && deltaY == 0)
            {
                return;
            }

            // Calculate angle in radians, then convert to degrees
            double angleRadians = Math.Atan2(deltaY, deltaX);
            double potentialAngle = angleRadians * 180.0 / Math.PI;

            // Normalize to 0-360 range
            if (potentialAngle < 0)
            {
                potentialAngle += 360.0;
            }

            // Get the current note's intended direction
            double currentAngle = current.Cubes[0].Direction;

            // Calculate angular difference (shortest path around circle)
            double angleDiff = AngleDifference(currentAngle, potentialAngle);
            double angleDiffPrev = AngleDifference(previous.Direction, potentialAngle);

            // Handle invert
            // this is kind of janky and can do the wrong thing in some cases
            // to do it properly you'd need to account for parity, rotation, angle strain, etc.
            // should be good enough for now (probably)
            if (angleDiff < -90 && Math.Abs(angleDiffPrev) < 90) angleDiff += 180;
            if (angleDiff > 90 && Math.Abs(angleDiffPrev) < 90) angleDiff -= 180;

            // Tolerance for angle adjustment (in degrees)
            double tolerance = GetTolerance(modifiers.strictAngles);

            // Adjust angle based on tolerance and speed
            const double ROLLOFF_MIN = 1.5;
            const double ROLLOFF_MAX = 15.0;

            double sps = current.SwingFrequency;

            // https://www.desmos.com/calculator/cxt6aehw0h
            double rolloff = Math.Clamp(
                0.05 * sps * sps + 0.25 * sps + 1.2,
                ROLLOFF_MIN,
                ROLLOFF_MAX
            );

            // https://www.desmos.com/calculator/wal5fknd9f
            double angleAdjust = tolerance * angleDiff / Math.Pow(Math.Pow(Math.Abs(angleDiff), rolloff) + Math.Pow(tolerance, rolloff), 1 / rolloff);
            if (current.Cubes.Count == 1) angleAdjust *= SingleNoteAdjustScale(previous, current, sps);
            current.Direction = Mod(current.Direction + angleAdjust, 360);

            (double centerX, double centerY) = GridToMeters(current.Cubes[0].X, current.Cubes[0].Y);

            double angleRad = ConvertDegreesToRadians(current.Direction);
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            current.EntryPosition = (centerX - cos * NOTE_SIZE, centerY - sin * NOTE_SIZE);
            if (current.Cubes[0].Head && current.Cubes[0].Pattern)
            {
                CalcMultiNoteExit(current, modifiers.strictAngles);
            }
            else
            {
                current.ExitPosition = (centerX + cos * NOTE_SIZE, centerY + sin * NOTE_SIZE);
            }
        }
    }
}