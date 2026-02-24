

using static beatleader_analyzer.BeatmapScanner.Helper.Common;
using Parser.Map.Difficulty.V3.Grid;

namespace Analyzer.BeatmapScanner.Data
{
    /// <summary>
    /// Beat Saber note with analysis metadata.
    /// </summary>
    public class Cube
    {
        public Note Note { get; set; } = null!;
        public float JsonTime { get; set; } = 0;
        public float BpmTime { get; set; } = 0;
        public float Seconds { get; set; } = 0;
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public int Type { get; set; } = 0;
        public int CutDirection { get; set; } = 0;
        public double AngleOffset { get; set; } = 0;
        public double Direction { get; set; } = -1;
        public float Njs { get; set; } = 0;
        public bool Head { get; set; } = false;
        public bool Tail { get; set; } = false;
        public bool Pattern { get; set; } = false;
        public bool Chain { get; set; } = false;
        public bool Forehand { get; set; } = true;
        public bool ParityErrors { get; set; } = false;
        public bool BombAvoidance { get; set; } = false;
        public int TailLine { get; set; } = 0;
        public int TailLayer {  get; set; } = 0;
        public int TailCutDirection { get; set; } = 0;
        public double TailDirection { get; set; } = -1;
        public float Squish { get; set; } = 0f;
        
        public Cube()
        {
        }

        public Cube(Cube cube)
        {
            Note = cube.Note;
            JsonTime = cube.JsonTime;
            BpmTime = cube.BpmTime;
            Seconds = cube.Seconds;
            X = cube.X;
            Y = cube.Y;
            Type = cube.Type;
            CutDirection = cube.CutDirection;
            AngleOffset = cube.AngleOffset;
            Direction = cube.Direction;
            Njs = cube.Njs;
            Head = cube.Head;
            Tail = cube.Tail;
            Pattern = cube.Pattern;
            Chain = cube.Chain;
            Forehand = cube.Forehand;
            ParityErrors = cube.ParityErrors;
            BombAvoidance = cube.BombAvoidance;
            TailLine = cube.TailLine;
            TailLayer = cube.TailLayer;
            TailDirection = cube.TailDirection;
            Squish = cube.Squish;
        }

        public Cube(Note note)
        {
            Note = note;
            JsonTime = note.Beats;
            BpmTime = note.BpmTime;
            Seconds = note.Seconds;
            X = note.x;
            Y = note.y;
            Type = note.Color;
            CutDirection = note.CutDirection;
            AngleOffset = note.AngleOffset;
            Njs = note.njs;
            if (note.CutDirection == 8) Direction = -1;
            else Direction = Mod(DirectionToDegree[note.CutDirection] + note.AngleOffset, 360);
        }
    }
}
