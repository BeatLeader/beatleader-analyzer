using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    public class Ratings
    {
        public string Characteristic { get; set; }
        public string Difficulty { get; set; }
        public double Pass { get; set; }
        public double Tech { get; set; }
        public double Nerf { get; set; }
        public Statistics Patterns { get; set; } = new Statistics();
        public List<SwingData> SwingData { get; set; } = new List<SwingData>();
    }

    public class PerSwing
    {
        public double Time { get; set; }
        public double Pass { get; set; }
        public double Tech { get; set; }

        public PerSwing(double time, double pass, double tech)
        {
            Time = time;
            Pass = pass;
            Tech = tech;
        }
    }
}
