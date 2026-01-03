using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Analysis;
using WebExplorationProject.Models;

namespace WebExplorationProject.Tasks
{
    /// <summary>
    /// Task 2: Page Ranking Analysis.
    /// Analyzes crawled pages and creates rankings based on:
    /// - Search position
    /// - External references
    /// - Specialist terminology
    /// - Emotional/propaganda content (AI-assisted)
    /// </summary>
    public class RankingTask : ITask
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public string Name => "Ranking Task";
        public string Description => "Analyze crawled pages ? Generate credibility rankings (CSV/TXT)";

        public RankingTask(IConfiguration configuration, HttpClient httpClient)
        {
            _configuration = configuration;
            _httpClient = httpClient;
        }

        public async Task ExecuteAsync()
        {
            Log.Information("=== TASK 2: Page Ranking Analysis ===");
            Log.Information(Description);

            // Get data path
            var projectDir = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName
                ?? AppContext.BaseDirectory;
            var dataPath = Path.Combine(projectDir, "data");

            if (!Directory.Exists(dataPath))
            {
                Log.Error("Data directory not found: {path}. Run Task 1 first.", dataPath);
                return;
            }

            // Load ranking configuration
            var config = LoadRankingConfiguration();
            LogConfiguration(config);

            // Setup AI analyzer if configured
            IAiAnalyzer? aiAnalyzer = null;
            if (config.UseAiAnalysis)
            {
                aiAnalyzer = SetupAiAnalyzer();
            }

            var rankingService = new RankingService(dataPath, config, aiAnalyzer);

            // Get search query for context
            string searchQuery = _configuration["Search:Query"] ?? "czy szczepionki powoduj¹ autyzm";

            // Find available crawl results
            var sources = FindAvailableSources(dataPath);
            
            if (sources.Count == 0)
            {
                Log.Error("No crawl data found in {path}. Run Task 1 first.", dataPath);
                return;
            }

            Log.Information("Found {count} source(s) to analyze: {sources}", 
                sources.Count, string.Join(", ", sources));

            // Generate rankings for each source
            var allRankings = new Dictionary<string, List<PageRanking>>();

            foreach (var source in sources)
            {
                Log.Information("\n=== Analyzing: {source} ===", source);
                var rankings = await rankingService.GenerateRankingAsync(source, searchQuery);
                allRankings[source] = rankings;
            }

            // Generate combined ranking if multiple sources
            if (allRankings.Count > 1)
            {
                Log.Information("\n=== Generating Combined Ranking ===");
                GenerateCombinedRanking(allRankings, dataPath, config);
            }

            Log.Information("\nTask 2 completed. Check 'data' folder for ranking results.");
            Log.Information("Files generated:");
            Log.Information("  - {source}_ranking.csv - Ranking scores");
            Log.Information("  - {source}_ranking_details.txt - Detailed report");
        }

        private RankingConfiguration LoadRankingConfiguration()
        {
            var config = new RankingConfiguration();

            // Load weights from configuration
            if (double.TryParse(_configuration["Ranking:PositionWeight"], out var posWeight))
                config.PositionWeight = posWeight;
            if (double.TryParse(_configuration["Ranking:ReferenceWeight"], out var refWeight))
                config.ReferenceWeight = refWeight;
            if (double.TryParse(_configuration["Ranking:SpecialistWeight"], out var specWeight))
                config.SpecialistWeight = specWeight;
            if (double.TryParse(_configuration["Ranking:EmotionWeight"], out var emoWeight))
                config.EmotionWeight = emoWeight;

            // AI settings
            if (bool.TryParse(_configuration["Ranking:UseAiAnalysis"], out var useAi))
                config.UseAiAnalysis = useAi;
            config.AiProvider = _configuration["Ranking:AiProvider"] ?? "OpenAI";

            return config;
        }

        private void LogConfiguration(RankingConfiguration config)
        {
            Log.Information("Ranking Configuration:");
            Log.Information("  Weights: Position={pos:F2}, Reference={ref:F2}, Specialist={spec:F2}, Emotion={emo:F2}",
                config.PositionWeight, config.ReferenceWeight, config.SpecialistWeight, config.EmotionWeight);
            Log.Information("  AI Analysis: {enabled} (Provider: {provider})", 
                config.UseAiAnalysis ? "Enabled" : "Disabled", config.AiProvider);
            
            if (!config.ValidateWeights())
            {
                Log.Warning("  ? Weights do not sum to 1.0!");
            }
        }

        private IAiAnalyzer? SetupAiAnalyzer()
        {
            var apiKey = _configuration["OPENAI_API_KEY"] ?? _configuration["Secrets:OpenAiApiKey"];
            
            if (string.IsNullOrEmpty(apiKey))
            {
                Log.Warning("OpenAI API key not configured. AI analysis will be disabled.");
                return null;
            }

            var model = _configuration["Ranking:AiModel"] ?? "gpt-4o-mini";
            
            // Get delay from config (default 1000ms)
            int delayMs = 1000;
            if (int.TryParse(_configuration["Ranking:AiRequestDelayMs"], out var configDelay))
            {
                delayMs = configDelay;
            }
            
            Log.Information("AI Analyzer configured: OpenAI ({model}), delay: {delay}ms", model, delayMs);
            
            return new OpenAiAnalyzer(_httpClient, apiKey, model, delayMs);
        }

        private List<string> FindAvailableSources(string dataPath)
        {
            var sources = new HashSet<string>();
            var csvFiles = Directory.GetFiles(dataPath, "*_graph.csv");

            foreach (var file in csvFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                
                // Extract source name (remove _BFS, _DFS, _graph suffixes)
                var parts = fileName.Split('_');
                if (parts.Length >= 1)
                {
                    var source = parts[0];
                    if (!source.Equals("Compare", StringComparison.OrdinalIgnoreCase))
                    {
                        sources.Add(source);
                    }
                }
            }

            return sources.ToList();
        }

        private void GenerateCombinedRanking(
            Dictionary<string, List<PageRanking>> allRankings, 
            string dataPath,
            RankingConfiguration config)
        {
            var combined = new List<PageRanking>();

            foreach (var kvp in allRankings)
            {
                combined.AddRange(kvp.Value);
            }

            // Remove duplicates (keep highest score)
            var deduped = combined
                .GroupBy(r => r.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(r => r.TotalScore).First())
                .OrderByDescending(r => r.TotalScore)
                .ToList();

            // Reassign ranks
            for (int i = 0; i < deduped.Count; i++)
            {
                deduped[i].FinalRank = i + 1;
            }

            // Save combined ranking
            var outputPath = Path.Combine(dataPath, "Combined_ranking.csv");
            using var writer = new System.IO.StreamWriter(outputPath, false, System.Text.Encoding.UTF8);
            
            writer.WriteLine("FinalRank,Source,Url,Title,TotalScore,PositionScore,ReferenceScore,SpecialistScore,CredibilityScore");

            foreach (var r in deduped)
            {
                writer.WriteLine(string.Join(",",
                    r.FinalRank,
                    EscapeCsv(r.Source),
                    EscapeCsv(r.Url),
                    EscapeCsv(r.Title),
                    r.TotalScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                    r.PositionScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                    r.ReferenceScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                    r.SpecialistScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
                    r.CredibilityScore.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)));
            }

            Log.Information("Combined ranking saved: {path} ({count} unique URLs)", outputPath, deduped.Count);
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return "\"" + value + "\"";
        }
    }
}
