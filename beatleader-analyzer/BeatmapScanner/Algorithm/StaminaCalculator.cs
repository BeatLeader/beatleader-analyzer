using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Algorithm
{
    /* Stamina rating calculator based on the following principles:
     * - total energy capacity and energy regeneration speed are linked
     * - on average regenerating all energy takes around 4 minutes
     * - each swing in a map has an energy cost associated with it
     * - calculate the minimum energy capacity needed to never run out of energy: this is the stamina rating
     */

    internal class StaminaCalculator
    {
        const double ratingScale = 1 / 7170.0;
        const double regenSeconds = 240; // 4 minutes to regenerate all energy

        public static double CalcStamina(List<SwingData> swingData)
        {
            swingData = CalcEnergyCost(swingData);

            double upperBound = 0;
            foreach (SwingData swing in swingData)
            {
                upperBound += swing.EnergyCost;
            }

            if (upperBound <= 0)
            {
                return 0;
            }

            var bpm = swingData[0].BpmTime / (swingData[0].Seconds / 60f);

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

        public static List<SwingData> CalcEnergyCost(List<SwingData> swingData)
        {
            // calculates energy cost per swing using a simple linear predictions model
            // derived from https://www.desmos.com/calculator/gkesplq7gh
            // kinetic energy is proportional to squared velocity
            // velocity is proportional to swing amplitude and swings per second
            // multiply those, square, and simplify to get kinetic energy per swing

            const double predictionsTime = 0.025; // in seconds
            const double predictionsSquared = predictionsTime * predictionsTime;
            const double piSquared = Math.PI * Math.PI;
            const double pathStrainScaling = 5.0; // how much path strain increases swing energy cost
            const double holdEnergyScaling = 50.0; // how much holding arms in position increases energy cost

            SwingData lastSwing = null;
            for (var i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                var bpm = swing.BpmTime / (swing.Seconds / 60f);
                var swingsPerSecond = Math.Max(swing.SwingFrequency * bpm / 60, 2); // assume lowest reasonable swing speed equivalent to 2 swings per second
                var spsSquared = swingsPerSecond * swingsPerSecond;

                swing.EnergyCost = spsSquared / (piSquared * spsSquared * predictionsSquared + 1);
                swing.EnergyCost *= 1 + swing.AngleStrain * 0.1 * pathStrainScaling * swing.StressMultiplier;

                if (swing.ParityErrors) swing.EnergyCost *= 2.0; // account for the extra swing on a reset

                if (lastSwing is not null)
                {
                    var swingYPos = (lastSwing.EntryPosition.y + lastSwing.ExitPosition.y) / 2;
                    var holdDuration = Math.Min((swing.BpmTime - lastSwing.BpmTime) * 60 / bpm, 1.0); // maximum 1 second hold
                    lastSwing.EnergyCost += holdDuration * (1 + swingYPos) * holdEnergyScaling;
                }

                lastSwing = swing;
            }

            return swingData;
        }

        public static bool HasEnoughStamina(double maxEnergy, double regenPerBeat, List<SwingData> swingData)
        {
            double currentEnergy = maxEnergy;
            double lastTime = 0;

            foreach (SwingData swing in swingData)
            {
                double deltaBeat = swing.BpmTime - lastTime;
                double regen = regenPerBeat * deltaBeat;
                currentEnergy = Math.Min(maxEnergy, currentEnergy + regen);
                currentEnergy -= swing.EnergyCost;
                lastTime = swing.BpmTime;
                if (currentEnergy < 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
