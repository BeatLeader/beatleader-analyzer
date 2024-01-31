using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace beatleader_analyzer.BeatmapScanner.Algorithm
{
    internal class StaminaCalculator
    {
        public static double CalcStamina(List<SwingData> swingData, double bpm)
        {
            const double ratingScale = 1 / 680.0;
            const double regenSeconds = 240; // 4 minutes to regenerate all energy
            swingData = CalcEnergyCost(swingData, bpm);

            double upperBound = 0;
            foreach (SwingData swing in swingData)
            {
                upperBound += swing.EnergyCost;
            }

            if (upperBound <= 0)
            {
                return 0;
            }

            // binary search
            double lowerBound = 0;
            while (Math.Abs(1 - (lowerBound / upperBound)) > 0.0001)
            {
                double current = (lowerBound + upperBound) / 2;
                double regenPerBeat = (current / regenSeconds) * (60 / bpm);

                if (HasEnoughStamina(current, regenPerBeat, swingData))
                {
                    upperBound = current;
                }
                else
                {
                    lowerBound = current;
                }
            }

            double result = (lowerBound + upperBound) / 2 * ratingScale;

            return result;
        }

        public static List<SwingData> CalcEnergyCost(List<SwingData> swingData, double bpm)
        {
            const double frequencyScalingPower = 1;
            foreach (SwingData swing in swingData)
            {
                swing.EnergyCost = Math.Pow(swing.SwingFrequency * bpm / 60, frequencyScalingPower);
            }

            return swingData;
        }

        public static bool HasEnoughStamina(double maxEnergy, double regenPerBeat, List<SwingData> swingData)
        {
            double currentEnergy = maxEnergy;
            double lastTime = 0;

            foreach (SwingData swing in swingData)
            {
                double deltaBeat = swing.Time - lastTime;
                double regen = regenPerBeat * deltaBeat;
                currentEnergy = Math.Min(maxEnergy, currentEnergy + regen);
                currentEnergy -= swing.EnergyCost;
                lastTime = swing.Time;
                if (currentEnergy < 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
