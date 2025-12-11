using System.Collections.Generic;

namespace Analyzer.BeatmapScanner.Data
{
    /// <summary>
    /// Analyzed data for a single swing motion with difficulty metrics.
    /// </summary>
    public class SwingData
    {
        public List<Cube> Notes { get; set; } = null!;
        public double BpmTime { get; set; } = 0;
        public double Direction { get; set; } = 0;
        public (double x, double y) EntryPosition { get; set; } = (0, 0);
        public (double x, double y) ExitPosition { get; set; } = (0, 0);
        public bool Forehand { get; set; } = true;
        public bool ParityErrors { get; set; } = false;
        public bool BombAvoidance { get; set; } = false;
        public double AngleStrain { get; set; } = 0;
        public double PathStrain { get; set; } = 0;
        public double BezierCurveDistance { get; set; } = 0;
        public double RepositioningDistance { get; set; } = 0;
        public double CurveComplexity { get; set; } = 0;
        public double SwingFrequency { get; set; } = 0;
        public double DistanceDiff { get; set; } = 0;
        public double SwingSpeed { get; set; } = 0;
        public double HitDistance { get; set; } = 0;
        public double HitDiff { get; set; } = 0;
        public double Stress { get; set; } = 0;
        public double SpeedFalloff { get; set; } = 0;
        public double StressMultiplier { get; set; } = 0;
        public double NjsBuff { get; set; } = 1.0;
        public double WallBuff { get; set; } = 1.0;
        public bool StreamBonusApplied { get; set; } = false;
        public double SwingDiff { get; set; } = 0;
        public double SwingTech { get; set; } = 0;
        public string PatternType { get; set; } = "Single";

        public SwingData(List<Cube> cubes)
        {
            var start = cubes[0];
            Notes = cubes;
            BpmTime = start.BpmTime;
            Direction = start.Direction;
            Forehand = start.Forehand;
            ParityErrors = start.ParityErrors;
            BombAvoidance = start.BombAvoidance;
        }

        internal readonly record struct Point(double X, double Y);
    }
}
