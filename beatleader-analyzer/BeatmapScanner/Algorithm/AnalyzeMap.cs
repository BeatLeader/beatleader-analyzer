using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;
using System.Linq;
using beatleader_analyzer.BeatmapScanner.Data;
using System.Runtime.InteropServices;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNoteClassifier;
using static beatleader_analyzer.BeatmapScanner.Helper.WallClassifier;
using Parser.Map.Difficulty.V3.Grid;
using beatleader_analyzer.BeatmapScanner.Helper;
using beatleader_analyzer.BeatmapScanner.Algorithm;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Main difficulty calculation algorithm.
    /// </summary>
    internal class AnalyzeMap
    {
        private const double PASS_CALIBRATION_FACTOR = 0.825;
        private const double ONE_SABER_NERF = 0.5;
        private const double BALANCED_TECH_SCALER = 14.0;

        public static Ratings UseAlgorithm(List<Cube> red, List<Cube> blue, 
            Modifiers modifiers, List<Wall> walls = null, List<Bomb> bombs = null)
        {
            List<SwingData> redSwingData = [];
            List<SwingData> blueSwingData = [];
            List<SwingData> combinedSwingData = [];
            double peakSustainedEBPM = 0.0;

            if (red.Count > 2)
            {
                PreprocessNotes.Detect(red, bombs, false);
                ParityPredictor.Predict(red, false, bombs);
                redSwingData = SwingCreation.Process(red, false, modifiers);
                
                if (redSwingData.Count > 0)
                {
                    SwingMovement.Calc(redSwingData, false);
                    combinedSwingData.AddRange(redSwingData);
                }
            }

            if (blue.Count > 2)
            {
                PreprocessNotes.Detect(blue, bombs, true);
                ParityPredictor.Predict(blue, true, bombs);
                blueSwingData = SwingCreation.Process(blue, true, modifiers);
                
                if (blueSwingData.Count > 0)
                {
                    SwingMovement.Calc(blueSwingData, true);
                    combinedSwingData.AddRange(blueSwingData);
                }
            }
            
            combinedSwingData.Sort((a, b) => a.Cubes[0].Seconds.CompareTo(b.Cubes[0].Seconds));
            double balancedPass = 0.0;
            double balancedTech = 0.0;

            // Classify all walls (for difficulty calculation) and count unique dodge actions (for statistics)
            var (dodgeWallsAll, crouchWallsAll, dodgeWallsCount, crouchWallsCount, dodgeDuration, crouchDuration) = walls != null 
                ? ClassifyWalls(walls) 
                : (new List<Wall>(), new List<Wall>(), 0, 0, 0, 0);

            if (combinedSwingData.Count > 0)
            {
                redSwingData = combinedSwingData.Where(x => x.Cubes[0].Type == 0).ToList();
                blueSwingData = combinedSwingData.Where(x => x.Cubes[0].Type == 1).ToList();

                // peakSustainedEBPM will be used to determine if a ParityError is a FalsePositive.
                if (redSwingData.Count > 1)
                {
                    peakSustainedEBPM = CalculatePeakSustainedEBPM(redSwingData);
                }
                if (blueSwingData.Count > 1)
                {
                    peakSustainedEBPM = Math.Max(CalculatePeakSustainedEBPM(blueSwingData), peakSustainedEBPM);
                }

                DetermineFalsePositive(combinedSwingData, peakSustainedEBPM);

                // Use all classified walls for difficulty calculation
                Difficulty.CalcSwingDiff(combinedSwingData, modifiers, dodgeWallsAll, crouchWallsAll);

                var windowSizes = new HashSet<int> { 8, 16, 32, 64, 128 };
                double passDiffRed = 0.0;
                double passDiffBlue = 0.0;
                double passDiffCombined = 0.0;

                foreach (var windowSize in windowSizes)
                {
                    if (redSwingData.Count > 1)
                    {
                        passDiffRed += Difficulty.CalcAverage(redSwingData, windowSize / 2).Select(x => x.Pass).Max();
                    }
                    if (blueSwingData.Count > 1)
                    {
                        passDiffBlue += Difficulty.CalcAverage(blueSwingData, windowSize / 2).Select(x => x.Pass).Max();
                    }
                    passDiffCombined += Difficulty.CalcAverage(combinedSwingData, windowSize).Select(x => x.Pass).Max();
                }
                
                passDiffRed /= windowSizes.Count;
                passDiffBlue /= windowSizes.Count;
                passDiffCombined /= windowSizes.Count;

                double easierHand = Math.Min(passDiffRed, passDiffBlue);
                double harderHand = Math.Max(passDiffRed, passDiffBlue);
                double handsRatio = 1.0;
                
                if (harderHand > 0 || passDiffCombined > 0)
                {
                    handsRatio = Math.Min(easierHand / Math.Max(Math.Min(harderHand, passDiffCombined), 0.0001), 1.0);
                }

                // https://www.desmos.com/calculator/ibkvqjsuoo
                double nerfMultiplier = 1.0 - (1.0 - handsRatio) * ONE_SABER_NERF;
                balancedPass = passDiffCombined * nerfMultiplier * PASS_CALIBRATION_FACTOR;
            }

            if (combinedSwingData.Count > 2)
            {
                foreach (var swing in combinedSwingData)
                {
                    double buff = NjsBuff.CalculateNjsBuff(swing.Cubes[0].Njs, modifiers);
                    swing.AngleStrain *= buff;
                    swing.RepositioningDistance *= buff;
                    swing.RotationAmount *= buff;
                    swing.SwingTech = swing.AngleStrain + swing.RepositioningDistance + swing.RotationAmount;
                    if (swing.ParityErrors) swing.SwingTech /= 2;
                }

                combinedSwingData.Sort(CompareSwingTech);
                double tech = AverageSwingTech(CollectionsMarshal.AsSpan(combinedSwingData)[(int)(combinedSwingData.Count * 0.25)..]);

                // https://www.desmos.com/calculator/dspid2fyyj
                balancedTech = tech * (1.0 - Math.Pow(1.4, -balancedPass)) * BALANCED_TECH_SCALER;
            }

            var redMultiNotes = redSwingData.Count > 0 ? CountMultiNoteHits(redSwingData) : new Statistics();
            var blueMultiNotes = blueSwingData.Count > 0 ? CountMultiNoteHits(blueSwingData) : new Statistics();

            // Label multi-note hit types for each swing
            if (redSwingData.Count > 0)
            {
                LabelSwingMultiNoteHits(redSwingData);
            }
            if (blueSwingData.Count > 0)
            {
                LabelSwingMultiNoteHits(blueSwingData);
            }

            // Use the counted walls (respecting cooldown) for statistics
            int dodgeWallCount = dodgeWallsCount;
            int crouchWallCount = crouchWallsCount;

            int parityErrorsCount = combinedSwingData.Count(s => s.ParityErrors);
            int bombAvoidanceCount = combinedSwingData.Count(s => s.BombAvoidance);

            int chainCount = 0;
            if (combinedSwingData.Count > 0)
            {
                chainCount = combinedSwingData.Where(x => x.Cubes[0].Chain).Count();
            }

            var combinedStatistics = new Statistics
            {
                Stacks = redMultiNotes.Stacks + blueMultiNotes.Stacks,
                Towers = redMultiNotes.Towers + blueMultiNotes.Towers,
                Sliders = redMultiNotes.Sliders + blueMultiNotes.Sliders,
                CurvedSliders = redMultiNotes.CurvedSliders + blueMultiNotes.CurvedSliders,
                Windows = redMultiNotes.Windows + blueMultiNotes.Windows,
                SlantedWindows = redMultiNotes.SlantedWindows + blueMultiNotes.SlantedWindows,
                Chains = chainCount,
                DodgeWalls = dodgeWallCount,
                DodgeWallDuration = dodgeDuration,
                CrouchWalls = crouchWallCount,
                CrouchWallDuration = crouchDuration,
                ParityErrors = parityErrorsCount,
                BombAvoidances = bombAvoidanceCount,
                LinearSwings = combinedSwingData.Count(s => s.IsLinear)
            };

            combinedSwingData.Sort((x, y) => x.BpmTime.CompareTo(y.BpmTime));
            double multiPercentage = 0;
            if (combinedSwingData.Count > 0)
            {
                multiPercentage = CalculateMultiNotePercentage(combinedStatistics, combinedSwingData.Count);
            }

            // stamina calc
            double staminaDiffLeft = StaminaCalculator.CalcStamina(redSwingData);
            double staminaDiffRight = StaminaCalculator.CalcStamina(blueSwingData);
            double stamina_rating = Math.Max(staminaDiffLeft, staminaDiffRight) * 0.9 + Math.Min(staminaDiffLeft, staminaDiffRight) * 0.1;

            Ratings ratings = new Ratings
            {
                PassRating = balancedPass,
                TechRating = balancedTech,
                LowNoteNerf = CalculateLowNoteNerf(combinedSwingData.Count),
                Statistics = combinedStatistics,
                SwingData = combinedSwingData,
                DodgeWalls = dodgeWallsAll,
                CrouchWalls = crouchWallsAll,
                LinearPercentage = combinedSwingData.Count(s => s.IsLinear) / (double)combinedSwingData.Count,
                PeakSustainedEBPM = peakSustainedEBPM,
                MultiPercentage = multiPercentage,
                StaminaRating = stamina_rating
            };

            return ratings;
        }

        // https://www.desmos.com/calculator/i4mh0n2ttd
        private static double CalculateLowNoteNerf(int noteCount)
        {
            double clamped = Math.Max(20, Math.Min(200, noteCount));
            return 0.6 + (clamped - 20) / 450.0;
        }

        private static double CalculateMultiNotePercentage(Statistics stats, int swingCount)
        {
            double multiNoteHits = stats.Stacks + stats.Towers + stats.Sliders + stats.CurvedSliders +
                                   stats.Windows + stats.SlantedWindows + stats.Chains;
            return multiNoteHits / swingCount;
        }

        private static readonly Comparer<SwingData> CompareSwingTech = 
            Comparer<SwingData>.Create((a, b) => (a.SwingTech).CompareTo(b.SwingTech));

        private static double CalculatePeakSustainedEBPM(List<SwingData> swingData)
        {
            if (swingData.Count == 0)
                return 0.0;

            int windowSize = Math.Min(4, swingData.Count);
            double maxEbpm = 0.0;

            for (int i = 0; i <= swingData.Count - windowSize; i++)
            {
                double freqSum = 0.0;

                for (int j = i; j < i + windowSize; j++)
                {
                    freqSum += swingData[j].SwingFrequency; // already 1 / seconds
                }

                double avgFrequency = freqSum / windowSize;

                // Convert Hz → BPM
                // Divided by 2 because it's 1 swing per half beat in Beat Saber
                double ebpm = avgFrequency * 60.0 / 2;

                if (ebpm > maxEbpm)
                {
                    maxEbpm = ebpm;
                }
            }

            return maxEbpm;
        }

        private static void DetermineFalsePositive(List<SwingData> swingData, double peakSustainedEbpm)
        {
            foreach(var swing in swingData)
            {
                double swingEbpm = swing.SwingFrequency * 60.0 / 2;

                // The swing is above expected EBPM, flag as FalsePositive
                if (swingEbpm > peakSustainedEbpm + 1 && swing.ParityErrors)
                {
                    swing.FalsePositive = true;
                    swing.SwingFrequency /= 2;
                }
            }
        }

        private static double AverageSwingTech(Span<SwingData> list)
        {
            double sum = 0;
            foreach (SwingData swing in list)
            {
                sum += swing.SwingTech;
            }
            return sum / list.Length;
        }
    }
}

