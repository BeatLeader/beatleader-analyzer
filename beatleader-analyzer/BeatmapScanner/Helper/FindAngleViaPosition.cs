using Analyzer.BeatmapScanner.Data;
using System;
using static Analyzer.BeatmapScanner.Helper.Helper;
using static Analyzer.BeatmapScanner.Helper.IsSameDirection;

namespace Analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Calculates swing directions for dot notes based on spatial relationship.
    /// </summary>
    internal class FindAngleViaPosition
    {
        public static double FindAngleViaPos(Cube current, Cube previous, double guideAngle, bool isSameSwing)
        {
            (double x, double y) startPosition;
            (double x, double y) currentPosition = (current.Line, current.Layer);

            if (isSameSwing)
            {
                startPosition = (previous.Line, previous.Layer);
            }
            else
            {
                startPosition = SimSwingPos(previous.Line, previous.Layer, guideAngle);
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
            return (x + distance * Math.Cos(ConvertDegreesToRadians(direction)), 
                    y + distance * Math.Sin(ConvertDegreesToRadians(direction)));
        }
    }
}

