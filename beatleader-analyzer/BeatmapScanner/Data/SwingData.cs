using System.Collections.Generic;

namespace Analyzer.BeatmapScanner.Data
{
    /// <summary>
    /// Analyzed data for a single swing motion with difficulty metrics.
    /// </summary>
    public class SwingData
    {
        public Cube Start { get; set; } = null!;
        public double Time { get; set; } = 0;
        public double Angle { get; set; } = 0;
        public (double x, double y) EntryPosition { get; set; } = (0, 0);
        public (double x, double y) ExitPosition { get; set; } = (0, 0);
        public bool Forehand { get; set; } = true;
        public bool Reset { get; set; } = false;
        public bool BombReset { get; set; } = false;
        public double AngleStrain { get; set; } = 0;
        public double PathStrain { get; set; } = 0;
        public double ExcessDistance { get; set; } = 0;
        public double PositionComplexity { get; set; } = 0;
        public double CurveComplexity { get; set; } = 0;
        public double SwingFrequency { get; set; } = 0;
        public double SwingDiff { get; set; } = 0;

        public SwingData(double beat, double angle, Cube start)
        {
            Time = beat;
            Angle = angle;
            Start = start;
        }

        internal readonly record struct Point(double X, double Y);
    }
}
