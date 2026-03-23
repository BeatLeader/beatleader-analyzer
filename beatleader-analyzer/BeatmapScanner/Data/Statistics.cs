namespace Analyzer.BeatmapScanner.Data
{
    public class Statistics
    {
        public int Stacks { get; set; } = 0;
        public int Towers { get; set; } = 0;
        public int Sliders { get; set; } = 0;
        public int CurvedSliders { get; set; } = 0;
        public int Windows { get; set; } = 0;
        public int SlantedWindows { get; set; } = 0;
        public int DodgeWalls { get; set; } = 0;
        public float DodgeWallDuration { get; set; } = 0;
        public int CrouchWalls { get; set; } = 0;
        public float CrouchWallDuration { get; set; } = 0;
        public int ParityErrors { get; set; } = 0;
        public int BombAvoidances { get; set; } = 0;
        public int LinearSwings { get; set; } = 0;
    }
}
