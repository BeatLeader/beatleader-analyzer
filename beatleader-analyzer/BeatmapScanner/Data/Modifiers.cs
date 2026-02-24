namespace beatleader_analyzer.BeatmapScanner.Data
{
    internal class Modifiers
    {
        public float baseBPM { get; set; }
        public float modifiedBPM { get; set; }
        public float speedMult { get; set; }
        public float njsMult { get; set; }
        public bool strictAngles { get; set; }

        public Modifiers(float bpm, float speedMult = 1, float njsMult = 1, bool strictAngles = false)
        {
            this.baseBPM = bpm;
            this.modifiedBPM = bpm * speedMult;
            this.speedMult = speedMult;
            this.njsMult = njsMult;
            this.strictAngles = strictAngles;
        }
    }
}
