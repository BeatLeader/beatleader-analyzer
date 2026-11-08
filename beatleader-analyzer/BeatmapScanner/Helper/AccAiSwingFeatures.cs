using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// The Acc AI's swing features: the Analyzer's view of each note's swing, appended to the model's note vector
    /// (RatingAPI DataProcessing) and exported for its training (Benchmark accaimaps -> beatsaber-replays-ai-2 data.py
    /// add_swing_features). One function for both, so training and inference can't drift apart. Notes in no swing get
    /// all zeros (Matched = 0).
    /// </summary>
    public static class AccAiSwingFeatures
    {
        public static readonly string[] Names =
        {
            "matched", "head", "multi", "pattern", "forehand", "reset", "bomb_avoid", "dir_sin", "dir_cos",
            "angle_strain", "rotation", "repositioning", "frequency", "linear",
        };

        public static int Size => Names.Length;

        /// <summary>Features of every note of the swings (Ratings.SwingData), by note object</summary>
        public static Dictionary<Note, float[]> Compute(IEnumerable<SwingData> swings)
        {
            var result = new Dictionary<Note, float[]>(ByReference.Instance);
            if (swings == null) return result;
            foreach (var swing in swings)
            {
                double direction = swing.Direction * Math.PI / 180;
                foreach (var cube in swing.Cubes)
                {
                    if (cube.Note == null) continue;
                    result[cube.Note] = new float[]
                    {
                        1,
                        cube.Head ? 1 : 0,
                        swing.Cubes.Count > 1 ? 1 : 0,
                        cube.Pattern ? 1 : 0,
                        swing.Forehand ? 1 : 0,
                        swing.ParityErrors ? 1 : 0,
                        cube.BombAvoidance ? 1 : 0,
                        (float)Math.Sin(direction),
                        (float)Math.Cos(direction),
                        (float)(Math.Min(swing.AngleStrain, 2.0) / 2.0),
                        (float)Math.Min(swing.RotationAmount, 1.0),
                        (float)(Math.Min(swing.RepositioningDistance, 1.5) / 1.5),
                        (float)(Math.Min(swing.SwingFrequency, 18.0) / 12.0),
                        swing.IsLinear ? 1 : 0,
                    };
                }
            }
            return result;
        }

        // Notes by object (System.Collections.Generic.ReferenceEqualityComparer isn't public on netstandard2.1)
        private sealed class ByReference : IEqualityComparer<Note>
        {
            public static readonly ByReference Instance = new();
            public bool Equals(Note x, Note y) => ReferenceEquals(x, y);
            public int GetHashCode(Note obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
