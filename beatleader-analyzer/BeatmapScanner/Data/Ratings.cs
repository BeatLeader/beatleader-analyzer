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
        public List<PerSwing> PerSwing { get; set; }

        public Ratings(string characteristic, string difficulty, List<double> ratings, List<PerSwing> perSwing)
        {
            Characteristic = characteristic;
            Difficulty = difficulty;
            Pass = ratings[0];
            Tech = ratings[1];
            Nerf = ratings[2];
            PerSwing = perSwing;
        }
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
