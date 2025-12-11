using Analyzer.BeatmapScanner.Algorithm;
using beatleader_analyzer;
using beatleader_parser;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Parser.Map;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Benchmark
{
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net90)]
    public class Benchmark
    {
        [ParamsAllValues]
        public bool UseParallelFor { get; set; }
        BeatmapV3 map;
        Analyze analyzer;

        [GlobalSetup]
        public void Globalsetup()
        {
            map = new Parse().TryDownloadLink(@"https://r2cdn.beatsaver.com/417f22a92dc4efb0750a4ea538e45eaf50ce628b.zip")[^1];
            analyzer = new Analyze();
        }

        [Benchmark]
        public void GetRating()
        {
            SwingCurve.UseParallel = UseParallelFor;
            analyzer.GetRating(map, "Standard");
        }

        /// <summary>
        /// Converts an angle in degrees to a readable direction name.
        /// </summary>
        /// <param name="angle">Angle in degrees (0-360)</param>
        /// <returns>Direction name (UP, DOWN, LEFT, RIGHT, UP-LEFT, UP-RIGHT, DOWN-LEFT, DOWN-RIGHT)</returns>
        public static string AngleToDirection(double angle)
        {
            // Normalize angle to 0-360 range
            angle = ((angle % 360) + 360) % 360;

            // Define angle ranges for each direction (±22.5° from cardinal/intercardinal)
            // RIGHT: 337.5-22.5 (0°)
            if (angle >= 337.5 || angle < 22.5)
                return "RIGHT";
            
            // UP-RIGHT: 22.5-67.5 (45°)
            if (angle >= 22.5 && angle < 67.5)
                return "UP-RIGHT";
            
            // UP: 67.5-112.5 (90°)
            if (angle >= 67.5 && angle < 112.5)
                return "UP";
            
            // UP-LEFT: 112.5-157.5 (135°)
            if (angle >= 112.5 && angle < 157.5)
                return "UP-LEFT";
            
            // LEFT: 157.5-202.5 (180°)
            if (angle >= 157.5 && angle < 202.5)
                return "LEFT";
            
            // DOWN-LEFT: 202.5-247.5 (225°)
            if (angle >= 202.5 && angle < 247.5)
                return "DOWN-LEFT";
            
            // DOWN: 247.5-292.5 (270°)
            if (angle >= 247.5 && angle < 292.5)
                return "DOWN";
            
            // DOWN-RIGHT: 292.5-337.5 (315°)
            if (angle >= 292.5 && angle < 337.5)
                return "DOWN-RIGHT";

            // Fallback (should never reach here)
            return "UNKNOWN";
        }
    }
}
