using Analyzer.BeatmapScanner.Data;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal class AngleStrain
    {
        const double LEFT_FOREHAND_NEUTRAL = 292.5;
        const double RIGHT_FOREHAND_NEUTRAL = 247.5;
        const double LEFT_BACKHAND_NEUTRAL = 112.5;
        const double RIGHT_BACKHAND_NEUTRAL = 67.5;

        /// <summary>
        /// Angle strain of a swing: how far its direction is from the natural direction of its parity, ((degrees) / 180)^2,
        /// lowered when there's time between the swings.
        /// </summary>
        public static double SwingAngleStrainCalc(SwingData current, SwingData previous, bool isRightHand)
        {
            // First swing can be considered to have no strain
            if (previous == null) return 0;

            double swingStrain = 0;

            double neutralAngle;
            if (current.Forehand)
            {
                neutralAngle = isRightHand ? RIGHT_FOREHAND_NEUTRAL : LEFT_FOREHAND_NEUTRAL;
            }
            else
            {
                neutralAngle = isRightHand ? RIGHT_BACKHAND_NEUTRAL : LEFT_BACKHAND_NEUTRAL;
            }

            double deviation = AngleDeviation(neutralAngle, DotDirections.DifficultyDirection(current));
            double normalizedStrain = deviation / 180.0;
            swingStrain += normalizedStrain * normalizedStrain;

            // Add falloff based on delta time between swings in seconds
            double deltaTime = Math.Abs(current.Cubes[0].Seconds - previous.Cubes[^1].Seconds);
            if (deltaTime >= 0.25)
            {
                // In seconds: 0.25 = 1, 0.5 = 0.707, 1 = 0.3535
                swingStrain *= Math.Exp((0.25 - deltaTime) * Math.Log(4.0)); 
            }

            return swingStrain;
        }

        private static double AngleDeviation(double angle1, double angle2)
        {
            double diff = Math.Abs(angle1 - angle2);
            return 180 - Math.Abs(diff - 180);
        }
    }
}
