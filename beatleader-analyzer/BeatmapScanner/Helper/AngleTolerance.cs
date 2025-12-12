using System;
using static beatleader_analyzer.BeatmapScanner.Helper.Common;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Configuration for note cut angle tolerance based on game settings.
    /// Default tolerance is 60° per side (120° total cone).
    /// Strict Angles modifier reduces tolerance to 40° per side (80° total cone).
    /// </summary>
    internal static class AngleTolerance
    {
        /// <summary>
        /// Default cut angle tolerance in degrees (60° per side from expected cut direction).
        /// </summary>
        public const double DEFAULT_TOLERANCE = 60.0;

        /// <summary>
        /// Strict Angles modifier cut angle tolerance in degrees (40° per side from expected cut direction).
        /// </summary>
        public const double STRICT_TOLERANCE = 40.0;

        /// <summary>
        /// Gets the angle tolerance based on whether Strict Angles modifier is active.
        /// </summary>
        /// <param name="strictAngles">True if Strict Angles modifier is active</param>
        /// <returns>Angle tolerance in degrees</returns>
        public static double GetTolerance(bool strictAngles)
        {
            return strictAngles ? STRICT_TOLERANCE : DEFAULT_TOLERANCE;
        }

        /// <summary>
        /// Checks if a swing angle is within the allowed angle tolerance cone for a given note.
        /// </summary>
        /// <param name="swingAngle">The angle of the swing path (in degrees)</param>
        /// <param name="noteExpectedAngle">The expected cut direction of the note (in degrees)</param>
        /// <param name="tolerance">The angle tolerance (in degrees)</param>
        /// <returns>True if the swing angle is valid for cutting the note</returns>
        public static bool IsAngleWithinTolerance(double swingAngle, double noteExpectedAngle, double tolerance)
        {
            double angleDiff = Math.Abs(swingAngle - noteExpectedAngle);
            // Handle wrap-around (e.g., 350° and 10° are only 20° apart)
            if (angleDiff > 180)
            {
                angleDiff = 360 - angleDiff;
            }
            return angleDiff <= tolerance;
        }
    }
}
