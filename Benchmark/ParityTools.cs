#nullable enable
using Analyzer.BeatmapScanner.Algorithm;
using Analyzer.BeatmapScanner.Parity;
using beatleader_analyzer;
using beatleader_parser;
using Parser.Map.Difficulty.V3.Base;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Benchmark
{
    // Tools of the Analyzer's parity model (BeatmapScanner/Parity/MlParityModel.cs). The model is trained in
    // beatsaber-replays-ai-2 (paritymodel/, replays from BLReplaysPrepare parity3); these are the Analyzer's side:
    //
    //   Benchmark paritymaps <parity3 folder> <output folder> [--maps <map cache>] [--force]
    //       The model's map data: every leaderboard of the parity3 folder as the Analyzer sees it, with ParityPredictor
    //       (the model's training data has the Analyzer's swings before any merge, and the old parity as its baseline).
    //   Benchmark parityml-verify <parity_reference.json.gz> <parity3-maps folder> [--maps <map cache>]
    //       MlParityModel against paritymodel/production.py's reference maps: every feature, score and decoded parity.
    //   Benchmark paritybench <parity3-maps folder> [count] [--maps <map cache>]
    //       Analyzer time per map with ParityPredictor and with MlParityModel, single threaded.
    //
    // Maps come from the map cache folder (default "maps": extracted maps in folders named by hash, as portaBLe's cache,
    // or <hash>.zip), downloaded from BeatSaver into it when missing.
    internal static class ParityTools
    {
        public static void Run(string[] args)
        {
            string? maps = Option(ref args, "--maps");
            bool force = Flag(ref args, "--force");
            var source = new MapSource(maps ?? "maps");
            switch (args.Length > 0 ? args[0] : "")
            {
                case "paritymaps" when args.Length >= 3:
                    ExportMaps(args[1], args[2], source, force).GetAwaiter().GetResult();
                    break;
                case "parityml-verify" when args.Length >= 3:
                    Verify(args[1], args[2], source).GetAwaiter().GetResult();
                    break;
                case "paritybench" when args.Length >= 2:
                    Bench(args[1], args.Length > 2 ? int.Parse(args[2]) : 200, source).GetAwaiter().GetResult();
                    break;
                default:
                    Console.WriteLine("Usage: paritymaps <parity3 folder> <output folder> [--maps <map cache>] [--force]");
                    Console.WriteLine("       parityml-verify <parity_reference.json.gz> <parity3-maps folder> [--maps <map cache>]");
                    Console.WriteLine("       paritybench <parity3-maps folder> [count] [--maps <map cache>]");
                    break;
            }
        }

        public static bool IsCommand(string command) => command is "paritymaps" or "parityml-verify" or "paritybench";

        static string? Option(ref string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            if (i < 0 || i + 1 >= args.Length) return null;
            string value = args[i + 1];
            args = args.Where((_, k) => k != i && k != i + 1).ToArray();
            return value;
        }

        static bool Flag(ref string[] args, string name)
        {
            if (!args.Contains(name)) return false;
            args = args.Where(a => a != name).ToArray();
            return true;
        }

        static int DiffCode(string difficulty) => difficulty switch
        {
            "Easy" => 1, "Normal" => 3, "Hard" => 5, "Expert" => 7, _ => 9,
        };

        // A difficulty of a map by hash: the map cache's extracted folder or zip, downloaded from BeatSaver when missing
        sealed class MapSource
        {
            static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
            readonly string folder;
            public MapSource(string folder) { this.folder = folder; }

            public async Task<(DifficultyV3 Data, float Bpm, string Name, string Mapper)?> Load(string hash, string mode, string difficulty)
            {
                var mapset = await LoadMapset(hash);
                var set = mapset?.Difficulties.FirstOrDefault(d => d.Characteristic == mode
                    && (d.BeatMap._difficulty == difficulty || d.BeatMap._difficultyRank == DiffCode(difficulty)));
                if (set == null) return null;
                return (set.Data, (float)mapset!.Info._beatsPerMinute, mapset.Info._songName ?? "", mapset.Info._levelAuthorName ?? "");
            }

            async Task<Parser.Map.BeatmapV3?> LoadMapset(string hash)
            {
                foreach (var name in new[] { hash.ToLowerInvariant(), hash.ToUpperInvariant() })
                {
                    string dir = Path.Combine(folder, name);
                    if (Directory.Exists(dir)) return new Parse().TryLoadPath(dir);
                }
                string zip = Path.Combine(folder, hash.ToLowerInvariant() + ".zip");
                if (!File.Exists(zip))
                {
                    try
                    {
                        var bytes = await http.GetByteArrayAsync($"https://r2cdn.beatsaver.com/{hash.ToLowerInvariant()}.zip");
                        Directory.CreateDirectory(folder);
                        string tmp = zip + ".tmp";
                        await File.WriteAllBytesAsync(tmp, bytes);
                        File.Move(tmp, zip, true);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Map {hash}: download failed ({e.Message})");
                        return null;
                    }
                }
                return new Parse().TryLoadZip(new MemoryStream(await File.ReadAllBytesAsync(zip)));
            }
        }

        static JsonDocument ReadGzipJson(string path) => JsonDocument.Parse(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));

        // (hash, difficulty, mode) of a parity3-maps file
        static (string Hash, string Difficulty, string Mode) MapKey(string mapsFile)
        {
            using var doc = ReadGzipJson(mapsFile);
            var r = doc.RootElement;
            return (r.GetProperty("Hash").GetString()!, r.GetProperty("Difficulty").GetString()!, r.GetProperty("Mode").GetString()!);
        }

        static double R(double value) => double.IsFinite(value) ? Math.Round(value, 4) : 0;

        // ------------------------------------------------------------------
        // paritymaps
        // ------------------------------------------------------------------

        static readonly string[] NoteColumns =
        {
            "Seconds", "Beats", "BpmTime", "X", "Y", "Color", "Cut", "AngleOffset", "Njs",
            "Chain", "ChainTailX", "ChainTailY", "ChainSquish",
            // Analyzer: index of the note's swing in Swings (-1 when in none), pattern flags, parity and directions
            "Swing", "Head", "Tail", "Pattern", "Forehand", "ParityErrors", "BombAvoidance", "Direction", "ParityDirection",
        };

        static readonly string[] SwingColumns =
        {
            "Hand", "FirstNote", "Notes", "Direction", "Forehand", "ParityErrors", "BombAvoidance", "AngleStrain", "RotationAmount",
            "SwingTech", "ArcOffset", "RepositioningDistance", "SwingFrequency", "IsLinear",
        };

        class MapFile
        {
            public int Version { get; set; } = 1;
            public string LeaderboardId { get; set; } = "";
            public string Hash { get; set; } = "";
            public string Name { get; set; } = "";
            public string Mapper { get; set; } = "";
            public string Difficulty { get; set; } = "";
            public string Mode { get; set; } = "";
            public string MapVersion { get; set; } = "";
            public float Bpm { get; set; }
            public double Pass { get; set; }
            public double Tech { get; set; }
            public string[] NoteColumns { get; set; } = ParityTools.NoteColumns;
            public string[] SwingColumns { get; set; } = ParityTools.SwingColumns;
            public List<double[]> Notes { get; set; } = new();
            public List<double[]> Swings { get; set; } = new();
            // [seconds, beats, x, y]
            public List<double[]> Bombs { get; set; } = new();
            // [seconds, duration seconds, x, y, width, height]
            public List<double[]> Walls { get; set; } = new();
            // [seconds, color, x, y, cut, tail x, tail y, slice count, squish]
            public List<double[]> Chains { get; set; } = new();
        }

        // Only the header of a parity3 file
        class Parity3Header
        {
            public string LeaderboardId { get; set; } = "";
            public List<ReplayHeader> Replays { get; set; } = new();
        }

        class ReplayHeader
        {
            public string Hash { get; set; } = "";
            public string Difficulty { get; set; } = "";
            public string Mode { get; set; } = "";
        }

        static async Task ExportMaps(string parityFolder, string outputFolder, MapSource source, bool force)
        {
            var timer = Stopwatch.StartNew();
            Directory.CreateDirectory(outputFolder);
            var files = Directory.EnumerateFiles(parityFolder, "*.json.gz").ToList();
            Console.WriteLine($"Parity maps: {files.Count} parity3 files -> {outputFolder}");
            int written = 0, skipped = 0, failed = 0;
            var parityModel = ParityModel.Current;
            ParityModel.Current = null;
            try
            {
                await Parallel.ForEachAsync(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, async (file, ct) =>
                {
                    string id = Path.GetFileName(file)[..^".json.gz".Length];
                    string output = Path.Combine(outputFolder, $"{id}.json.gz");
                    if (!force && File.Exists(output))
                    {
                        Interlocked.Increment(ref skipped);
                        return;
                    }
                    try
                    {
                        var map = await BuildMap(file, id, source);
                        if (map == null)
                        {
                            Interlocked.Increment(ref failed);
                            return;
                        }
                        string tmp = output + ".tmp";
                        await using (var stream = new GZipStream(File.Create(tmp), CompressionLevel.Optimal))
                        {
                            await JsonSerializer.SerializeAsync(stream, map, cancellationToken: ct);
                        }
                        File.Move(tmp, output, true);
                        int n = Interlocked.Increment(ref written);
                        if (n % 250 == 0) Console.WriteLine($"Parity maps: {n} written ({timer.Elapsed:mm\\:ss})");
                    }
                    catch (Exception e)
                    {
                        Interlocked.Increment(ref failed);
                        Console.WriteLine($"Parity maps: {id} failed ({e.Message})");
                    }
                });
            }
            finally
            {
                ParityModel.Current = parityModel;
            }
            Console.WriteLine($"Parity maps: {written} written, {skipped} already there, {failed} failed ({timer.Elapsed:mm\\:ss})");
        }

        sealed class ByReference<T> : IEqualityComparer<T> where T : class
        {
            public bool Equals(T? a, T? b) => ReferenceEquals(a, b);
            public int GetHashCode(T o) => RuntimeHelpers.GetHashCode(o);
        }

        static async Task<MapFile?> BuildMap(string parityFile, string id, MapSource source)
        {
            Parity3Header? header;
            await using (var stream = new GZipStream(File.OpenRead(parityFile), CompressionMode.Decompress))
            {
                header = await JsonSerializer.DeserializeAsync<Parity3Header>(stream);
            }
            if (header == null || header.Replays.Count == 0) return null;
            // The map version most replays were played on
            var played = header.Replays.Where(r => r.Hash.Length > 0)
                .GroupBy(r => (Hash: r.Hash.ToLowerInvariant(), r.Difficulty, r.Mode))
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key;
            if (played == null) return null;
            var (hash, difficulty, mode) = played.Value;
            if (mode.Length == 0) mode = "Standard";

            var loaded = await source.Load(hash, mode, difficulty);
            if (loaded == null)
            {
                Console.WriteLine($"Parity maps: {id}: no {mode} {difficulty} in map {hash}");
                return null;
            }
            var (data, bpm, name, mapper) = loaded.Value;
            var ratings = new Analyze().GetRating(data, mode, difficulty, bpm);
            if (ratings == null) return null;

            var map = new MapFile
            {
                LeaderboardId = id,
                Hash = hash,
                Name = name,
                Mapper = mapper,
                Difficulty = difficulty,
                Mode = mode,
                MapVersion = data.Version ?? "",
                Bpm = bpm,
                Pass = R(ratings.PassRating),
                Tech = R(ratings.TechRating),
            };

            // Notes in the Analyzer's order (BeatmapScanner.Analyzer); swings in time order, red first at equal times
            var notes = data.Notes.ToList();
            notes.Sort((a, b) =>
            {
                int cmp = a.BpmTime.CompareTo(b.BpmTime);
                if (cmp == 0) cmp = a.Color.CompareTo(b.Color);
                if (cmp == 0) cmp = a.x.CompareTo(b.x);
                if (cmp == 0) cmp = a.y.CompareTo(b.y);
                if (cmp == 0) cmp = a.CutDirection.CompareTo(b.CutDirection);
                return cmp;
            });
            var index = new Dictionary<Parser.Map.Difficulty.V3.Grid.Note, int>(new ByReference<Parser.Map.Difficulty.V3.Grid.Note>());
            for (int i = 0; i < notes.Count; i++) index[notes[i]] = i;

            var rows = notes.Select(n => new double[]
            {
                R(n.Seconds), R(n.Beats), R(n.BpmTime), n.x, n.y, n.Color, n.CutDirection, n.AngleOffset, R(n.njs),
                0, 0, 0, 0, -1, 0, 0, 0, 0, 0, 0, -1, -1,
            }).ToList();

            var swings = ratings.SwingData.OrderBy(s => s.Cubes[0].Seconds).ThenBy(s => s.Cubes[0].Type).ToList();
            for (int s = 0; s < swings.Count; s++)
            {
                var swing = swings[s];
                int first = -1;
                foreach (var cube in swing.Cubes)
                {
                    if (cube.Note == null || !index.TryGetValue(cube.Note, out int i)) continue;
                    if (first < 0) first = i;
                    var row = rows[i];
                    row[9] = cube.Chain ? 1 : 0;
                    row[10] = cube.TailLine;
                    row[11] = cube.TailLayer;
                    row[12] = R(cube.Squish);
                    row[13] = s;
                    row[14] = cube.Head ? 1 : 0;
                    row[15] = cube.Tail ? 1 : 0;
                    row[16] = cube.Pattern ? 1 : 0;
                    row[17] = swing.Forehand ? 1 : 0;
                    row[18] = swing.ParityErrors ? 1 : 0;
                    row[19] = cube.BombAvoidance ? 1 : 0;
                    row[20] = R(swing.Direction);
                    row[21] = R(cube.ParityDirection);
                }
                map.Swings.Add(new double[]
                {
                    swing.Cubes[0].Type, first, swing.Cubes.Count, R(swing.Direction), swing.Forehand ? 1 : 0, swing.ParityErrors ? 1 : 0,
                    swing.BombAvoidance ? 1 : 0, R(swing.AngleStrain), R(swing.RotationAmount), R(swing.SwingTech), R(swing.ArcOffset),
                    R(swing.RepositioningDistance), R(swing.SwingFrequency), swing.IsLinear ? 1 : 0,
                });
            }
            map.Notes = rows;
            map.Bombs = data.Bombs.OrderBy(b => b.BpmTime).Select(b => new double[] { R(b.Seconds), R(b.Beats), b.x, b.y }).ToList();
            map.Walls = data.Walls.OrderBy(w => w.BpmTime).Select(w => new double[] { R(w.Seconds), R(w.DurationInSeconds), w.x, w.y, w.Width, w.Height }).ToList();
            map.Chains = data.Chains.OrderBy(c => c.BpmTime).Select(c => new double[]
            {
                R(c.Seconds), c.Color, c.x, c.y, c.CutDirection, c.tx, c.ty, c.SliceCount, R(c.Squish),
            }).ToList();
            return map;
        }

        // ------------------------------------------------------------------
        // parityml-verify
        // ------------------------------------------------------------------

        static async Task Verify(string referencePath, string mapsFolder, MapSource source)
        {
            using var doc = ReadGzipJson(referencePath);
            var features = ParityFeatures.Swing;
            double worstNn = 0, worstTree = 0, worstScore = 0, worstTreeOut = 0, dirDiff = 0;
            string worstNnWhere = "", worstTreeWhere = "", worstTreeOutWhere = "";
            int hands = 0, swings = 0, parityDiff = 0, mergeDiff = 0, countDiff = 0, treeOff = 0;
            long notBitEqual = 0, inputs = 0;
            var columnDiffs = new Dictionary<int, (int Count, string Example)>();
            var previous = ParityModel.Current;
            ParityModel.Current = new MlParityModel();
            try
            {
                foreach (var map in doc.RootElement.GetProperty("maps").EnumerateArray())
                {
                    string id = map.GetProperty("id").GetString()!;
                    var (hash, difficulty, mode) = MapKey(Path.Combine(mapsFolder, $"{id}.json.gz"));
                    var loaded = await source.Load(hash, mode, difficulty);
                    if (loaded == null) { Console.WriteLine($"{id}: map not found"); continue; }
                    MlParityModel.HandResult?[]? got = null;
                    MlParityModel.Debug = r => got = r;
                    new Analyze().GetRating(loaded.Value.Data, mode, difficulty, loaded.Value.Bpm);
                    MlParityModel.Debug = null;
                    if (got == null) { Console.WriteLine($"{id}: model not called"); continue; }

                    var refHands = map.GetProperty("hands");
                    for (int hand = 0; hand < 2; hand++)
                    {
                        var rh = refHands[hand];
                        var g = got[hand];
                        if (rh.ValueKind == JsonValueKind.Null || g == null)
                        {
                            if ((rh.ValueKind == JsonValueKind.Null) != (g == null)) { Console.WriteLine($"{id} hand {hand}: present in one only"); countDiff++; }
                            continue;
                        }
                        var refNn = Matrix(rh.GetProperty("nn_in"));
                        if (refNn.Length != g.Inputs.Nn.Length)
                        {
                            Console.WriteLine($"{id} hand {hand}: {g.Inputs.Nn.Length} swings, reference {refNn.Length}");
                            countDiff++;
                            continue;
                        }
                        hands++;
                        swings += refNn.Length;
                        Compare(refNn, g.Inputs.Nn, ref worstNn, ref worstNnWhere, $"{id} hand {hand} nn", c => c < features.Length ? features[c] : $"extra {c - features.Length}");
                        var refTree = Matrix(rh.GetProperty("tree_in"));
                        Compare(refTree, g.Inputs.Tree, ref worstTree, ref worstTreeWhere, $"{id} hand {hand} tree", c => c < features.Length ? features[c] : $"tree {c}");
                        // The trees split on exact float32 values (down to sin / cos rounding noise): inputs that differ at all
                        for (int t = 0; t < refTree.Length; t++)
                        {
                            for (int c = 0; c < refTree[t].Length; c++)
                            {
                                inputs++;
                                if ((float)refTree[t][c] == g.Inputs.Tree[t][c]) continue;
                                notBitEqual++;
                                columnDiffs.TryGetValue(c, out var cd);
                                columnDiffs[c] = (cd.Count + 1, cd.Example ?? $"{id} hand {hand} swing {t}: {(float)refTree[t][c]:R} vs {g.Inputs.Tree[t][c]:R}");
                            }
                        }
                        var refUnary = Matrix(rh.GetProperty("unary"));
                        var refStay = Vector(rh.GetProperty("stay"));
                        var refMerge = Vector(rh.GetProperty("merge"));
                        for (int t = 0; t < refStay.Length; t++)
                        {
                            worstScore = Math.Max(worstScore, Math.Max(Math.Abs(refUnary[t][0] - g.Unary[t][0]), Math.Abs(refUnary[t][1] - g.Unary[t][1])));
                            worstScore = Math.Max(worstScore, Math.Max(Math.Abs(refStay[t] - g.Stay[t]), Math.Abs(refMerge[t] - g.Merge[t])));
                        }
                        var refFh = Vector(rh.GetProperty("tree_fh"));
                        var refReset = Vector(rh.GetProperty("tree_reset"));
                        for (int t = 0; t < refFh.Length; t++)
                        {
                            double d = Math.Max(Math.Abs(refFh[t] - g.TreeForehand[t]), Math.Abs(refReset[t] - g.TreeReset[t]));
                            if (d > 0.01) treeOff++;
                            if (d > worstTreeOut) { worstTreeOut = d; worstTreeOutWhere = $"{id} hand {hand} swing {t}"; }
                        }
                        var refParity = Vector(rh.GetProperty("parity"));
                        var refMerged = Vector(rh.GetProperty("merged"));
                        var refDir = Vector(rh.GetProperty("direction"));
                        for (int t = 0; t < refParity.Length; t++)
                        {
                            if ((int)refParity[t] != g.Parity[t]) parityDiff++;
                            if ((refMerged[t] != 0) != g.Merged[t]) mergeDiff++;
                            dirDiff = Math.Max(dirDiff, Math.Abs(((refDir[t] - g.Direction[t]) % 360 + 540) % 360 - 180));
                        }
                    }
                }
            }
            finally
            {
                ParityModel.Current = previous;
            }
            Console.WriteLine($"parityml-verify: {hands} hands, {swings} swings; hands not matching {countDiff}");
            Console.WriteLine($"  network inputs: max abs diff {worstNn:G4} ({worstNnWhere})");
            Console.WriteLine($"  tree inputs: max abs diff {worstTree:G4} ({worstTreeWhere}); not bit-equal {notBitEqual} of {inputs}");
            foreach (var (c, (count, example)) in columnDiffs.OrderByDescending(kv => kv.Value.Count).Take(12))
                Console.WriteLine($"    tree column {c}: {count} differ, e.g. {example}");
            Console.WriteLine($"  network scores: max abs diff {worstScore:G4}; tree margins: max abs diff {worstTreeOut:G4} ({worstTreeOutWhere}), off by more than 0.01 on {treeOff} swings");
            Console.WriteLine($"  decoded parity differs on {parityDiff} swings, merges on {mergeDiff}; direction max diff {dirDiff:G4} deg");
        }

        static void Compare(double[][] reference, float[][] got, ref double worst, ref string where, string label, Func<int, string> name)
        {
            for (int t = 0; t < reference.Length; t++)
            {
                for (int c = 0; c < reference[t].Length; c++)
                {
                    double d = Math.Abs(reference[t][c] - got[t][c]);
                    if (d > worst) { worst = d; where = $"{label} swing {t} {name(c)}: reference {reference[t][c]:G6}, C# {got[t][c]:G6}"; }
                }
            }
        }

        static double[][] Matrix(JsonElement e) => e.EnumerateArray().Select(Vector).ToArray();
        static double[] Vector(JsonElement e) => e.EnumerateArray().Select(v => v.GetDouble()).ToArray();

        // ------------------------------------------------------------------
        // paritybench
        // ------------------------------------------------------------------

        static async Task Bench(string mapsFolder, int count, MapSource source)
        {
            var maps = new List<(DifficultyV3 Data, string Mode, string Difficulty, float Bpm)>();
            foreach (var file in Directory.EnumerateFiles(mapsFolder, "*.json.gz").OrderBy(f => f))
            {
                if (maps.Count >= count) break;
                var (hash, difficulty, mode) = MapKey(file);
                var loaded = await source.Load(hash, mode, difficulty);
                if (loaded != null) maps.Add((loaded.Value.Data, mode, difficulty, loaded.Value.Bpm));
            }
            Console.WriteLine($"paritybench: {maps.Count} maps, {maps.Sum(m => m.Data.Notes.Count)} notes, single threaded");

            var previous = ParityModel.Current;
            bool cache = MlParityModel.UseCache;
            MlParityModel.UseCache = false;
            try
            {
                var ml = new MlParityModel();
                // Warm up (model loading, JIT) on a few maps with both
                foreach (var m in maps.Take(5))
                {
                    ParityModel.Current = null; new Analyze().GetRating(m.Data, m.Mode, m.Difficulty, m.Bpm);
                    ParityModel.Current = ml; new Analyze().GetRating(m.Data, m.Mode, m.Difficulty, m.Bpm);
                }
                double Time(IParityModel? model)
                {
                    ParityModel.Current = model;
                    var sw = Stopwatch.StartNew();
                    foreach (var m in maps) new Analyze().GetRating(m.Data, m.Mode, m.Difficulty, m.Bpm);
                    return sw.Elapsed.TotalMilliseconds;
                }
                double dp = Time(null);
                MlParityModel.ResetTimings();
                double mlTime = Time(ml);
                var (features, network, trees, decode) = MlParityModel.Timings;
                Console.WriteLine($"  ParityPredictor: {dp / maps.Count,8:F1} ms per map");
                Console.WriteLine($"  MlParityModel:   {mlTime / maps.Count,8:F1} ms per map ({mlTime / dp:F1}x)");
                Console.WriteLine($"    features {features.TotalMilliseconds / maps.Count:F1} ms, network {network.TotalMilliseconds / maps.Count:F1} ms, " +
                                  $"trees {trees.TotalMilliseconds / maps.Count:F1} ms, decoding {decode.TotalMilliseconds / maps.Count:F1} ms per map; " +
                                  $"the rest of the Analyzer {(mlTime - (features + network + trees + decode).TotalMilliseconds) / maps.Count:F1} ms");
                var applied = beatleader_analyzer.BeatmapScanner.Helper.ModelDirections.Applied;
                var clamped = beatleader_analyzer.BeatmapScanner.Helper.ModelDirections.Clamped;
                Console.WriteLine($"  model directions: {applied} swings, {clamped} clamped to their arrow's tolerance ({100.0 * clamped / Math.Max(applied, 1):F3}%)");
            }
            finally
            {
                ParityModel.Current = previous;
                MlParityModel.UseCache = cache;
            }
        }
    }
}
