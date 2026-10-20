using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    public class Ratings
    {
        /// <summary>
        /// Display of the pass rating: PassRating = PassDisplayScale x internal pass ^ PassDisplayExponent, fitted so ranked
        /// maps show prod's pass at every level (median, p90, p99 and the hardest map within ~3%; the internal pass is steeper
        /// at the top than prod's). Purely cosmetic: every formula that uses the pass rating (pass pp, stars, acc rating)
        /// converts it back with InternalPass, and tech uses the internal pass.
        /// </summary>
        public const double PassDisplayScale = 1.68;
        public const double PassDisplayExponent = 0.844;

        /// <summary>Displayed pass rating from the internal pass.</summary>
        public static double DisplayPass(double internalPass) =>
            internalPass > 0 ? PassDisplayScale * System.Math.Pow(internalPass, PassDisplayExponent) : internalPass;

        /// <summary>Internal pass (what the formulas expect) from a displayed pass rating.</summary>
        public static double InternalPass(double displayedPass) =>
            displayedPass > 0 ? System.Math.Pow(displayedPass / PassDisplayScale, 1 / PassDisplayExponent) : displayedPass;

        public string Characteristic { get; set; }
        public string Difficulty { get; set; }
        public double PassRating { get; set; }
        public double TechRating { get; set; }
        public double MultiPercentage { get; set; }
        public double LinearPercentage { get; set; }
        public double PeakSustainedEBPM { get; set; }
        public Statistics Statistics { get; set; } = new Statistics();
        public List<SwingData> SwingData { get; set; } = new List<SwingData>();
        public List<Wall> DodgeWalls { get; set; } = new List<Wall>();
        public List<Wall> CrouchWalls { get; set; } = new List<Wall>();
    }
}
