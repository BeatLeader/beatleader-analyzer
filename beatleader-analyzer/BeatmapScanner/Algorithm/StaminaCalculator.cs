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
            const double ratingScale = 1 / 4000.0;
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
            // calculates energy cost per swing using a simple linear predictions model
            // derived from https://www.desmos.com/calculator/gkesplq7gh
            // kinetic energy is proportional to squared velocity
            // velocity is proportional to swing amplitude and swings per second
            // multiply those, square, and simplify to get kinetic energy per swing

            const double predictionsTime = 0.025; // in seconds
            const double predictionsSquared = predictionsTime * predictionsTime;
            const double piSquared = Math.PI * Math.PI;

            foreach (SwingData swing in swingData)
            {
                var swingsPerSecond = Math.Max(swing.SwingFrequency * bpm / 60, 2); // assume lowest reasonable swing speed equivalent to 2 swings per second
                var spsSquared = swingsPerSecond * swingsPerSecond;

                swing.EnergyCost = spsSquared / (piSquared * spsSquared * predictionsSquared + 1);
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
