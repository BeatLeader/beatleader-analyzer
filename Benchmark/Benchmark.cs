﻿using Analyzer.BeatmapScanner.Algorithm;
using beatleader_analyzer;
using beatleader_parser;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Parser.Map;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Benchmark
{
    [MemoryDiagnoser]
    [SimpleJob(RuntimeMoniker.Net481)]
    [SimpleJob(RuntimeMoniker.Net80)]
    public class Benchmark
    {
        [ParamsAllValues]
        public bool UseParallelFor { get; set; }
        BeatmapV3 map;
        Analyze analyzer;

        [GlobalSetup]
        public void Globalsetup()
        {
            map = new Parse().TryDownloadLink(@"https://r2cdn.beatsaver.com/417f22a92dc4efb0750a4ea538e45eaf50ce628b.zip").Last();
            analyzer = new Analyze();
        }

        [Benchmark]
        public void GetRating()
        {
            SwingCurve.UseParallel = UseParallelFor;
            analyzer.GetRating(map, "Standard");
        }
    }
}
