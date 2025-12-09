using System.Collections.Generic;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    /// <summary>
    /// Debug data structure for capturing all intermediate values from SwingCurve calculations.
    /// </summary>
    public class SwingCurveDebugData
    {
        public int SwingIndex { get; set; }
        public double Beat { get; set; }
        public string Hand { get; set; }
        public bool IsReset { get; set; }
        
        // Entry/Exit positions
        public double EntryX { get; set; }
        public double EntryY { get; set; }
        public double ExitX { get; set; }
        public double ExitY { get; set; }
        
        // Bezier control points
        public double Point0X { get; set; }
        public double Point0Y { get; set; }
        public double Point1X { get; set; }
        public double Point1Y { get; set; }
        public double Point2X { get; set; }
        public double Point2Y { get; set; }
        public double Point3X { get; set; }
        public double Point3Y { get; set; }
        
        // Bezier curve points (sampled)
        public List<(double x, double y)> BezierPoints { get; set; } = new List<(double x, double y)>();
        
        // Distance calculation
        public double Distance { get; set; }
        public double DistanceMinusOffset { get; set; }
        
        // Position complexity
        public double SimHandCurPosX { get; set; }
        public double SimHandCurPosY { get; set; }
        public double SimHandPrePosX { get; set; }
        public double SimHandPrePosY { get; set; }
        public double RawPositionComplexity { get; set; }
        public double PositionComplexity { get; set; }
        
        // Angle lists
        public List<double> AngleList { get; set; } = new List<double>();
        public List<double> AngleChangeList { get; set; } = new List<double>();
        public int AngleCount { get; set; }
        public int AngleChangeCount { get; set; }
        
        // Slice parameters
        public double First { get; set; }
        public double Last { get; set; }
        public double PathLookback { get; set; }
        public int FirstIndex { get; set; }
        public int LastIndex { get; set; }
        public int PathLookbackIndex { get; set; }
        
        // Complexity calculations
        public double AvgAngleChange { get; set; }
        public double CurveComplexity { get; set; }
        public double PathAngleStrain { get; set; }
        public double PathStrain { get; set; }
    }
}
