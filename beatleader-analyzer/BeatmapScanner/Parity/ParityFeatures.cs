#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Analyzer.BeatmapScanner.Algorithm;
using Analyzer.BeatmapScanner.Data;

namespace Analyzer.BeatmapScanner.Parity
{
    /// <summary>
    /// The parity model's inputs, a port of beatsaber-replays-ai-2 paritymodel (dataset.py build, gbdt.py angle_pair /
    /// hand_features / map_aggregates / hand_matrix, nn.py physics and map_inputs). Keep both in sync: the Benchmark
    /// project's parityml-verify compares them on the reference maps written by paritymodel/production.py.
    ///
    /// One row per swing (the Analyzer's grouping after PreprocessNotes: a swing starts at a cube with !Pattern || Head),
    /// each hand in the right hand's frame (the left hand mirrored: column 3 - x, left / right arrows swapped, angles
    /// 180 - a).
    /// </summary>
    public static class ParityFeatures
    {
        public static readonly string[] Swing =
        [
            "head_x", "head_y", "tail_x", "tail_y",
            "head_cut_0", "head_cut_1", "head_cut_2", "head_cut_3", "head_cut_4", "head_cut_5", "head_cut_6", "head_cut_7", "head_cut_8",
            "tail_cut_0", "tail_cut_1", "tail_cut_2", "tail_cut_3", "tail_cut_4", "tail_cut_5", "tail_cut_6", "tail_cut_7", "tail_cut_8",
            "head_sin", "head_cos", "tail_sin", "tail_cos", "pre_sin", "pre_cos", "pre_dot",
            "notes_log", "pattern", "chain", "chain_dx", "chain_dy", "duration",
            "gap_s", "gap_beats", "gap_s_log", "gap_beats_log", "next_gap_s_log", "head_gap_s_log",
            "njs", "beat_s",
            "other_dt", "other_x", "other_y", "other_sin", "other_cos", "other_dot", "other_same_time", "other_gap_log",
            "bombs", "bombs_side", "bomb_dx", "bomb_dy", "bomb_above", "bomb_below", "bomb_avoid",
            "walls", "wall_crouch", "wall_dx",
            "dens_1s", "dens_4s", "dens_all_1s", "pos",
            "bomb_exit_side", "bomb_entry_side", "bomb_path", "bomb_above_tail", "bomb_below_tail", "bomb_above_head",
            "bomb_below_head", "bomb_time_frac",
        ];
        static readonly Dictionary<string, int> F = Swing.Select((f, i) => (f, i)).ToDictionary(p => p.f, p => p.i);

        static readonly string[] Neigh = ["head_x", "head_y", "tail_x", "tail_y", "head_sin", "head_cos", "tail_sin", "tail_cos",
            "pre_sin", "pre_cos", "pre_dot", "pattern", "notes_log", "gap_s_log", "gap_beats_log", "bombs", "bomb_avoid", "other_dt", "other_same_time"];
        static readonly int[] Offsets = [-2, -1, 1, 2];
        const int AnglePairCount = 7, HandCount = 7, PhysicsCount = 5, MapCount = 16;
        public static int NnInputs => Swing.Length + AnglePairCount + HandCount + PhysicsCount + MapCount;
        public static int TreeInputs => Swing.Length + Offsets.Length * (Neigh.Length + 1) + AnglePairCount + HandCount + MapCount;

        static readonly int[] MirrorCut = [0, 1, 3, 2, 5, 4, 7, 6, 8];
        static readonly double[] CutAngle = [90, 270, 180, 0, 135, 45, 225, 315, double.NaN];
        static readonly int[] Up = [0, 4, 5], Down = [1, 6, 7], Horizontal = [2, 3];
        const double ForehandNeutral = 247.5, BackhandNeutral = 67.5;

        /// <summary>A swing in the map's frame</summary>
        public class SwingInfo
        {
            public List<Cube> Cubes = [];   // time order
            public double T0, T1, B0, B1, Hx, Hy, Tx, Ty, Pre, Njs, ChainDx, ChainDy;
            public int Hc, Tc;
            public bool Chain, BombAvoid;
        }

        public class HandInputs
        {
            public List<SwingInfo> Swings = [];
            public float[][] Nn = [];      // NnInputs per swing
            public float[][] Tree = [];    // TreeInputs per swing
            public int[] HeadCut = [], TailCut = [];   // map frame
        }

