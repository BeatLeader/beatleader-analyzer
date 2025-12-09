using Analyzer.BeatmapScanner.Data;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.IsSameDirection;

namespace beatleader_analyzer.BeatmapScanner.Helper.Grid
{
    /// <summary>
    /// Calculates swing directions for dot notes based on spatial relationship.
    /// </summary>
    internal class FindAngleViaPosition
    {
        public static double FindAngleViaPos(Cube current, Cube previous, double guideAngle, bool isSameSwing)
        {
            (double x, double y) startPosition;
            (double x, double y) currentPosition = (current.X, current.Y);

            if (isSameSwing)
            {
                startPosition = (previous.X, previous.Y);
            }
            else
            {
                startPosition = SimSwingPos(previous.X, previous.Y, guideAngle);
            }

            if (Math.Abs(startPosition.x - currentPosition.x) < 0.001 && 
                Math.Abs(startPosition.y - currentPosition.y) < 0.001)
            {
                return isSameSwing ? guideAngle : ReverseCutDirection(guideAngle);
            }

            double deltaX = currentPosition.x - startPosition.x;
            double deltaY = currentPosition.y - startPosition.y;
            double spatialAngle = Mod(ConvertRadiansToDegrees(Math.Atan2(deltaY, deltaX)), 360);

            double calculatedAngle = spatialAngle;

            if (isSameSwing)
            {
                if (!IsSameDir(spatialAngle, guideAngle))
                {
                    calculatedAngle = ReverseCutDirection(spatialAngle);
                    
                    if (!IsSameDir(calculatedAngle, guideAngle))
                    {
                        calculatedAngle = guideAngle;
                    }
                }
            }
            else
            {
                if (IsSameDir(spatialAngle, guideAngle))
                {
                    calculatedAngle = ReverseCutDirection(spatialAngle);
                }
            }

            return calculatedAngle;
        }

        public static (double x, double y) SimSwingPos(double x, double y, double direction, double distance = 1)
        {
            // Distance is in grid units along the X axis (0.6m spacing)
            // This simulates where the hand would be after swinging in the given direction
            double distanceInMeters = distance * 0.6;
            return (x + distanceInMeters * Math.Cos(ConvertDegreesToRadians(direction)), 
                    y + distanceInMeters * Math.Sin(ConvertDegreesToRadians(direction)));
        }
    }
}

