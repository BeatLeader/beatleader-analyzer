using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Benchmark
{
    /// <summary>
    /// Exports detailed difficulty breakdown showing how all intermediate values contribute to final ratings.
    /// </summary>
    public static class DifficultyBreakdownHtmlExporter
    {
        private const double PASS_CALIBRATION_FACTOR = 0.8;
        private const double ONE_SABER_NERF = 0.35;
        private const double BALANCED_TECH_SCALER = 10.0;

        public static string GenerateBreakdownHtml(Ratings ratings, float bpm)
        {
            var html = new StringBuilder();
            
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"en\">");
            html.AppendLine("<head>");
            html.AppendLine("    <meta charset=\"UTF-8\">");
            html.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            html.AppendLine($"    <title>Difficulty Breakdown - {ratings.Characteristic} {ratings.Difficulty}</title>");
            html.AppendLine("    <style>");
            html.AppendLine(GetStyles());
            html.AppendLine("    </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            
            // Header
            html.AppendLine("    <div class=\"header\">");
            html.AppendLine($"        <h1>Difficulty Breakdown Analysis</h1>");
            html.AppendLine($"        <p class=\"subtitle\">{ratings.Characteristic} - {ratings.Difficulty} | BPM: {bpm}</p>");
            html.AppendLine("    </div>");
            
            html.AppendLine("    <div class=\"container\">");
            
            // Final ratings summary
            GenerateFinalRatingsSummary(html, ratings);
            
            // Pass rating breakdown
            GeneratePassRatingBreakdown(html, ratings, bpm);
            
            // Tech rating breakdown
            GenerateTechRatingBreakdown(html, ratings);
            
            // Per-swing detailed table
            GeneratePerSwingTable(html, ratings.SwingData, bpm);
            
            html.AppendLine("    </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            
            return html.ToString();
        }

        private static void GenerateFinalRatingsSummary(StringBuilder html, Ratings ratings)
        {
            html.AppendLine("        <div class=\"section\">");
            html.AppendLine("            <h2>Final Ratings</h2>");
            html.AppendLine("            <div class=\"grid-2\">");
            html.AppendLine($"                <div class=\"metric-box pass\">");
            html.AppendLine($"                    <div class=\"metric-label\">Balanced Pass</div>");
            html.AppendLine($"                    <div class=\"metric-value\">{ratings.PassRating:F2}</div>");
            html.AppendLine($"                </div>");
            html.AppendLine($"                <div class=\"metric-box tech\">");
            html.AppendLine($"                    <div class=\"metric-label\">Balanced Tech</div>");
            html.AppendLine($"                    <div class=\"metric-value\">{ratings.TechRating:F2}</div>");
            html.AppendLine($"                </div>");
            html.AppendLine("            </div>");
            html.AppendLine($"            <div class=\"info-box\">");
            html.AppendLine($"                <p><strong>Low Note Nerf:</strong> {ratings.LowNoteNerf:F4} (applied to final rating)</p>");
            html.AppendLine($"                <p><strong>Total Swings:</strong> {ratings.SwingData.Count}</p>");
            html.AppendLine($"            </div>");
            html.AppendLine("        </div>");
        }

        private static void GeneratePassRatingBreakdown(StringBuilder html, Ratings ratings, float bpm)
        {
            var redSwings = ratings.SwingData.Where(x => x.Notes[0].Type == 0).ToList();
            var blueSwings = ratings.SwingData.Where(x => x.Notes[0].Type == 1).ToList();
            var allSwings = ratings.SwingData;

            html.AppendLine("        <div class=\"section\">");
            html.AppendLine("            <h2>Pass Rating Calculation</h2>");
            
            // Window-based averaging
            html.AppendLine("            <h3>1. Window-Based Peak Difficulty</h3>");
            html.AppendLine("            <p class=\"explanation\">The pass rating is calculated by averaging swing difficulties over multiple window sizes and taking the maximum average for each window.</p>");
            
            var windowSizes = new int[] { 8, 16, 32, 64, 128 };
            double passDiffRed = 0.0;
            double passDiffBlue = 0.0;
            double passDiffCombined = 0.0;

            html.AppendLine("            <table>");
            html.AppendLine("                <tr>");
            html.AppendLine("                    <th>Window Size</th>");
            html.AppendLine("                    <th>Red Max Avg</th>");
            html.AppendLine("                    <th>Blue Max Avg</th>");
            html.AppendLine("                    <th>Combined Max Avg</th>");
            html.AppendLine("                </tr>");

            foreach (var windowSize in windowSizes)
            {
                double redMax = 0;
                double blueMax = 0;
                double combinedMax = 0;

                if (redSwings.Count > 1)
                {
                    redMax = CalculateMaxWindowAverage(redSwings, windowSize / 2);
                    passDiffRed += redMax;
                }
                if (blueSwings.Count > 1)
                {
                    blueMax = CalculateMaxWindowAverage(blueSwings, windowSize / 2);
                    passDiffBlue += blueMax;
                }
                combinedMax = CalculateMaxWindowAverage(allSwings, windowSize);
                passDiffCombined += combinedMax;

                html.AppendLine("                <tr>");
                html.AppendLine($"                    <td>{windowSize}</td>");
                html.AppendLine($"                    <td>{redMax:F4}</td>");
                html.AppendLine($"                    <td>{blueMax:F4}</td>");
                html.AppendLine($"                    <td>{combinedMax:F4}</td>");
                html.AppendLine("                </tr>");
            }

            passDiffRed /= windowSizes.Length;
            passDiffBlue /= windowSizes.Length;
            passDiffCombined /= windowSizes.Length;

            html.AppendLine("                <tr class=\"total-row\">");
            html.AppendLine($"                    <td><strong>Average</strong></td>");
            html.AppendLine($"                    <td><strong>{passDiffRed:F4}</strong></td>");
            html.AppendLine($"                    <td><strong>{passDiffBlue:F4}</strong></td>");
            html.AppendLine($"                    <td><strong>{passDiffCombined:F4}</strong></td>");
            html.AppendLine("                </tr>");
            html.AppendLine("            </table>");

            // Hand balance calculation
            html.AppendLine("            <h3>2. Hand Balance Adjustment</h3>");
            double easierHand = Math.Min(passDiffRed, passDiffBlue);
            double harderHand = Math.Max(passDiffRed, passDiffBlue);
            double handsRatio = 1.0;
            
            if (harderHand > 0 || passDiffCombined > 0)
            {
                handsRatio = Math.Min(easierHand / Math.Max(Math.Min(harderHand, passDiffCombined), 0.0001), 1.0);
            }
            
            double nerfMultiplier = 1.0 - (1.0 - handsRatio) * ONE_SABER_NERF;

            html.AppendLine("            <div class=\"calculation-steps\">");
            html.AppendLine($"                <p><strong>Easier Hand:</strong> {easierHand:F4}</p>");
            html.AppendLine($"                <p><strong>Harder Hand:</strong> {harderHand:F4}</p>");
            html.AppendLine($"                <p><strong>Hands Ratio:</strong> {handsRatio:F4} = min(easier / max(min(harder, combined), 0.0001), 1.0)</p>");
            html.AppendLine($"                <p><strong>One Saber Nerf Constant:</strong> {ONE_SABER_NERF}</p>");
            html.AppendLine($"                <p><strong>Nerf Multiplier:</strong> {nerfMultiplier:F4} = 1.0 - (1.0 - {handsRatio:F4}) × {ONE_SABER_NERF}</p>");
            html.AppendLine("            </div>");

            // Final calculation
            html.AppendLine("            <h3>3. Final Pass Rating</h3>");
            double balancedPass = passDiffCombined * nerfMultiplier * PASS_CALIBRATION_FACTOR;
            
            html.AppendLine("            <div class=\"calculation-steps\">");
            html.AppendLine($"                <p><strong>Formula:</strong> Combined Average × Nerf Multiplier × Calibration Factor</p>");
            html.AppendLine($"                <p><strong>Calculation:</strong> {passDiffCombined:F4} × {nerfMultiplier:F4} × {PASS_CALIBRATION_FACTOR} = <strong class=\"highlight\">{balancedPass:F4}</strong></p>");
            html.AppendLine($"                <p><strong>With Low Note Nerf Applied:</strong> {balancedPass:F4} × {ratings.LowNoteNerf:F4} = <strong class=\"highlight\">{ratings.PassRating:F2}</strong></p>");
            html.AppendLine("            </div>");
            
            html.AppendLine("        </div>");
        }

        private static void GenerateTechRatingBreakdown(StringBuilder html, Ratings ratings)
        {
            html.AppendLine("        <div class=\"section\">");
            html.AppendLine("            <h2>Tech Rating Calculation</h2>");
            
            html.AppendLine("            <h3>1. Top 75% Angle + Path Strain</h3>");
            html.AppendLine("            <p class=\"explanation\">Tech rating is based on the top 75% most difficult swings (by AngleStrain + PathStrain), excluding the easiest 25%.</p>");
            
            var sortedSwings = ratings.SwingData.OrderBy(s => s.AngleStrain + s.PathStrain).ToList();
            int startIndex = (int)(sortedSwings.Count * 0.25);
            var top75Percent = sortedSwings.Skip(startIndex).ToList();
            
            double sumAnglePath = top75Percent.Sum(s => s.AngleStrain + s.PathStrain);
            double avgAnglePath = sumAnglePath / top75Percent.Count;
            
            html.AppendLine("            <div class=\"calculation-steps\">");
            html.AppendLine($"                <p><strong>Total Swings:</strong> {sortedSwings.Count}</p>");
            html.AppendLine($"                <p><strong>Top 75% Count:</strong> {top75Percent.Count} (excluding bottom {startIndex})</p>");
            html.AppendLine($"                <p><strong>Sum of (AngleStrain + PathStrain):</strong> {sumAnglePath:F4}</p>");
            html.AppendLine($"                <p><strong>Average:</strong> {avgAnglePath:F4}</p>");
            html.AppendLine("            </div>");

            // Pass dependency
            html.AppendLine("            <h3>2. Pass-Dependent Scaling</h3>");
            double passScaler = 1.0 - Math.Pow(1.4, -ratings.PassRating);
            
            html.AppendLine("            <div class=\"calculation-steps\">");
            html.AppendLine($"                <p><strong>Formula:</strong> 1.0 - 1.4<sup>-BalancedPass</sup></p>");
            html.AppendLine($"                <p><strong>Calculation:</strong> 1.0 - 1.4<sup>-{ratings.PassRating:F4}</sup> = <strong>{passScaler:F4}</strong></p>");
            html.AppendLine($"                <p class=\"explanation\">This scaling ensures tech rating increases with pass difficulty (harder maps have more impactful tech).</p>");
            html.AppendLine("            </div>");

            // Final calculation
            html.AppendLine("            <h3>3. Final Tech Rating</h3>");
            double balancedTech = avgAnglePath * passScaler * BALANCED_TECH_SCALER;
            
            html.AppendLine("            <div class=\"calculation-steps\">");
            html.AppendLine($"                <p><strong>Formula:</strong> Average × Pass Scaler × Tech Scaler Constant</p>");
            html.AppendLine($"                <p><strong>Calculation:</strong> {avgAnglePath:F4} × {passScaler:F4} × {BALANCED_TECH_SCALER} = <strong class=\"highlight\">{balancedTech:F4}</strong></p>");
            html.AppendLine($"                <p><strong>With Low Note Nerf Applied:</strong> {balancedTech:F4} × {ratings.LowNoteNerf:F4} = <strong class=\"highlight\">{ratings.TechRating:F2}</strong></p>");
            html.AppendLine("            </div>");
            
            html.AppendLine("        </div>");
        }

        private static void GeneratePerSwingTable(StringBuilder html, List<SwingData> swingData, float bpm)
        {
            html.AppendLine("        <div class=\"section\">");
            html.AppendLine("            <h2>Per-Swing Breakdown</h2>");
            html.AppendLine("            <p class=\"explanation\">Detailed breakdown of every swing showing all intermediate calculations.</p>");
            
            html.AppendLine("            <div class=\"table-container\">");
            html.AppendLine("            <table class=\"detailed-table\">");
            html.AppendLine("                <thead>");
            html.AppendLine("                    <tr>");
            html.AppendLine("                        <th rowspan=\"2\">#</th>");
            html.AppendLine("                        <th rowspan=\"2\">Beat</th>");
            html.AppendLine("                        <th rowspan=\"2\">Hand</th>");
            html.AppendLine("                        <th rowspan=\"2\">Pattern</th>");
            html.AppendLine("                        <th colspan=\"4\">Speed Components</th>");
            html.AppendLine("                        <th colspan=\"3\">Stress Components</th>");
            html.AppendLine("                        <th colspan=\"3\">Multipliers</th>");
            html.AppendLine("                        <th rowspan=\"2\">Final<br/>SwingDiff</th>");
            html.AppendLine("                    </tr>");
            html.AppendLine("                    <tr>");
            html.AppendLine("                        <th>Frequency</th>");
            html.AppendLine("                        <th>Hit<br/>Distance</th>");
            html.AppendLine("                        <th>Distance<br/>Difficulty</th>");
            html.AppendLine("                        <th>Speed</th>");
            html.AppendLine("                        <th>Angle<br/>Strain</th>");
            html.AppendLine("                        <th>Path<br/>Strain</th>");
            html.AppendLine("                        <th>Stress</th>");
            html.AppendLine("                        <th>Speed<br/>Falloff</th>");
            html.AppendLine("                        <th>Stress<br/>Mult</th>");
            html.AppendLine("                        <th>Buffs</th>");
            html.AppendLine("                    </tr>");
            html.AppendLine("                </thead>");
            html.AppendLine("                <tbody>");

            for (int i = 0; i < swingData.Count; i++)
            {
                var swing = swingData[i];
                string handClass = swing.Notes[0].Type == 0 ? "red" : "blue";
                string buffsText = GetBuffsText(swing);

                html.AppendLine("                    <tr>");
                html.AppendLine($"                        <td>{i + 1}</td>");
                html.AppendLine($"                        <td>{swing.Notes[0].JsonTime:F2}</td>");
                html.AppendLine($"                        <td class=\"{handClass}\">{(swing.Notes[0].Type == 0 ? "Red" : "Blue")}</td>");
                html.AppendLine($"                        <td>{swing.PatternType}</td>");
                html.AppendLine($"                        <td>{swing.SwingFrequency:F3}</td>");
                html.AppendLine($"                        <td>{swing.BezierCurveDistance:F3}</td>");
                html.AppendLine($"                        <td>{swing.DistanceDiff:F3}</td>");
                html.AppendLine($"                        <td>{swing.SwingSpeed:F3}</td>");
                html.AppendLine($"                        <td>{swing.AngleStrain:F3}</td>");
                html.AppendLine($"                        <td>{swing.PathStrain:F3}</td>");
                html.AppendLine($"                        <td>{swing.Stress:F3}</td>");
                html.AppendLine($"                        <td>{swing.SpeedFalloff:F3}</td>");
                html.AppendLine($"                        <td>{swing.StressMultiplier:F3}</td>");
                html.AppendLine($"                        <td title=\"{buffsText}\">{GetBuffsMultiplier(swing):F3}</td>");
                html.AppendLine($"                        <td class=\"final-value\">{swing.SwingDiff:F3}</td>");
                html.AppendLine("                    </tr>");
            }

            html.AppendLine("                </tbody>");
            html.AppendLine("            </table>");
            html.AppendLine("            </div>");
            
            // Formula explanation
            html.AppendLine("            <div class=\"info-box\">");
            html.AppendLine("                <h4>Calculation Formulas:</h4>");
            html.AppendLine("                <ul>");
            html.AppendLine("                    <li><strong>SwingFrequency:</strong> 2 / (nextBeat - prevBeat)</li>");
            html.AppendLine("                    <li><strong>DistanceDiff:</strong> HitDistance / (HitDistance + 1.8) + 1.0</li>");
            html.AppendLine("                    <li><strong>SwingSpeed:</strong> SwingFrequency × DistanceDiff × (BPM / 60)</li>");
            html.AppendLine("                    <li><strong>Stress:</strong> (AngleStrain × 0.1 + PathStrain) × HitDiff</li>");
            html.AppendLine("                    <li><strong>SpeedFalloff:</strong> 1.0 - 1.4<sup>-SwingSpeed</sup></li>");
            html.AppendLine("                    <li><strong>StressMultiplier:</strong> Stress / (Stress + 2.0) + 1.0</li>");
            html.AppendLine("                    <li><strong>SwingDiff:</strong> SwingSpeed × SpeedFalloff × StressMultiplier × NjsBuff × WallBuff × StreamBonus</li>");
            html.AppendLine("                </ul>");
            html.AppendLine("            </div>");
            
            html.AppendLine("        </div>");
        }

        private static string GetBuffsText(SwingData swing)
        {
            var buffs = new List<string>();
            if (swing.NjsBuff > 1.0) buffs.Add($"NJS: {swing.NjsBuff:F3}");
            if (swing.WallBuff > 1.0) buffs.Add($"Wall: {swing.WallBuff:F3}");
            if (swing.StreamBonusApplied) buffs.Add("Stream: 1.050");
            if (swing.ParityErrors) buffs.Add("Parity: 2.000");
            return buffs.Count > 0 ? string.Join(", ", buffs) : "None";
        }

        private static double GetBuffsMultiplier(SwingData swing)
        {
            double mult = swing.NjsBuff * swing.WallBuff;
            if (swing.StreamBonusApplied) mult *= 1.05;
            if (swing.ParityErrors) mult *= 2.0; // Already applied to speed
            return mult;
        }

        private static double CalculateMaxWindowAverage(List<SwingData> swings, int windowSize)
        {
            if (swings.Count < 2) return 0;

            double maxAvg = 0;
            var window = new Queue<double>();

            for (int i = 0; i < swings.Count; i++)
            {
                window.Enqueue(swings[i].SwingDiff);
                
                if (window.Count > windowSize)
                {
                    window.Dequeue();
                }

                if (i >= windowSize - 1)
                {
                    double avg = window.Average();
                    if (avg > maxAvg) maxAvg = avg;
                }
            }

            return maxAvg;
        }

        private static string GetStyles()
        {
            return @"
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }

        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            padding: 20px;
            min-height: 100vh;
        }

        .header {
            text-align: center;
            color: white;
            margin-bottom: 30px;
        }

        .header h1 {
            font-size: 2.5em;
            margin-bottom: 10px;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.3);
        }

        .subtitle {
            font-size: 1.2em;
            opacity: 0.9;
        }

        .container {
            max-width: 1600px;
            margin: 0 auto;
        }

        .section {
            background: white;
            border-radius: 12px;
            padding: 30px;
            margin-bottom: 30px;
            box-shadow: 0 4px 6px rgba(0,0,0,0.1);
        }

        .section h2 {
            color: #333;
            margin-bottom: 20px;
            padding-bottom: 10px;
            border-bottom: 3px solid #667eea;
        }

        .section h3 {
            color: #555;
            margin-top: 25px;
            margin-bottom: 15px;
            font-size: 1.3em;
        }

        .grid-2 {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 20px;
            margin-bottom: 20px;
        }

        .metric-box {
            padding: 30px;
            border-radius: 8px;
            text-align: center;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }

        .metric-box.pass {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
        }

        .metric-box.tech {
            background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%);
            color: white;
        }

        .metric-label {
            font-size: 1.1em;
            margin-bottom: 10px;
            opacity: 0.9;
        }

        .metric-value {
            font-size: 3em;
            font-weight: bold;
        }

        .info-box {
            background: #f8f9fa;
            border-left: 4px solid #667eea;
            padding: 20px;
            margin: 20px 0;
            border-radius: 4px;
        }

        .info-box p, .info-box ul {
            margin-bottom: 10px;
            line-height: 1.6;
        }

        .info-box ul {
            margin-left: 20px;
        }

        .explanation {
            color: #666;
            font-style: italic;
            margin-bottom: 15px;
            line-height: 1.6;
        }

        .calculation-steps {
            background: #f8f9fa;
            padding: 20px;
            border-radius: 8px;
            margin: 15px 0;
        }

        .calculation-steps p {
            margin-bottom: 12px;
            line-height: 1.8;
        }

        .highlight {
            color: #667eea;
            font-size: 1.1em;
        }

        table {
            width: 100%;
            border-collapse: collapse;
            margin: 20px 0;
        }

        table th {
            background: #667eea;
            color: white;
            padding: 12px 8px;
            text-align: center;
            font-weight: 600;
            border: 1px solid #5568d3;
            position: sticky;
            top: 0;
            z-index: 100;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }

        table td {
            padding: 10px 8px;
            text-align: center;
            border: 1px solid #ddd;
        }

        table tbody tr:nth-child(even) {
            background: #f8f9fa;
        }

        table tbody tr:hover {
            background: #e9ecef;
        }

        .total-row {
            background: #e9ecef !important;
            font-weight: bold;
        }

        .table-container {
            max-height: 800px;
            overflow: auto;
            margin: 20px 0;
            border-radius: 8px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }

        .detailed-table {
            font-size: 0.85em;
            width: 100%;
            border-collapse: collapse;
            background: white;
        }

        .detailed-table th {
            font-size: 0.9em;
            padding: 8px 4px;
        }

        .detailed-table td {
            padding: 6px 4px;
        }

        .red {
            color: #dc3545;
            font-weight: bold;
        }

        .blue {
            color: #007bff;
            font-weight: bold;
        }

        .final-value {
            background: #fffacd;
            font-weight: bold;
        }

        @media (max-width: 768px) {
            .grid-2 {
                grid-template-columns: 1fr;
            }

            .header h1 {
                font-size: 1.8em;
            }

            .section {
                padding: 20px;
            }

            .detailed-table {
                font-size: 0.7em;
            }
        }
";
        }
    }
}
