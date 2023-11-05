using Analyzer.BeatmapScanner.Algorithm;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using Parser.Map.Difficulty.V3.Grid;
using System.Linq;

namespace Analyzer.BeatmapScanner
{
    internal class BeatmapScanner
    {
        static public List<Cube> Cubes = new();
        static public List<SwingData>? Datas = new();

        #region Analyzer

        public static List<double> Analyzer(List<Colornote> notes, List<Burstslider> chains, List<Bombnote> bombs, List<Obstacle> obstacles, float bpm)
        {
            #region Prep

            List<double> value = new();
            List<Cube> cube = new();
            List<SwingData>? data = new();

            foreach (var note in notes)
            {
                cube.Add(new Cube(note));
            }

            cube.OrderBy(c => c.Time);
            var red = cube.Where(c => c.Type == 0).OrderBy(c => c.Time).ToList();
            var blue = cube.Where(c => c.Type == 1).OrderBy(c => c.Time).ToList();

            #endregion

            #region Algorithm

            (value, data) = Analyze.UseLackWizAlgorithm(red, blue, bpm, bombs);

            cube = new(red);
            cube.AddRange(blue);
            cube = cube.OrderBy(c => c.Time).ToList();

            #endregion

            Cubes = cube;
            Datas = data;

            return value;
        }

        #endregion
    }
}
