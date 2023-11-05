using Analyzer.BeatmapScanner;
using beatleader_parser;
using Parser.Map;
using Parser.Map.Difficulty.V3.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace beatleader_analyzer
{
    public class Analyze
    {
        public static List<double> GetDataFromPathOne(string folderPath, string characteristic, string difficulty)
        {
            try
            {
                Parse.TryLoadPath(folderPath);
                var map = Parse.GetBeatmap();
                var diff = map.Difficulties.FirstOrDefault(d => d.Difficulty == difficulty && d.Characteristic == characteristic);
                if (diff.Data.colorNotes.Count >= 20)
                {
                    return BeatmapScanner.Analyzer(diff.Data.colorNotes, diff.Data.burstSliders, diff.Data.bombNotes, diff.Data.obstacles, map.Info._beatsPerMinute);
                }
                else
                {
                    return new();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new();
            }
        }

        public static List<List<double>> GetDataFromPathAll(string folderPath)
        {
            try
            {
                Parse.TryLoadPath(folderPath);
                var map = Parse.GetBeatmap();

                List<List<double>> output = new();

                foreach (var difficulty in map.Difficulties)
                {
                    if (difficulty.Data.colorNotes.Count >= 20)
                    {
                        output.Add(new());
                        output[output.Count - 1] = BeatmapScanner.Analyzer(difficulty.Data.colorNotes, difficulty.Data.burstSliders, difficulty.Data.bombNotes, difficulty.Data.obstacles, map.Info._beatsPerMinute);
                    }
                }
                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new();
            }
        }

        public static List<List<double>> GetDataFromZip(MemoryStream zip)
        {
            try
            {
                Parse.TryLoadZip(zip);
                var map = Parse.GetBeatmap();
                List<List<double>> output = new();

                foreach (var difficulty in map.Difficulties)
                {
                    if (difficulty.Data.colorNotes.Count >= 20)
                    {
                        output.Add(new());
                        output[output.Count - 1] = BeatmapScanner.Analyzer(difficulty.Data.colorNotes, difficulty.Data.burstSliders, difficulty.Data.bombNotes, difficulty.Data.obstacles, map.Info._beatsPerMinute);
                    }
                }
                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new();
            }
        }

        public static List<double> GetDataOneDiff(DifficultyV3 difficulty, float bpm)
        {
            try
            {
                if (difficulty.colorNotes.Count >= 20)
                {
                    return BeatmapScanner.Analyzer(difficulty.colorNotes, difficulty.burstSliders, difficulty.bombNotes, difficulty.obstacles, bpm);
                }
                else
                {
                    return new();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new();
            }
        }

        public static List<List<double>> GetDataAllDiff(BeatmapV3 beatmap)
        {
            try
            {
                List<List<double>> output = new();

                foreach (var difficulty in beatmap.Difficulties)
                {
                    if (difficulty.Data.colorNotes.Count >= 20)
                    {
                        output.Add(new());
                        output[output.Count - 1] = BeatmapScanner.Analyzer(difficulty.Data.colorNotes, difficulty.Data.burstSliders, difficulty.Data.bombNotes, difficulty.Data.obstacles, beatmap.Info._beatsPerMinute);
                    }
                }
                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return new();
            }
        }
    }
}
