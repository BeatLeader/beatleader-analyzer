namespace beatleader_analyzer.BeatmapScanner.Helper
{
    internal class NjsBuff
    {
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
