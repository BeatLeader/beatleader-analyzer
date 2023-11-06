using beatleader_analyzer.BeatmapScanner.Data;
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
        public static List<Ratings> Ratings = new();

        public static List<Ratings> GetDataFromPathOne(string folderPath, string characteristic, string difficulty, float timescale = 1)
        {
            Ratings = new();

            try
            {
                Parse.TryLoadPath(folderPath);
                var map = Parse.GetBeatmap();
                var diff = map.Difficulties.FirstOrDefault(d => d.Difficulty == difficulty && d.Characteristic == characteristic);
                if (diff.Data.colorNotes.Count >= 20)
                {
                    Ratings.Add(new(characteristic, difficulty, Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(diff.Data.colorNotes, diff.Data.burstSliders, diff.Data.bombNotes, diff.Data.obstacles, map.Info._beatsPerMinute * timescale)));
                    return Ratings;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public static List<Ratings> GetDataFromPathAll(string folderPath, float timescale = 1)
        {
            Ratings = new();

            try
            {
                Parse.TryLoadPath(folderPath);
                var map = Parse.GetBeatmap();

                foreach (var difficulty in map.Difficulties)
                {
                    if (difficulty.Data.colorNotes.Count >= 20)
                    {
                        Ratings.Add(new(difficulty.Characteristic, difficulty.Difficulty, Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(difficulty.Data.colorNotes, difficulty.Data.burstSliders, difficulty.Data.bombNotes, difficulty.Data.obstacles, map.Info._beatsPerMinute * timescale)));
                    }
                }
                return Ratings;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public static List<Ratings> GetDataFromZip(MemoryStream zip, float timescale = 1)
        {
            Ratings = new();

            try
            {
                Parse.TryLoadZip(zip);
                var map = Parse.GetBeatmap();

                foreach (var difficulty in map.Difficulties)
                {
                    if (difficulty.Data.colorNotes.Count >= 20)
                    {
                        Ratings.Add(new(difficulty.Characteristic, difficulty.Difficulty, Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(difficulty.Data.colorNotes, difficulty.Data.burstSliders, difficulty.Data.bombNotes, difficulty.Data.obstacles, map.Info._beatsPerMinute * timescale)));
                    }
                }
                return Ratings;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public static List<Ratings> GetDataOneDiff(DifficultyV3 diff, string characteristic, string difficulty, float bpm, float timescale = 1)
        {
            Ratings = new();

            try
            {
                if (diff.colorNotes.Count >= 20)
                {
                    Ratings.Add(new(characteristic, difficulty, Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(diff.colorNotes, diff.burstSliders, diff.bombNotes, diff.obstacles, bpm * timescale)));
                    return Ratings;
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public static List<Ratings> GetDataAllDiff(BeatmapV3 beatmap, float timescale = 1)
        {
            Ratings = new();

            try
            {
                foreach (var difficulty in beatmap.Difficulties)
                {
                    if (difficulty.Data.colorNotes.Count >= 20)
                    {
                        Ratings.Add(new(difficulty.Characteristic, difficulty.Difficulty, Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(difficulty.Data.colorNotes, difficulty.Data.burstSliders, difficulty.Data.bombNotes, difficulty.Data.obstacles, beatmap.Info._beatsPerMinute * timescale)));
                    }
                }
                return Ratings;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }

        public static Ratings GetRating(string characteristic, string difficulty)
        {
            return Ratings.FirstOrDefault(x => x.Characteristic == characteristic && x.Difficulty == difficulty);
        }

        public static List<Ratings> GetRatings()
        {
            return Ratings;
        }
    }
}
