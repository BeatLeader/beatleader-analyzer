namespace Analyzer.BeatmapScanner.Algorithm
{
    internal class NjsBuff
    {
        //NJS buff for >24 njs
        public static double CalculateNjsBuff(float njs)
        {
            double buff = 1f;
            if (njs > 24)
            {
                buff = 1 + 0.01 * (njs - 24);
            }
            return buff;
        }
    }
}
