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
using System.IO;
using System.Text;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// Main difficulty calculation algorithm (LackWiz Algorithm).
    /// </summary>
    internal class Analyze
    {
        private const double PASS_CALIBRATION_FACTOR = 0.752;
        private const double ONE_SABER_NERF = 0.35;

        public static Ratings UseLackWizAlgorithm(List<Cube> red, List<Cube> blue, float bpm, List<Wall> walls = null, List<Bomb> bombs = null)
        {
            List<SwingData> redSwingData = [];
            List<SwingData> blueSwingData = [];
            List<SwingData> combinedSwingData = [];

            if (red.Count > 2)
            {
                FlowDetector.Detect(red, bpm, false, bombs);
                redSwingData = SwingProcesser.Process(red);
                
                if (redSwingData.Count > 0)
                {
                    ParityPredictor.Predict(redSwingData, false, bombs);
                    SwingCurve.Calc(redSwingData, false);
                    combinedSwingData.AddRange(redSwingData);
                }
            }

            if (blue.Count > 2)
            {
                FlowDetector.Detect(blue, bpm, true, bombs);
                blueSwingData = SwingProcesser.Process(blue);
                
                if (blueSwingData.Count > 0)
                {
                    ParityPredictor.Predict(blueSwingData, true, bombs);
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
                balancedTech = tech * (1.0 - Math.Pow(1.4, -balancedPass));
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

            int resetCount = combinedSwingData.Count(s => s.Reset);
            int bombResetCount = combinedSwingData.Count(s => s.BombReset);

            var combinedPatterns = new Statistics
            {
                Stacks = redMultiNotes.Stacks + blueMultiNotes.Stacks,
                Towers = redMultiNotes.Towers + blueMultiNotes.Towers,
                Sliders = redMultiNotes.Sliders + blueMultiNotes.Sliders,
                CurvedSliders = redMultiNotes.CurvedSliders + blueMultiNotes.CurvedSliders,
                Windows = redMultiNotes.Windows + blueMultiNotes.Windows,
                SlantedWindows = redMultiNotes.SlantedWindows + blueMultiNotes.SlantedWindows,
                Loloppes = redMultiNotes.Loloppes + blueMultiNotes.Loloppes,
                DodgeWalls = dodgeWallCount,
                CrouchWalls = crouchWallCount,
                Resets = resetCount,
                BombResets = bombResetCount
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
