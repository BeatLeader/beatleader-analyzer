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
        // Scale of the pass: ranked maps' pass on prod's (median 4.47 vs 4.44, p99 12.96 vs 13.39). Includes what used to be a
        // display curve on the pass (1.4937 x pass^0.9954, so x1.4862: 0.5195 -> 0.7721). When the swing difficulty or the
        // windows change, bring the median back with it, then check the balance.
        private const double PASS_CALIBRATION_FACTOR = 0.7721;
        // 0.548 -> 0.49 with portaBLe's AccCurvePower 0.9 (OneSaber maps were worth 0.058 less than Standard maps of their
        // stars; -0.003 with it). -> 0.44 with RatingAPI's hard swing correction (AccRating.HardSwingMultiplier), which
        // leaves one hand maps out: it raised Standard maps, so OneSaber maps went from -0.003 to -0.007 (balance tuner's
        // zero crossing 0.44, +0.000).
        private const double ONE_SABER_NERF = 0.44;
        // Scale of the tech: TechRating = BALANCED_TECH_SCALER x (average swing tech x Ratings.TechPassFactor(pass)) ^
        // Ratings.TechExponent, on prod's scale. Tech pp is proportional to it, so changing it changes tech pp unless portaBLe's
        // PpFormula.TechWeight compensates.
        private const double BALANCED_TECH_SCALER = 20.611;
        // Linear maps: pass x (1 + LINEAR_PASS_SLOPE x (linear share of the swings - LINEAR_PASS_KNEE)) above the knee (after
        // the tech, which doesn't change). In a linear run each swing goes on where the last one came from (the hand cuts
        // through the top row, travels back down along the same line, cuts the bottom note), so the swings have almost no
        // stress (repositioning only counts movement across the swing, no rotation or angle strain) though the arm covers
        // the whole height every time. Dev (26 Oct, no acc reweight), player-adjusted worth: speed / midspeed maps of 8+ stars
        // by linear share 0-0.15 / 0.15-0.25 / 0.25-0.35 / 0.35-0.45 / 0.45+: +0.000, +0.006, +0.003, -0.006, -0.024 (in
        // every pass and tech range; prod +0.022 at 0.45+); DA'AT Expert -0.067. Counting the travel along the swing as stress
        // on linear swings fixed the most linear maps only by overpaying the 0.25-0.45 ones (the hardest windows of those are
        // linear too, but the shortfall comes from the whole map being linear), and widened the worth spread. 0.375: -0.001,
        // +0.005, +0.003, -0.001, -0.003, DA'AT -0.018 (the rest is the Acc AI's), worth spread 0.0377 -> 0.0376; 0.5 overshot
        // (+0.006 at 0.45+).
        private const double LINEAR_PASS_KNEE = 0.3;
        private const double LINEAR_PASS_SLOPE = 0.375;
        // Pass windows (see UseAlgorithm), in seconds. Windows in swings (prod's 8-128) last only 1-19 s on a typical map and
        // under 10 s on 10+ stars, so short dense bursts set the pass; 16-128 s fixed that, but then long dense streams were
        // overvalued (Ascension to Heaven Expert) and peak-driven speed maps undervalued (The Purple Dimension). 4-32 s, against
        // 16-128 s (Megametric75 relative to the star range): Ascension Expert +1.57 -> +1.41, The Purple Dimension -0.13 ->
        // +0.08, pass share gap 0.016 -> 0.008, OneSaber gap 0.020 -> 0.005 (tech share slope 0.008 -> 0.071 until portaBLe's
        // PpFormula.TechWeight 0.1695 -> 0.1485). 8-64 s did less on all of these; shorter windows still let a few seconds
        // of burst count more than the longer ones did.
        private static readonly double[] WindowSeconds = { 4, 8, 16, 32 };
        private static readonly int[] WindowSwings = { 16, 32, 64, 128 };
        // Weight of the windows in seconds in the pass: 1 = seconds only, 0 = swings only (prod). Changing it or the windows
        // changes the pass scale, so recalibrate PASS_CALIBRATION_FACTOR and check the balance after.
        private static readonly double SECONDS_WINDOW_WEIGHT = 1.0;
        // Swing density in the seconds windows: each window's average swing difficulty times (swings per second /
        // DENSITY_REFERENCE)^DENSITY_WEIGHT (half the reference for one hand, and for both when only one hand plays, as on
        // OneSaber). 0 = average per swing only.
        private static readonly double DENSITY_WEIGHT = 0.25;
        private const double DENSITY_REFERENCE = 8;

        // Inline: a note hidden behind the previous note in the same column and row (either color), at most 1/4 beat after it
        // (JsonTime). Notes at the same time aren't inlines.
        public const double INLINE_MAX_GAP = 0.25;
        public const double INLINE_MIN_GAP = 0.001;

        private static int CountInlines(List<Cube> red, List<Cube> blue)
        {
            int count = 0;
            foreach (var spot in red.Concat(blue).GroupBy(c => (c.X, c.Y)))
            {
                var ordered = spot.OrderBy(c => c.JsonTime).ToList();
                for (int i = 1; i < ordered.Count; i++)
                {
                    double gap = ordered[i].JsonTime - ordered[i - 1].JsonTime;
                    if (gap >= INLINE_MIN_GAP && gap <= INLINE_MAX_GAP + INLINE_MIN_GAP) count++;
                }
            }
            return count;
        }

        public static Ratings UseAlgorithm(List<Cube> red, List<Cube> blue, 
            Modifiers modifiers, List<Wall> walls = null, List<Bomb> bombs = null)
        {
            // Before the preprocessing, on the notes as mapped
            int inlines = CountInlines(red, blue);
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

                // https://www.desmos.com/calculator/dspid2fyyj (with the power, see Ratings.TechExponent)
                balancedTech = BALANCED_TECH_SCALER * Math.Pow(tech * Ratings.TechPassFactor(balancedPass), Ratings.TechExponent);
            }

            double linearPercentage = combinedSwingData.Count > 0 ? combinedSwingData.Count(s => s.IsLinear) / (double)combinedSwingData.Count : 0;
            balancedPass *= 1 + LINEAR_PASS_SLOPE * Math.Max(0, linearPercentage - LINEAR_PASS_KNEE);

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
                LinearSwings = combinedSwingData.Count(s => s.IsLinear),
                Inlines = inlines,
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
                PassRating = balancedPass,
                TechRating = balancedTech,
                Statistics = combinedStatistics,
                SwingData = combinedSwingData,
                DodgeWalls = dodgeWallsAll,
                CrouchWalls = crouchWallsAll,
                LinearPercentage = linearPercentage,
                PeakSustainedEBPM = peakSustainedEBPM,
                HitDistance = combinedSwingData.Count > 0 ? combinedSwingData.Average(s => s.HitDistance) : 0,
                HardSwingRatio = redSwingData.Count > 1 && blueSwingData.Count > 1 ? HardSwingRatio(combinedSwingData) : 0,
                MultiPercentage = multiPercentage
            };

            return ratings;
        }

        // Ratings.HardSwingRatio: the hardest 10% of swings' mean difficulty over the highest average of any 8 s of the map
        // (song time, from each swing on; no density factor). swingData in time order.
        private const double HARD_SWING_WINDOW = 8;
        private static double HardSwingRatio(List<SwingData> swingData)
        {
            int count = swingData.Count;
            if (count < 2) return 0;
            double last = swingData[count - 1].Cubes[0].Seconds;
            double peak = 0, sum = 0;
            int end = 0;
            for (int start = 0; start < count; start++)
            {
                double windowEnd = swingData[start].Cubes[0].Seconds + HARD_SWING_WINDOW;
                if (windowEnd > last) break;
                if (end < start) { end = start; sum = 0; }
                while (end < count && swingData[end].Cubes[0].Seconds < windowEnd) { sum += swingData[end].SwingDiff; end++; }
                if (end > start) peak = Math.Max(peak, sum / (end - start));
                sum -= swingData[start].SwingDiff;
            }
            if (peak <= 0) return 0;
            var hardest = swingData.Select(s => s.SwingDiff).OrderByDescending(d => d).Take(Math.Max(1, count / 10));
            return hardest.Average() / peak;
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

