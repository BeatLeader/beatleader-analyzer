using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    public class Ratings
    {
        /// <summary>
        /// Pass factor of the tech rating: TechRating = BALANCED_TECH_SCALER x (average swing tech x TechPassFactor(pass)) ^
        /// TechExponent (AnalyzeMap). The base is 1.4^(1 / 1.4862): it was 1.4 on a pass 1.4862 times smaller, before the pass
        /// display curve was folded into the pass (PASS_CALIBRATION_FACTOR), so the factor is the same for the same map.
        /// </summary>
        public const double TechPassBase = 1.25408;

        /// <summary>
        /// Power of the tech rating: the average swing tech's top end is much steeper than prod's tech ratings, fitted on the
        /// quantiles of prod's ranked tech (median 3.88 vs 3.75, p95 8.86 vs 8.98, p99 11.86 vs 11.21). portaBLe's tech pp
        /// takes the tech rating to the inverse power (PpFormula.TechRatingPower), so tech pp is what it was with the power 1.
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
