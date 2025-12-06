using beatleader_analyzer;
using beatleader_analyzer.BeatmapScanner.Data;
using beatleader_parser;
using Parser.Map;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

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
            Console.WriteLine($"Note: Local file loading not supported by parser library.");
            Console.WriteLine($"Please use a BeatSaver URL instead.");
            Console.WriteLine($"Example: export url https://r2cdn.beatsaver.com/[hash].zip");
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
                    Sliders = rating.Patterns.Sliders,
                    CurvedSliders = rating.Patterns.CurvedSliders,
                    Loloppes = rating.Patterns.Loloppes,
                    DodgeWalls = rating.Patterns.DodgeWalls,
                    CrouchWalls = rating.Patterns.CrouchWalls,
                    Resets = rating.Patterns.Resets,
                    BombResets = rating.Patterns.BombResets
                },
                SwingCount = rating.SwingData.Count,
                TopDifficultSwings = rating.SwingData
                    .OrderByDescending(s => s.SwingDiff)
                    .Take(10)
                    .Select((s, index) => new
                    {
                        Rank = index + 1,
                        Time = Math.Round(s.Time, 2),
                        Difficulty = Math.Round(s.SwingDiff, 3),
                        Hand = s.Start.Type == 0 ? "Red" : "Blue",
                        Angle = Math.Round(s.Angle, 1),
                        Parity = s.Forehand ? "Forehand" : "Backhand",
                        Reset = s.Reset,
                        BombReset = s.BombReset,
                        AngleStrain = Math.Round(s.AngleStrain, 3),
                        PathStrain = Math.Round(s.PathStrain, 3)
                    })
                    .ToList(),
                Statistics = new
                {
                    AverageDifficulty = Math.Round(rating.SwingData.Average(s => s.SwingDiff), 3),
                    MaxDifficulty = Math.Round(rating.SwingData.Max(s => s.SwingDiff), 3),
                    ResetPercentage = Math.Round((double)rating.Patterns.Resets / rating.SwingData.Count * 100, 2),
                    BombResetPercentage = Math.Round((double)rating.Patterns.BombResets / rating.SwingData.Count * 100, 2),
                    RedHandSwings = rating.SwingData.Count(s => s.Start.Type == 0),
                    BlueHandSwings = rating.SwingData.Count(s => s.Start.Type == 1)
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
                        <div class=""value"">${{data.Ratings.Pass.toFixed(2)}}</div>
                    </div>
                    <div class=""stat-card"">
                        <h3>Tech Difficulty</h3>
                        <div class=""value"">${{data.Ratings.Tech.toFixed(2)}}</div>
                    </div>
                    <div class=""stat-card"">
                        <h3>Total Swings</h3>
                        <div class=""value"">${{data.SwingCount}}</div>
                    </div>
                    <div class=""stat-card"">
                        <h3>Note Count Nerf</h3>
                        <div class=""value"">${{data.Ratings.Nerf.toFixed(3)}}</div>
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
                        <div class=""count"">${{data.Patterns.Sliders}}</div>
                        <div class=""label"">Sliders</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.CurvedSliders}}</div>
                        <div class=""label"">Curved Sliders</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.Loloppes}}</div>
                        <div class=""label"">Loloppes</div>
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
                        <div class=""count"">${{data.Patterns.Resets}}</div>
                        <div class=""label"">Resets</div>
                    </div>
                    <div class=""pattern-badge"">
                        <div class=""count"">${{data.Patterns.BombResets}}</div>
                        <div class=""label"">Bomb Resets</div>
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
                                    <strong>Time:</strong> ${{swing.Time}}s | 
                                    <strong>Difficulty:</strong> ${{swing.Difficulty}} | 
                                    <strong>Angle:</strong> ${{swing.Angle}}° | 
                                    <strong>Parity:</strong> ${{swing.Parity}}
                                </div>
                                <div style=""margin-top: 5px;"">
                                    <span class=""badge badge-${{swing.Hand.toLowerCase()}}"">${{swing.Hand}} Hand</span>
                                    ${{swing.Reset ? '<span class=""badge badge-reset"">RESET</span>' : ''}}
                                    ${{swing.BombReset ? '<span class=""badge badge-bomb"">BOMB RESET</span>' : ''}}
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
                    labels: ['Avg Difficulty', 'Max Difficulty', 'Reset %', 'Bomb Reset %'],
                    datasets: [{{
                        label: 'Statistics',
                        data: [
                            data.Statistics.AverageDifficulty,
                            data.Statistics.MaxDifficulty,
                            data.Statistics.ResetPercentage,
                            data.Statistics.BombResetPercentage
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
                    Time = Math.Round(s.Time, 2),
                    Hand = s.Start.Type == 0 ? "Red" : "Blue",
                    Position = new
                    {
                        Line = s.Start.Line,
                        Layer = s.Start.Layer
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
                    Angle = Math.Round(s.Angle, 1),
                    Parity = s.Forehand ? "Forehand" : "Backhand",
                    Reset = s.Reset,
                    BombReset = s.BombReset,
                    Difficulty = Math.Round(s.SwingDiff, 3),
                    Metrics = new
                    {
                        AngleStrain = Math.Round(s.AngleStrain, 3),
                        PathStrain = Math.Round(s.PathStrain, 3),
                        ExcessDistance = Math.Round(s.ExcessDistance, 3),
                        PositionComplexity = Math.Round(s.PositionComplexity, 3),
                        CurveComplexity = Math.Round(s.CurveComplexity, 3),
                        SwingFrequency = Math.Round(s.SwingFrequency, 3)
                    }
                }).ToList()
            };

            var html = GenerateDetailedSwingHtml(map, characteristic, difficulty, rating);

            File.WriteAllText(outputPath, html);
            Console.WriteLine($"✓ Detailed swing data exported to: {outputPath}");
            
            OpenInBrowser(outputPath);
        }

        private string GenerateDetailedSwingHtml(BeatmapV3 map, string characteristic, string difficulty, Ratings rating)
        {
            var swingDataJson = JsonConvert.SerializeObject(rating.SwingData.Select((s, index) => new
            {
                Index = index,
                Time = Math.Round(s.Time, 2),
                Hand = s.Start.Type == 0 ? "Red" : "Blue",
                Line = s.Start.Line,
                Layer = s.Start.Layer,
                EntryX = Math.Round(s.EntryPosition.x, 3),
                EntryY = Math.Round(s.EntryPosition.y, 3),
                ExitX = Math.Round(s.ExitPosition.x, 3),
                ExitY = Math.Round(s.ExitPosition.y, 3),
                Angle = Math.Round(s.Angle, 1),
                Parity = s.Forehand ? "Forehand" : "Backhand",
                Reset = s.Reset,
                BombReset = s.BombReset,
                Difficulty = Math.Round(s.SwingDiff, 3),
                AngleStrain = Math.Round(s.AngleStrain, 3),
                PathStrain = Math.Round(s.PathStrain, 3),
                ExcessDistance = Math.Round(s.ExcessDistance, 3),
                PositionComplexity = Math.Round(s.PositionComplexity, 3),
                CurveComplexity = Math.Round(s.CurveComplexity, 3),
                SwingFrequency = Math.Round(s.SwingFrequency, 3)
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
                <label>
                    <input type=""checkbox"" id=""filterReset""> Show Resets Only
                </label>
                <label>
                    <input type=""checkbox"" id=""filterBombReset""> Show Bomb Resets Only
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
                            <th>#</th>
                            <th>Time</th>
                            <th>Hand</th>
                            <th>Position</th>
                            <th>Entry</th>
                            <th>Exit</th>
                            <th>Angle</th>
                            <th>Parity</th>
                            <th>Flags</th>
                            <th>Difficulty</th>
                            <th>Angle Strain</th>
                            <th>Path Strain</th>
                            <th>Frequency</th>
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
        
        function renderTable() {{
            const tbody = document.getElementById('swingTableBody');
            tbody.innerHTML = filteredData.map(swing => `
                <tr>
                    <td>${{swing.Index + 1}}</td>
                    <td>${{swing.Time}}s</td>
                    <td><span class=""badge badge-${{swing.Hand.toLowerCase()}}"">${{swing.Hand}}</span></td>
                    <td>(${{swing.Line}}, ${{swing.Layer}})</td>
                    <td>(${{swing.EntryX}}, ${{swing.EntryY}})</td>
                    <td>(${{swing.ExitX}}, ${{swing.ExitY}})</td>
                    <td>${{swing.Angle}}°</td>
                    <td><span class=""badge badge-${{swing.Parity.toLowerCase()}}"">${{swing.Parity}}</span></td>
                    <td>
                        ${{swing.Reset ? '<span class=""badge badge-reset"">R</span>' : ''}}
                        ${{swing.BombReset ? '<span class=""badge badge-reset"">B</span>' : ''}}
                    </td>
                    <td><strong>${{swing.Difficulty}}</strong></td>
                    <td>${{swing.AngleStrain}}</td>
                    <td>${{swing.PathStrain}}</td>
                    <td>${{swing.SwingFrequency}}</td>
                </tr>
            `).join('');
        }}
        
        function applyFilters() {{
            const search = document.getElementById('searchBox').value.toLowerCase();
            const handFilter = document.getElementById('filterHand').value;
            const parityFilter = document.getElementById('filterParity').value;
            const resetOnly = document.getElementById('filterReset').checked;
            const bombResetOnly = document.getElementById('filterBombReset').checked;
            
            filteredData = swingData.filter(swing => {{
                if (search && !JSON.stringify(swing).toLowerCase().includes(search)) return false;
                if (handFilter && swing.Hand !== handFilter) return false;
                if (parityFilter && swing.Parity !== parityFilter) return false;
                if (resetOnly && !swing.Reset) return false;
                if (bombResetOnly && !swing.BombReset) return false;
                return true;
            }});
            
            renderTable();
        }}
        
        function resetFilters() {{
            document.getElementById('searchBox').value = '';
            document.getElementById('filterHand').value = '';
            document.getElementById('filterParity').value = '';
            document.getElementById('filterReset').checked = false;
            document.getElementById('filterBombReset').checked = false;
            applyFilters();
        }}
        
        document.getElementById('searchBox').addEventListener('input', applyFilters);
        document.getElementById('filterHand').addEventListener('change', applyFilters);
        document.getElementById('filterParity').addEventListener('change', applyFilters);
        document.getElementById('filterReset').addEventListener('change', applyFilters);
        document.getElementById('filterBombReset').addEventListener('change', applyFilters);
        
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
    }
}
