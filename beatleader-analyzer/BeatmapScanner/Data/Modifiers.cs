using beatleader_parser.Timescale;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace beatleader_analyzer.BeatmapScanner.Data
{
    internal class Modifiers
    {
        public Timescale timescale { get; set; }
        public float speedMult { get; set; }
        public float njsMult { get; set; }
        public bool strictAngles { get; set; }

        public Modifiers(Timescale timescale, float speedMult = 1, float njsMult = 1, bool strictAngles = false)
        {
            this.timescale = timescale;
            this.speedMult = speedMult;
            this.njsMult = njsMult;
            this.strictAngles = strictAngles;
        }
    }
}
