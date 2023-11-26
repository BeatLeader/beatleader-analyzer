﻿
using Parser.Map.Difficulty.V3.Grid;

namespace Analyzer.BeatmapScanner.Data
{
    public class Cube
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
        public bool Chain { get; set; } = false;
        public int TailLine { get; set; } = 0;
        public int TailLayer {  get; set; } = 0;
        public float Squish { get; set; } = 0f;
        
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


        public Cube(Note note)
        {
            AngleOffset = note.AngleOffset;
            CutDirection = note.CutDirection;
            Type = note.Color;
            Time = note.BpmTime;
            Line = note.x;
            Layer = note.y;
            Direction = note.CutDirection;
        }
    }
}