        /// <summary>Swing groups of a hand in the Analyzer's order, cubes of each in time order (ties as the Analyzer's note
        /// order: time, column, row, cut)</summary>
        public static List<SwingInfo> Group(List<Cube> cubes)
        {
            var groups = new List<List<Cube>>();
            foreach (var c in cubes)
            {
                if (!c.Pattern || c.Head || groups.Count == 0) groups.Add([c]);
                else groups[^1].Add(c);
            }
            return groups.Select(g =>
            {
                var s = g.OrderBy(c => c.Seconds).ThenBy(c => c.BpmTime).ThenBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.CutDirection).ToList();
                var h = s[0];
                var t = s[^1];
                return new SwingInfo
                {
                    Cubes = s, T0 = R(h.Seconds), T1 = R(t.Seconds), B0 = R(h.JsonTime), B1 = R(t.JsonTime), Hx = h.X, Hy = h.Y, Tx = t.X, Ty = t.Y,
                    Hc = h.CutDirection, Tc = t.CutDirection, Pre = R(h.Direction), Njs = R(h.Njs), Chain = h.Chain,
                    ChainDx = h.TailLine - h.X, ChainDy = h.TailLayer - h.Y, BombAvoid = s.Any(c => c.BombAvoidance),
                };
            }).ToList();
        }

        // The model was trained on portaBLe paritymaps' export, which rounds times, NJS and directions to 4 decimals. Rounded
        // the same way, notes exactly half a second apart stay exactly 0.5 s apart (dens_1s, ... compare with <=).
        static double R(double v) => double.IsFinite(v) ? Math.Round(v, 4) : 0;

        // Slack of the time windows, far over the rounding of the tests
        const double WindowSlack = 0.01;

        static bool IsSorted(double[] times)
        {
            for (int k = 1; k < times.Length; k++) if (!(times[k] >= times[k - 1])) return false;
            return true;
        }

