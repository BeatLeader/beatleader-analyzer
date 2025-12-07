using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Performance;
using System.Linq;
using beatleader_analyzer.BeatmapScanner.Data;
using System.Runtime.InteropServices;
using static Analyzer.BeatmapScanner.Helper.MultiNotesClassifier;
using static Analyzer.BeatmapScanner.Helper.WallClassifier;
using Parser.Map.Difficulty.V3.Grid;

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
                FlowDetector.Detect(red, bpm, false);
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
                FlowDetector.Detect(blue, bpm, true);
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

            var redPatterns = red.Count > 2 ? AnalyzeMultiNotes(red, bpm) : new Statistics();
            var bluePatterns = blue.Count > 2 ? AnalyzeMultiNotes(blue, bpm) : new Statistics();

            // Classify pattern types for each swing
            if (red.Count > 2)
            {
                ClassifySwingPatterns(red, redSwingData, bpm);
            }
            if (blue.Count > 2)
            {
                ClassifySwingPatterns(blue, blueSwingData, bpm);
            }

            // Use the counted walls (respecting cooldown) for statistics
            int dodgeWallCount = dodgeWallsCount;
            int crouchWallCount = crouchWallsCount;

            int resetCount = combinedSwingData.Count(s => s.Reset);
            int bombResetCount = combinedSwingData.Count(s => s.BombReset);

            var combinedPatterns = new Statistics
            {
                Stacks = redPatterns.Stacks + bluePatterns.Stacks,
                Towers = redPatterns.Towers + bluePatterns.Towers,
                Sliders = redPatterns.Sliders + bluePatterns.Sliders,
                CurvedSliders = redPatterns.CurvedSliders + bluePatterns.CurvedSliders,
                Windows = redPatterns.Windows + bluePatterns.Windows,
                Loloppes = redPatterns.Loloppes + bluePatterns.Loloppes,
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

        private static void ClassifySwingPatterns(List<Cube> cubes, List<SwingData> swingData, float bpm)
        {
            if (cubes.Count < 2 || swingData.Count == 0)
            {
                return;
            }

            // Build a map from cube time to swing data
            var cubeToSwing = new Dictionary<float, SwingData>();
            int swingIndex = 0;
            
            for (int i = 0; i < cubes.Count; i++)
            {
                // Find the swing that contains this cube
                if (!cubes[i].Pattern || cubes[i].Head)
                {
                    if (swingIndex < swingData.Count)
                    {
                        cubeToSwing[cubes[i].Time] = swingData[swingIndex];
                        swingIndex++;
                    }
                }
            }

            // Process multi-note patterns
            var processed = new HashSet<int>();
            
            for (int i = 0; i < cubes.Count; i++)
            {
                if (processed.Contains(i) || !cubes[i].Head)
                {
                    continue;
                }

                // Collect all notes in this multi-note hit
                var patternNotes = new List<int> { i };
                for (int j = i + 1; j < cubes.Count; j++)
                {
                    if (cubes[j].Pattern && !cubes[j].Head)
                    {
                        patternNotes.Add(j);
                        if (cubes[j].Tail)
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (patternNotes.Count < 2)
                {
                    continue;
                }

                // Mark all as processed
                foreach (int idx in patternNotes)
                {
                    processed.Add(idx);
                }

                // Determine pattern type
                string patternType = DeterminePatternType(cubes, patternNotes, bpm);
                
                // Assign pattern type to the corresponding swing
                if (cubeToSwing.TryGetValue(cubes[i].Time, out var swing))
                {
                    swing.PatternType = patternType;
                }
            }
        }

        private static string DeterminePatternType(List<Cube> cubes, List<int> patternIndices, float bpm)
        {
            const float SIMULTANEOUS_TIME_TOLERANCE = 0.001f;
            
            int noteCount = patternIndices.Count;
            
            // Check if simultaneous
            bool isSimultaneous = true;
            float firstTime = cubes[patternIndices[0]].Time;
            for (int i = 1; i < patternIndices.Count; i++)
            {
                if (Math.Abs(cubes[patternIndices[i]].Time - firstTime) > SIMULTANEOUS_TIME_TOLERANCE)
                {
                    isSimultaneous = false;
                    break;
                }
            }

            if (isSimultaneous)
            {
                if (noteCount == 2)
                {
                    if (IsStack(cubes, patternIndices))
                    {
                        return "Stack";
                    }
                    else if (IsWindow(cubes, patternIndices))
                    {
                        return "Window";
                    }
                    else if (IsLoloppe(cubes, patternIndices))
                    {
                        return "Loloppe";
                    }
                }
                else if (noteCount == 3)
                {
                    if (IsTower(cubes, patternIndices))
                    {
                        return "Tower";
                    }
                }
                return "Multi-Note";
            }
            else
            {
                if (noteCount >= 2)
                {
                    if (IsCurvedSlider(cubes, patternIndices))
                    {
                        return "Curved Slider";
                    }
                    else
                    {
                        return "Slider";
                    }
                }
            }

            return "Pattern";
        }

        // Helper methods for pattern detection (copied from MultiNotesClassifier)
        private static bool IsStack(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 2) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            bool isAdjacent = (lineDiff == 1 && layerDiff == 0) || (lineDiff == 0 && layerDiff == 1);
            
            if (!isAdjacent) return false;

            double direction = note1.Direction != 8 ? note1.Direction : note2.Direction;
            if (direction == 8) return true;

            return IsAlignedInSwingDirection(note1, note2, direction);
        }

        private static bool IsTower(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 3) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];
            Cube note3 = cubes[indices[2]];

            bool verticalLine = (note1.Line == note2.Line && note2.Line == note3.Line);
            bool horizontalLine = (note1.Layer == note2.Layer && note2.Layer == note3.Layer);

            if (verticalLine)
            {
                var layers = new[] { note1.Layer, note2.Layer, note3.Layer };
                Array.Sort(layers);
                return layers[1] - layers[0] == 1 && layers[2] - layers[1] == 1;
            }
            
            if (horizontalLine)
            {
                var lines = new[] { note1.Line, note2.Line, note3.Line };
                Array.Sort(lines);
                return lines[1] - lines[0] == 1 && lines[2] - lines[1] == 1;
            }

            int vec1X = note2.Line - note1.Line;
            int vec1Y = note2.Layer - note1.Layer;
            int vec2X = note3.Line - note2.Line;
            int vec2Y = note3.Layer - note2.Layer;

            bool isCollinear = (vec1X * vec2Y - vec1Y * vec2X) == 0;
            bool isDiagonal1 = Math.Abs(vec1X) == 1 && Math.Abs(vec1Y) == 1;
            bool isDiagonal2 = Math.Abs(vec2X) == 1 && Math.Abs(vec2Y) == 1;
            bool sameDirection = vec1X == vec2X && vec1Y == vec2Y;

            return isCollinear && isDiagonal1 && isDiagonal2 && sameDirection;
        }

        private static bool IsLoloppe(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 2) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];

            double direction = note1.Direction != 8 ? note1.Direction : note2.Direction;
            if (direction == 8) return false;

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            bool isAdjacent = (lineDiff == 1 && layerDiff == 0) || (lineDiff == 0 && layerDiff == 1);
            if (!isAdjacent) return false;

            return IsPerpendicularToSwingDirection(note1, note2, direction);
        }

        private static bool IsWindow(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count != 2) return false;

            Cube note1 = cubes[indices[0]];
            Cube note2 = cubes[indices[1]];

            int lineDiff = Math.Abs(note1.Line - note2.Line);
            int layerDiff = Math.Abs(note1.Layer - note2.Layer);

            bool hasGap = (lineDiff >= 2 && layerDiff == 0) || (lineDiff == 0 && layerDiff >= 2);
            if (!hasGap) return false;

            double direction = note1.Direction != 8 ? note1.Direction : note2.Direction;
            if (direction == 8) return false;

            return IsAlignedInSwingDirection(note1, note2, direction);
        }

        private static bool IsCurvedSlider(List<Cube> cubes, List<int> indices)
        {
            if (indices.Count < 3) return false;

            for (int i = 1; i < indices.Count - 1; i++)
            {
                Cube prev = cubes[indices[i - 1]];
                Cube curr = cubes[indices[i]];
                Cube next = cubes[indices[i + 1]];

                int vec1X = curr.Line - prev.Line;
                int vec1Y = curr.Layer - prev.Layer;
                int vec2X = next.Line - curr.Line;
                int vec2Y = next.Layer - curr.Layer;

                int crossProduct = vec1X * vec2Y - vec1Y * vec2X;
                if (crossProduct != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAlignedInSwingDirection(Cube note1, Cube note2, double direction)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            return direction switch
            {
                >= 67.5 and <= 112.5 => layerDiff > 0,
                >= 247.5 and <= 292.5 => layerDiff < 0,
                >= 157.5 and <= 202.5 => lineDiff < 0,
                <= 22.5 or >= 337.5 => lineDiff > 0,
                >= 112.5 and <= 157.5 => layerDiff >= 0 || lineDiff <= 0,
                >= 22.5 and <= 67.5 => layerDiff >= 0 || lineDiff >= 0,
                >= 202.5 and <= 247.5 => layerDiff <= 0 || lineDiff <= 0,
                >= 292.5 and <= 337.5 => layerDiff <= 0 || lineDiff >= 0,
                _ => false
            };
        }

        private static bool IsPerpendicularToSwingDirection(Cube note1, Cube note2, double direction)
        {
            int lineDiff = note2.Line - note1.Line;
            int layerDiff = note2.Layer - note1.Layer;

            double radians = direction * Math.PI / 180.0;
            double swingDirX = Math.Cos(radians);
            double swingDirY = Math.Sin(radians);

            double posVectorX = lineDiff;
            double posVectorY = layerDiff;

            double dotProduct = swingDirX * posVectorX + swingDirY * posVectorY;

            const double tolerance = 0.5;
            return Math.Abs(dotProduct) < tolerance;
        }
    }
}
