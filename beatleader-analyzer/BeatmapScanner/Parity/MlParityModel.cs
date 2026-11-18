#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Analyzer.BeatmapScanner.Algorithm;
using Analyzer.BeatmapScanner.Data;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Analyzer.BeatmapScanner.Parity
{
    /// <summary>
    /// Parity model trained on top players' replays (beatsaber-replays-ai-2 paritymodel, the "ens-best" model): a neural
    /// sequence model (10 fold models averaged, parity_nn.onnx) and gradient boosted trees (5 folds, parity_trees.gzbin)
    /// give per swing forehand / backhand scores and per swing pair scores of keeping the parity, decoded together with
    /// Viterbi so every hand's parity is consistent. A swing whose parity is kept with a positive merge margin is a merged
    /// swing: the players hit it with the previous one (the Analyzer split one swing), so it joins the previous swing
    /// instead of being a reset. Every swing takes the model's direction (Analyzer ModelDirections).
    ///
    /// The Analyzer's parity by default (ParityModel.Current; null goes back to ParityPredictor, e.g. portaBLe's parity=dp
    /// setting). The constants and the decoding are paritymodel/production.py's; keep them in sync. The Benchmark project's
    /// parityml-verify checks this port against Python's reference maps, paritybench its speed.
    /// </summary>
    public class MlParityModel : IParityModel
    {
        const double Beta = 0.5;
        const double TreesAlpha = 0.35;

        /// <summary>Called with every hand's inputs, scores and decoding (Benchmark parityml-verify)</summary>
        public static Action<HandResult?[]>? Debug { get; set; }

        /// <summary>Reuse a map's result when it's rated again (other speed modifiers); off for benchmarks</summary>
        public static bool UseCache { get; set; } = true;

        // Time spent per stage (Stopwatch ticks, all threads): features, network, trees, decoding
        static long featureTicks, networkTicks, treeTicks, decodeTicks;
        public static (TimeSpan Features, TimeSpan Network, TimeSpan Trees, TimeSpan Decode) Timings =>
            (Ticks(featureTicks), Ticks(networkTicks), Ticks(treeTicks), Ticks(decodeTicks));
        static TimeSpan Ticks(long t) => TimeSpan.FromSeconds(t / (double)System.Diagnostics.Stopwatch.Frequency);
        public static void ResetTimings() => featureTicks = networkTicks = treeTicks = decodeTicks = 0;

        public class HandResult
        {
            public ParityFeatures.HandInputs Inputs = null!;
            public float[][] Unary = [];
            public float[] Stay = [], Merge = [];
            public float[][] Dir = [];
            public double[] TreeForehand = [], TreeReset = [];
            public int[] Parity = [];
            public bool[] Merged = [];
            /// <summary>Swing direction for the decoded parity, degrees, right hand frame (the swing's direction, in the map's frame)</summary>
            public double[] Direction = [];
        }

        static readonly ThreadLocal<InferenceSession?> session = new(() => null);
        static readonly Lazy<List<XgbForest>> forests = new(() => XgbForest.Load(Resource("parity_trees.gzbin")));

        static byte[] Resource(string name)
        {
            using var stream = typeof(MlParityModel).Assembly.GetManifestResourceStream($"Analyzer.Parity.{name}")
                ?? throw new FileNotFoundException($"Embedded resource Analyzer.Parity.{name} not found");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        static InferenceSession Session()
        {
            session.Value ??= new InferenceSession(Resource("parity_nn.onnx"),
                new Microsoft.ML.OnnxRuntime.SessionOptions { IntraOpNumThreads = 1, ExecutionMode = ExecutionMode.ORT_SEQUENTIAL });
            return session.Value;
        }

        // Decoded parity and merges per map: a map is rated once per speed modifier, and the model's inputs don't change
        // with it (cube times aren't scaled; the Analyzer applies the speed later), so the result is the same
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int[] Parity, bool[] Merged, double[] Direction)?[]> cache = new();
        const int CacheSize = 4096;

        static string Key(ParityModelInput input)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            {
                w.Write(input.MapVersion ?? "");
                w.Write(input.Bpm);
                foreach (var hand in new[] { input.Red, input.Blue })
                {
                    w.Write(hand.Count);
                    foreach (var c in hand)
                    {
                        w.Write(c.Seconds); w.Write(c.JsonTime); w.Write(c.X); w.Write(c.Y); w.Write(c.CutDirection); w.Write(c.Njs);
                        w.Write(c.Direction); w.Write(c.Head); w.Write(c.Pattern); w.Write(c.Tail); w.Write(c.Chain); w.Write(c.BombAvoidance);
                        w.Write(c.TailLine); w.Write(c.TailLayer);
                    }
                }
                w.Write(input.Bombs.Count);
                foreach (var b in input.Bombs) { w.Write(b.Seconds); w.Write(b.x); w.Write(b.y); }
                w.Write(input.Walls.Count);
                foreach (var wl in input.Walls) { w.Write(wl.Seconds); w.Write(wl.DurationInSeconds); w.Write(wl.x); w.Write(wl.y); w.Write(wl.Width); }
            }
            using var sha = System.Security.Cryptography.SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(ms.ToArray()));
        }

        public void Predict(ParityModelInput input)
        {
            var cubes = new[] { input.Red, input.Blue };
            // Kept for portaBLe's ParityCheck, as ParityPredictor does
            foreach (var c in input.Red.Concat(input.Blue)) c.ParityDirection = c.Direction;
            string key = Key(input);
            if (Debug == null && UseCache && cache.TryGetValue(key, out var cached))
            {
                for (int hand = 0; hand < 2; hand++)
                {
                    if (cached[hand] is var (parity, merged, direction)) Apply(ParityFeatures.Group(cubes[hand]).OrderBy(i => i.T0).ToList(), parity, merged, direction, hand, cubes[hand]);
                    else if (cubes[hand].Count > 0) ParityPredictor.Predict(cubes[hand], hand == 1, input.Bombs);
                }
                return;
            }
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var hands = ParityFeatures.Build(input);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            Interlocked.Add(ref featureTicks, t1 - t0);
            var results = new HandResult?[2];
            var present = Enumerable.Range(0, 2).Where(h => hands[h] != null).ToList();
            if (present.Count > 0)
            {
                var a = hands[present[0]]!;
                var b = hands[present[^1]]!;
                var outputs = RunNetwork(a.Nn, b.Nn);
                long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
                Interlocked.Add(ref networkTicks, t2 - t1);
                var all = forests.Value;
                foreach (int hand in present)
                {
                    var h = hands[hand]!;
                    int o = hand == present[0] ? 0 : 4;
                    long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
                    var r = new HandResult
                    {
                        Inputs = h,
                        Unary = outputs[o],
                        Stay = outputs[o + 1].Select(v => v[0]).ToArray(),
                        Merge = outputs[o + 2].Select(v => v[0]).ToArray(),
                        Dir = outputs[o + 3],
                        TreeForehand = MeanMargins(all.Take(all.Count / 2), h.Tree),
                        TreeReset = MeanMargins(all.Skip(all.Count / 2), h.Tree),
                    };
                    long t4 = System.Diagnostics.Stopwatch.GetTimestamp();
                    Interlocked.Add(ref treeTicks, t4 - t3);
                    Decode(r);
                    Apply(h.Swings, r.Parity, r.Merged, r.Direction, hand, cubes[hand]);
                    Interlocked.Add(ref decodeTicks, System.Diagnostics.Stopwatch.GetTimestamp() - t4);
                    results[hand] = r;
                }
            }
            // A hand too short for the model (fewer than 2 swings): the current predictor
            for (int hand = 0; hand < 2; hand++)
            {
                if (results[hand] == null && cubes[hand].Count > 0) ParityPredictor.Predict(cubes[hand], hand == 1, input.Bombs);
            }
            if (cache.Count >= CacheSize) cache.Clear();
            cache[key] = results.Select(r => r == null ? ((int[], bool[], double[])?)null : (r.Parity, r.Merged, r.Direction)).ToArray();
            Debug?.Invoke(results);
        }

        // Mean logit over forests (the emission forests are the first half of the file, the reset ones the second half)
        static double[] MeanMargins(IEnumerable<XgbForest> forests, float[][] rows)
        {
            var list = forests.ToList();
            var sum = new double[rows.Length];
            foreach (var f in list)
            {
                var m = f.Margins(rows);
                for (int i = 0; i < m.Length; i++) sum[i] += m[i];
            }
            for (int i = 0; i < sum.Length; i++) sum[i] /= list.Count;
            return sum;
        }

        /// <summary>unary (T, 2), stay (T), merge (T), dir (T, 4) of hand a then hand b (stay / merge as (T, 1) rows)</summary>
        static float[][][] RunNetwork(float[][] xa, float[][] xb)
        {
            int f = ParityFeatures.NnInputs;
            DenseTensor<float> Tensor(float[][] rows)
            {
                var t = new DenseTensor<float>(new[] { 1, rows.Length, f });
                for (int i = 0; i < rows.Length; i++)
                    for (int j = 0; j < f; j++) t[0, i, j] = rows[i][j];
                return t;
            }
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("xa", Tensor(xa)), NamedOnnxValue.CreateFromTensor("xb", Tensor(xb)) };
            using var res = Session().Run(inputs);
            var names = new[] { "unary_a", "stay_a", "merge_a", "dir_a", "unary_b", "stay_b", "merge_b", "dir_b" };
            return names.Select(n =>
            {
                var t = res.First(r => r.Name == n).AsTensor<float>();
                int rows = t.Dimensions[0];
                int cols = t.Dimensions.Length > 1 ? t.Dimensions[1] : 1;
                var arr = t.ToArray();
                return Enumerable.Range(0, rows).Select(i => arr.Skip(i * cols).Take(cols).ToArray()).ToArray();
            }).ToArray();
        }

        // production.decode
        static void Decode(HandResult r)
        {
            int n = r.Stay.Length;
            const double eps = 1e-5;
            var unary = new double[n, 2];
            var keep = new double[n];
            var merged = new bool[n];
            for (int t = 0; t < n; t++)
            {
                double pf = Math.Clamp(1 / (1 + Math.Exp(-r.TreeForehand[t])), eps, 1 - eps);
                double pr = Math.Clamp(1 / (1 + Math.Exp(-r.TreeReset[t])), eps, 1 - eps);
                double ts = Math.Log(pr) - Math.Log(1 - pr);
                merged[t] = r.Merge[t] > 0;
                // The trees can't tell a merged swing: there their push towards alternating doesn't count
                if (merged[t]) ts = Math.Max(ts, 0);
                unary[t, 0] = r.Unary[t][0] + Beta * TreesAlpha * Math.Log(1 - pf);
                unary[t, 1] = r.Unary[t][1] + Beta * TreesAlpha * Math.Log(pf);
                keep[t] = r.Stay[t] + Beta * ts;
            }
            // Viterbi over the two parities (0 backhand, 1 forehand); keeping the parity adds keep[t]
            var back = new int[n, 2];
            double s0 = unary[0, 0], s1 = unary[0, 1];
            for (int t = 1; t < n; t++)
            {
                double k0 = s0 + keep[t], f0 = s1, k1 = s1 + keep[t], f1 = s0;
                back[t, 0] = k0 >= f0 ? 0 : 1;
                back[t, 1] = k1 >= f1 ? 1 : 0;
                double n0 = Math.Max(k0, f0) + unary[t, 0], n1 = Math.Max(k1, f1) + unary[t, 1];
                s0 = n0; s1 = n1;
            }
            var p = new int[n];
            p[n - 1] = s1 >= s0 ? 1 : 0;
            for (int t = n - 1; t > 0; t--) p[t - 1] = back[t, p[t]];
            r.Parity = p;
            r.Merged = Enumerable.Range(0, n).Select(t => t > 0 && p[t] == p[t - 1] && merged[t]).ToArray();
            r.Direction = Enumerable.Range(0, n).Select(t =>
            {
                double vx = p[t] == 1 ? r.Dir[t][0] : r.Dir[t][2], vy = p[t] == 1 ? r.Dir[t][1] : r.Dir[t][3];
                return ((Math.Atan2(vy, vx) * 180 / Math.PI) % 360 + 360) % 360;
            }).ToArray();
        }

        // direction: right hand frame (the left hand mirrored), as the model gives it
        static void Apply(List<ParityFeatures.SwingInfo> sw, int[] parity, bool[] mergedFlags, double[] direction, int hand, List<Cube> cubes)
        {
            for (int k = 0; k < sw.Count; k++)
            {
                bool forehand = parity[k] == 1;
                bool reset = k > 0 && parity[k] == parity[k - 1] && !mergedFlags[k];
                // Swing direction in the map's frame (Analyzer ModelDirections)
                double modelDirection = hand == 0 ? ((180 - direction[k]) % 360 + 360) % 360 : direction[k];
                foreach (var c in sw[k].Cubes)
                {
                    c.Forehand = forehand;
                    c.ParityErrors = reset;
                    c.ModelDirection = modelDirection;
                }
            }
            if (!mergedFlags.Any(m => m)) return;
            // Merged swings join the previous one: one group in the cubes' order (what SwingCreation reads), Head on its
            // first cube, Tail on its last, Pattern on all
            var position = new Dictionary<Cube, int>(new ByReference());
            for (int i = 0; i < cubes.Count; i++) position[cubes[i]] = i;
            var merged = new List<(List<Cube> Cubes, bool Joined)>();
            for (int k = 0; k < sw.Count; k++)
            {
                if (k > 0 && mergedFlags[k])
                {
                    merged[^1].Cubes.AddRange(sw[k].Cubes);
                    merged[^1] = (merged[^1].Cubes, true);
                }
                else merged.Add(([.. sw[k].Cubes], false));
            }
            foreach (var (g, joined) in merged)
            {
                if (!joined) continue;
                var ordered = g.OrderBy(c => position[c]).ToList();
                for (int i = 0; i < ordered.Count; i++)
                {
                    ordered[i].Pattern = true;
                    ordered[i].Head = i == 0;
                    ordered[i].Tail = i == ordered.Count - 1;
                }
            }
        }

        sealed class ByReference : IEqualityComparer<Cube>
        {
            public bool Equals(Cube? a, Cube? b) => ReferenceEquals(a, b);
            public int GetHashCode(Cube c) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(c);
        }
    }
}