        // First index whose time is at least t
        static int LowerBound(double[] times, double t)
        {
            int lo = 0, hi = times.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // The items whose time is in [from, to] (plus the slack), in order: all of them when the times aren't sorted
        static IEnumerable<T> Window<T>(T[] items, double[] times, bool sorted, double from, double to)
        {
            if (!sorted) return items;
            int start = LowerBound(times, from - WindowSlack), end = LowerBound(times, to + WindowSlack);
            return new ArraySegment<T>(items, start, Math.Max(end - start, 0));
        }

        // Count of the sorted times with |t - center| <= radius
        static int CountNear(double[] sorted, double center, double radius)
        {
            int count = 0;
            for (int k = LowerBound(sorted, center - radius - WindowSlack); k < sorted.Length && sorted[k] <= center + radius + WindowSlack; k++)
                if (Math.Abs(sorted[k] - center) <= radius) count++;
            return count;
        }
        static double MirrorAngle(double a) => Mod(180 - a, 360);
        static double Mod(double a, double m) => ((a % m) + m) % m;
        // As numpy (radians / degrees multiply by the constant): the trees split on sin / cos rounding noise (cos 90 deg =
        // 6.1e-17 vs a dot's 0), so these have to give the very same bits as the Python features
        static double Radians(double d) => d * (Math.PI / 180);
        static double Degrees(double r) => r * (180 / Math.PI);
        static double Median(List<double> v)
        {
            v.Sort();
            int n = v.Count;
            return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
        }

        /// <summary>Inputs of both hands (null for a hand with fewer than 2 swings)</summary>
        public static HandInputs?[] Build(ParityModelInput input)
        {
            var info = new[] { Group(input.Red), Group(input.Blue) };
            var all = input.Red.Concat(input.Blue).ToList();
            double duration = Math.Max(all.Count > 0 ? all.Max(c => R(c.Seconds)) : 0, 1.0);
            var slopes = info.SelectMany(h => h).Where(i => i.B1 - i.B0 > 0.05).Select(i => (i.T1 - i.T0) / (i.B1 - i.B0)).ToList();
            double beatS = slopes.Count > 0 ? Median(slopes) : 60.0 / input.Bpm;
            var headTimes = info.Select(h => h.Select(i => i.T0).ToArray()).ToArray();
            var allTimes = headTimes[0].Concat(headTimes[1]).OrderBy(t => t).ToArray();
            var bombs = input.Bombs.OrderBy(b => b.BpmTime).Select(b => (T: R(b.Seconds), X: (double)b.x, Y: (double)b.y)).ToArray();
            var walls = input.Walls.OrderBy(w => w.BpmTime).Select(w => (T: R(w.Seconds), D: R(w.DurationInSeconds), X: (double)w.x, Y: (double)w.y, W: (double)w.Width)).ToArray();
            // Per swing, only the bombs, walls and head times near it are tested (binary search on the times, with some slack;
            // the same tests as over the whole map, in the same order). Seconds follow the beats, so the bombs and walls sorted
            // by beat are sorted by time too; if not, whole scans
            var bombTimes = bombs.Select(b => b.T).ToArray();
            var wallTimes = walls.Select(w => w.T).ToArray();
            bool sortedTimes = IsSorted(bombTimes) && IsSorted(wallTimes);
            double longestWall = walls.Length > 0 ? Math.Max(walls.Max(w => w.D), 0) : 0;
            var sortedHeadTimes = headTimes.Select(t => t.OrderBy(v => v).ToArray()).ToArray();

            var hands = new HandInputs?[2];
            for (int hand = 0; hand < 2; hand++)
            {
                var sw = info[hand].OrderBy(i => i.T0).ToList();
                int n = sw.Count;
                if (n < 2) continue;
                var other = info[1 - hand].OrderBy(i => i.T0).ToList();
                bool mir = hand == 0;
                Func<double, double> mx = mir ? x => 3 - x : x => x;
                Func<int, int> mc = mir ? c => c >= 0 && c < 9 ? MirrorCut[c] : 8 : c => c >= 0 && c < 9 ? c : 8;
                Func<double, double> ma = mir ? MirrorAngle : a => a;
                var X = new double[n][];
                for (int k = 0; k < n; k++)
                {
                    var x = X[k] = new double[Swing.Length];
                    var i = sw[k];
                    x[F["head_x"]] = mx(i.Hx) / 3; x[F["head_y"]] = i.Hy / 2; x[F["tail_x"]] = mx(i.Tx) / 3; x[F["tail_y"]] = i.Ty / 2;
                    int hc = mc(i.Hc), tc = mc(i.Tc);
                    x[F[$"head_cut_{hc}"]] = 1;
                    x[F[$"tail_cut_{tc}"]] = 1;
                    if (hc < 8) { double r = Radians(CutAngle[hc]); x[F["head_sin"]] = Math.Sin(r); x[F["head_cos"]] = Math.Cos(r); }
                    if (tc < 8) { double r = Radians(CutAngle[tc]); x[F["tail_sin"]] = Math.Sin(r); x[F["tail_cos"]] = Math.Cos(r); }
                    if (i.Pre >= 0) { double r = Radians(ma(i.Pre)); x[F["pre_sin"]] = Math.Sin(r); x[F["pre_cos"]] = Math.Cos(r); }
                    x[F["pre_dot"]] = hc == 8 ? 1 : 0;
                    x[F["notes_log"]] = Math.Log(i.Cubes.Count);
                    x[F["pattern"]] = i.Cubes.Count > 1 ? 1 : 0;
                    x[F["chain"]] = i.Chain ? 1 : 0;
                    if (i.Chain) { x[F["chain_dx"]] = (mir ? -i.ChainDx : i.ChainDx) / 3; x[F["chain_dy"]] = i.ChainDy / 2; }
                    x[F["duration"]] = Math.Min(i.T1 - i.T0, 2);
                    if (k > 0)
                    {
                        var p = sw[k - 1];
                        double g = Math.Max(i.T0 - p.T1, 0), gb = Math.Max(i.B0 - p.B1, 0);
                        x[F["gap_s"]] = Math.Min(g, 4); x[F["gap_beats"]] = Math.Min(gb, 8);
                        x[F["gap_s_log"]] = Math.Log(g + 0.02); x[F["gap_beats_log"]] = Math.Log(gb + 0.05);
                        x[F["head_gap_s_log"]] = Math.Log(Math.Max(i.T0 - p.T0, 0) + 0.02);
                    }
                    else
                    {
                        x[F["gap_s"]] = 4; x[F["gap_beats"]] = 8; x[F["gap_s_log"]] = Math.Log(4); x[F["gap_beats_log"]] = Math.Log(8); x[F["head_gap_s_log"]] = Math.Log(4);
                    }
                    x[F["next_gap_s_log"]] = k + 1 < n ? Math.Log(Math.Max(sw[k + 1].T0 - i.T1, 0) + 0.02) : Math.Log(4);
                    x[F["njs"]] = i.Njs / 20;
                    x[F["beat_s"]] = beatS;
                    if (other.Count > 0)
                    {
                        int j = 0;
                        double best = double.MaxValue;
                        for (int q = 0; q < other.Count; q++)
                        {
                            double d = Math.Abs(other[q].T0 - i.T0);
                            if (d < best) { best = d; j = q; }
                        }
                        var o = other[j];
                        x[F["other_dt"]] = Math.Clamp(o.T0 - i.T0, -1, 1);
                        x[F["other_x"]] = mx(o.Hx) / 3; x[F["other_y"]] = o.Hy / 2;
                        int oc = mc(o.Hc);
                        if (oc < 8) { double r = Radians(CutAngle[oc]); x[F["other_sin"]] = Math.Sin(r); x[F["other_cos"]] = Math.Cos(r); }
                        x[F["other_dot"]] = oc == 8 ? 1 : 0;
                        x[F["other_same_time"]] = Math.Abs(o.T0 - i.T0) < 0.02 ? 1 : 0;
                        x[F["other_gap_log"]] = Math.Log(Math.Abs(o.T0 - i.T0) + 0.02);
                    }
                    else
                    {
                        x[F["other_dt"]] = 1; x[F["other_gap_log"]] = Math.Log(4);
                    }
                    double tFrom = k > 0 ? sw[k - 1].T1 : i.T0 - 1;
                    var bb = Window(bombs, bombTimes, sortedTimes, tFrom, i.T0).Where(b => b.T > tFrom + 1e-4 && b.T < i.T0 - 1e-4).ToArray();
                    if (bb.Length > 0)
                    {
                        var bx = bb.Select(b => mx(b.X)).ToArray();
                        x[F["bombs"]] = Math.Log(1 + bb.Length);
                        x[F["bombs_side"]] = Math.Log(1 + bx.Count(v => v >= 1));
                        double hx = mx(i.Hx);
                        int jn = 0;
                        double bd = double.MaxValue;
                        for (int q = 0; q < bb.Length; q++)
                        {
                            double d = Math.Sqrt((bx[q] - hx) * (bx[q] - hx) + (bb[q].Y - i.Hy) * (bb[q].Y - i.Hy));
                            if (d < bd) { bd = d; jn = q; }
                        }
                        x[F["bomb_dx"]] = (bx[jn] - hx) / 3; x[F["bomb_dy"]] = (bb[jn].Y - i.Hy) / 2;
                        x[F["bomb_above"]] = bb.Any(b => b.Y > i.Hy) ? 1 : 0;
                        x[F["bomb_below"]] = bb.Any(b => b.Y < i.Hy) ? 1 : 0;
                        if (k > 0) BombGeometry(x, bb.Select(b => (b.T, b.Y)).ToArray(), bx, sw[k - 1], i, mx, mc, ma);
                    }
                    x[F["bomb_avoid"]] = i.BombAvoid ? 1 : 0;
                    var active = Window(walls, wallTimes, sortedTimes, i.T0 - longestWall, i.T0).Where(w => w.T <= i.T0 && w.T + w.D >= i.T0).ToArray();
                    if (active.Length > 0)
                    {
                        x[F["walls"]] = Math.Log(1 + active.Length);
                        // As dataset.py: rows [seconds, duration, x, y, width, height], "crouch" tests y >= 2 and width >= 3
                        x[F["wall_crouch"]] = active.Any(w => w.Y >= 2 && w.W >= 3) ? 1 : 0;
                        x[F["wall_dx"]] = (active.Average(w => mx(w.X + w.W / 2 - 0.5)) - 1.5) / 3;
                    }
                    var ht = sortedHeadTimes[hand];
                    x[F["dens_1s"]] = CountNear(ht, i.T0, 0.5);
                    x[F["dens_4s"]] = CountNear(ht, i.T0, 2) / 4.0;
                    x[F["dens_all_1s"]] = CountNear(allTimes, i.T0, 0.5);
                    x[F["pos"]] = i.T0 / duration;
                }
                hands[hand] = new HandInputs
                {
                    Swings = sw,
                    HeadCut = sw.Select(i => i.Hc).ToArray(),
                    TailCut = sw.Select(i => i.Tc).ToArray(),
                    Nn = X.Select(r => r.Select(v => (float)v).ToArray()).ToArray(),   // the swing part, completed below
                };
            }

            // Python keeps the swing features as float32 and computes the rest from them
            var present = hands.Where(h => h != null).Select(h => h!).ToList();
            if (present.Count == 0) return hands;
            var agg = MapAggregates(present, input.MapVersion);
            foreach (var h in present)
            {
                var x = h.Nn;
                var ap = AnglePair(h);
                var hf = HandFeatures(h);
                var ph = Physics(h);
                int n = x.Length;
                var nn = new float[n][];
                var tree = new float[n][];
                for (int k = 0; k < n; k++)
                {
                    nn[k] = x[k].Concat(ap[k]).Concat(hf[k]).Concat(ph[k]).Concat(agg).ToArray();
                    var row = new List<float>(TreeInputs);
                    row.AddRange(x[k]);
                    foreach (int o in Offsets)
                    {
                        int src = k + o;
                        if (src >= 0 && src < n)
                        {
                            row.AddRange(Neigh.Select(f => x[src][F[f]]));
                            row.Add(1);
                        }
                        else
                        {
                            row.AddRange(new float[Neigh.Length + 1]);
                        }
                    }
                    row.AddRange(ap[k]);
                    row.AddRange(hf[k]);
                    row.AddRange(agg);
                    tree[k] = row.ToArray();
                }
                h.Nn = nn;
                h.Tree = tree;
            }
            return hands;
        }

        static void BombGeometry(double[] x, (double T, double Y)[] bb, double[] bx, SwingInfo prev, SwingInfo cur,
            Func<double, double> mx, Func<int, int> mc, Func<double, double> ma)
        {
            double tailX = mx(prev.Tx), tailY = prev.Ty, headX = mx(cur.Hx), headY = cur.Hy;
            double? exitA = SwingAngle(mc(prev.Tc), prev.Pre >= 0 ? ma(prev.Pre) : null);
            double? entryA = SwingAngle(mc(cur.Hc), cur.Pre >= 0 ? ma(cur.Pre) : null);
            int m = bb.Length;
            bool[] nearTail = new bool[m], nearHead = new bool[m];
            for (int q = 0; q < m; q++)
            {
                nearTail[q] = Math.Sqrt((bx[q] - tailX) * (bx[q] - tailX) + (bb[q].Y - tailY) * (bb[q].Y - tailY)) <= 1.5;
                nearHead[q] = Math.Sqrt((bx[q] - headX) * (bx[q] - headX) + (bb[q].Y - headY) * (bb[q].Y - headY)) <= 1.5;
            }
            if (exitA is double ea)
            {
                double ux = Math.Cos(Radians(ea)), uy = Math.Sin(Radians(ea));
                x[F["bomb_exit_side"]] = Math.Log(1 + Enumerable.Range(0, m).Count(q => nearTail[q] && (bx[q] - tailX) * ux + (bb[q].Y - tailY) * uy > 0.2));
            }
            if (entryA is double na)
            {
                double ux = Math.Cos(Radians(na)), uy = Math.Sin(Radians(na));
                x[F["bomb_entry_side"]] = Math.Log(1 + Enumerable.Range(0, m).Count(q => nearHead[q] && (bx[q] - headX) * ux + (bb[q].Y - headY) * uy < -0.2));
            }
            double sx = headX - tailX, sy = headY - tailY, l2 = sx * sx + sy * sy;
            int path = 0;
            for (int q = 0; q < m; q++)
            {
                double tp = l2 > 1e-6 ? Math.Clamp(((bx[q] - tailX) * sx + (bb[q].Y - tailY) * sy) / l2, 0, 1) : 0;
                double dx = bx[q] - (tailX + tp * sx), dy = bb[q].Y - (tailY + tp * sy);
                if (Math.Sqrt(dx * dx + dy * dy) <= 0.75) path++;
            }
            x[F["bomb_path"]] = Math.Log(1 + path);
            x[F["bomb_above_tail"]] = Math.Log(1 + Enumerable.Range(0, m).Count(q => Math.Abs(bx[q] - tailX) <= 1 && bb[q].Y > tailY));
            x[F["bomb_below_tail"]] = Math.Log(1 + Enumerable.Range(0, m).Count(q => Math.Abs(bx[q] - tailX) <= 1 && bb[q].Y < tailY));
            x[F["bomb_above_head"]] = Math.Log(1 + Enumerable.Range(0, m).Count(q => Math.Abs(bx[q] - headX) <= 1 && bb[q].Y > headY));
            x[F["bomb_below_head"]] = Math.Log(1 + Enumerable.Range(0, m).Count(q => Math.Abs(bx[q] - headX) <= 1 && bb[q].Y < headY));
            double gap = cur.T0 - prev.T1;
            if (gap > 1e-4) x[F["bomb_time_frac"]] = bb.Average(b => (b.T - prev.T1) / gap);
        }

        static double? SwingAngle(int cut, double? pre) => cut < 8 ? CutAngle[cut] : pre is double p && p >= 0 ? p : null;

        // gbdt.angle_pair
        static float[][] AnglePair(HandInputs h)
        {
            var x = h.Nn;
            int n = x.Length;
            var o = new float[n][];
            o[0] = new float[AnglePairCount];
            for (int k = 1; k < n; k++)
            {
                var a = x[k - 1]; var b = x[k];
                float ps = a[F["tail_sin"]], pc = a[F["tail_cos"]], cs = b[F["head_sin"]], cc = b[F["head_cos"]];
                float prs = a[F["pre_sin"]], prc = a[F["pre_cos"]], qs = b[F["pre_sin"]], qc = b[F["pre_cos"]];
                o[k] =
                [
                    pc * cc + ps * cs,
                    ps * cc - pc * cs,
                    prc * qc + prs * qs,
                    h.TailCut[k - 1] == h.HeadCut[k] && h.HeadCut[k] < 8 ? 1 : 0,
                    b[F["head_x"]] - a[F["tail_x"]],
                    b[F["head_y"]] - a[F["tail_y"]],
                    h.HeadCut[k] == 8 || h.TailCut[k - 1] == 8 ? 1 : 0,
                ];
            }
            return o;
        }

        // numpy's sum of a float32 array (pairwise summation in float32, loops_utils.h pairwise_sum) and mean (that sum
        // divided in float32): the map and hand averages are inputs of trees whose thresholds are exact training values,
        // so they must be the same bits as Python's
        static float NpSum(float[] a, int start, int n)
        {
            if (n < 8)
            {
                float res = -0.0f;
                for (int i = 0; i < n; i++) res += a[start + i];
                return res;
            }
            if (n <= 128)
            {
                var r = new float[8];
                for (int j = 0; j < 8; j++) r[j] = a[start + j];
                int i = 8;
                for (; i < n - (n % 8); i += 8)
                    for (int j = 0; j < 8; j++) r[j] += a[start + i + j];
                float res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
                for (; i < n; i++) res += a[start + i];
                return res;
            }
            int n2 = n / 2;
            n2 -= n2 % 8;
            return NpSum(a, start, n2) + NpSum(a, start + n2, n - n2);
        }

        static float NpSum(float[] a) => NpSum(a, 0, a.Length);
        static float NpMean(float[] a) => NpSum(a) / a.Length;
        static float[] Column(IEnumerable<float[]> rows, string f) => rows.Select(r => r[F[f]]).ToArray();

        // gbdt.cut_share: float32 column sums added up in float32, divided in float32
        static float CutShare(IEnumerable<float[]> rows, int[] cuts)
        {
            var list = rows.ToList();
            float s = 0;
            foreach (int c in cuts) s += NpSum(Column(list, $"head_cut_{c}"));
            return s / Math.Max(list.Count, 1);
        }

        // gbdt.hand_features
        static float[][] HandFeatures(HandInputs h)
        {
            var x = h.Nn;
            int n = x.Length;
            float up = CutShare(x, Up), down = CutShare(x, Down), hor = CutShare(x, Horizontal);
            float dots = NpMean(Column(x, "pre_dot"));
            var o = Enumerable.Range(0, n).Select(_ => new float[] { up, down, hor, dots, 0, 0, 0 }).ToArray();
            int i = 0;
            while (i < n)
            {
                if (h.HeadCut[i] != 8) { i++; continue; }
                int j = i;
                while (j < n && h.HeadCut[j] == 8) j++;
                int run = j - i;
                for (int k = i; k < j; k++)
                {
                    o[k][4] = (float)Math.Log(run);
                    o[k][5] = (float)Math.Log(1 + (k - i));
                    o[k][6] = (float)Math.Log(1 + (run - 1 - (k - i)));
                }
                i = j;
            }
            return o;
        }

        // nn.physics: the current ParityPredictor's cost terms, from the map only
        static float[][] Physics(HandInputs h)
        {
            var x = h.Nn;
            int n = x.Length;
            var head = new double[n];
            var tail = new double[n];
            for (int k = 0; k < n; k++)
            {
                head[k] = Mod(h.HeadCut[k] < 8 ? Degrees(Math.Atan2(x[k][F["head_sin"]], x[k][F["head_cos"]]))
                                               : Degrees(Math.Atan2(x[k][F["pre_sin"]], x[k][F["pre_cos"]])), 360);
                tail[k] = Mod(h.TailCut[k] < 8 ? Degrees(Math.Atan2(x[k][F["tail_sin"]], x[k][F["tail_cos"]])) : head[k], 360);
            }
            static double Diff(double a, double b) => Math.Abs(Mod(a - b + 180, 360) - 180);
            var o = new float[n][];
            for (int k = 0; k < n; k++)
            {
                double gap = Math.Max(x[k][F["gap_s"]], 0.02);
                o[k] =
                [
                    (float)Math.Pow(Diff(head[k], ForehandNeutral) / 180, 2),
                    (float)Math.Pow(Diff(head[k], BackhandNeutral) / 180, 2),
                    k > 0 ? (float)Math.Pow(Diff(tail[k - 1], head[k]) / 180, 3) : 0,
                    k > 0 ? (float)Math.Pow(Diff(tail[k - 1] + 180, head[k]) / 180, 3) : 0,
                    (float)Math.Log(Math.Min(Math.Pow(0.2 / gap, 1.5), 100) + 0.01),
                ];
            }
            return o;
        }

        // gbdt.map_aggregates
        static float[] MapAggregates(List<HandInputs> hands, string version)
        {
            var X = hands.SelectMany(h => h.Nn).ToList();
            double same = 0, slow = 0, turn = 0, pairs = 0;
            foreach (var h in hands)
            {
                var ap = AnglePair(h);
                for (int k = 1; k < h.Nn.Length; k++)
                {
                    bool nob = h.Nn[k][F["bombs"]] == 0;
                    pairs++;
                    if (nob) same += ap[k][3];
                    if (nob && h.Nn[k][F["gap_beats"]] >= 1) slow += ap[k][3];
                    if (nob && ap[k][0] > 0.7) turn++;
                }
            }
            string v = string.IsNullOrEmpty(version) ? "2" : version;
            // The dataset keeps swing end times as float32
            double duration = Math.Max(hands.Max(h => h.Swings.Max(s => (double)(float)s.T1)), 1.0);
            return
            [
                NpMean(Column(X, "gap_beats_log")), NpMean(Column(X, "pre_dot")), NpMean(X.Select(r => r[F["head_cut_0"]] + r[F["head_cut_1"]]).ToArray()),
                NpMean(Column(X, "pattern")), (float)X.Average(r => r[F["bombs"]] > 0 ? 1.0 : 0.0), NpMean(Column(X, "njs")), NpMean(Column(X, "beat_s")),
                NpMean(Column(X, "dens_all_1s")), (float)(same / Math.Max(pairs, 1)), (float)(slow / Math.Max(pairs, 1)), (float)(turn / Math.Max(pairs, 1)),
                char.IsDigit(v[0]) ? v[0] - '0' : 2, (float)Math.Log(X.Count / duration),
                CutShare(X, Up), CutShare(X, Down), CutShare(X, Horizontal),
            ];
        }
    }
}
