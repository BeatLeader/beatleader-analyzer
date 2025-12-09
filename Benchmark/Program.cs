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
                Console.WriteLine("  [3] Export Difficulty Breakdown");
                Console.WriteLine("  [4] Export SwingCurve Debug Data");
                Console.WriteLine("  [5] Run Benchmark");
                Console.WriteLine("  [6] Help & Documentation");
                Console.WriteLine("  [7] Exit");
                Console.WriteLine();
                Console.Write("Select an option (1-7): ");

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
                        RunExportDifficultyBreakdown();
                        break;
                    case "4":
                        RunExportCurveDebug();
                        break;
                    case "5":
                        RunBenchmarkMenu();
                        break;
                    case "6":
                        ShowExporterHelp();
                        Console.WriteLine();
                        Console.WriteLine("Press any key to return to menu...");
                        Console.ReadKey();
                        break;
                    case "7":
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

            Console.WriteLine("Enter map source:");
            Console.WriteLine("  [1] BeatSaver URL");
            Console.WriteLine("  [2] Local file path");
            Console.Write("Select option (1-2): ");
            string sourceChoice = Console.ReadLine()?.Trim();

            string input;
            bool isFile = false;

            if (sourceChoice == "2")
            {
                Console.Write("Enter path (zip file or extracted folder): ");
                input = Console.ReadLine()?.Trim();
                isFile = true;
            }
            else
            {
                Console.Write("Enter BeatSaver URL: ");
                input = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("Error: Input cannot be empty.");
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
            if (isFile)
            {
                exporter.ExportFromFile(input, string.IsNullOrEmpty(outputPath) ? null : outputPath);
            }
            else
            {
                exporter.ExportFromUrl(input, string.IsNullOrEmpty(outputPath) ? null : outputPath);
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }

        static void RunExportDifficultyBreakdown()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   Export Difficulty Breakdown                 ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Enter map source:");
            Console.WriteLine("  [1] BeatSaver URL");
            Console.WriteLine("  [2] Local file path");
            Console.Write("Select option (1-2): ");
            string sourceChoice = Console.ReadLine()?.Trim();

            string input;
            bool isFile = false;

            if (sourceChoice == "2")
            {
                Console.Write("Enter path (zip file or extracted folder): ");
                input = Console.ReadLine()?.Trim();
                isFile = true;
            }
            else
            {
                Console.Write("Enter BeatSaver URL: ");
                input = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("Error: Input cannot be empty.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Available Characteristics:");
            Console.WriteLine("  [1] Standard");
            Console.WriteLine("  [2] OneSaber");
            Console.WriteLine("  [3] NoArrows");
            Console.WriteLine("  [4] 90Degree");
            Console.WriteLine("  [5] 360Degree");
            Console.WriteLine("  [6] Lawless");
            Console.WriteLine();
            Console.Write("Select Characteristic (1-6): ");
            string charChoice = Console.ReadLine()?.Trim();

            string characteristic = charChoice switch
            {
                "1" => "Standard",
                "2" => "OneSaber",
                "3" => "NoArrows",
                "4" => "90Degree",
                "5" => "360Degree",
                "6" => "Lawless",
                _ => null
            };

            if (characteristic == null)
            {
                Console.WriteLine("Error: Invalid characteristic selection.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Available Difficulties:");
            Console.WriteLine("  [1] Easy");
            Console.WriteLine("  [2] Normal");
            Console.WriteLine("  [3] Hard");
            Console.WriteLine("  [4] Expert");
            Console.WriteLine("  [5] ExpertPlus");
            Console.WriteLine();
            Console.Write("Select Difficulty (1-5): ");
            string diffChoice = Console.ReadLine()?.Trim();

            string difficulty = diffChoice switch
            {
                "1" => "Easy",
                "2" => "Normal",
                "3" => "Hard",
                "4" => "Expert",
                "5" => "ExpertPlus",
                _ => null
            };

            if (difficulty == null)
            {
                Console.WriteLine("Error: Invalid difficulty selection.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.Write("Enter output path (or press Enter for 'difficulty_breakdown.html'): ");
            string outputPath = Console.ReadLine()?.Trim();

            Console.WriteLine();
            Console.WriteLine("Starting difficulty breakdown analysis...");
            Console.WriteLine();

            try
            {
                var exporter = new AnalysisExporter();
                if (isFile)
                {
                    exporter.ExportDifficultyBreakdownFromFile(input, characteristic, difficulty, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
                else
                {
                    exporter.ExportDifficultyBreakdown(input, characteristic, difficulty, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

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

            Console.WriteLine("Enter map source:");
            Console.WriteLine("  [1] BeatSaver URL");
            Console.WriteLine("  [2] Local file path");
            Console.Write("Select option (1-2): ");
            string sourceChoice = Console.ReadLine()?.Trim();

            string input;
            bool isFile = false;

            if (sourceChoice == "2")
            {
                Console.Write("Enter path (zip file or extracted folder): ");
                input = Console.ReadLine()?.Trim();
                isFile = true;
            }
            else
            {
                Console.Write("Enter BeatSaver URL: ");
                input = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("Error: Input cannot be empty.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Export options:");
            Console.WriteLine("  [1] All difficulties (with selector in HTML)");
            Console.WriteLine("  [2] Single difficulty");
            Console.Write("Select option (1-2): ");
            string exportChoice = Console.ReadLine()?.Trim();

            if (exportChoice == "1")
            {
                // Export all difficulties
                Console.Write("Enter output path (or press Enter for 'detailed_swings_all.html'): ");
                string outputPath = Console.ReadLine()?.Trim();

                Console.WriteLine();
                Console.WriteLine("Starting detailed analysis for all difficulties...");
                Console.WriteLine();

                var exporter = new AnalysisExporter();
                if (isFile)
                {
                    exporter.ExportAllDetailedSwingDataFromFile(input, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
                else
                {
                    exporter.ExportAllDetailedSwingData(input, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
            }
            else
            {
                // Export single difficulty
                Console.WriteLine();
                Console.WriteLine("Available Characteristics:");
                Console.WriteLine("  [1] Standard");
                Console.WriteLine("  [2] OneSaber");
                Console.WriteLine("  [3] NoArrows");
                Console.WriteLine("  [4] 90Degree");
                Console.WriteLine("  [5] 360Degree");
                Console.WriteLine("  [6] Lawless");
                Console.WriteLine();
                Console.Write("Select Characteristic (1-6): ");
                string charChoice = Console.ReadLine()?.Trim();

                string characteristic = charChoice switch
                {
                    "1" => "Standard",
                    "2" => "OneSaber",
                    "3" => "NoArrows",
                    "4" => "90Degree",
                    "5" => "360Degree",
                    "6" => "Lawless",
                    _ => null
                };

                if (characteristic == null)
                {
                    Console.WriteLine("Error: Invalid characteristic selection.");
                    Console.WriteLine("Press any key to return to menu...");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine();
                Console.WriteLine("Available Difficulties:");
                Console.WriteLine("  [1] Easy");
                Console.WriteLine("  [2] Normal");
                Console.WriteLine("  [3] Hard");
                Console.WriteLine("  [4] Expert");
                Console.WriteLine("  [5] ExpertPlus");
                Console.WriteLine();
                Console.Write("Select Difficulty (1-5): ");
                string diffChoice = Console.ReadLine()?.Trim();

                string difficulty = diffChoice switch
                {
                    "1" => "Easy",
                    "2" => "Normal",
                    "3" => "Hard",
                    "4" => "Expert",
                    "5" => "ExpertPlus",
                    _ => null
                };

                if (difficulty == null)
                {
                    Console.WriteLine("Error: Invalid difficulty selection.");
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
                if (isFile)
                {
                    exporter.ExportDetailedSwingDataFromFile(input, characteristic, difficulty, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
                else
                {
                    exporter.ExportDetailedSwingData(input, characteristic, difficulty, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to return to menu...");
            Console.ReadKey();
        }

        static void RunExportCurveDebug()
        {
            Console.Clear();
            Console.WriteLine("╔════════════════════════════════════════════════╗");
            Console.WriteLine("║   Export SwingCurve Debug Data                ║");
            Console.WriteLine("╚════════════════════════════════════════════════╝");
            Console.WriteLine();

            Console.WriteLine("Enter map source:");
            Console.WriteLine("  [1] BeatSaver URL");
            Console.WriteLine("  [2] Local file path");
            Console.Write("Select option (1-2): ");
            string sourceChoice = Console.ReadLine()?.Trim();

            string input;
            bool isUrl = sourceChoice == "1";

            if (isUrl)
            {
                Console.Write("Enter BeatSaver URL: ");
                input = Console.ReadLine()?.Trim();
            }
            else
            {
                Console.Write("Enter path (zip file or extracted folder): ");
                input = Console.ReadLine()?.Trim();
            }

            if (string.IsNullOrEmpty(input))
            {
                Console.WriteLine("Error: Input cannot be empty.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Available Characteristics:");
            Console.WriteLine("  [1] Standard");
            Console.WriteLine("  [2] OneSaber");
            Console.WriteLine("  [3] NoArrows");
            Console.WriteLine("  [4] 90Degree");
            Console.WriteLine("  [5] 360Degree");
            Console.WriteLine("  [6] Lawless");
            Console.WriteLine();
            Console.Write("Select Characteristic (1-6): ");
            string charChoice = Console.ReadLine()?.Trim();

            string characteristic = charChoice switch
            {
                "1" => "Standard",
                "2" => "OneSaber",
                "3" => "NoArrows",
                "4" => "90Degree",
                "5" => "360Degree",
                "6" => "Lawless",
                _ => null
            };

            if (characteristic == null)
            {
                Console.WriteLine("Error: Invalid characteristic selection.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.WriteLine();
            Console.WriteLine("Available Difficulties:");
            Console.WriteLine("  [1] Easy");
            Console.WriteLine("  [2] Normal");
            Console.WriteLine("  [3] Hard");
            Console.WriteLine("  [4] Expert");
            Console.WriteLine("  [5] ExpertPlus");
            Console.WriteLine();
            Console.Write("Select Difficulty (1-5): ");
            string diffChoice = Console.ReadLine()?.Trim();

            string difficulty = diffChoice switch
            {
                "1" => "Easy",
                "2" => "Normal",
                "3" => "Hard",
                "4" => "Expert",
                "5" => "ExpertPlus",
                _ => null
            };

            if (difficulty == null)
            {
                Console.WriteLine("Error: Invalid difficulty selection.");
                Console.WriteLine("Press any key to return to menu...");
                Console.ReadKey();
                return;
            }

            Console.Write("Enter output path (or press Enter for auto-generated name): ");
            string outputPath = Console.ReadLine()?.Trim();

            Console.WriteLine();
            Console.WriteLine("Generating SwingCurve debug data...");
            Console.WriteLine();

            try
            {
                var exporter = new AnalysisExporter();
                if (isUrl)
                {
                    exporter.ExportCurveDebugDataFromUrl(input, characteristic, difficulty, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
                else
                {
                    exporter.ExportCurveDebugData(input, characteristic, difficulty, string.IsNullOrEmpty(outputPath) ? null : outputPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

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
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: File path required");
                        Console.WriteLine("Usage: export file <zip-path> [output-path]");
                        return;
                    }
                    string filePath = args[1];
                    string fileOutputPath = args.Length > 2 ? args[2] : null;
                    exporter.ExportFromFile(filePath, fileOutputPath);
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
            Console.WriteLine("    Example: export url https://r2cdn.beatsaver.com/417f22a92dc4efb0750a4ea538e45eaf50ce628b.zip analysis.html");
            Console.WriteLine();
            Console.WriteLine("  export file <path> [output-path]");
            Console.WriteLine("    Export analysis for all difficulties from a local zip file or folder");
            Console.WriteLine("    Example: export file C:\\\\maps\\\\mymap.zip analysis.html");
            Console.WriteLine("    Example: export file C:\\\\maps\\\\extracted_map analysis.html");
            Console.WriteLine();
            Console.WriteLine("  export detailed <beatsaver-url> <characteristic> <difficulty> [output-path]");
            Console.WriteLine("    Export detailed per-swing data for a specific difficulty from URL");
            Console.WriteLine("    Example: export detailed https://r2cdn.beatsaver.com/...zip Standard ExpertPlus detailed.html");
            Console.WriteLine();
            Console.WriteLine("Note: Supports both .zip files and extracted map folders.");
            Console.WriteLine();
        }
    }
}
