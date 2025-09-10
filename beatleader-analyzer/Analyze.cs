using beatleader_analyzer.BeatmapScanner.Data;
using Parser.Map;
using Parser.Map.Difficulty.V3.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer
{
    public class Analyze
    {
        public List<Ratings> GetRating(DifficultyV3 diff, string characteristic, string difficulty, float bpm, float timescale = 1)
        {
            List<Ratings> ratings = [];

            try
            {
                if (diff.Notes.Count >= 20)
                {
                    (List<double> rating, List<PerSwing> perSwing) = Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(diff.Notes, diff.Chains, diff.Bombs, diff.Walls, bpm, timescale);
                    ratings.Add(new(characteristic, difficulty, rating, perSwing));
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

        public List<Ratings> GetRating(BeatmapV3 beatmap, string characteristic, float timescale = 1)
        {
            List<Ratings> ratings = [];
            var data = beatmap.Info._difficultyBeatmapSets.FirstOrDefault(x => x._beatmapCharacteristicName == characteristic);

            try
            {
                foreach (var difficulty in beatmap.Difficulties.Where(x => x.Characteristic == characteristic))
                {
                    if (difficulty.Data.Notes.Count >= 20)
                    {
                        (List<double> rating, List<PerSwing> perSwing) = Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(difficulty.Data.Notes, difficulty.Data.Chains, difficulty.Data.Bombs, difficulty.Data.Walls, beatmap.Info._beatsPerMinute, timescale);
                        ratings.Add(new(difficulty.Characteristic, difficulty.Difficulty, rating, perSwing));
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
