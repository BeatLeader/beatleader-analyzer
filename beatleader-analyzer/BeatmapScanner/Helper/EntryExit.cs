using Analyzer.BeatmapScanner.Data;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.SwingSimulation;
using static beatleader_analyzer.BeatmapScanner.Helper.GridPosition;
using static beatleader_analyzer.BeatmapScanner.Helper.AngleTolerance;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal class EntryExit
    {
        public static void CalcMultiNoteExit(SwingData current, Cube tailCube, Cube headCube, bool strictAngles = false)
        {
            if (headCube != null && headCube.Direction != 8)
            {
                double presetAngle = headCube.Direction;
                double radians = ConvertDegreesToRadians(presetAngle);
                double cos = Math.Cos(radians);
                double sin = Math.Sin(radians);

                var (tailPosX, tailPosY) = GridToMeters(tailCube.X, tailCube.Y);
                current.Direction = presetAngle;
                current.ExitPosition = (tailPosX + cos * NOTE_SIZE, tailPosY + sin * NOTE_SIZE);
                return;
            }

            double currentAngle = current.Direction;

            if (headCube != null)
            {
                currentAngle = FindAngleViaPos(tailCube, headCube, current.Direction, true);
            }

            double angleInRadians = ConvertDegreesToRadians(currentAngle);
            double cosAngle = Math.Cos(angleInRadians);
            double sinAngle = Math.Sin(angleInRadians);

            (double tailX, double tailY) = GridToMeters(tailCube.X, tailCube.Y);
            double tailExitX = tailX + cosAngle * NOTE_SIZE;
            double tailExitY = tailY + sinAngle * NOTE_SIZE;

            double dx = current.EntryPosition.x - tailExitX;
            double dy = current.EntryPosition.y - tailExitY;
            double averagedAngle = Mod(ConvertRadiansToDegrees(Math.Atan2(dy, dx)), 360);

            double diff = (averagedAngle - current.Direction + 540) % 360 - 180;
            double newAngle = current.Direction + diff * 0.5;

            double tolerance = GetTolerance(strictAngles);
            bool canUseAveragedAngle = true;

            if (headCube != null && headCube.CutDirection != 8)
            {
                double noteAngle = Mod(DirectionToDegree[headCube.CutDirection] + headCube.AngleOffset, 360);
                canUseAveragedAngle = IsAngleWithinTolerance(newAngle, noteAngle, tolerance);
            }

            if (canUseAveragedAngle)
            {
                current.Direction = newAngle;

                angleInRadians = ConvertDegreesToRadians(newAngle);
                cosAngle = Math.Cos(angleInRadians);
                sinAngle = Math.Sin(angleInRadians);

                tailExitX = tailX + cosAngle * NOTE_SIZE;
                tailExitY = tailY + sinAngle * NOTE_SIZE;
            }

            current.ExitPosition = (tailExitX, tailExitY);
        }

        public static void CalcChainExit(SwingData current, Cube chainCube)
        {
            double tailDirection = current.Direction;
            if (chainCube.TailDirection != 8)
            {
                tailDirection = Mod(DirectionToDegree[chainCube.TailDirection], 360);
            }

            double angleInRadians = ConvertDegreesToRadians(tailDirection);
            double cosAngle = Math.Cos(angleInRadians);
            double sinAngle = Math.Sin(angleInRadians);

            (double tailX, double tailY) = GridToMeters(chainCube.TailLine, chainCube.TailLayer);
            current.ExitPosition = (
                (tailX + cosAngle * NOTE_SIZE) * chainCube.Squish,
                (tailY + sinAngle * NOTE_SIZE) * chainCube.Squish
            );
        }

        public static void CalcEntryExit(SwingData current, Cube tail = null)
        {
            Cube headCube = current.Notes[0];
            (double headCenterX, double headCenterY) = GridToMeters(headCube.X, headCube.Y);

            double swingAngle = headCube.Direction;

            double angleRad = ConvertDegreesToRadians(swingAngle);
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            current.EntryPosition = (headCenterX - cos * NOTE_SIZE, headCenterY - sin * NOTE_SIZE);
            
            // If tail exists, use tail position for exit; otherwise use head position
            if (tail != null)
            {
                (double tailCenterX, double tailCenterY) = GridToMeters(tail.X, tail.Y);
                current.ExitPosition = (tailCenterX + cos * NOTE_SIZE, tailCenterY + sin * NOTE_SIZE);
            }
            else
            {
                current.ExitPosition = (headCenterX + cos * NOTE_SIZE, headCenterY + sin * NOTE_SIZE);
            }
        }

        public static void NormalizeAngle(SwingData previous, SwingData current, bool strictAngles)
        {
            // Calculate the geometric angle
            double deltaX = current.Notes[0].X - previous.Notes[0].X;
            double deltaY = current.Notes[0].Y - previous.Notes[0].Y;

            // Calculate angle in radians, then convert to degrees
            double angleRadians = Math.Atan2(deltaY, deltaX);
            double potentialAngle = angleRadians * 180.0 / Math.PI;

            // Normalize to 0-360 range
            if (potentialAngle < 0)
            {
                potentialAngle += 360.0;
            }

            // Get the current note's intended direction
            double currentAngle = current.Notes[0].Direction;

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

                (double centerX, double centerY) = GridToMeters(current.Notes[0].X, current.Notes[0].Y);

                double angleRad = ConvertDegreesToRadians(current.Direction);
                double cos = Math.Cos(angleRad);
                double sin = Math.Sin(angleRad);

                current.EntryPosition = (centerX - cos * NOTE_SIZE, centerY - sin * NOTE_SIZE);
                current.ExitPosition = (centerX + cos * NOTE_SIZE, centerY + sin * NOTE_SIZE);
            }
        }
    }
}