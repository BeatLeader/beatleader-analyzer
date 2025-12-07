using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Helper.MathHelper
{
    internal class SwingAngleStrain
    {
        const double LEFT_FOREHAND_NEUTRAL = 292.5;
        const double RIGHT_FOREHAND_NEUTRAL = 247.5;
        const double LEFT_BACKHAND_NEUTRAL = 112.5;
        const double RIGHT_BACKHAND_NEUTRAL = 67.5;

        public static double SwingAngleStrainCalc(List<SwingData> swingData, bool isRightHand)
        {
            if (swingData.Count == 0)
            {
                return 0;
            }

            double totalStrain = 0;

            foreach (var swing in swingData)
            {
                double neutralAngle;
                if (swing.Forehand)
                {
                    neutralAngle = isRightHand ? RIGHT_FOREHAND_NEUTRAL : LEFT_FOREHAND_NEUTRAL;
                }
                else
                {
                    neutralAngle = isRightHand ? RIGHT_BACKHAND_NEUTRAL : LEFT_BACKHAND_NEUTRAL;
                }

                double deviation = AngleDeviation(neutralAngle, swing.Angle);
                double normalizedStrain = deviation / 180.0;
                totalStrain += normalizedStrain * normalizedStrain;
            }

            return totalStrain / swingData.Count;
        }

        public static double BezierAngleTotalStrain(Span<double> angleData, bool forehand, bool isRightHand)
        {
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

            return totalStrain;
        }

        private static double AngleDeviation(double angle1, double angle2)
        {
            double diff = Math.Abs(angle1 - angle2);
            return 180 - Math.Abs(diff - 180);
        }
    }
}
