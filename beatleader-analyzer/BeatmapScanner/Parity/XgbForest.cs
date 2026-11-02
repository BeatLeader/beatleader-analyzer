#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.IO.Compression;

namespace Analyzer.BeatmapScanner.Parity
{
    /// <summary>
    /// XGBoost binary:logistic forests exported by paritymodel/production.py (write_trees): parity_trees.gzbin (gzip; not
    /// .gz, which ASP.NET projects leave out of the resources), little endian: int32 forest count; per forest float64 base
    /// margin, int32 tree count; per tree int32 node count, then per node int32 left, int32 right, int32 feature, float32
    /// threshold (leaf value on leaves, where left is -1). x &lt; threshold goes left, as XGBoost (the features are never
    /// missing). Every tree's nodes are kept in one array per forest (children offset to it), which walks faster.
    /// XGBoost puts the right child right after the left one, so the walk is branchless: child = left + (x &lt; threshold ?
    /// 0 : 1), the same number of steps (the tree's depth) for every row. A leaf goes back to itself (left = itself - 1,
    /// threshold NaN, which never goes left), so rows that reach a leaf early stay there. A node is 8 bytes: left child x 256
    /// + feature (fewer than 256 features), threshold.
    /// </summary>
    public class XgbForest
    {
        struct Node
        {
            public int LeftFeature;
            public float Threshold;
        }

        readonly double baseMargin;
        readonly Node[] nodes;
        readonly float[] value;
        readonly int[] roots, depths;

        XgbForest(BinaryReader r)
        {
            baseMargin = r.ReadDouble();
            int trees = r.ReadInt32();
            roots = new int[trees];
            depths = new int[trees];
            var lefts = new List<int>();
            var features = new List<int>();
            var thresholds = new List<float>();
            var values = new List<float>();
            for (int t = 0; t < trees; t++)
            {
                int n = r.ReadInt32();
                int offset = lefts.Count;
                roots[t] = offset;
                var depth = new int[n];
                for (int i = 0; i < n; i++)
                {
                    int l = r.ReadInt32(), right = r.ReadInt32(), f = r.ReadInt32();
                    float threshold = r.ReadSingle();
                    if (l < 0)
                    {
                        lefts.Add(offset + i - 1);
                        features.Add(0);
                        thresholds.Add(float.NaN);
                        values.Add(threshold);
                        continue;
                    }
                    if (right != l + 1) throw new InvalidDataException("XgbForest: a right child isn't right after the left one");
                    if (f is < 0 or > 255) throw new InvalidDataException("XgbForest: feature index over 255");
                    depth[l] = depth[right] = depth[i] + 1;
                    depths[t] = Math.Max(depths[t], depth[l]);
                    lefts.Add(l + offset);
                    features.Add(f);
                    thresholds.Add(threshold);
                    values.Add(0);
                }
            }
            nodes = new Node[lefts.Count];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = new Node { LeftFeature = lefts[i] * 256 + features[i], Threshold = thresholds[i] };
            value = values.ToArray();
        }

        public static List<XgbForest> Load(byte[] gzipped)
        {
            using var gz = new GZipStream(new MemoryStream(gzipped), CompressionMode.Decompress);
            using var r = new BinaryReader(gz);
            int count = r.ReadInt32();
            var forests = new List<XgbForest>(count);
            for (int i = 0; i < count; i++) forests.Add(new XgbForest(r));
            return forests;
        }

        // Rows walked together through every tree: their nodes stay in the cache while the block's rows go through them
        const int Block = 64;

        /// <summary>Logits of the probabilities of all the rows</summary>
        public double[] Margins(float[][] rows)
        {
            var sums = new double[rows.Length];
            Array.Fill(sums, baseMargin);
            var n = nodes;
            for (int start = 0; start < rows.Length; start += Block)
            {
                int end = Math.Min(start + Block, rows.Length);
                for (int t = 0; t < roots.Length; t++)
                {
                    int root = roots[t], depth = depths[t];
                    int i = start;
                    // Four rows at a time: their walks are independent, so the CPU overlaps them
                    for (; i + 4 <= end; i += 4)
                    {
                        float[] x0 = rows[i], x1 = rows[i + 1], x2 = rows[i + 2], x3 = rows[i + 3];
                        int n0 = root, n1 = root, n2 = root, n3 = root;
                        for (int d = 0; d < depth; d++)
                        {
                            Node a = n[n0], b = n[n1], c = n[n2], e = n[n3];
                            n0 = (a.LeftFeature >> 8) + (x0[a.LeftFeature & 255] < a.Threshold ? 0 : 1);
                            n1 = (b.LeftFeature >> 8) + (x1[b.LeftFeature & 255] < b.Threshold ? 0 : 1);
                            n2 = (c.LeftFeature >> 8) + (x2[c.LeftFeature & 255] < c.Threshold ? 0 : 1);
                            n3 = (e.LeftFeature >> 8) + (x3[e.LeftFeature & 255] < e.Threshold ? 0 : 1);
                        }
                        sums[i] += value[n0]; sums[i + 1] += value[n1]; sums[i + 2] += value[n2]; sums[i + 3] += value[n3];
                    }
                    for (; i < end; i++)
                    {
                        var x = rows[i];
                        int node = root;
                        for (int d = 0; d < depth; d++)
                        {
                            Node a = n[node];
                            node = (a.LeftFeature >> 8) + (x[a.LeftFeature & 255] < a.Threshold ? 0 : 1);
                        }
                        sums[i] += value[node];
                    }
                }
            }
            return sums;
        }
    }
}
