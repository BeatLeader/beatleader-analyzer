using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    public class Ratings
    {
        /// <summary>
        /// Top compression of the tech rating as shown: above TechRatingKnee, each point counts TechRatingSlope, so the maps
        /// the parity predictor no longer reads with false resets (their tech up to 20.6) top out near 18 as before. Display
        /// only: PpFormula.TechPp undoes it (UncompressTech), so tech pp and stars are as without it.
        /// </summary>
        public const double TechRatingKnee = 15;
        public const double TechRatingSlope = 0.53;
        public static double CompressTech(double tech) => tech > TechRatingKnee ? TechRatingKnee + (tech - TechRatingKnee) * TechRatingSlope : tech;
        public static double UncompressTech(double tech) => tech > TechRatingKnee ? TechRatingKnee + (tech - TechRatingKnee) / TechRatingSlope : tech;

        /// <summary>
        /// Pass factor of the tech rating: TechRating = BALANCED_TECH_SCALER x (average swing tech x TechPassFactor(pass)) ^
        /// TechExponent (AnalyzeMap). The base is 1.4^(1 / 1.4862), matching the pass scale that includes
        /// PASS_CALIBRATION_FACTOR.
        /// </summary>
        public const double TechPassBase = 1.25408;

        /// <summary>
        /// Power of the tech rating: compresses the steep top end of average swing tech, fitted on the quantiles of prod's
        /// ranked tech ratings. portaBLe's tech pp applies the inverse power (PpFormula.TechRatingPower), so tech pp is
        /// unaffected.
        /// </summary>
        public const double TechExponent = 0.8015;

        public static double TechPassFactor(double pass) => 1.0 - System.Math.Pow(TechPassBase, -pass);

        public string Characteristic { get; set; }
        public string Difficulty { get; set; }
        public double PassRating { get; set; }
        public double TechRating { get; set; }
        public double MultiPercentage { get; set; }
        public double LinearPercentage { get; set; }
        public double PeakSustainedEBPM { get; set; }
        // Mean distance between the entries of consecutive swings of a hand (SwingData.HitDistance), in meters: how far the
        // hands travel. RatingAPI's acc rating uses it (AccRating.EffectiveAcc).
        public double HitDistance { get; set; }
        // Mean swing difficulty (SwingData.SwingDiff) of the hardest 10% of swings over the hardest 8 s stretch's average (both
        // hands, song time): above 1, the hardest swings are scattered over the map, so the pass (the hardest windows'
        // averages) and the Acc AI (cut notes only) underrate them. 0 when a hand has fewer than 2 swings (one hand maps).
        // RatingAPI's acc rating uses it (AccRating.HardSwingMultiplier).
        public double HardSwingRatio { get; set; }
        public Statistics Statistics { get; set; } = new Statistics();
        public List<SwingData> SwingData { get; set; } = new List<SwingData>();
        public List<Wall> DodgeWalls { get; set; } = new List<Wall>();
        public List<Wall> CrouchWalls { get; set; } = new List<Wall>();
    }
}
