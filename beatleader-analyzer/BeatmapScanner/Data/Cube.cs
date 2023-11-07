
using Parser.Map.Difficulty.V3.Grid;

namespace Analyzer.BeatmapScanner.Data
{
    internal class Cube
    {
        public float Time { get; set; } = 0;
        public int Line { get; set; } = 0;
        public int Layer { get; set; } = 0;
        public int Type { get; set; } = 0;
        public int CutDirection { get; set; } = 0;
        public double AngleOffset { get; set; } = 0;
        public double Direction { get; set; } = 8;
        public bool Head { get; set; } = false;
        public bool Pattern { get; set; } = false;

        public Cube()
        {
        }

        public Cube(Cube cube)
        {
            AngleOffset = cube.AngleOffset;
            CutDirection = cube.CutDirection;
            Type = cube.Type;
            Time = cube.Time;
            Line = cube.Line;
            Layer = cube.Layer;
            Direction = cube.Direction;
        }


        public Cube(Notes note)
        {
            AngleOffset = note.AngleOffset;
            CutDirection = note.CutDirection;
            Type = note.Color;
            Time = note.Beats;
            Line = note.x;
            Layer = note.y;
            Direction = note.CutDirection;
        }
    }
}
