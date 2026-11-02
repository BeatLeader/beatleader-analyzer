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
    /// </summary>
    public class XgbForest
    {
        struct Node
        {
            public int Left, Right, Feature;
            public float Threshold;
        }

        readonly double baseMargin;
        readonly Node[] nodes;
        readonly int[] roots;

        XgbForest(BinaryReader r)
        {
            baseMargin = r.ReadDouble();
            int trees = r.ReadInt32();
            roots = new int[trees];
            var all = new List<Node>();
            for (int t = 0; t < trees; t++)
            {
                int n = r.ReadInt32();
                int offset = all.Count;
                roots[t] = offset;
                for (int i = 0; i < n; i++)
                {
                    int left = r.ReadInt32(), right = r.ReadInt32(), feature = r.ReadInt32();
                    float threshold = r.ReadSingle();
                    all.Add(new Node
                    {
                        Left = left < 0 ? -1 : left + offset,
                        Right = left < 0 ? -1 : right + offset,
                        Feature = feature,
                        Threshold = threshold,
                    });
                }
            }
            nodes = all.ToArray();
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

        /// <summary>Logits of the probabilities of all the rows: one tree at a time over every row, so the tree's nodes stay
        /// in the cache (much faster than row by row on whole forests)</summary>
        public double[] Margins(float[][] rows)
        {
            var sums = new double[rows.Length];
            Array.Fill(sums, baseMargin);
            var n = nodes;
            foreach (int root in roots)
            {
                for (int i = 0; i < rows.Length; i++)
                {
                    var x = rows[i];
                    int node = root;
                    while (n[node].Left >= 0) node = x[n[node].Feature] < n[node].Threshold ? n[node].Left : n[node].Right;
                    sums[i] += n[node].Threshold;
                }
            }
            return sums;
        }
    }
}
