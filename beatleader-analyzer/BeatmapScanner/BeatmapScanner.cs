using Analyzer.BeatmapScanner.Algorithm;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using Parser.Map.Difficulty.V3.Grid;
using System.Linq;

namespace Analyzer.BeatmapScanner
{
    internal class BeatmapScanner
    {
        public static List<double> Analyzer(List<Note> notes, List<Chain> chains, List<Bomb> bombs, List<Wall> walls, float bpm)
        {
            List<double> value = new();
            List<Cube> cubes = new();

            foreach (var note in notes)
            {
                cubes.Add(new Cube(note));
            }

            cubes.OrderBy(c => c.Time);

            foreach (var chain in chains)
            {
                var found = cubes.FirstOrDefault(x => x.Time == chain.Beats && x.Type == chain.Color && x.Line == chain.x && x.Layer == chain.y && x.CutDirection == chain.Direction);
                if (found != null)
                {
                    found.Chain = true;
                    found.TailLine = chain.tx;
                    found.TailLayer = chain.ty;
                    found.Squish = chain.Squish;
                }
            }

            var red = cubes.Where(c => c.Type == 0).OrderBy(c => c.Time).ToList();
            var blue = cubes.Where(c => c.Type == 1).OrderBy(c => c.Time).ToList();

            value = Analyze.UseLackWizAlgorithm(red, blue, bpm);

            return value;
        }
    }
}
