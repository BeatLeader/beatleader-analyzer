using beatleader_analyzer.BeatmapScanner.Data;
using beatleader_parser.Timescale;
using Parser.Map;
using Parser.Map.Difficulty.V3.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace beatleader_analyzer
{
    public class Analyze
    {
        /// <summary>
        /// Analyzer entry point (for single difficulty)
        /// </summary>
        /// <param name="diff">Map data from Beatleader-Parser</param>
        /// <param name="characteristic">Difficulty characteristic (ie. Standard)</param>
        /// <param name="difficulty">Difficulty name (ie. ExpertPlus)</param>
        /// <param name="bpm">Initial map BPM</param>
        /// <param name="speedMult">BPM and NJS Multiplier for speed modifiers</param>
        /// <param name="njsMult">NJS Multiplier for custom modifiers</param>
        /// <param name="strictAngle">Strict Angle modifier</param>
        /// <returns>Filled Ratings object</returns>
        public Ratings GetRating(DifficultyV3 diff, string characteristic, string difficulty, float bpm, float speedMult = 1, float njsMult = 1, bool strictAngle = false)
        {
            try
            {
                if (diff.Notes.Count >= 20)
                {
                    Modifiers modifiers = new Modifiers(bpm, speedMult, njsMult, strictAngle);

                    Ratings rating = Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(diff.Notes, diff.Chains, 
                        diff.Bombs, diff.Walls, modifiers);

                    rating.Characteristic = characteristic;
                    rating.Difficulty = difficulty;

                    return rating;
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

        /// <summary>
        /// Analyzer entry point (for multiple difficulties)
        /// </summary>
        /// <param name="beatmap">Beatmap data from Beatleader-Parser</param>
        /// <param name="characteristic">Difficulty characteristic (ie. Standard)</param>
        /// <param name="speedMult">BPM and NJS Multiplier for speed modifiers</param>
        /// <param name="njsMult">NJS Multiplier for custom modifiers</param>
        /// <param name="strictAngle">Strict Angle modifier</param>
        /// <returns>Filled Ratings object</returns>
        public List<Ratings> GetRating(BeatmapV3 beatmap, string characteristic, float speedMult = 1, float njsMult = 1, bool strictAngle = false)
        {
            List<Ratings> ratings = [];
            var data = beatmap.Info._difficultyBeatmapSets.FirstOrDefault(x => x._beatmapCharacteristicName == characteristic);

            try
            {
                var bpm = beatmap.Info._beatsPerMinute;

                foreach (var difficulty in beatmap.Difficulties.Where(x => x.Characteristic == characteristic))
                {
                    if (difficulty.Data.Notes.Count >= 20)
                    {
                        Modifiers modifiers = new Modifiers(bpm, speedMult, njsMult, strictAngle);

                        Ratings rating = Analyzer.BeatmapScanner.BeatmapScanner.Analyzer(difficulty.Data.Notes,
                            difficulty.Data.Chains, difficulty.Data.Bombs, difficulty.Data.Walls, modifiers);

                        rating.Characteristic = difficulty.Characteristic;
                        rating.Difficulty = difficulty.Difficulty;

                        ratings.Add(rating);
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
