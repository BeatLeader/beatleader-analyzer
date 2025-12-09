using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;
using System.Linq;
using beatleader_analyzer.BeatmapScanner.Data;
using System.Runtime.InteropServices;
using static beatleader_analyzer.BeatmapScanner.Helper.MultiNote.MultiNoteClassifier;
using static beatleader_analyzer.BeatmapScanner.Helper.WallHelper.WallClassifier;
using Parser.Map.Difficulty.V3.Grid;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Main difficulty calculation algorithm (LackWiz Algorithm).
    /// </summary>
    internal class Analyze
    {
        private const double PASS_CALIBRATION_FACTOR = 0.8;
        private const double ONE_SABER_NERF = 0.35;
        private const double BALANCED_TECH_SCALER = 10.0;

        public static Ratings UseLackWizAlgorithm(List<Cube> red, List<Cube> blue, float bpm, List<Wall> walls = null, List<Bomb> bombs = null, bool strictAngles = false)
        {
            List<SwingData> redSwingData = [];
            List<SwingData> blueSwingData = [];
            List<SwingData> combinedSwingData = [];

            if (red.Count > 2)
            {
                PreprocessNotes.Detect(red, bombs, bpm, false);
                ParityPredictor.Predict(red, false, bombs);
                redSwingData = SwingProcesser.Process(red, false, strictAngles);
                
                if (redSwingData.Count > 0)
                {
                    SwingCurve.Calc(redSwingData, false);
                    combinedSwingData.AddRange(redSwingData);
                }
            }

            if (blue.Count > 2)
            {
                PreprocessNotes.Detect(blue, bombs, bpm, true);
                ParityPredictor.Predict(blue, true, bombs);
                blueSwingData = SwingProcesser.Process(blue, true, strictAngles);
                
                if (blueSwingData.Count > 0)
                {
                    SwingCurve.Calc(blueSwingData, true);
                    combinedSwingData.AddRange(blueSwingData);
                }
            }
            
            combinedSwingData.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            double balancedPass = 0.0;
            double balancedTech = 0.0;

            // Classify all walls (for difficulty calculation) and count unique dodge actions (for statistics)
            var (dodgeWallsAll, crouchWallsAll, dodgeWallsCount, crouchWallsCount) = walls != null 
                ? ClassifyWalls(walls, bpm) 
                : (new List<Wall>(), new List<Wall>(), 0, 0);

            if (combinedSwingData.Count > 0)
            {
                // Use all classified walls for difficulty calculation
                DiffToPass.CalcSwingDiff(combinedSwingData, bpm, dodgeWallsAll, crouchWallsAll);
                
                redSwingData = combinedSwingData.Where(x => x.Start.Type == 0).ToList();
                blueSwingData = combinedSwingData.Where(x => x.Start.Type == 1).ToList();

                var windowSizes = new HashSet<int> { 8, 16, 32, 64, 128 };
                double passDiffRed = 0.0;
                double passDiffBlue = 0.0;
                double passDiffCombined = 0.0;

                foreach (var windowSize in windowSizes)
                {
                    if (redSwingData.Count > 1)
                    {
                        passDiffRed += DiffToPass.CalcAverage(redSwingData, windowSize / 2).Select(x => x.Pass).Max();
                    }
                    if (blueSwingData.Count > 1)
                    {
                        passDiffBlue += DiffToPass.CalcAverage(blueSwingData, windowSize / 2).Select(x => x.Pass).Max();
                    }
                    passDiffCombined += DiffToPass.CalcAverage(combinedSwingData, windowSize).Select(x => x.Pass).Max();
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
                
                double nerfMultiplier = 1.0 - (1.0 - handsRatio) * ONE_SABER_NERF;
                balancedPass = passDiffCombined * nerfMultiplier * PASS_CALIBRATION_FACTOR;
            }

            if (combinedSwingData.Count > 2)
            {
                foreach (var swing in combinedSwingData)
                {
                    double buff = NjsBuff.CalculateNjsBuff(swing.Start.Njs);
                    swing.AngleStrain *= buff;
                    swing.PathStrain *= buff;
                }

                combinedSwingData.Sort(CompareAngleAndPathStrain);
                double tech = AverageAnglePath(CollectionsMarshal.AsSpan(combinedSwingData)[(int)(combinedSwingData.Count * 0.25)..]);
                balancedTech = tech * (1.0 - Math.Pow(1.4, -balancedPass)) * BALANCED_TECH_SCALER;
            }

            var redMultiNotes = red.Count > 2 ? CountMultiNoteHits(red, bpm) : new Statistics();
            var blueMultiNotes = blue.Count > 2 ? CountMultiNoteHits(blue, bpm) : new Statistics();

            // Label multi-note hit types for each swing using the MultiNoteHitLabeler helper
            if (red.Count > 2)
            {
                LabelSwingMultiNoteHits(red, redSwingData, bpm);
            }
            if (blue.Count > 2)
            {
                LabelSwingMultiNoteHits(blue, blueSwingData, bpm);
            }

            // Use the counted walls (respecting cooldown) for statistics
            int dodgeWallCount = dodgeWallsCount;
            int crouchWallCount = crouchWallsCount;

            int parityErrorsCount = combinedSwingData.Count(s => s.ParityErrors);
            int bombAvoidanceCount = combinedSwingData.Count(s => s.BombAvoidance);

            var combinedPatterns = new Statistics
            {
                Stacks = redMultiNotes.Stacks + blueMultiNotes.Stacks,
                Towers = redMultiNotes.Towers + blueMultiNotes.Towers,
                Sliders = redMultiNotes.Sliders + blueMultiNotes.Sliders,
                CurvedSliders = redMultiNotes.CurvedSliders + blueMultiNotes.CurvedSliders,
                Windows = redMultiNotes.Windows + blueMultiNotes.Windows,
                SlantedWindows = redMultiNotes.SlantedWindows + blueMultiNotes.SlantedWindows,
                DodgeWalls = dodgeWallCount,
                CrouchWalls = crouchWallCount,
                ParityErrors = parityErrorsCount,
                BombAvoidances = bombAvoidanceCount
            };

            combinedSwingData.Sort((x, y) => x.Beat.CompareTo(y.Beat));

            Ratings ratings = new Ratings
            {
                Pass = balancedPass,
                Tech = balancedTech,
                Nerf = CalculateLowNoteNerf(combinedSwingData.Count),
                Patterns = combinedPatterns,
                SwingData = combinedSwingData,
                DodgeWalls = dodgeWallsAll,
                CrouchWalls = crouchWallsAll
            };

            return ratings;
        }

        private static double CalculateLowNoteNerf(int noteCount)
        {
            return 1.0 / (1.0 + Math.Pow(Math.E, -1.4 - (noteCount / 50.0)));
        }

        private static readonly Comparer<SwingData> CompareAngleAndPathStrain = 
            Comparer<SwingData>.Create((a, b) => (a.AngleStrain + a.PathStrain).CompareTo(b.AngleStrain + b.PathStrain));

        private static double AverageAnglePath(Span<SwingData> list)
        {
            double sum = 0;
            foreach (SwingData swing in list)
            {
                sum += swing.AngleStrain + swing.PathStrain;
            }
            return sum / list.Length;
        }
    }
}

