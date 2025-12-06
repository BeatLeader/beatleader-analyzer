using BenchmarkDotNet.Running;
using System;
using System.Linq;

namespace Benchmark
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].ToLower() == "export")
            {
                RunExporter(args.Skip(1).ToArray());
                return;
            }

            if (args.Length == 0)
            {
                ShowMainMenu();
                return;
            }

#if DEBUG
            Console.WriteLine("Unknown command. Use 'export' to export analysis.");
#else
            BenchmarkRunner.Run<Benchmark>();
#endif
        }

        static void ShowMainMenu()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("╔════════════════════════════════════════════════╗");
                Console.WriteLine("║   Beat Saber Analyzer - Main Menu             ║");
                Console.WriteLine("╚════════════════════════════════════════════════╝");
                Console.WriteLine();
                Console.WriteLine("  [1] Export Analysis (All Difficulties)");
                Console.WriteLine("  [2] Export Detailed Swing Data");
                Console.WriteLine("  [3] Run Benchmark");
                Console.WriteLine("  [4] Help & Documentation");
                Console.WriteLine("  [5] Exit");
                Console.WriteLine();
                Console.Write("Select an option (1-5): ");

                var choice = Console.ReadLine()?.Trim();

                switch (choice)
                {
                    case "1":
                        RunExportAnalysis();
                        break;
                    case "2":
                        RunExportDetailedSwings();
                        break;
                    case "3":
                        RunBenchmarkMenu();
                        break;
                    case "4":
                        ShowExporterHelp();
                        Console.WriteLine();
                        Console.WriteLine("Press any key to return to menu...");
                        Console.ReadKey();
                        break;
                    case "5":
                        return;
                    default:
                        Console.WriteLine("Invalid option. Press any key to continue...");
                        Console.ReadKey();
                        break;
                }
            }
        }

        static void RunExportAnalysis()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   Export Analysis (All Difficulties)          ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.Write("Enter BeatSaver URL: ");
            string url = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("Error: URL cannot be empty.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.Write("Enter output path (or press Enter for 'analysis.html'): ");
            string outputPath = Console.ReadLine()?.Trim();

            Console.WriteLine();
            Console.WriteLine("Starting analysis...");
            Console.WriteLine();

            var exporter = new AnalysisExporter();
            exporter.ExportFromUrl(url, string.IsNullOrEmpty(outputPath) ? null : outputPath);

            Console.WriteLine();
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }

        static void RunExportDetailedSwings()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   Export Detailed Swing Data                  ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.Write("Enter BeatSaver URL: ");
            string url = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("Error: URL cannot be empty.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Available Characteristics:");
            Console.WriteLine("  - Standard");
            Console.WriteLine("  - OneSaber");
            Console.WriteLine("  - NoArrows");
            Console.WriteLine("  - 90Degree");
            Console.WriteLine("  - 360Degree");
            Console.WriteLine("  - Lawless");
            Console.WriteLine();
            Console.Write("Enter Characteristic: ");
            string characteristic = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(characteristic))
            {
                Console.WriteLine("Error: Characteristic cannot be empty.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Available Difficulties:");
            Console.WriteLine("  - Easy");
            Console.WriteLine("  - Normal");
            Console.WriteLine("  - Hard");
            Console.WriteLine("  - Expert");
            Console.WriteLine("  - ExpertPlus");
            Console.WriteLine();
            Console.Write("Enter Difficulty: ");
            string difficulty = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(difficulty))
            {
                Console.WriteLine("Error: Difficulty cannot be empty.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.Write("Enter output path (or press Enter for 'detailed_swings.html'): ");
            string outputPath = Console.ReadLine()?.Trim();

            Console.WriteLine();
            Console.WriteLine("Starting detailed analysis...");
            Console.WriteLine();

            var exporter = new AnalysisExporter();
            exporter.ExportDetailedSwingData(url, characteristic, difficulty, string.IsNullOrEmpty(outputPath) ? null : outputPath);

            Console.WriteLine();
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }

        static void RunBenchmarkMenu()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   Run Benchmark                                ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("  [1] Continuous Debug Benchmark (Press Ctrl+C to stop)");
            Console.WriteLine("  [2] Official BenchmarkDotNet Suite");
            Console.WriteLine("  [3] Back to Main Menu");
            Console.WriteLine();
            Console.Write("Select an option (1-3): ");

            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    RunContinuousBenchmark();
                    break;
                case "2":
#if DEBUG
                    Console.WriteLine();
                    Console.WriteLine("BenchmarkDotNet suite requires Release mode.");
                    Console.WriteLine("Please build and run with: dotnet run -c Release --framework net9.0");
                    Console.WriteLine();
                    Console.WriteLine("Press any key to continue...");
                    Console.ReadKey();
#else
                    Console.WriteLine();
                    Console.WriteLine("Starting BenchmarkDotNet suite...");
                    BenchmarkRunner.Run<Benchmark>();
                    Console.WriteLine("Press any key to return to menu...");
                    Console.ReadKey();
#endif
                    break;
                case "3":
                    return;
                default:
                    Console.WriteLine("Invalid option. Press any key to continue...");
                    Console.ReadKey();
                    break;
            }
        }

        static void RunContinuousBenchmark()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   Continuous Benchmark Mode                    ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("Running continuous benchmark...");
            Console.WriteLine("Press Ctrl+C to stop");
            Console.WriteLine();

            var bench = new Benchmark();
            bench.Globalsetup();
            int iterations = 0;
            var startTime = DateTime.Now;

            try
            {
                while (true)
                {
                    bench.GetRating();
                    iterations++;

                    if (iterations % 10 == 0)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var rate = iterations / elapsed.TotalSeconds;
                        Console.WriteLine($"Completed {iterations} iterations ({rate:F2} iter/sec)");
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine();
                Console.WriteLine("Benchmark stopped.");
            }
        }

        static void RunExporter(string[] args)
        {
            var exporter = new AnalysisExporter();

            if (args.Length == 0)
            {
                ShowExporterHelp();
                return;
            }

            string command = args[0].ToLower();

            switch (command)
            {
                case "url":
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: URL required");
                        Console.WriteLine("Usage: export url <beatsaver-url> [output-path]");
                        return;
                    }
                    string url = args[1];
                    string outputPath = args.Length > 2 ? args[2] : null;
                    exporter.ExportFromUrl(url, outputPath);
                    break;

                case "file":
                    Console.WriteLine("Local file loading not supported.");
                    Console.WriteLine("Please use: export url <beatsaver-url>");
                    break;

                case "detailed":
                    if (args.Length < 4)
                    {
                        Console.WriteLine("Error: Missing parameters");
                        Console.WriteLine("Usage: export detailed <beatsaver-url> <characteristic> <difficulty> [output-path]");
                        return;
                    }
                    string detailedUrl = args[1];
                    string characteristic = args[2];
                    string difficulty = args[3];
                    string detailedOutputPath = args.Length > 4 ? args[4] : null;
                    exporter.ExportDetailedSwingData(detailedUrl, characteristic, difficulty, detailedOutputPath);
                    break;

                default:
                    Console.WriteLine($"Unknown export command: {command}");
                    ShowExporterHelp();
                    break;
            }
        }

        static void ShowExporterHelp()
        {
            Console.WriteLine("=== Beat Saber Analysis Exporter ===");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  export url <beatsaver-url> [output-path]");
            Console.WriteLine("    Export analysis for all difficulties from a BeatSaver URL");
            Console.WriteLine("    Example: export url https://r2cdn.beatsaver.com/417f22a92dc4efb0750a4ea538e45eaf50ce628b.zip analysis.json");
            Console.WriteLine();
            Console.WriteLine("  export file <zip-path> [output-path]");
            Console.WriteLine("    Export analysis for all difficulties from a local zip file");
            Console.WriteLine("    Example: export file C:\\maps\\mymap.zip analysis.json");
            Console.WriteLine();
            Console.WriteLine("  export detailed <beatsaver-url> <characteristic> <difficulty> [output-path]");
            Console.WriteLine("    Export detailed per-swing data for a specific difficulty");
            Console.WriteLine("    Example: export detailed https://r2cdn.beatsaver.com/...zip Standard ExpertPlus detailed.json");
            Console.WriteLine();
        }
    }
}
