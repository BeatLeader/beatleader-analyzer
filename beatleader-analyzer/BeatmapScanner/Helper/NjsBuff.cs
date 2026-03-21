using beatleader_analyzer.BeatmapScanner.Data;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal class NjsBuff
    {
        public static double CalculateNjsBuff(float njs, Modifiers modifiers)
        {
            // We need to take into account of both speed modifier and njs modifier
            njs = njs * modifiers.speedMult * modifiers.njsMult;

            // Cap at 50
            if (njs > 50) njs = 50;

            double buff = 1f;
            if (njs > 24)
            {
                buff = 1 + 0.01 * (njs - 24);
            }
            return buff;
        }
    }
}
