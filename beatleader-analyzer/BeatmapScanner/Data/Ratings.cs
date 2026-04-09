using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    public class Ratings
    {
        public string Characteristic { get; set; }
        public string Difficulty { get; set; }
        public double PassRating { get; set; }
        public double TechRating { get; set; }
        public double MultiPercentage { get; set; }
        public double LinearPercentage { get; set; }
        public double PeakSustainedEBPM { get; set; }
        public double LowNoteNerf { get; set; }
        public Statistics Statistics { get; set; } = new Statistics();
        public List<SwingData> SwingData { get; set; } = new List<SwingData>();
        public List<Wall> DodgeWalls { get; set; } = new List<Wall>();
        public List<Wall> CrouchWalls { get; set; } = new List<Wall>();
    }
}
