using Analyzer.BeatmapScanner.Data;
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
            double diff = (currentAngle - current.Direction + 540) % 360 - 180;
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
                    double delta = (blendedAngle - arrowAngle + 540) % 360 - 180;

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

        public static void NormalizeAngle(SwingData previous, SwingData current, bool strictAngles)
        {
            // Calculate the geometric angle
            double deltaX = current.Cubes[0].X - previous.Cubes[0].X;
            double deltaY = current.Cubes[0].Y - previous.Cubes[0].Y;

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
            double angleDiff = Math.Abs(potentialAngle - currentAngle);
            if (angleDiff > 180)
            {
                angleDiff = 360 - angleDiff;
            }

            // Tolerance for angle adjustment (in degrees)
            double tolerance = GetTolerance(strictAngles);

            // If the geometric angle is within tolerance, use it instead
            if (angleDiff <= tolerance)
            {
                current.Direction = potentialAngle;

                (double centerX, double centerY) = GridToMeters(current.Cubes[0].X, current.Cubes[0].Y);

                double angleRad = ConvertDegreesToRadians(current.Direction);
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);

                current.EntryPosition = (centerX - cos * NOTE_SIZE, centerY - sin * NOTE_SIZE);
                current.ExitPosition = (centerX + cos * NOTE_SIZE, centerY + sin * NOTE_SIZE);
            }
        }
    }
}