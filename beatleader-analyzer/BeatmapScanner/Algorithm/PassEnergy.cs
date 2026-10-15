using Analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;

namespace Analyzer.BeatmapScanner.Algorithm
{
    public enum PassRatingModel
    {
        /// <summary>Peak rolling means of SwingDiff over 8..128 swings (production).</summary>
        Classic,
        /// <summary>Energy-bar model (PassEnergy).</summary>
        Energy,
    }

    /// <summary>
    /// Pass rating from the game's energy bar. Every note is missed with probability sigmoid(MissSlope * (ln PassDiff - skill));
    /// energy starts at 0.5, gains 0.01 per hit note and loses 0.15 per missed note, the attempt fails at 0. The probability of
    /// clearing is computed exactly (dynamic programming over the energy in 1 % steps), and the rating is the skill at which a player
    /// clears the map half the time, exp(skill50) * Scale, so it keeps SwingDiff units and today's pass-rating scale.
    ///
    /// Short bursts are survivable on a full bar, sustained difficulty drains it, and the start (50 % energy) is the most
    /// dangerous part, as fails in the attempts data show. Per-swing difficulty (PassDiff, SwingPassDiff) is built from the swing's
    /// raw metrics like SwingDiff (swing speed with its low-speed falloff, stress), but with its own constants, plus a few pattern
    /// terms, NJS, streams and walls; the classic SwingDiff constants stay with the classic pass and tech ratings. Constants are
    /// fitted against pass difficulty measured from 24 M attempts (fails vs clears, player skill removed; portaBLe
    /// Analysis/py/a19_pass_attempts.py, a22_energy_pass.py, a31_v6_pass.py).
    /// </summary>
    public static class PassEnergy
    {
        public static PassRatingModel Model = PassRatingModel.Classic;

        // per-swing pass difficulty: swing speed and stress from the swing's raw metrics (own constants, independent of the classic
        // SwingDiff), stress multiplier with STRESS_SCALE inside, and pattern terms
        public const double DISTANCE_FALLOFF = 1.765;
        public const double SPEED_FALLOFF_BASE = 3.623;
        public const double ANGLE_STRAIN_STRESS = 0.4291, REPOSITIONING_STRESS = 1.493, ROTATION_STRESS = 0;   // rotation adds nothing once angle strain and repositioning are in (fit at 0)
        public const double STRESS_SCALE = 1.0;      // folded into the three stress weights above
        public const double STREAM = 1.082;         // alternating hands
        public const double DODGE_WALL = 1.193, CROUCH_WALL = 1.552;
        public const double CROSSOVER = 0.5632;      // red note in the rightmost lane or blue in the leftmost: x1.56
        public const double HORIZONTAL = 0.3309;     // left / right cuts
        public const double DIAGONAL = 0.2589;       // diagonal cuts
        public const double PARITY_BREAK = -0.1517;  // resets were over-counted (SwingFrequency already doubles on them)

        // energy model (game rules) and the one fitted slope
        public const double MISS_SLOPE = 2.5;
        public const int START_ENERGY = 50, HIT_GAIN = 1, MISS_LOSS = 15, MAX_ENERGY = 100;
        public const double SCALE = 0.2039;           // exp(skill50) -> today's pass-rating scale (equal medians on Standard maps)
        public const double ONE_SABER = 0.74;         // maps whose other hand has no swings (v2 value; the v6 attempts fit gives 0.79, but One Saber maps are already worth more than Standard)

        /// <summary>Pass difficulty of one swing before the NJS, stream and wall terms.</summary>
        public static double SwingPassDiff(SwingData swing, double speedMult)
        {
            double distanceDiff = swing.HitDistance / (swing.HitDistance + DISTANCE_FALLOFF) + 1;
            double swingSpeed = swing.SwingFrequency * distanceDiff * speedMult;
            double stress = (swing.AngleStrain * ANGLE_STRAIN_STRESS + swing.RepositioningDistance * REPOSITIONING_STRESS
                + swing.RotationAmount * ROTATION_STRESS) * distanceDiff;
            double lowSpeedFalloff = 1.0 - Math.Pow(SPEED_FALLOFF_BASE, -swingSpeed);
            return swingSpeed * lowSpeedFalloff * PatternMultiplier(swing, stress);
        }

        /// <param name="wallBuff">the classic wall buff of the swing (dodge or crouch)</param>
        /// <param name="classicCrouch">the classic crouch buff, to tell the two apart</param>
        public static double WallBuff(double wallBuff, double classicCrouch) => wallBuff >= classicCrouch ? CROUCH_WALL : DODGE_WALL;

        public static double PatternMultiplier(SwingData swing, double stress)
        {
            const double STRESS_FALLOFF = 2.0;
            double s = stress * STRESS_SCALE;
            double m = STRESS_FALLOFF * s / (s + STRESS_FALLOFF) + 1.0;
            var cube = swing.Cubes[0];
            bool cross = (cube.Type == 0 && cube.X == 3) || (cube.Type == 1 && cube.X == 0);
            if (cross) m *= 1 + CROSSOVER;
            if (cube.CutDirection is 2 or 3) m *= 1 + HORIZONTAL;
            else if (cube.CutDirection is >= 4 and <= 7) m *= 1 + DIAGONAL;
            if (swing.ParityErrors) m *= 1 + PARITY_BREAK;
            return m;
        }

        /// <param name="swings">both hands, in time order</param>
        /// <param name="oneSaber">one hand has no swings at all</param>
        public static double Rating(List<SwingData> swings, bool oneSaber)
        {
            if (swings.Count < 2) return 0;
            var logDiff = new List<double>(swings.Count * 2);
            foreach (var swing in swings)
            {
                double ld = Math.Log(Math.Max(swing.PassDiff, 1e-3));
                for (int k = Math.Clamp(swing.Cubes.Count, 1, 4); k > 0; k--) logDiff.Add(ld);
            }
            double lo = -2, hi = 5;
            for (int it = 0; it < 13; it++)
            {
                double mid = (lo + hi) / 2;
                if (ClearProbability(logDiff, mid) >= 0.5) hi = mid; else lo = mid;
            }
            return Math.Exp((lo + hi) / 2) * SCALE * (oneSaber ? ONE_SABER : 1.0);
        }

        /// <summary>Exact probability that a player of this skill ends the map with energy above 0.</summary>
        public static double ClearProbability(List<double> logDiff, double skill)
        {
            var e = new double[MAX_ENERGY + 1]; var next = new double[MAX_ENERGY + 1];
            e[START_ENERGY] = 1.0;
            foreach (double ld in logDiff)
            {
                double p = 1.0 / (1.0 + Math.Exp(-MISS_SLOPE * (ld - skill)));
                Array.Clear(next, 0, next.Length);
                for (int k = 1; k <= MAX_ENERGY; k++)
                {
                    double m = e[k];
                    if (m == 0) continue;
                    next[Math.Min(k + HIT_GAIN, MAX_ENERGY)] += m * (1 - p);
                    if (k - MISS_LOSS > 0) next[k - MISS_LOSS] += m * p;     // otherwise the attempt fails
                }
                (e, next) = (next, e);
            }
            double alive = 0;
            for (int k = 1; k <= MAX_ENERGY; k++) alive += e[k];
            return alive;
        }
    }
}
