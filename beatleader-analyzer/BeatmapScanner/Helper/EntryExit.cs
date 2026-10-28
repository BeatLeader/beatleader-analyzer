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
        /// Sets a multi-note swing's exit direction and world-space exit position: halfway between its current direction
        /// and the head -> tail geometry, clamped to the head arrow's cut tolerance (dots are not clamped).
        /// </summary>
        public static void CalcMultiNoteExit(SwingData current, bool strictAngles = false)
        {
            Cube headCube = current.Cubes[0];
            Cube tailCube = current.Cubes[^1];

            // Geometric angle from the head cube to the tail cube
            double currentAngle = FindAngleViaPos(tailCube, headCube, current.Direction, true);

            (double tailX, double tailY) = GridToMeters(tailCube.X, tailCube.Y);

            double diff = AngleDifference(current.Direction, currentAngle);
            double blendedAngle = Mod(current.Direction + diff * 0.5, 360);

            double finalAngle = blendedAngle;

            if (headCube.CutDirection != 8)
            {
                double arrowAngle = headCube.Direction;
                double tolerance = GetTolerance(strictAngles);

                if (!IsAngleWithinTolerance(blendedAngle, arrowAngle, tolerance))
                {
                    // Clamp to the nearest edge of the allowed cone
                    double delta = AngleDifference(arrowAngle, blendedAngle);

                    if (delta > 0)
                        finalAngle = Mod(arrowAngle + tolerance, 360);
                    else
                        finalAngle = Mod(arrowAngle - tolerance, 360);
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

        // Share of the pull towards the path from the previous swing that a single arrow gets, fitted on cut directions in
        // top players' replays. Players cut arrows close to the way they point and reach the next note with the arm, so the
        // full pull overshoots: more so on slow swings, and above all on the same row, where the path runs sideways and would
        // bend up/down streams into diagonals (fake wrist rotation and angle strain).
        // Row change: 0.25 + 0.045 * swings per second, clamped to [0.3, 0.85]. Same row: 0.15.
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

            double angleRadians = Math.Atan2(deltaY, deltaX);
            double potentialAngle = angleRadians * 180.0 / Math.PI;

            if (potentialAngle < 0)
            {
                potentialAngle += 360.0;
            }

            double currentAngle = current.Cubes[0].Direction;

            double angleDiff = AngleDifference(currentAngle, potentialAngle);
            double angleDiffPrev = AngleDifference(previous.Direction, potentialAngle);

            // Path points against the note but along the previous swing: flip it. Heuristic; a proper fix would need
            // parity, rotation and angle strain.
            if (angleDiff < -90 && Math.Abs(angleDiffPrev) < 90) angleDiff += 180;
            if (angleDiff > 90 && Math.Abs(angleDiffPrev) < 90) angleDiff -= 180;

            double tolerance = GetTolerance(modifiers.strictAngles);

            // Soft clamp of the adjustment to the tolerance; faster swings get a harder knee (higher rolloff)
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