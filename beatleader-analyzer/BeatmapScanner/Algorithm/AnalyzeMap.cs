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

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Main difficulty calculation algorithm.
    /// </summary>
    internal class AnalyzeMap
    {
        // Puts the median pass of ranked maps at prod's (with the windows below); recalibrate when the windows change
        private const double PASS_CALIBRATION_FACTOR = 0.829;
        private const double ONE_SABER_NERF = 0.53;
        // Tech ratings on about prod's scale (tech is flatter than prod's: median ~8% lower, the top ~18)
        private const double BALANCED_TECH_SCALER = 22.0;
        // Pass windows (see UseAlgorithm). In seconds: 16-128 s gave the smoothest pp vs stars curves and star bands of the
        // sets tried (2-32, 4-64, 16-128 s; swings 8-128, 16-128), and removes the outliers swing windows created on maps
        // with short dense bursts (prod's 8-128 swings last only 1-19 s on a typical map, under 10 s on 10+ stars).
        private static readonly double[] WindowSeconds = { 16, 32, 64, 128 };
        private static readonly int[] WindowSwings = { 16, 32, 64, 128 };
        // Weight of the windows in seconds in the pass: 1 = seconds only, 0 = swings only (prod). Changing it or the windows
        // changes the pass scale, so recalibrate PASS_CALIBRATION_FACTOR and check the balance after.
        private static readonly double SECONDS_WINDOW_WEIGHT = 1.0;
        // Swing density in the seconds windows: each window's average swing difficulty times (swings per second /
        // DENSITY_REFERENCE)^DENSITY_WEIGHT (half the reference for one hand, and for both when only one hand plays, as on
        // OneSaber). 0 = average per swing only.
        private static readonly double DENSITY_WEIGHT = 0.25;
        private const double DENSITY_REFERENCE = 8;

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
                ParityPredictor.Predict(red, false);
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
                ParityPredictor.Predict(blue, true);
                blueSwingData = SwingCreation.Process(blue, true, modifiers);
                
                if (blueSwingData.Count > 0)
                {
                    SwingMovement.Calc(blueSwingData, true);
                    combinedSwingData.AddRange(blueSwingData);
                }
            }
            
            combinedSwingData.Sort((a, b) =>
            {
                int cmp = a.Cubes[0].Seconds.CompareTo(b.Cubes[0].Seconds);
                if (cmp != 0) return cmp;
                return a.Cubes[0].Type.CompareTo(b.Cubes[0].Type);
            });

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
                    peakSustainedEBPM = CalculatePeakSustainedEBPM(redSwingData, 8);
                }
                if (blueSwingData.Count > 1)
                {
                    peakSustainedEBPM = Math.Max(CalculatePeakSustainedEBPM(blueSwingData, 8), peakSustainedEBPM);
                }

                // Resets faster than the map's peak sustained EBPM are probably not real: their frequency shouldn't be doubled
                DetermineFalsePositive(combinedSwingData, peakSustainedEBPM);

                if (redSwingData.Count > 1)
                {
                    peakSustainedEBPM = CalculatePeakSustainedEBPM(redSwingData);
                }
                if (blueSwingData.Count > 1)
                {
                    peakSustainedEBPM = Math.Max(CalculatePeakSustainedEBPM(blueSwingData), peakSustainedEBPM);
                }

                // Use all classified walls for difficulty calculation
                Difficulty.CalcSwingDiff(combinedSwingData, modifiers, dodgeWallsAll, crouchWallsAll);

                // Blend of windows in seconds (real time: how long the hard parts last matters for failing, and very fast maps
                // aren't rated on a few seconds of burst) and windows in swings (prod: dense sections count more)
                double passDiffRed = 0.0;
                double passDiffBlue = 0.0;
                double passDiffCombined = 0.0;

                double combinedDensityReference = redSwingData.Count > 1 && blueSwingData.Count > 1 ? DENSITY_REFERENCE : DENSITY_REFERENCE / 2;
                // A kind of window with no weight is skipped (the swing windows cost as much as the seconds ones)
                foreach (var seconds in SECONDS_WINDOW_WEIGHT > 0 ? WindowSeconds : [])
                {
                    double weight = SECONDS_WINDOW_WEIGHT / WindowSeconds.Length;
                    if (redSwingData.Count > 1)
                    {
                        passDiffRed += Difficulty.MaxAverageOverSeconds(redSwingData, seconds, modifiers, DENSITY_REFERENCE / 2, DENSITY_WEIGHT) * weight;
                    }
                    if (blueSwingData.Count > 1)
                    {
                        passDiffBlue += Difficulty.MaxAverageOverSeconds(blueSwingData, seconds, modifiers, DENSITY_REFERENCE / 2, DENSITY_WEIGHT) * weight;
                    }
                    passDiffCombined += Difficulty.MaxAverageOverSeconds(combinedSwingData, seconds, modifiers, combinedDensityReference, DENSITY_WEIGHT) * weight;
                }

                foreach (var swings in SECONDS_WINDOW_WEIGHT < 1 ? WindowSwings : [])
                {
                    double weight = (1 - SECONDS_WINDOW_WEIGHT) / WindowSwings.Length;
                    // Half of the window per hand
                    if (redSwingData.Count > 1)
                    {
                        passDiffRed += Difficulty.MaxAverageOverSwings(redSwingData, swings / 2) * weight;
                    }
                    if (blueSwingData.Count > 1)
                    {
                        passDiffBlue += Difficulty.MaxAverageOverSwings(blueSwingData, swings / 2) * weight;
                    }
                    passDiffCombined += Difficulty.MaxAverageOverSwings(combinedSwingData, swings) * weight;
                }

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
                    // Tech is the wrist work: angle strain and rotation. Repositioning (arm movement) is part of pass; fitted on
                    // ~3800 ranked maps against how much accuracy players lose beyond the Acc AI prediction (accounting for who
                    // played each map), tech explains ~15% more without it.
                    swing.SwingTech = swing.AngleStrain + swing.RotationAmount;
                    // Resets aren't tech in the players' eyes: without this, reset maps (e.g. Speedcore Paradise) get top tech
                    // ratings. Costs almost nothing in the fit (accuracy loss explained about the same either way).
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

            combinedSwingData.Sort((x, y) =>
            {
                int cmp = x.Cubes[0].BpmTime.CompareTo(y.Cubes[0].BpmTime);
                if (cmp != 0) return cmp;
                return x.Cubes[0].Type.CompareTo(y.Cubes[0].Type);
            });

            double multiPercentage = 0;
            if (combinedSwingData.Count > 0)
            {
                multiPercentage = CalculateMultiNotePercentage(combinedStatistics, combinedSwingData.Count);
            }

            Ratings ratings = new Ratings
            {
                // Displayed value; balancedPass (used for tech above) is the internal pass
                PassRating = Ratings.DisplayPass(balancedPass),
                TechRating = balancedTech,
                Statistics = combinedStatistics,
                SwingData = combinedSwingData,
                DodgeWalls = dodgeWallsAll,
                CrouchWalls = crouchWallsAll,
                LinearPercentage = combinedSwingData.Count(s => s.IsLinear) / (double)combinedSwingData.Count,
                PeakSustainedEBPM = peakSustainedEBPM,
                MultiPercentage = multiPercentage
            };

            return ratings;
        }

        private static double CalculateMultiNotePercentage(Statistics stats, int swingCount)
        {
            double multiNoteHits = stats.Stacks + stats.Towers + stats.Sliders + stats.CurvedSliders +
                                   stats.Windows + stats.SlantedWindows + stats.Chains;
            return multiNoteHits / swingCount;
        }

        private static readonly Comparer<SwingData> CompareSwingTech = 
            Comparer<SwingData>.Create((a, b) => (a.SwingTech).CompareTo(b.SwingTech));

        private static double CalculatePeakSustainedEBPM(List<SwingData> swingData, int count = 4)
        {
            if (swingData.Count == 0)
                return 0.0;

            int windowSize = Math.Min(count, swingData.Count);
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

                // The swing is above expected EBPM, flag as FalsePositive for EBPM and ratings purpose.
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

