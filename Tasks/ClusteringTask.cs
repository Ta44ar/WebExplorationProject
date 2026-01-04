using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Analysis;
using WebExplorationProject.Helpers;
using WebExplorationProject.Models;

namespace WebExplorationProject.Tasks
{
    /// <summary>
    /// Task 3: Page Clustering.
    /// Groups pages based on ranking scores using K-Means algorithm.
    /// Generates clusters for K=2, K=4, and K=6.
    /// </summary>
    public class ClusteringTask : ITask
    {
        private readonly IConfiguration _configuration;

        public string Name => "Clustering Task";
        public string Description => "Group pages by ranking scores using K-Means (K=2,4,6) -> CSV reports";

        public ClusteringTask(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task ExecuteAsync()
        {
            Log.Information("=== TASK 3: Page Clustering (ML.NET K-Means) ===");
            Log.Information(Description);

            var rankingPath = DataPaths.RankingPath;
            var clusteringPath = DataPaths.ClusteringPath;

            if (!Directory.Exists(rankingPath))
            {
                Log.Error("Ranking data not found: {path}. Run Task 2 first.", rankingPath);
                return;
            }

            Log.Information("Input: {input}", rankingPath);
            Log.Information("Output: {output}", clusteringPath);

            // Load configuration
            var config = LoadClusteringConfiguration();
            LogConfiguration(config);

            var clusteringService = new ClusteringService(rankingPath, clusteringPath, config);

            // Find available ranking results
            var sources = FindAvailableSources(rankingPath);

            if (sources.Count == 0)
            {
                Log.Error("No ranking data found in {path}. Run Task 2 first.", rankingPath);
                return;
            }

            Log.Information("Found {count} source(s) to cluster: {sources}",
                sources.Count, string.Join(", ", sources));

            // Cluster each source
            foreach (var source in sources)
            {
                Log.Information("\n=== Clustering: {source} ===", source);
                await clusteringService.ClusterPagesAsync(source);
            }

            // Cluster combined data if available
            if (File.Exists(Path.Combine(rankingPath, "Combined_ranking.csv")))
            {
                Log.Information("\n=== Clustering: Combined ===");
                await ClusterCombinedDataAsync(clusteringService, rankingPath, clusteringPath, config);
            }

            Log.Information("\nTask 3 completed. Results saved to: {path}", clusteringPath);
        }

        private async Task ClusterCombinedDataAsync(ClusteringService service, string rankingPath, string clusteringPath, ClusteringConfiguration config)
        {
            var combinedPath = Path.Combine(rankingPath, "Combined_ranking.csv");
            var tempRankingPath = Path.Combine(rankingPath, "CombinedData_ranking.csv");

            try
            {
                // Read combined and convert to ranking format
                var lines = File.ReadAllLines(combinedPath, System.Text.Encoding.UTF8);
                
                // Build output lines
                var outputLines = new List<string>
                {
                    "FinalRank,Url,Title,TotalScore,PositionScore,ReferenceScore,SpecialistScore,CredibilityScore,EmotionScore"
                };

                foreach (var line in lines.Skip(1))
                {
                    var fields = ParseCsvLine(line);
                    if (fields.Count >= 9)
                    {
                        outputLines.Add($"{fields[0]},{EscapeCsv(fields[2])},{EscapeCsv(fields[3])},{fields[4]},{fields[5]},{fields[6]},{fields[7]},{fields[8]},0");
                    }
                }

                File.WriteAllLines(tempRankingPath, outputLines, System.Text.Encoding.UTF8);
                
                // Cluster using the combined data
                await service.ClusterPagesAsync("CombinedData");

                // Rename output files to "Combined_" prefix (files are in clusteringPath)
                foreach (var k in config.ClusterCounts)
                {
                    var oldPath = Path.Combine(clusteringPath, $"CombinedData_clusters_k{k}.csv");
                    var newPath = Path.Combine(clusteringPath, $"Combined_clusters_k{k}.csv");
                    if (File.Exists(oldPath))
                    {
                        if (File.Exists(newPath)) File.Delete(newPath);
                        File.Move(oldPath, newPath);
                    }
                }

                var oldReport = Path.Combine(clusteringPath, "CombinedData_clustering_report.txt");
                var newReport = Path.Combine(clusteringPath, "Combined_clustering_report.txt");
                if (File.Exists(oldReport))
                {
                    if (File.Exists(newReport)) File.Delete(newReport);
                    File.Move(oldReport, newReport);
                }

                // Cleanup temp file
                if (File.Exists(tempRankingPath))
                {
                    File.Delete(tempRankingPath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Error clustering combined data: {error}", ex.Message);
            }
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;

            foreach (char c in line)
            {
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim().Trim('"'));
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim().Trim('"'));

            return result;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return "\"" + value + "\"";
        }

        private ClusteringConfiguration LoadClusteringConfiguration()
        {
            var config = new ClusteringConfiguration();

            // Load cluster counts from configuration
            var clusterCountsStr = _configuration["Clustering:ClusterCounts"];
            if (!string.IsNullOrEmpty(clusterCountsStr))
            {
                var counts = clusterCountsStr.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                    .Where(v => v > 0)
                    .ToArray();
                if (counts.Length > 0)
                {
                    config.ClusterCounts = counts;
                }
            }

            // Load other settings
            if (int.TryParse(_configuration["Clustering:MaxIterations"], out var maxIter))
                config.MaxIterations = maxIter;
            if (int.TryParse(_configuration["Clustering:NumberOfThreads"], out var threads))
                config.NumberOfThreads = threads;

            return config;
        }

        private void LogConfiguration(ClusteringConfiguration config)
        {
            Log.Information("Clustering Configuration:");
            Log.Information("  Algorithm: K-Means (ML.NET)");
            Log.Information("  Cluster counts: {counts}", string.Join(", ", config.ClusterCounts));
            Log.Information("  Max iterations: {max}", config.MaxIterations);
            Log.Information("  Threads: {threads}", config.NumberOfThreads);
        }

        private List<string> FindAvailableSources(string dataPath)
        {
            var sources = new HashSet<string>();
            var rankingFiles = Directory.GetFiles(dataPath, "*_ranking.csv");

            foreach (var file in rankingFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);

                // Extract source name (remove _ranking suffix)
                if (fileName.EndsWith("_ranking"))
                {
                    var source = fileName.Replace("_ranking", "");
                    if (!source.Equals("Combined", StringComparison.OrdinalIgnoreCase) &&
                        !source.Contains("_temp") &&
                        !source.Contains("_formatted"))
                    {
                        sources.Add(source);
                    }
                }
            }

            return sources.ToList();
        }
    }
}
