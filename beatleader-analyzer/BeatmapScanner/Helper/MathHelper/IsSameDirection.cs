using System;
using static beatleader_analyzer.BeatmapScanner.Helper.MathHelper.Helper;

namespace beatleader_analyzer.BeatmapScanner.Helper.MathHelper
{
    /// <summary>
    /// Checks if two swing directions are similar (handles angle wrapping).
    /// </summary>
    internal class IsSameDirection
    {
        public static bool IsSameDir(double before, double after, double degree = 67.5)
        {
            before = Mod(before, 360);
            after = Mod(after, 360);

            if (Math.Abs(before - after) <= 180)
            {
                if (Math.Abs(before - after) < degree)
                {
                    return true;
                }
            }
            else
            {
                if (360 - Math.Abs(before - after) < degree)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
