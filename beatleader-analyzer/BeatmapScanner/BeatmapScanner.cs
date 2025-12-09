using Analyzer.BeatmapScanner.Algorithm;
using Analyzer.BeatmapScanner.Data;
using System.Collections.Generic;
using Parser.Map.Difficulty.V3.Grid;
using System.Linq;
using beatleader_analyzer.BeatmapScanner.Data;

namespace Analyzer.BeatmapScanner
{
    internal class BeatmapScanner
    {
        public static Ratings Analyzer(List<Note> notes, List<Chain> chains, List<Bomb> bombs, List<Wall> walls, float bpm, float timescale, float njsMult = 1, bool strictAngles = false)
        {
            Ratings ratings;
            List<Cube> cubes = [];

            bpm *= timescale;
            
            foreach (var note in notes)
            {
                var cube = new Cube(note);
                cube.Njs *= timescale * njsMult;
                cubes.Add(cube);
            }

            cubes = cubes.OrderBy(c => c.Beat).ToList();

            foreach (var chain in chains)
            {
                var found = cubes.FirstOrDefault(x => x.Beat == chain.BpmTime && x.Type == chain.Color && x.X == chain.x && x.Y == chain.y && x.CutDirection == chain.CutDirection);
                if (found != null)
                {
                    found.Chain = true;
                    found.TailLine = chain.tx;
                    found.TailLayer = chain.ty;
                    found.TailDirection = chain.TailCutDirection;
                    found.Squish = chain.Squish;
                }
            }

            var red = cubes.Where(c => c.Type == 0).ToList();
            var blue = cubes.Where(c => c.Type == 1).ToList();

            return Analyze.UseLackWizAlgorithm(red, blue, bpm, walls, bombs, strictAngles);
        }
    }
}
