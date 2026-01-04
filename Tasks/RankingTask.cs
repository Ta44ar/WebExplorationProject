using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Analysis;
using WebExplorationProject.Helpers;
using WebExplorationProject.Models;

namespace WebExplorationProject.Tasks
{
    /// <summary>
    /// Task 2: Page Ranking Analysis.
    /// </summary>
    public class RankingTask : ITask
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public string Name => "Ranking Task";
        public string Description => "Analyze crawled pages -> Generate credibility rankings (CSV/TXT)";

        public RankingTask(IConfiguration configuration, HttpClient httpClient)
        {
            _configuration = configuration;
            _httpClient = httpClient;
        }

        public async Task ExecuteAsync()
        {
            Log.Information("=== TASK 2: Page Ranking Analysis ===");
            Log.Information(Description);

            var crawlingPath = DataPaths.CrawlingPath;
            var rankingPath = DataPaths.RankingPath;

            if (!Directory.Exists(crawlingPath))
            {
                Log.Error("Crawling data not found: {path}. Run Task 1 first.", crawlingPath);
                return;
            }

            Log.Information("Input: {input}", crawlingPath);
            Log.Information("Output: {output}", rankingPath);

            // Load ranking configuration
            var config = LoadRankingConfiguration();
            LogConfiguration(config);

            // Get search query for context
            string searchQuery = _configuration["Search:Query"] ?? "default query";

            // Generate topic-specific dictionaries using AI (if available)
            GeneratedDictionaries? generatedDictionaries = null;
            if (ShouldGenerateDictionaries())
            {
                generatedDictionaries = await GenerateDictionariesAsync(searchQuery);
            }

            // RankingService reads from crawlingPath, writes to rankingPath
            var rankingService = new RankingService(crawlingPath, rankingPath, config, generatedDictionaries);

            // Find available crawl results
            var sources = FindAvailableSources(crawlingPath);
            
            if (sources.Count == 0)
            {
                Log.Error("No crawl data found in {path}. Run Task 1 first.", crawlingPath);
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
                GenerateCombinedRanking(allRankings, rankingPath);
            }

            Log.Information("\nTask 2 completed. Results saved to: {path}", rankingPath);
        }

        private bool ShouldGenerateDictionaries()
        {
            // Check if dictionary generation is enabled
            if (bool.TryParse(_configuration["Ranking:GenerateDictionaries"], out var generate))
            {
                if (!generate) return false;
            }
            
            // Check if API key is available
            var apiKey = _configuration["OPENAI_API_KEY"] ?? _configuration["Secrets:OpenAiApiKey"];
            return !string.IsNullOrEmpty(apiKey);
        }

        private async Task<GeneratedDictionaries?> GenerateDictionariesAsync(string searchQuery)
        {
            var apiKey = _configuration["OPENAI_API_KEY"] ?? _configuration["Secrets:OpenAiApiKey"];
            
            if (string.IsNullOrEmpty(apiKey))
            {
                Log.Information("No OpenAI API key - using default dictionaries");
                return null;
            }

            var model = _configuration["Ranking:AiModel"] ?? "gpt-4o-mini";
            var generator = new DictionaryGeneratorService(_httpClient, apiKey, model);
            
            Log.Information("Generating topic-specific dictionaries for: \"{query}\"", searchQuery);
            
            try
            {
                var dictionaries = await generator.GenerateDictionariesAsync(searchQuery);
                
                if (dictionaries != null)
                {
                    SaveGeneratedDictionaries(dictionaries, searchQuery);
                }
                
                return dictionaries;
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to generate dictionaries: {error}. Using defaults.", ex.Message);
                return null;
            }
        }

        private void SaveGeneratedDictionaries(GeneratedDictionaries dictionaries, string query)
        {
            var filePath = Path.Combine(DataPaths.RankingPath, "generated_dictionaries.txt");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== GENERATED DICTIONARIES ===");
            sb.AppendLine($"Query: {query}");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            
            sb.AppendLine($"=== SPECIALIST TERMS ({dictionaries.SpecialistTerms.Count}) ===");
            foreach (var term in dictionaries.SpecialistTerms.OrderBy(t => t))
            {
                sb.AppendLine($"  - {term}");
            }
            sb.AppendLine();
            
            sb.AppendLine($"=== EMOTIONAL WORDS ({dictionaries.EmotionalWords.Count}) ===");
            foreach (var word in dictionaries.EmotionalWords.OrderBy(w => w))
            {
                sb.AppendLine($"  - {word}");
            }
            sb.AppendLine();
            
            sb.AppendLine($"=== PROPAGANDA PHRASES ({dictionaries.PropagandaPhrases.Count}) ===");
            foreach (var phrase in dictionaries.PropagandaPhrases.OrderBy(p => p))
            {
                sb.AppendLine($"  - {phrase}");
            }

            File.WriteAllText(filePath, sb.ToString(), System.Text.Encoding.UTF8);
            Log.Information("Generated dictionaries saved: {path}", filePath);
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

            return config;
        }

        private void LogConfiguration(RankingConfiguration config)
        {
            Log.Information("Ranking Configuration:");
            Log.Information("  Weights: Position={pos:F2}, Reference={ref:F2}, Specialist={spec:F2}, Emotion={emo:F2}",
                config.PositionWeight, config.ReferenceWeight, config.SpecialistWeight, config.EmotionWeight);
            
            if (!config.ValidateWeights())
            {
                Log.Warning("  ? Weights do not sum to 1.0!");
            }
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
            string dataPath)
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
