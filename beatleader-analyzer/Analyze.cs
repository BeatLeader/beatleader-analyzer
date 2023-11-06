using beatleader_analyzer.BeatmapScanner.Data;
using Parser.Map;
using Parser.Map.Difficulty.V3.Base;
using System;
using System.Collections.Generic;

namespace beatleader_analyzer
{
    public class Analyze
    {
        public List<Ratings> GetRating(DifficultyV3 diff, string characteristic, string difficulty, float bpm, float timescale = 1)
        {
            List<Ratings> ratings = new();

            try
            {
                if (diff.colorNotes.Count >= 20)
                {
                    ratings.Add(new(characteristic, difficulty, Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(diff.colorNotes, diff.burstSliders, diff.bombNotes, diff.obstacles, bpm * timescale)));
                    return ratings;
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

        public List<Ratings> GetRating(BeatmapV3 beatmap, float timescale = 1)
        {
            List<Ratings> ratings = new();

            try
            {
                foreach (var difficulty in beatmap.Difficulties)
                {
                    if (difficulty.Data.colorNotes.Count >= 20)
                    {
                        ratings.Add(new(difficulty.Characteristic, difficulty.Difficulty, Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(difficulty.Data.colorNotes, difficulty.Data.burstSliders, difficulty.Data.bombNotes, difficulty.Data.obstacles, beatmap.Info._beatsPerMinute * timescale)));
                    }
                }
                return ratings;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return null;
            }
        }
    }
}
