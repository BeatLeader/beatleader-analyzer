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

            double deviation = AngleDeviation(neutralAngle, current.Direction);
            double normalizedStrain = deviation / 180.0;
            swingStrain += normalizedStrain * normalizedStrain;

            // Add falloff based on delta time between swings in seconds
            double deltaTime = Math.Abs(current.Notes[0].Seconds - previous.Notes[^1].Seconds);
            if (deltaTime >= 0.25)
            {
                // In seconds: 0.25 = 1, 0.5 = 0.707, 1 = 0.3535
                swingStrain *= Math.Exp((0.25 - deltaTime) * Math.Log(4.0)); 
            }

            return swingStrain;
        }

        public static double BezierAngleTotalStrain(Span<double> angleData, double currentTime, double previousTime, bool forehand, bool isRightHand)
        {
            if (previousTime == 0) return 0;

            double neutralAngle;
            if (forehand)
            {
                neutralAngle = isRightHand ? RIGHT_FOREHAND_NEUTRAL : LEFT_FOREHAND_NEUTRAL;
            }
            else
            {
                neutralAngle = isRightHand ? RIGHT_BACKHAND_NEUTRAL : LEFT_BACKHAND_NEUTRAL;
            }

            double totalStrain = 0;
            foreach (double angle in angleData)
            {
                double deviation = AngleDeviation(neutralAngle, angle);
                double normalizedStrain = deviation / 180.0;
                totalStrain += normalizedStrain * normalizedStrain;
            }

            // Add falloff based on delta time between swings in seconds
            double deltaTime = Math.Abs(currentTime - previousTime);
            if (deltaTime >= 0.25)
            {
                // In seconds: 0.25 = 1, 0.5 = 0.707, 1 = 0.3535
                totalStrain *= Math.Exp((0.25 - deltaTime) * Math.Log(4.0));
            }

            return totalStrain;
        }

        private static double AngleDeviation(double angle1, double angle2)
        {
            double diff = Math.Abs(angle1 - angle2);
            return 180 - Math.Abs(diff - 180);
        }

        public static double ParityAngleStrainCalc(SwingData current, SwingData previous, bool isRightHand)
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

            double deviation = AngleDeviation(neutralAngle, current.Direction);
            double normalizedStrain = deviation / 180.0;
            swingStrain += normalizedStrain * normalizedStrain;
            var similarAngle = IsSameDir(previous.Direction, current.Direction);
            if (previous.Forehand == current.Forehand && !similarAngle) swingStrain *= 8;
            if (previous.Forehand != current.Forehand && similarAngle) swingStrain *= 0.5;

            // Add falloff based on delta time between swings in seconds
            double deltaTime = Math.Abs(current.Notes[0].Seconds - previous.Notes[^1].Seconds);
            if (deltaTime >= 0.25)
            {
                // In seconds: 0.25 = 1, 0.5 = 0.707, 1 = 0.3535
                swingStrain *= Math.Exp((0.25 - deltaTime) * Math.Log(4.0));
            }

            return swingStrain;
        }
    }
}
