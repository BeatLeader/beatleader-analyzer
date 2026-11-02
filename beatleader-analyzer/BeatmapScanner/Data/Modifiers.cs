namespace beatleader_analyzer.BeatmapScanner.Data
{
    internal class Modifiers
    {
        public float baseBPM { get; set; }
        public float modifiedBPM { get; set; }
        public float speedMult { get; set; }
        public float njsMult { get; set; }
        public bool strictAngles { get; set; }
        // Format version of the difficulty file ("2.0.0", "3.3.0"...): an input of the external parity model (ParityModel)
        public string MapVersion { get; set; } = "";

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
