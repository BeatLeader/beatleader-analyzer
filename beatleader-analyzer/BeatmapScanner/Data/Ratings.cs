using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    public class Ratings
    {
        /// <summary>
        /// Display scale of the pass rating: PassRating is the internal pass times this, so the hardest ranked map shows
        /// about 17. Purely cosmetic: every formula that uses the pass rating (pass pp, stars, acc rating) divides it back
        /// out, and tech uses the internal pass. Change only this value to rescale the displayed pass.
        /// </summary>
        public const double PassDisplayScale = 1.26;

        /// <summary>Internal pass (what the formulas expect) from a displayed pass rating.</summary>
        public static double InternalPass(double displayedPass) => displayedPass / PassDisplayScale;

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
