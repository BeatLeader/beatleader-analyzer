using beatleader_analyzer;
using beatleader_analyzer.BeatmapScanner.Data;
using beatleader_parser;
using Newtonsoft.Json;
using Parser.Map;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Benchmark
{
    public class AnalysisExporter
    {
        private readonly Analyze analyzer;

        public AnalysisExporter()
        {
            analyzer = new Analyze();
        }

        private static void OpenInBrowser(string filePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = fullPath,
                        UseShellExecute = true
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", fullPath);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", fullPath);
                }
                
                Console.WriteLine($"Opening {filePath} in browser...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not open browser automatically: {ex.Message}");
                Console.WriteLine($"Please open the file manually: {Path.GetFullPath(filePath)}");
            }
        }

        public void ExportFromUrl(string beatSaverUrl, string outputPath = null)
        {
            Console.WriteLine($"Downloading map from: {beatSaverUrl}");
            var map = new Parse().TryDownloadLink(beatSaverUrl).LastOrDefault();

            if (map == null)
            {
                Console.WriteLine("Failed to download map!");
                return;
            }

            ExportBeatmap(map, outputPath);
        }

        public void ExportFromFile(string zipPath, string outputPath = null)
        {
            Console.WriteLine($"Loading map from file: {zipPath}");
            
            if (!File.Exists(zipPath) && !Directory.Exists(zipPath))
            {
                Console.WriteLine($"Error: File/Directory not found: {zipPath}");
                return;
            }

            try
            {
                var parser = new Parse();
                BeatmapV3 map = null;

                // Check if it's a zip file or a folder
                if (zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // Load zip file into memory and use TryLoadZip
                    using (var fileStream = File.OpenRead(zipPath))
                    using (var memoryStream = new MemoryStream())
                    {
                        fileStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;
                        
                        var maps = parser.TryLoadZip(memoryStream);
                        map = maps?.LastOrDefault();
                    }
                }
                else
                {
                    // Load from extracted folder using TryLoadPath
                    map = parser.TryLoadPath(zipPath);
                }

                if (map == null)
                {
                    Console.WriteLine("Failed to load map from file!");
                    return;
                }

                ExportBeatmap(map, outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading map: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public void ExportDetailedSwingDataFromFile(string zipPath, string characteristic, string difficulty, string outputPath = null)
        {
            outputPath ??= "detailed_swings.html";

            Console.WriteLine($"Loading map from file: {zipPath}");
            
            if (!File.Exists(zipPath) && !Directory.Exists(zipPath))
            {
                Console.WriteLine($"Error: Path not found: {zipPath}");
                return;
            }

            try
            {
                var parser = new Parse();
                BeatmapV3 map = null;

                // Check if it's a zip file or a folder
                if (zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // Load zip file into memory and use TryLoadZip
                    using (var fileStream = File.OpenRead(zipPath))
                    using (var memoryStream = new MemoryStream())
                    {
                        fileStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;
                        
                        var maps = parser.TryLoadZip(memoryStream);
                        map = maps?.LastOrDefault();
                    }
                }
                else
                {
                    // Load from extracted folder using TryLoadPath
                    map = parser.TryLoadPath(zipPath);
                }

                if (map == null)
                {
                    Console.WriteLine("Failed to load map from file!");
                    return;
                }

                var ratings = analyzer.GetRating(map, characteristic);
                var rating = ratings?.FirstOrDefault(r => r.Difficulty == difficulty);

                if (rating == null)
                {
                    Console.WriteLine($"Could not find difficulty: {characteristic} - {difficulty}");
                    return;
                }

                Console.WriteLine($"\nExporting detailed swing data for: {characteristic} - {difficulty}");
                Console.WriteLine($"Total swings: {rating.SwingData.Count}");

                var html = GenerateDetailedSwingHtml(map, characteristic, difficulty, rating);

                File.WriteAllText(outputPath, html);
                Console.WriteLine($"✓ Detailed swing data exported to: {outputPath}");
                
                OpenInBrowser(outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading map: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void ExportBeatmap(BeatmapV3 map, string outputPath)
        {
            outputPath ??= "analysis.html";

            Console.WriteLine($"\nAnalyzing map: {map.Info._songName} by {map.Info._songAuthorName}");
            Console.WriteLine($"Mapper: {map.Info._levelAuthorName}");
            Console.WriteLine($"BPM: {map.Info._beatsPerMinute}");

            var allResults = new List<object>();

            foreach (var characteristic in map.Info._difficultyBeatmapSets)
            {
                Console.WriteLine($"\nAnalyzing characteristic: {characteristic._beatmapCharacteristicName}");
                
                var ratings = analyzer.GetRating(map, characteristic._beatmapCharacteristicName);

                if (ratings != null)
                {
                    foreach (var rating in ratings)
                    {
                        Console.WriteLine($"  {rating.Difficulty}: Pass={rating.Pass:F2}, Tech={rating.Tech:F2}");

                        var result = CreateAnalysisObject(map, rating);
                        allResults.Add(result);
                    }
                }
            }

            var html = GenerateHtmlReport(map, allResults);

            File.WriteAllText(outputPath, html);
            Console.WriteLine($"\n✓ Analysis exported to: {outputPath}");
            
            OpenInBrowser(outputPath);
        }

        private object CreateAnalysisObject(BeatmapV3 map, Ratings rating)
        {
            return new
            {
                Metadata = new
                {
                    SongName = map.Info._songName,
                    SongAuthor = map.Info._songAuthorName,
                    Mapper = map.Info._levelAuthorName,
                    BPM = map.Info._beatsPerMinute,
                    Characteristic = rating.Characteristic,
                    Difficulty = rating.Difficulty
                },
                Ratings = new
                {
                    Pass = Math.Round(rating.Pass, 3),
                    Tech = Math.Round(rating.Tech, 3),
                    Nerf = Math.Round(rating.Nerf, 3)
                },
                Patterns = new
                {
                    Stacks = rating.Patterns.Stacks,
                    Towers = rating.Patterns.Towers,
                    Windows = rating.Patterns.Windows,
                    SlantedWindows = rating.Patterns.SlantedWindows,
                    Sliders = rating.Patterns.Sliders,
                    CurvedSliders = rating.Patterns.CurvedSliders,
                    DodgeWalls = rating.Patterns.DodgeWalls,
                    CrouchWalls = rating.Patterns.CrouchWalls,
                    ParityErrors = rating.Patterns.ParityErrors,
                    BombAvoidances = rating.Patterns.BombAvoidances
                },
                Walls = new
                {
                    DodgeWalls = rating.DodgeWalls.Select(w => new
                    {
                        Time = Math.Round(w.BpmTime, 3),
                        Duration = Math.Round(w.DurationInBeats, 3),
                        X = w.x,
                        Y = w.y,
                        Width = w.Width,
                        Height = w.Height
                    }).ToList(),
                    CrouchWalls = rating.CrouchWalls.Select(w => new
                    {
                        Time = Math.Round(w.BpmTime, 3),
                        Duration = Math.Round(w.DurationInBeats, 3),
                        X = w.x,
                        Y = w.y,
                        Width = w.Width,
                        Height = w.Height
                    }).ToList()
                },
                SwingCount = rating.SwingData.Count,
                TopDifficultSwings = rating.SwingData
                    .OrderByDescending(s => s.SwingDiff)
                    .Take(10)
                    .Select((s, index) => new
                    {
                        Rank = index + 1,
                        Time = Math.Round(s.Beat, 3),
                        Difficulty = Math.Round(s.SwingDiff, 3),
                        Hand = s.Notes[0].Type == 0 ? "Red" : "Blue",
                        Angle = Math.Round(s.Direction, 1),
                        Direction = Benchmark.AngleToDirection(s.Direction),
                        Parity = s.Forehand ? "Forehand" : "Backhand",
                        ParityError = s.ParityErrors,
                        BombAvoidance = s.BombAvoidance,
                    })
                    .ToList(),
                Statistics = new
                {
                    AverageDifficulty = Math.Round(rating.SwingData.Average(s => s.SwingDiff), 3),
                    MaxDifficulty = Math.Round(rating.SwingData.Max(s => s.SwingDiff), 3),
                    ParityErrorPercentage = Math.Round((double)rating.Patterns.ParityErrors / rating.SwingData.Count * 100, 3),
                    BombAvoidancePercentage = Math.Round((double)rating.Patterns.BombAvoidances / rating.SwingData.Count * 100, 3),
                    RedHandSwings = rating.SwingData.Count(s => s.Notes[0].Type == 0),
                    BlueHandSwings = rating.SwingData.Count(s => s.Notes[0].Type == 1)
                }
            };
        }

        private string GenerateHtmlReport(BeatmapV3 map, List<object> allResults)
        {
            var jsonData = JsonConvert.SerializeObject(allResults);
            
            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Beat Saber Analysis - {map.Info._songName}</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js""></script>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #333;
            padding: 20px;
            min-height: 100vh;
        }}
        
        .container {{
            max-width: 1400px;
            margin: 0 auto;
            background: white;
            border-radius: 15px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }}
        
        .header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }}
        
        .header h1 {{
            font-size: 2.5em;
            margin-bottom: 10px;
        }}
        
        .header .subtitle {{
            font-size: 1.2em;
            opacity: 0.9;
        }}
        
        .header .bpm {{
            font-size: 1.5em;
            margin-top: 10px;
            font-weight: bold;
        }}
        
        .content {{
            padding: 40px;
        }}
        
        .difficulty-selector {{
            display: flex;
            flex-wrap: wrap;
            gap: 10px;
            margin-bottom: 30px;
            padding: 20px;
            background: #f8f9fa;
            border-radius: 10px;
        }}
        
        .diff-btn {{
            padding: 12px 24px;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            font-size: 1em;
            font-weight: bold;
            transition: all 0.3s;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
        }}
        
        .diff-btn:hover {{
            transform: translateY(-2px);
            box-shadow: 0 4px 10px rgba(0,0,0,0.2);
        }}
        
        .diff-btn.active {{
            transform: scale(1.05);
            box-shadow: 0 6px 15px rgba(0,0,0,0.3);
        }}
        
        .diff-Easy {{ background: #4CAF50; color: white; }}
        .diff-Normal {{ background: #2196F3; color: white; }}
        .diff-Hard {{ background: #FF9800; color: white; }}
        .diff-Expert {{ background: #F44336; color: white; }}
        .diff-ExpertPlus {{ background: #9C27B0; color: white; }}
        
        .difficulty-section {{
            display: none;
        }}
        
        .difficulty-section.active {{
            display: block;
            animation: fadeIn 0.5s;
        }}
        
        @keyframes fadeIn {{
            from {{ opacity: 0; transform: translateY(20px); }}
            to {{ opacity: 1; transform: translateY(0); }}
        }}
        
        .stats-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
            gap: 20px;
            margin-bottom: 30px;
        }}
        
        .stat-card {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 25px;
            border-radius: 10px;
            box-shadow: 0 4px 15px rgba(0,0,0,0.1);
        }}
        
        .stat-card h3 {{
            font-size: 0.9em;
            opacity: 0.9;
            margin-bottom: 10px;
        }}
        
        .stat-card .value {{
            font-size: 2.5em;
            font-weight: bold;
        }}
        
        .patterns-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
            gap: 15px;
            margin: 30px 0;
        }}
        
        .pattern-badge {{
            background: #f8f9fa;
            padding: 15px;
            border-radius: 8px;
            text-align: center;
            border: 2px solid #e0e0e0;
        }}
        
        .pattern-badge .count {{
            font-size: 2em;
            font-weight: bold;
            color: #667eea;
        }}
        
        .pattern-badge .label {{
            font-size: 0.9em;
            color: #666;
            margin-top: 5px;
        }}
        
        .chart-container {{
            margin: 30px 0;
            padding: 20px;
            background: #f8f9fa;
            border-radius: 10px;
        }}
        
        .top-swings {{
            margin-top: 30px;
        }}
        
        .swing-item {{
            background: white;
            padding: 15px;
            margin: 10px 0;
            border-radius: 8px;
            border-left: 4px solid #667eea;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
            display: flex;
            justify-content: space-between;
            align-items: center;
        }}
        
        .swing-item .rank {{
            font-size: 1.5em;
            font-weight: bold;
            color: #667eea;
            width: 50px;
        }}
        
        .swing-item .info {{
            flex: 1;
            padding: 0 20px;
        }}
        
        .swing-item .badge {{
            padding: 5px 10px;
            border-radius: 5px;
            font-size: 0.8em;
            margin: 0 5px;
        }}
        
        .badge-red {{ background: #ffcdd2; color: #c62828; }}
        .badge-blue {{ background: #bbdefb; color: #1565c0; }}
        .badge-reset {{ background: #fff9c4; color: #f57f17; }}
        .badge-bomb {{ background: #ffccbc; color: #bf360c; }}
        
        h2 {{
            color: #333;
            margin: 30px 0 20px 0;
            padding-bottom: 10px;
            border-bottom: 3px solid #667eea;
        }}
        
        .walls-section {{
            margin: 20px 0;
        }}
        
        .wall-category {{
            background: white;
            border-radius: 8px;
            margin: 15px 0;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
            overflow: hidden;
        }}
        
        .wall-category-header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 15px 20px;
            cursor: pointer;
            display: flex;
            justify-content: space-between;
            align-items: center;
            transition: background 0.3s;
        }}
        
        .wall-category-header:hover {{
            background: linear-gradient(135deg, #764ba2 0%, #667eea 100%);
        }}
        
        .wall-category-header .title {{
            font-weight: bold;
            font-size: 1.1em;
        }}
        
        .wall-category-header .count {{
            background: rgba(255,255,255,0.2);
            padding: 5px 15px;
            border-radius: 20px;
        }}
        
        .wall-list {{
            display: none;
            padding: 0;
        }}
        
        .wall-list.expanded {{
            display: block;
        }}
        
        .wall-item {{
            padding: 15px 20px;
            border-bottom: 1px solid #eee;
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
            gap: 15px;
        }}
        
        .wall-item:last-child {{
            border-bottom: none;
        }}
        
        .wall-item .property {{
            display: flex;
            flex-direction: column;
        }}
        
        .wall-item .property .label {{
            font-size: 0.85em;
            color: #666;
            margin-bottom: 3px;
        }}
        
        .wall-item .property .value {{
            font-weight: bold;
            color: #333;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>{map.Info._songName}</h1>
            <div class=""subtitle"">by {map.Info._songAuthorName} • Mapped by {map.Info._levelAuthorName}</div>
            <div class=""bpm"">♪ {map.Info._beatsPerMinute} BPM</div>
        </div>
        
        <div class=""content"">
            <div class=""difficulty-selector"" id=""difficultySelector""></div>
            <div id=""difficultyContent""></div>
        </div>
    </div>
    
    <script>
        const analysisData = {jsonData};
        
        function toggleWallList(id) {{
            const element = document.getElementById(id);
            element.classList.toggle('expanded');
        }}
        
        function createDifficultyButtons() {{
            const selector = document.getElementById('difficultySelector');
            const grouped = {{}};
            
            analysisData.forEach((data, index) => {{
                const char = data.Metadata.Characteristic;
                if (!grouped[char]) grouped[char] = [];
                grouped[char].push({{ data, index }});
            }});
            
            Object.keys(grouped).forEach(char => {{
                const group = document.createElement('div');
                group.style.display = 'flex';
                group.style.gap = '5px';
                group.style.alignItems = 'center';
                
                const label = document.createElement('span');
                label.textContent = char + ': ';
                label.style.fontWeight = 'bold';
                label.style.marginRight = '5px';
                group.appendChild(label);
                
                grouped[char].forEach(({{ data, index }}) => {{
                    const btn = document.createElement('button');
                    btn.className = `diff-btn diff-${{data.Metadata.Difficulty}}`;
                    btn.textContent = data.Metadata.Difficulty;
                    btn.onclick = () => showDifficulty(index);
                    group.appendChild(btn);
                }});
                
                selector.appendChild(group);
            }});
            
            if (analysisData.length > 0) showDifficulty(0);
        }}
        
        function showDifficulty(index) {{
            const data = analysisData[index];
            const content = document.getElementById('difficultyContent');
            
            // Update active button
            document.querySelectorAll('.diff-btn').forEach(btn => btn.classList.remove('active'));
            document.querySelectorAll('.diff-btn')[index].classList.add('active');
            
            content.innerHTML = `
                <div class=""stats-grid"">
                    <div class=""stat-card"">
                        <h3>Pass Difficulty</h3>
                        <div class=""value"">${{(typeof data.Ratings.Pass === 'number' ? data.Ratings.Pass.toFixed(2) : 'N/A')}}</div>
                    </div>
                    <div class=""stat-card"">
                        <h3>Tech Difficulty</h3>
                        <div class=""value"">${{(typeof data.Ratings.Tech === 'number' ? data.Ratings.Tech.toFixed(2) : 'N/A')}}</div>
                    </div>
                    <div class=""stat-card"">
                        <h3>Total Swings</h3>
                        <div class=""value"">${{data.SwingCount || 0}}</div>
                    </div>
                    <div class=""stat-card"">
                        <h3>Note Count Nerf</h3>
                        <div class=""value"">${{(typeof data.Ratings.Nerf === 'number' ? data.Ratings.Nerf.toFixed(3) : 'N/A')}}</div>
                    </div>
                </div>
                
                <h2>📊 Pattern Statistics</h2>
                <div class=""patterns-grid"">
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.Stacks}}</div>
                        <div class=""label"">Stacks</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.Towers}}</div>
                        <div class=""label"">Towers</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.Windows}}</div>
                        <div class=""label"">Windows</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.SlantedWindows}}</div>
                        <div class=""label"">Slanted Windows</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.Sliders}}</div>
                        <div class=""label"">Sliders</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.CurvedSliders}}</div>
                        <div class=""label"">Curved Sliders</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.DodgeWalls}}</div>
                        <div class=""label"">Dodge Walls</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.CrouchWalls}}</div>
                        <div class=""label"">Crouch Walls</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.ParityErrors}}</div>
                        <div class=""label"">Parity Errors</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.BombAvoidances}}</div>
                        <div class=""label"">Bomb Avoidances</div>
                    </div>
                </div>
                
                <h2>🧱 Walls</h2>
                <div class=""walls-section"">
                    <div class=""wall-category"">
                        <div class=""wall-category-header"" onclick=""toggleWallList('dodge-walls')"">
                            <span class=""title"">🏃 Dodge Walls</span>
                            <span class=""count"">${{data.Walls.DodgeWalls.length}} walls</span>
                        </div>
                        <div id=""dodge-walls"" class=""wall-list"">
                            ${{data.Walls.DodgeWalls.length === 0 ? '<div class=""wall-item"">No dodge walls in this difficulty</div>' : data.Walls.DodgeWalls.map((wall, idx) => `
                                <div class=""wall-item"">
                                    <div class=""property"">
                                        <span class=""label"">#</span>
                                        <span class=""value"">${{idx + 1}}</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Time (beats)</span>
                                        <span class=""value"">${{wall.Time}}</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Duration (beats)</span>
                                        <span class=""value"">${{wall.Duration}}</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Position (X, Y)</span>
                                        <span class=""value"">(${{wall.X}}, ${{wall.Y}})</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Size (W × H)</span>
                                        <span class=""value"">${{wall.Width}} × ${{wall.Height}}</span>
                                    </div>
                                </div>
                            `).join('')}}
                        </div>
                    </div>
                    
                    <div class=""wall-category"">
                        <div class=""wall-category-header"" onclick=""toggleWallList('crouch-walls')"">
                            <span class=""title"">🦆 Crouch Walls</span>
                            <span class=""count"">${{data.Walls.CrouchWalls.length}} walls</span>
                        </div>
                        <div id=""crouch-walls"" class=""wall-list"">
                            ${{data.Walls.CrouchWalls.length === 0 ? '<div class=""wall-item"">No crouch walls in this difficulty</div>' : data.Walls.CrouchWalls.map((wall, idx) => `
                                <div class=""wall-item"">
                                    <div class=""property"">
                                        <span class=""label"">#</span>
                                        <span class=""value"">${{idx + 1}}</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Time (beats)</span>
                                        <span class=""value"">${{wall.Time}}</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Duration (beats)</span>
                                        <span class=""value"">${{wall.Duration}}</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Position (X, Y)</span>
                                        <span class=""value"">(${{wall.X}}, ${{wall.Y}})</span>
                                    </div>
                                    <div class=""property"">
                                        <span class=""label"">Size (W × H)</span>
                                        <span class=""value"">${{wall.Width}} × ${{wall.Height}}</span>
                                    </div>
                                </div>
                            `).join('')}}
                        </div>
                    </div>
                </div>
                
                <h2>📈 Statistics</h2>
                <div class=""chart-container"">
                    <canvas id=""statsChart""></canvas>
                </div>
                
                <h2>🔥 Top 10 Most Difficult Swings</h2>
                <div class=""top-swings"">
                    ${{data.TopDifficultSwings.map(swing => `
                        <div class=""swing-item"">
                            <div class=""rank"">#${{swing.Rank}}</div>
                            <div class=""info"">
                                <div>
                                    <strong>Beat:</strong> ${{swing.Time}} | 
                                    <strong>Difficulty:</strong> ${{swing.Difficulty}} | 
                                    <strong>Angle:</strong> ${{swing.Angle}}° (${{swing.Direction}}) | 
                                    <strong>Parity:</strong> ${{swing.Parity}}
                                </div>
                                <div style=""margin-top: 5px;"">
                                    <span class=""badge badge-${{swing.Hand.toLowerCase()}}"">${{swing.Hand}} Hand</span>
                                    ${{swing.ParityError ? '<span class=""badge badge-reset"">RESET</span>' : ''}}
                                    ${{swing.BombAvoidance ? '<span class=""badge badge-bomb"">BOMB RESET</span>' : ''}}
                                </div>
                            </div>
                        </div>
                    `).join('')}}
                </div>
            `;
            
            // Create chart
            const ctx = document.getElementById('statsChart').getContext('2d');
            new Chart(ctx, {{
                type: 'bar',
                data: {{
                    labels: ['Avg Difficulty', 'Max Difficulty', 'Parity Error %', 'Bomb Avoidance %'],
                    datasets: [{{
                        label: 'Statistics',
                        data: [
                            data.Statistics.AverageDifficulty,
                            data.Statistics.MaxDifficulty,
                            data.Statistics.ParityErrorPercentage,
                            data.Statistics.BombAvoidancePercentage
                        ],
                        backgroundColor: [
                            'rgba(102, 126, 234, 0.8)',
                            'rgba(118, 75, 162, 0.8)',
                            'rgba(255, 193, 7, 0.8)',
                            'rgba(244, 67, 54, 0.8)'
                        ],
                        borderColor: [
                            'rgba(102, 126, 234, 1)',
                            'rgba(118, 75, 162, 1)',
                            'rgba(255, 193, 7, 1)',
                            'rgba(244, 67, 54, 1)'
                        ],
                        borderWidth: 2
                    }}]
                }},
                options: {{
                    responsive: true,
                    maintainAspectRatio: true,
                    plugins: {{
                        legend: {{
                            display: false
                        }}
                    }},
                    scales: {{
                        y: {{
                            beginAtZero: true
                        }}
                    }}
                }}
            }});
        }}
        
        createDifficultyButtons();
    </script>
</body>
</html>";
}

public void ExportDetailedSwingData(string beatSaverUrl, string characteristic, string difficulty, string outputPath = null)
        {
            outputPath ??= "detailed_swings.html";

            Console.WriteLine($"Downloading map from: {beatSaverUrl}");
            var map = new Parse().TryDownloadLink(beatSaverUrl).LastOrDefault();

            if (map == null)
            {
                Console.WriteLine("Failed to download map!");
                return;
            }

            var ratings = analyzer.GetRating(map, characteristic);
            var rating = ratings?.FirstOrDefault(r => r.Difficulty == difficulty);

            if (rating == null)
            {
                Console.WriteLine($"Could not find difficulty: {characteristic} - {difficulty}");
                return;
            }

            Console.WriteLine($"\nExporting detailed swing data for: {characteristic} - {difficulty}");
            Console.WriteLine($"Total swings: {rating.SwingData.Count}");

            var detailedData = new
            {
                Metadata = new
                {
                    SongName = map.Info._songName,
                    Characteristic = characteristic,
                    Difficulty = difficulty,
                    BPM = map.Info._beatsPerMinute,
                    TotalSwings = rating.SwingData.Count
                },
                Swings = rating.SwingData.Select((s, index) => new
                {
                    Index = index,
                    Time = Math.Round(s.Beat, 3),
                    Hand = s.Notes[0].Type == 0 ? "Red" : "Blue",
                    Position = new
                    {
                        Line = s.Notes[0].X,
                        Layer = s.Notes[0].Y
                    },
                    Entry = new
                    {
                        X = Math.Round(s.EntryPosition.x, 3),
                        Y = Math.Round(s.EntryPosition.y, 3)
                    },
                    Exit = new
                    {
                        X = Math.Round(s.ExitPosition.x, 3),
                        Y = Math.Round(s.ExitPosition.y, 3)
                    },
                    Angle = Math.Round(s.Direction, 1),
                    Direction = Benchmark.AngleToDirection(s.Direction),
                    Parity = s.Forehand ? "Forehand" : "Backhand",
                    ParityError = s.ParityErrors,
                    BombAvoidance = s.BombAvoidance,
                    Difficulty = Math.Round(s.SwingDiff, 3)
                }).ToList()
            };

            var html = GenerateDetailedSwingHtml(map, characteristic, difficulty, rating);

            File.WriteAllText(outputPath, html);
            Console.WriteLine($"✓ Detailed swing data exported to: {outputPath}");
            
            OpenInBrowser(outputPath);
        }

        public void ExportAllDetailedSwingData(string beatSaverUrl, string outputPath = null)
        {
            outputPath ??= "detailed_swings_all.html";

            Console.WriteLine($"Downloading map from: {beatSaverUrl}");
            var map = new Parse().TryDownloadLink(beatSaverUrl).LastOrDefault();

            if (map == null)
            {
                Console.WriteLine("Failed to download map!");
                return;
            }

            var allDifficulties = new List<(string characteristic, string difficulty, Ratings rating)>();

            foreach (var characteristic in map.Info._difficultyBeatmapSets)
            {
                Console.WriteLine($"\nAnalyzing characteristic: {characteristic._beatmapCharacteristicName}");
                
                var ratings = analyzer.GetRating(map, characteristic._beatmapCharacteristicName);

                if (ratings != null)
                {
                    foreach (var rating in ratings)
                    {
                        Console.WriteLine($"  {rating.Difficulty}: {rating.SwingData.Count} swings");
                        allDifficulties.Add((characteristic._beatmapCharacteristicName, rating.Difficulty, rating));
                    }
                }
            }

            if (allDifficulties.Count == 0)
            {
                Console.WriteLine("No difficulties found!");
                return;
            }

            var html = GenerateMultiDifficultySwingHtml(map, allDifficulties);

            File.WriteAllText(outputPath, html);
            Console.WriteLine($"\n✓ Detailed swing data for all difficulties exported to: {outputPath}");
            
            OpenInBrowser(outputPath);
        }

        public void ExportAllDetailedSwingDataFromFile(string zipPath, string outputPath = null)
        {
            outputPath ??= "detailed_swings_all.html";

            Console.WriteLine($"Loading map from file: {zipPath}");
            
            if (!File.Exists(zipPath) && !Directory.Exists(zipPath))
            {
                Console.WriteLine($"Error: Path not found: {zipPath}");
                return;
            }

            try
            {
                var parser = new Parse();
                BeatmapV3 map = null;

                if (zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var fileStream = File.OpenRead(zipPath))
                    using (var memoryStream = new MemoryStream())
                    {
                        fileStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;
                        
                        var maps = parser.TryLoadZip(memoryStream);
                        map = maps?.LastOrDefault();
                    }
                }
                else
                {
                    map = parser.TryLoadPath(zipPath);
                }

                if (map == null)
                {
                    Console.WriteLine("Failed to load map from file!");
                    return;
                }

                var allDifficulties = new List<(string characteristic, string difficulty, Ratings rating)>();

                foreach (var characteristic in map.Info._difficultyBeatmapSets)
                {
                    Console.WriteLine($"\nAnalyzing characteristic: {characteristic._beatmapCharacteristicName}");
                    
                    var ratings = analyzer.GetRating(map, characteristic._beatmapCharacteristicName);

                    if (ratings != null)
                    {
                        foreach (var rating in ratings)
                        {
                            Console.WriteLine($"  {rating.Difficulty}: {rating.SwingData.Count} swings");
                            allDifficulties.Add((characteristic._beatmapCharacteristicName, rating.Difficulty, rating));
                        }
                    }
                }

                if (allDifficulties.Count == 0)
                {
                    Console.WriteLine("No difficulties found!");
                    return;
                }

                var html = GenerateMultiDifficultySwingHtml(map, allDifficulties);

                File.WriteAllText(outputPath, html);
                Console.WriteLine($"\n✓ Detailed swing data for all difficulties exported to: {outputPath}");
                
                OpenInBrowser(outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading map: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private string GenerateDetailedSwingHtml(BeatmapV3 map, string characteristic, string difficulty, Ratings rating)
        {
            var swingDataJson = JsonConvert.SerializeObject(rating.SwingData.Select((s, index) => new
            {
                Index = index,
                Time = Math.Round(s.Beat, 3),
                Hand = s.Notes[0].Type == 0 ? "Red" : "Blue",
                Line = s.Notes[0].X,
                Layer = s.Notes[0].Y,
                EntryX = Math.Round(s.EntryPosition.x, 3),
                EntryY = Math.Round(s.EntryPosition.y, 3),
                ExitX = Math.Round(s.ExitPosition.x, 3),
                ExitY = Math.Round(s.ExitPosition.y, 3),
                Angle = Math.Round(s.Direction, 1),
                Direction = Benchmark.AngleToDirection(s.Direction),
                Parity = s.Forehand ? "Forehand" : "Backhand",
                PatternType = s.PatternType,
                ParityError = s.ParityErrors,
                BombAvoidance = s.BombAvoidance,
                Difficulty = Math.Round(s.SwingDiff, 3)
            }).ToList());

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Detailed Swing Analysis - {map.Info._songName}</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js""></script>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #333;
            padding: 20px;
            min-height: 100vh;
        }}
        
        .container {{
            max-width: 1600px;
            margin: 0 auto;
            background: white;
            border-radius: 15px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }}
        
        .header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }}
        
        .header h1 {{
            font-size: 2.5em;
            margin-bottom: 10px;
        }}
        
        .header .subtitle {{
            font-size: 1.2em;
            opacity: 0.9;
        }}
        
        .content {{
            padding: 40px;
        }}
        
        .controls {{
            background: #f8f9fa;
            padding: 20px;
            border-radius: 10px;
            margin-bottom: 30px;
            display: flex;
            gap: 15px;
            flex-wrap: wrap;
            align-items: center;
        }}
        
        .controls input, .controls select {{
            padding: 10px 15px;
            border: 2px solid #ddd;
            border-radius: 8px;
            font-size: 1em;
        }}
        
        .controls button {{
            padding: 10px 20px;
            border: none;
            border-radius: 8px;
            background: #667eea;
            color: white;
            font-weight: bold;
            cursor: pointer;
            transition: all 0.3s;
        }}
        
        .controls button:hover {{
            background: #764ba2;
            transform: translateY(-2px);
        }}
        
        .chart-section {{
            margin: 30px 0;
            padding: 20px;
            background: #f8f9fa;
            border-radius: 10px;
        }}
        
        .table-container {{
            overflow-x: auto;
            margin-top: 20px;
        }}
        
        table {{
            width: 100%;
            border-collapse: collapse;
            background: white;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
            border-radius: 8px;
            overflow: hidden;
        }}
        
        th {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 15px;
            text-align: left;
            font-weight: bold;
            position: sticky;
            top: 0;
            z-index: 10;
            cursor: pointer;
            user-select: none;
            transition: background 0.3s;
        }}
        
        th:hover {{
            background: linear-gradient(135deg, #764ba2 0%, #667eea 100%);
        }}
        
        th.sortable {{
            position: relative;
            padding-right: 30px;
        }}
        
        th.sortable::after {{
            content: '⇅';
            position: absolute;
            right: 10px;
            opacity: 0.5;
        }}
        
        th.sorted-asc::after {{
            content: '▲';
            opacity: 1;
        }}
        
        th.sorted-desc::after {{
            content: '▼';
            opacity: 1;
        }}
        
        td {{
            padding: 12px 15px;
            border-bottom: 1px solid #eee;
        }}
        
        tr:hover {{
            background: #f8f9fa;
        }}
        
        .badge {{
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.85em;
            font-weight: bold;
        }}
        
        .badge-red {{ background: #ffcdd2; color: #c62828; }}
        .badge-blue {{ background: #bbdefb; color: #1565c0; }}
        .badge-forehand {{ background: #c8e6c9; color: #2e7d32; }}
        .badge-backhand {{ background: #fff9c4; color: #f57f17; }}
        .badge-reset {{ background: #ffccbc; color: #bf360c; }}
        
        .highlight {{
            background: #fff9c4 !important;
        }}
        
        h2 {{
            color: #333;
            margin: 30px 0 20px 0;
            padding-bottom: 10px;
            border-bottom: 3px solid #667eea;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>Detailed Swing Analysis</h1>
            <div class=""subtitle"">{map.Info._songName} • {characteristic} - {difficulty}</div>
            <div style=""margin-top: 10px;"">Total Swings: {rating.SwingData.Count}</div>
        </div>
        
        <div class=""content"">
            <div class=""controls"">
                <input type=""text"" id=""searchBox"" placeholder=""Search by time, hand, angle..."">
                <select id=""filterHand"">
                    <option value="""">All Hands</option>
                    <option value=""Red"">Red Hand</option>
                    <option value=""Blue"">Blue Hand</option>
                </select>
                <select id=""filterParity"">
                    <option value="""">All Parities</option>
                    <option value=""Forehand"">Forehand</option>
                    <option value=""Backhand"">Backhand</option>
                </select>
                <select id=""filterPattern"">
                    <option value="""">All Pattern Types</option>
                    <option value=""Multi-Notes"">Multi-Notes Only</option>
                    <option value=""Single"">Single</option>
                    <option value=""Stack"">Stack</option>
                    <option value=""Tower"">Tower</option>
                    <option value=""Window"">Window</option>
                    <option value=""Slanted Window"">Slanted Window</option>
                    <option value=""Slider"">Slider</option>
                    <option value=""Curved Slider"">Curved Slider</option>
                </select>
                <label>
                    <input type=""checkbox"" id=""filterParityError""> Show Parity Errors Only
                </label>
                <label>
                    <input type=""checkbox"" id=""filterBombAvoidance""> Show Bomb Avoidances Only
                </label>
                <button onclick=""resetFilters()"">Reset Filters</button>
            </div>
            
            <div class=""chart-section"">
                <h2>📊 Difficulty Distribution</h2>
                <canvas id=""difficultyChart""></canvas>
            </div>
            
            <h2>🎯 Swing Details</h2>
            <div class=""table-container"">
                <table id=""swingTable"">
                    <thead>
                        <tr>
                            <th class=""sortable"" data-column=""Index"" data-type=""number"">#</th>
                            <th class=""sortable"" data-column=""Time"" data-type=""number"">Beat</th>
                            <th class=""sortable"" data-column=""Hand"" data-type=""string"">Hand</th>
                            <th class=""sortable"" data-column=""Position"" data-type=""string"">Position</th>
                            <th class=""sortable"" data-column=""Entry"" data-type=""string"">Entry</th>
                            <th class=""sortable"" data-column=""Exit"" data-type=""string"">Exit</th>
                            <th class=""sortable"" data-column=""Angle"" data-type=""number"">Angle / Direction</th>
                            <th class=""sortable"" data-column=""Parity"" data-type=""string"">Parity</th>
                            <th class=""sortable"" data-column=""PatternType"" data-type=""string"">Pattern Type</th>
                            <th>Flags</th>
                            <th class=""sortable"" data-column=""Difficulty"" data-type=""number"">Difficulty</th>
                        </tr>
                    </thead>
                    <tbody id=""swingTableBody""></tbody>
                </table>
            </div>
        </div>
    </div>
    
    <script>
        const swingData = {swingDataJson};
        let filteredData = [...swingData];
        let currentSortColumn = null;
        let currentSortDirection = 'asc';
        
        function renderTable() {{
            const tbody = document.getElementById('swingTableBody');
            tbody.innerHTML = filteredData.map(swing => `
                <tr>
                    <td>${{swing.Index + 1}}</td>
                    <td>${{swing.Time}}</td>
                    <td><span class=""badge badge-${{swing.Hand.toLowerCase()}}"">${{swing.Hand}}</span></td>
                    <td>(${{swing.Line}}, ${{swing.Layer}})</td>
                    <td>(${{swing.EntryX}}, ${{swing.EntryY}})</td>
                    <td>(${{swing.ExitX}}, ${{swing.ExitY}})</td>
                    <td>${{swing.Angle}}° (${{swing.Direction}})</td>
                    <td><span class=""badge badge-${{swing.Parity.toLowerCase()}}"">${{swing.Parity}}</span></td>
                    <td>${{swing.PatternType}}</td>
                    <td>
                        ${{swing.ParityError ? '<span class=""badge badge-reset"">PE</span>' : ''}}
                        ${{swing.BombAvoidance ? '<span class=""badge badge-reset"">BA</span>' : ''}}
                    </td>
                    <td><strong>${{swing.Difficulty}}</strong></td>
                </tr>
            `).join('');
        }}
        
        function applyFilters() {{
            const search = document.getElementById('searchBox').value.toLowerCase();
            const handFilter = document.getElementById('filterHand').value;
            const parityFilter = document.getElementById('filterParity').value;
            const patternFilter = document.getElementById('filterPattern').value;
            const parityErrorOnly = document.getElementById('filterParityError').checked;
            const bombAvoidanceOnly = document.getElementById('filterBombAvoidance').checked;
            
            filteredData = swingData.filter(swing => {{
                if (search && !JSON.stringify(swing).toLowerCase().includes(search)) return false;
                if (handFilter && swing.Hand !== handFilter) return false;
                if (parityFilter && swing.Parity !== parityFilter) return false;
                if (patternFilter && swing.PatternType !== patternFilter) return false;
                if (parityErrorOnly && !swing.ParityError) return false;
                if (bombAvoidanceOnly && !swing.BombAvoidance) return false;
                return true;
            }});
            
            renderTable();
        }}
        
        function resetFilters() {{
            document.getElementById('searchBox').value = '';
            document.getElementById('filterHand').value = '';
            document.getElementById('filterParity').value = '';
            document.getElementById('filterPattern').value = '';
            document.getElementById('filterParityError').checked = false;
            document.getElementById('filterBombAvoidance').checked = false;
            applyFilters();
        }}
        
        function applyFilters() {{
            const search = document.getElementById('searchBox').value.toLowerCase();
            const handFilter = document.getElementById('filterHand').value;
            const parityFilter = document.getElementById('filterParity').value;
            const patternFilter = document.getElementById('filterPattern').value;
            const parityErrorOnly = document.getElementById('filterParityError').checked;
            const bombAvoidanceOnly = document.getElementById('filterBombAvoidance').checked;
            
            filteredData = swingData.filter(swing => {{
                if (search && !JSON.stringify(swing).toLowerCase().includes(search)) return false;
                if (handFilter && swing.Hand !== handFilter) return false;
                if (parityFilter && swing.Parity !== parityFilter) return false;
                if (patternFilter) {{
                    if (patternFilter === 'Multi-Notes') {{
                        // Filter to show only multi-note patterns (everything except Single)
                        if (swing.PatternType === 'Single') return false;
                    }} else {{
                        // Filter by specific pattern type
                        if (swing.PatternType !== patternFilter) return false;
                    }}
                }}
                if (parityErrorOnly && !swing.ParityError) return false;
                if (bombAvoidanceOnly && !swing.BombAvoidance) return false;
                return true;
            }});
            
            renderTable();
        }}
        
        document.getElementById('searchBox').addEventListener('input', applyFilters);
        document.getElementById('filterHand').addEventListener('change', applyFilters);
        document.getElementById('filterParity').addEventListener('change', applyFilters);
        document.getElementById('filterPattern').addEventListener('change', applyFilters);
        document.getElementById('filterParityError').addEventListener('change', applyFilters);
        document.getElementById('filterBombAvoidance').addEventListener('change', applyFilters);
        
        // Add sort functionality
        function sortTable(column, type) {{
            // Toggle sort direction if clicking the same column
            if (currentSortColumn === column) {{
                currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
            }} else {{
                currentSortColumn = column;
                currentSortDirection = 'asc';
            }}
            
            // Sort the data
            filteredData.sort((a, b) => {{
                let aVal, bVal;
                
                if (type === 'number') {{
                    aVal = a[column];
                    bVal = b[column];
                }} else {{
                    aVal = String(a[column]).toLowerCase();
                    bVal = String(b[column]).toLowerCase();
                }}
                
                let comparison = 0;
                if (aVal < bVal) comparison = -1;
                if (aVal > bVal) comparison = 1;
                
                return currentSortDirection === 'asc' ? comparison : -comparison;
            }});
            
            // Update header classes
            document.querySelectorAll('th.sortable').forEach(th => {{
                th.classList.remove('sorted-asc', 'sorted-desc');
            }});
            
            const activeHeader = document.querySelector('th[data-column=""' + column + '""]');
            if (activeHeader) {{
                activeHeader.classList.add('sorted-' + currentSortDirection);
            }}
            
            renderTable();
        }}
        
        // Attach click handlers to sortable headers
        document.querySelectorAll('th.sortable').forEach(th => {{
            th.addEventListener('click', () => {{
                const column = th.getAttribute('data-column');
                const type = th.getAttribute('data-type');
                sortTable(column, type);
            }});
        }});
        
        // Create difficulty chart
        const ctx = document.getElementById('difficultyChart').getContext('2d');
        const buckets = 20;
        const maxDiff = Math.max(...swingData.map(s => s.Difficulty));
        const bucketSize = maxDiff / buckets;
        const histogram = new Array(buckets).fill(0);
        
        swingData.forEach(swing => {{
            const bucket = Math.min(Math.floor(swing.Difficulty / bucketSize), buckets - 1);
            histogram[bucket]++;
        }});
        
        new Chart(ctx, {{
            type: 'line',
            data: {{
                labels: Array.from({{ length: buckets }}, (_, i) => (i * bucketSize).toFixed(1)),
                datasets: [{{
                    label: 'Number of Swings',
                    data: histogram,
                    borderColor: 'rgba(102, 126, 234, 1)',
                    backgroundColor: 'rgba(102, 126, 234, 0.2)',
                    fill: true,
                    tension: 0.4
                }}]
            }},
            options: {{
                responsive: true,
                plugins: {{
                    legend: {{
                        display: true
                    }}
                }},
                scales: {{
                    x: {{
                        title: {{
                            display: true,
                            text: 'Difficulty Range'
                        }}
                    }},
                    y: {{
                        title: {{
                            display: true,
                            text: 'Count'
                        }},
                        beginAtZero: true
                    }}
                }}
            }}
        }});
        
        renderTable();
    </script>
</body>
</html>";
        }

        public void ExportDifficultyBreakdown(string beatSaverUrl, string characteristic, string difficulty, string outputPath = null)
        {
            outputPath ??= "difficulty_breakdown.html";

            Console.WriteLine($"Downloading map from: {beatSaverUrl}");
            var map = new Parse().TryDownloadLink(beatSaverUrl).LastOrDefault();

            if (map == null)
            {
                Console.WriteLine("Failed to download map!");
                return;
            }

            var ratings = analyzer.GetRating(map, characteristic);
            var rating = ratings?.FirstOrDefault(r => r.Difficulty == difficulty);

            if (rating == null)
            {
                Console.WriteLine($"Could not find difficulty: {characteristic} - {difficulty}");
                return;
            }

            Console.WriteLine($"\nGenerating difficulty breakdown for: {characteristic} - {difficulty}");
            Console.WriteLine($"Total swings: {rating.SwingData.Count}");
            Console.WriteLine($"Pass: {rating.Pass:F2}, Tech: {rating.Tech:F2}");

            var html = DifficultyBreakdownHtmlExporter.GenerateBreakdownHtml(rating, map.Info._beatsPerMinute);

            File.WriteAllText(outputPath, html);
            Console.WriteLine($"✓ Difficulty breakdown exported to: {outputPath}");
            
            OpenInBrowser(outputPath);
        }

        public void ExportDifficultyBreakdownFromFile(string zipPath, string characteristic, string difficulty, string outputPath = null)
        {
            outputPath ??= "difficulty_breakdown.html";

            Console.WriteLine($"Loading map from file: {zipPath}");
            
            if (!File.Exists(zipPath) && !Directory.Exists(zipPath))
            {
                Console.WriteLine($"Error: Path not found: {zipPath}");
                return;
            }

            try
            {
                var parser = new Parse();
                BeatmapV3 map = null;

                if (zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using (var fileStream = File.OpenRead(zipPath))
                    using (var memoryStream = new MemoryStream())
                    {
                        fileStream.CopyTo(memoryStream);
                        memoryStream.Position = 0;
                        
                        var maps = parser.TryLoadZip(memoryStream);
                        map = maps?.LastOrDefault();
                    }
                }
                else
                {
                    map = parser.TryLoadPath(zipPath);
                }

                if (map == null)
                {
                    Console.WriteLine("Failed to load map from file!");
                    return;
                }

                var ratings = analyzer.GetRating(map, characteristic);
                var rating = ratings?.FirstOrDefault(r => r.Difficulty == difficulty);

                if (rating == null)
                {
                    Console.WriteLine($"Could not find difficulty: {characteristic} - {difficulty}");
                    return;
                }

                Console.WriteLine($"\nGenerating difficulty breakdown for: {characteristic} - {difficulty}");
                Console.WriteLine($"Total swings: {rating.SwingData.Count}");
                Console.WriteLine($"Pass: {rating.Pass:F2}, Tech: {rating.Tech:F2}");

                var html = DifficultyBreakdownHtmlExporter.GenerateBreakdownHtml(rating, map.Info._beatsPerMinute);

                File.WriteAllText(outputPath, html);
                Console.WriteLine($"✓ Difficulty breakdown exported to: {outputPath}");
                
                OpenInBrowser(outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading map: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        private string GenerateMultiDifficultySwingHtml(BeatmapV3 map, List<(string characteristic, string difficulty, Ratings rating)> difficulties)
        {
            // Create a comprehensive data structure for all difficulties
            var allDifficultiesData = difficulties.Select((diff, idx) => new
            {
                Index = idx,
                Characteristic = diff.characteristic,
                Difficulty = diff.difficulty,
                TotalSwings = diff.rating.SwingData.Count,
                Swings = diff.rating.SwingData.Select((s, index) => new
                {
                    Index = index,
                    Time = Math.Round(s.Beat, 3),
                    Hand = s.Notes[0].Type == 0 ? "Red" : "Blue",
                    Line = s.Notes[0].X,
                    Layer = s.Notes[0].Y,
                    EntryX = Math.Round(s.EntryPosition.x, 3),
                    EntryY = Math.Round(s.EntryPosition.y, 3),
                    ExitX = Math.Round(s.ExitPosition.x, 3),
                    ExitY = Math.Round(s.ExitPosition.y, 3),
                    Angle = Math.Round(s.Direction, 1),
                    Direction = Benchmark.AngleToDirection(s.Direction),
                    Parity = s.Forehand ? "Forehand" : "Backhand",
                    PatternType = s.PatternType,
                    ParityError = s.ParityErrors,
                    BombAvoidance = s.BombAvoidance,
                    Difficulty = Math.Round(s.SwingDiff, 3)
                }).ToList()
            }).ToList();

            var jsonData = JsonConvert.SerializeObject(allDifficultiesData);

            return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Detailed Swing Analysis - All Difficulties - {map.Info._songName}</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js""></script>
    <style>
        * {{
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }}
        
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #333;
            padding: 20px;
            min-height: 100vh;
        }}
        
        .container {{
            max-width: 1600px;
            margin: 0 auto;
            background: white;
            border-radius: 15px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }}
        
        .header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 40px;
            text-align: center;
        }}
        
        .header h1 {{
            font-size: 2.5em;
            margin-bottom: 10px;
        }}
        
        .header .subtitle {{
            font-size: 1.2em;
            opacity: 0.9;
        }}
        
        .content {{
            padding: 40px;
        }}
        
        .difficulty-selector {{
            display: flex;
            flex-wrap: wrap;
            gap: 10px;
            margin-bottom: 30px;
            padding: 20px;
            background: #f8f9fa;
            border-radius: 10px;
        }}
        
        .diff-group {{
            display: flex;
            gap: 5px;
            align-items: center;
        }}
        
        .diff-group-label {{
            font-weight: bold;
            margin-right: 5px;
        }}
        
        .diff-btn {{
            padding: 12px 24px;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            font-size: 1em;
            font-weight: bold;
            transition: all 0.3s;
            box-shadow: 0 2px 5px rgba(0,0,0,0.1);
        }}
        
        .diff-btn:hover {{
            transform: translateY(-2px);
            box-shadow: 0 4px 10px rgba(0,0,0,0.2);
        }}
        
        .diff-btn.active {{
            transform: scale(1.05);
            box-shadow: 0 6px 15px rgba(0,0,0,0.3);
        }}
        
        .diff-Easy {{ background: #4CAF50; color: white; }}
        .diff-Normal {{ background: #2196F3; color: white; }}
        .diff-Hard {{ background: #FF9800; color: white; }}
        .diff-Expert {{ background: #F44336; color: white; }}
        .diff-ExpertPlus {{ background: #9C27B0; color: white; }}
        
        .controls {{
            background: #f8f9fa;
            padding: 20px;
            border-radius: 10px;
            margin-bottom: 30px;
            display: flex;
            gap: 15px;
            flex-wrap: wrap;
            align-items: center;
        }}
        
        .controls input, .controls select {{
            padding: 10px 15px;
            border: 2px solid #ddd;
            border-radius: 8px;
            font-size: 1em;
        }}
        
        .controls button {{
            padding: 10px 20px;
            border: none;
            border-radius: 8px;
            background: #667eea;
            color: white;
            font-weight: bold;
            cursor: pointer;
            transition: all 0.3s;
        }}
        
        .controls button:hover {{
            background: #764ba2;
            transform: translateY(-2px);
        }}
        
        .chart-section {{
            margin: 30px 0;
            padding: 20px;
            background: #f8f9fa;
            border-radius: 10px;
        }}
        
        .table-container {{
            overflow-x: auto;
            margin-top: 20px;
        }}
        
        table {{
            width: 100%;
            border-collapse: collapse;
            background: white;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
            border-radius: 8px;
            overflow: hidden;
        }}
        
        th {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 15px;
            text-align: left;
            font-weight: bold;
            position: sticky;
            top: 0;
            z-index: 10;
            cursor: pointer;
            user-select: none;
            transition: background 0.3s;
        }}
        
        th:hover {{
            background: linear-gradient(135deg, #764ba2 0%, #667eea 100%);
        }}
        
        th.sortable {{
            position: relative;
            padding-right: 30px;
        }}
        
        th.sortable::after {{
            content: '⇅';
            position: absolute;
            right: 10px;
            opacity: 0.5;
        }}
        
        th.sorted-asc::after {{
            content: '▲';
            opacity: 1;
        }}
        
        th.sorted-desc::after {{
            content: '▼';
            opacity: 1;
        }}
        
        td {{
            padding: 12px 15px;
            border-bottom: 1px solid #eee;
        }}
        
        tr:hover {{
            background: #f8f9fa;
        }}
        
        .badge {{
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.85em;
            font-weight: bold;
        }}
        
        .badge-red {{ background: #ffcdd2; color: #c62828; }}
        .badge-blue {{ background: #bbdefb; color: #1565c0; }}
        .badge-forehand {{ background: #c8e6c9; color: #2e7d32; }}
        .badge-backhand {{ background: #fff9c4; color: #f57f17; }}
        .badge-reset {{ background: #ffccbc; color: #bf360c; }}
        
        h2 {{
            color: #333;
            margin: 30px 0 20px 0;
            padding-bottom: 10px;
            border-bottom: 3px solid #667eea;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>Detailed Swing Analysis - All Difficulties</h1>
            <div class=""subtitle"">{map.Info._songName}</div>
            <div id=""currentDiffInfo"" style=""margin-top: 10px;""></div>
        </div>
        
        <div class=""content"">
            <div class=""difficulty-selector"" id=""difficultySelector""></div>
            
            <div class=""controls"">
                <input type=""text"" id=""searchBox"" placeholder=""Search by time, hand, angle..."">
                <select id=""filterHand"">
                    <option value="""">All Hands</option>
                    <option value=""Red"">Red Hand</option>
                    <option value=""Blue"">Blue Hand</option>
                </select>
                <select id=""filterParity"">
                    <option value="""">All Parities</option>
                    <option value=""Forehand"">Forehand</option>
                    <option value=""Backhand"">Backhand</option>
                </select>
                <select id=""filterPattern"">
                    <option value="""">All Pattern Types</option>
                    <option value=""Multi-Notes"">Multi-Notes Only</option>
                    <option value=""Single"">Single</option>
                    <option value=""Stack"">Stack</option>
                    <option value=""Tower"">Tower</option>
                    <option value=""Window"">Window</option>
                    <option value=""Slanted Window"">Slanted Window</option>
                    <option value=""Slider"">Slider</option>
                    <option value=""Curved Slider"">Curved Slider</option>
                </select>
                <label>
                    <input type=""checkbox"" id=""filterParityError""> Show Parity Errors Only
                </label>
                <label>
                    <input type=""checkbox"" id=""filterBombAvoidance""> Show Bomb Avoidances Only
                </label>
                <button onclick=""resetFilters()"">Reset Filters</button>
            </div>
            
            <div class=""chart-section"">
                <h2>📊 Difficulty Distribution</h2>
                <canvas id=""difficultyChart""></canvas>
            </div>
            
            <h2>🎯 Swing Details</h2>
            <div class=""table-container"">
                <table id=""swingTable"">
                    <thead>
                        <tr>
                            <th class=""sortable"" data-column=""Index"" data-type=""number"">#</th>
                            <th class=""sortable"" data-column=""Time"" data-type=""number"">Beat</th>
                            <th class=""sortable"" data-column=""Hand"" data-type=""string"">Hand</th>
                            <th class=""sortable"" data-column=""Position"" data-type=""string"">Position</th>
                            <th class=""sortable"" data-column=""Entry"" data-type=""string"">Entry</th>
                            <th class=""sortable"" data-column=""Exit"" data-type=""string"">Exit</th>
                            <th class=""sortable"" data-column=""Angle"" data-type=""number"">Angle / Direction</th>
                            <th class=""sortable"" data-column=""Parity"" data-type=""string"">Parity</th>
                            <th class=""sortable"" data-column=""PatternType"" data-type=""string"">Pattern Type</th>
                            <th>Flags</th>
                            <th class=""sortable"" data-column=""Difficulty"" data-type=""number"">Difficulty</th>
                        </tr>
                    </thead>
                    <tbody id=""swingTableBody""></tbody>
                </table>
            </div>
        </div>
    </div>
    
    <script>
        const allDifficulties = {jsonData};
        let currentDifficultyIndex = 0;
        let swingData = [];
        let filteredData = [];
        let currentSortColumn = null;
        let currentSortDirection = 'asc';
        let currentChart = null;
        
        function createDifficultyButtons() {{
            const selector = document.getElementById('difficultySelector');
            const grouped = {{}};
            
            allDifficulties.forEach((data, index) => {{
                const char = data.Characteristic;
                if (!grouped[char]) grouped[char] = [];
                grouped[char].push({{ data, index }});
            }});
            
            Object.keys(grouped).forEach(char => {{
                const group = document.createElement('div');
                group.className = 'diff-group';
                
                const label = document.createElement('span');
                label.className = 'diff-group-label';
                label.textContent = char + ': ';
                group.appendChild(label);
                
                grouped[char].forEach(({{ data, index }}) => {{
                    const btn = document.createElement('button');
                    btn.className = `diff-btn diff-${{data.Difficulty}}`;
                    btn.textContent = data.Difficulty;
                    btn.onclick = () => switchDifficulty(index);
                    btn.id = `diff-btn-${{index}}`;
                    group.appendChild(btn);
                }});
                
                selector.appendChild(group);
            }});
            
            if (allDifficulties.length > 0) switchDifficulty(0);
        }}
        
        function switchDifficulty(index) {{
            currentDifficultyIndex = index;
            const data = allDifficulties[index];
            swingData = data.Swings;
            filteredData = [...swingData];
            
            // Update active button
            document.querySelectorAll('.diff-btn').forEach(btn => btn.classList.remove('active'));
            document.getElementById(`diff-btn-${{index}}`).classList.add('active');
            
            // Update header info
            document.getElementById('currentDiffInfo').textContent = 
                `${{data.Characteristic}} - ${{data.Difficulty}} • Total Swings: ${{data.TotalSwings}}`;
            
            // Reset filters
            currentSortColumn = null;
            currentSortDirection = 'asc';
            resetFilters();
            
            // Update chart
            updateChart();
            
            // Render table
            renderTable();
        }}
        
        function renderTable() {{
            const tbody = document.getElementById('swingTableBody');
            tbody.innerHTML = filteredData.map(swing => `
                <tr>
                    <td>${{swing.Index + 1}}</td>
                    <td>${{swing.Time}}</td>
                    <td><span class=""badge badge-${{swing.Hand.toLowerCase()}}"">${{swing.Hand}}</span></td>
                    <td>(${{swing.Line}}, ${{swing.Layer}})</td>
                    <td>(${{swing.EntryX}}, ${{swing.EntryY}})</td>
                    <td>(${{swing.ExitX}}, ${{swing.ExitY}})</td>
                    <td>${{swing.Angle}}° (${{swing.Direction}})</td>
                    <td><span class=""badge badge-${{swing.Parity.toLowerCase()}}"">${{swing.Parity}}</span></td>
                    <td>${{swing.PatternType}}</td>
                    <td>
                        ${{swing.ParityError ? '<span class=""badge badge-reset"">PE</span>' : ''}}
                        ${{swing.BombAvoidance ? '<span class=""badge badge-reset"">BA</span>' : ''}}
                    </td>
                    <td><strong>${{swing.Difficulty}}</strong></td>
                </tr>
            `).join('');
        }}
        
        function applyFilters() {{
            const search = document.getElementById('searchBox').value.toLowerCase();
            const handFilter = document.getElementById('filterHand').value;
            const parityFilter = document.getElementById('filterParity').value;
            const patternFilter = document.getElementById('filterPattern').value;
            const parityErrorOnly = document.getElementById('filterParityError').checked;
            const bombAvoidanceOnly = document.getElementById('filterBombAvoidance').checked;
            
            filteredData = swingData.filter(swing => {{
                if (search && !JSON.stringify(swing).toLowerCase().includes(search)) return false;
                if (handFilter && swing.Hand !== handFilter) return false;
                if (parityFilter && swing.Parity !== parityFilter) return false;
                if (patternFilter) {{
                    if (patternFilter === 'Multi-Notes') {{
                        // Filter to show only multi-note patterns (everything except Single)
                        if (swing.PatternType === 'Single') return false;
                    }} else {{
                        // Filter by specific pattern type
                        if (swing.PatternType !== patternFilter) return false;
                    }}
                }}
                if (parityErrorOnly && !swing.ParityError) return false;
                if (bombAvoidanceOnly && !swing.BombAvoidance) return false;
                return true;
            }});
            
            renderTable();
        }}
        
        function resetFilters() {{
            document.getElementById('searchBox').value = '';
            document.getElementById('filterHand').value = '';
            document.getElementById('filterParity').value = '';
            document.getElementById('filterPattern').value = '';
            document.getElementById('filterParityError').checked = false;
            document.getElementById('filterBombAvoidance').checked = false;
            applyFilters();
        }}
        
        document.getElementById('searchBox').addEventListener('input', applyFilters);
        document.getElementById('filterHand').addEventListener('change', applyFilters);
        document.getElementById('filterParity').addEventListener('change', applyFilters);
        document.getElementById('filterPattern').addEventListener('change', applyFilters);
        document.getElementById('filterParityError').addEventListener('change', applyFilters);
        document.getElementById('filterBombAvoidance').addEventListener('change', applyFilters);
        
        function sortTable(column, type) {{
            if (currentSortColumn === column) {{
                currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
            }} else {{
                currentSortColumn = column;
                currentSortDirection = 'asc';
            }}
            
            filteredData.sort((a, b) => {{
                let aVal, bVal;
                
                if (type === 'number') {{
                    aVal = a[column];
                    bVal = b[column];
                }} else {{
                    aVal = String(a[column]).toLowerCase();
                    bVal = String(b[column]).toLowerCase();
                }}
                
                let comparison = 0;
                if (aVal < bVal) comparison = -1;
                if (aVal > bVal) comparison = 1;
                
                return currentSortDirection === 'asc' ? comparison : -comparison;
            }});
            
            document.querySelectorAll('th.sortable').forEach(th => {{
                th.classList.remove('sorted-asc', 'sorted-desc');
            }});
            
            const activeHeader = document.querySelector('th[data-column=""' + column + '""]');
            if (activeHeader) {{
                activeHeader.classList.add('sorted-' + currentSortDirection);
            }}
            
            renderTable();
        }}
        
        document.querySelectorAll('th.sortable').forEach(th => {{
            th.addEventListener('click', () => {{
                const column = th.getAttribute('data-column');
                const type = th.getAttribute('data-type');
                sortTable(column, type);
            }});
        }});
        
        function updateChart() {{
            const ctx = document.getElementById('difficultyChart').getContext('2d');
            
            if (currentChart) {{
                currentChart.destroy();
            }}
            
            const buckets = 20;
            const maxDiff = Math.max(...swingData.map(s => s.Difficulty));
            const bucketSize = maxDiff / buckets;
            const histogram = new Array(buckets).fill(0);
            
            swingData.forEach(swing => {{
                const bucket = Math.min(Math.floor(swing.Difficulty / bucketSize), buckets - 1);
                histogram[bucket]++;
            }});
            
            currentChart = new Chart(ctx, {{
                type: 'line',
                data: {{
                    labels: Array.from({{ length: buckets }}, (_, i) => (i * bucketSize).toFixed(1)),
                    datasets: [{{
                        label: 'Number of Swings',
                        data: histogram,
                        borderColor: 'rgba(102, 126, 234, 1)',
                        backgroundColor: 'rgba(102, 126, 234, 0.2)',
                        fill: true,
                        tension: 0.4
                    }}]
                }},
                options: {{
                    responsive: true,
                    plugins: {{
                        legend: {{
                            display: true
                        }}
                    }},
                    scales: {{
                        x: {{
                            title: {{
                                display: true,
                                text: 'Difficulty Range'
                            }}
                        }},
                        y: {{
                            title: {{
                                display: true,
                                text: 'Count'
                            }},
                            beginAtZero: true
                        }}
                    }}
                }}
            }});
        }}
        
        createDifficultyButtons();
    </script>
</body>
</html>";
        }
    }
}


