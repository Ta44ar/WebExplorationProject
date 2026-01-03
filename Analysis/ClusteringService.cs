using System.Globalization;
using System.Text;
using Microsoft.ML;
using Microsoft.ML.Trainers;
using Serilog;
using WebExplorationProject.Models;

namespace WebExplorationProject.Analysis
{
    /// <summary>
    /// Service for clustering pages using K-Means algorithm (ML.NET).
    /// Groups pages based on their ranking scores from Task 2.
    /// 
    /// ALGORITHM: K-Means Clustering
    /// - Unsupervised learning algorithm
    /// - Groups data points into K clusters
    /// - Minimizes within-cluster variance
    /// 
    /// FEATURES USED:
    /// - PositionScore (0-1)
    /// - ReferenceScore (0-1)
    /// - SpecialistScore (0-1)
    /// - CredibilityScore (0-1)
    /// </summary>
    public class ClusteringService
    {
        private readonly string _dataPath;
        private readonly ClusteringConfiguration _config;
        private readonly MLContext _mlContext;

        public ClusteringService(string dataPath, ClusteringConfiguration? config = null)
        {
            _dataPath = dataPath;
            _config = config ?? new ClusteringConfiguration();
            _mlContext = new MLContext(seed: 42); // Fixed seed for reproducibility

            Directory.CreateDirectory(_dataPath);
        }

        /// <summary>
        /// Performs clustering on ranking data for specified K values.
        /// </summary>
        public async Task<Dictionary<int, List<ClusteredPage>>> ClusterPagesAsync(string sourceName)
        {
            var results = new Dictionary<int, List<ClusteredPage>>();

            // Load ranking data
            var rankingData = LoadRankingData(sourceName);
            if (rankingData.Count == 0)
            {
                Log.Error("[{src}] No ranking data found", sourceName);
                return results;
            }

            Log.Information("[{src}] Loaded {count} pages for clustering", sourceName, rankingData.Count);

            // Cluster for each K value
            foreach (var k in _config.ClusterCounts)
            {
                Log.Information("[{src}] Clustering with K={k}...", sourceName, k);
                
                var clustered = await ClusterWithKMeansAsync(rankingData, k);
                AssignClusterLabels(clustered, k);
                
                results[k] = clustered;

                // Save results
                SaveClusteringResults(clustered, sourceName, k);
                
                // Log statistics
                var stats = CalculateClusterStatistics(clustered, k);
                LogClusterStatistics(stats, sourceName, k);
            }

            // Save comprehensive report
            SaveClusteringReport(results, sourceName);

            return results;
        }

        private List<ClusteringInput> LoadRankingData(string sourceName)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_ranking.csv");

            if (!File.Exists(csvPath))
            {
                Log.Warning("[{src}] Ranking file not found: {path}", sourceName, csvPath);
                return new List<ClusteringInput>();
            }

            var data = new List<ClusteringInput>();
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8).Skip(1); // Skip header

            foreach (var line in lines)
            {
                var fields = SplitCsvLine(line);
                if (fields.Length < 9) continue;

                try
                {
                    data.Add(new ClusteringInput
                    {
                        OriginalRank = int.Parse(fields[0]),
                        Url = fields[1],
                        Title = fields[2],
                        TotalScore = float.Parse(fields[3], CultureInfo.InvariantCulture),
                        PositionScore = float.Parse(fields[4], CultureInfo.InvariantCulture),
                        ReferenceScore = float.Parse(fields[5], CultureInfo.InvariantCulture),
                        SpecialistScore = float.Parse(fields[6], CultureInfo.InvariantCulture),
                        CredibilityScore = float.Parse(fields[7], CultureInfo.InvariantCulture),
                        Source = sourceName
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning("Error parsing line: {error}", ex.Message);
                }
            }

            return data;
        }

        private async Task<List<ClusteredPage>> ClusterWithKMeansAsync(List<ClusteringInput> data, int k)
        {
            return await Task.Run(() =>
            {
                // Convert to IDataView
                var dataView = _mlContext.Data.LoadFromEnumerable(data);

                // Define feature columns
                var featureColumns = new[] { "PositionScore", "ReferenceScore", "SpecialistScore", "CredibilityScore" };

                // Build pipeline with K-Means trainer
                var pipeline = _mlContext.Transforms
                    .Concatenate("Features", featureColumns)
                    .Append(_mlContext.Clustering.Trainers.KMeans(
                        featureColumnName: "Features",
                        numberOfClusters: k));

                // Train the model
                var model = pipeline.Fit(dataView);

                // Create prediction engine
                var predictor = _mlContext.Model.CreatePredictionEngine<ClusteringInput, ClusteringPrediction>(model);

                // Predict clusters for all data
                var results = new List<ClusteredPage>();

                foreach (var item in data)
                {
                    var prediction = predictor.Predict(item);
                    
                    // Calculate distance to center safely
                    float distanceToCenter = 0;
                    if (prediction.Distances != null && prediction.Distances.Length > 0)
                    {
                        // ClusterId is 1-based in ML.NET, array is 0-based
                        var clusterIndex = (int)prediction.ClusterId - 1;
                        if (clusterIndex >= 0 && clusterIndex < prediction.Distances.Length)
                        {
                            distanceToCenter = prediction.Distances[clusterIndex];
                        }
                        else if (prediction.Distances.Length > 0)
                        {
                            distanceToCenter = prediction.Distances.Min();
                        }
                    }

                    results.Add(new ClusteredPage
                    {
                        Url = item.Url,
                        Title = item.Title,
                        Source = item.Source,
                        OriginalRank = item.OriginalRank,
                        PositionScore = item.PositionScore,
                        ReferenceScore = item.ReferenceScore,
                        SpecialistScore = item.SpecialistScore,
                        CredibilityScore = item.CredibilityScore,
                        TotalScore = item.TotalScore,
                        ClusterId = (int)prediction.ClusterId,
                        DistanceToCenter = distanceToCenter
                    });
                }

                return results;
            });
        }

        private void AssignClusterLabels(List<ClusteredPage> clustered, int k)
        {
            // Calculate average TotalScore per cluster
            var clusterAvgs = clustered
                .GroupBy(p => p.ClusterId)
                .Select(g => new { ClusterId = g.Key, AvgScore = g.Average(p => p.TotalScore) })
                .OrderBy(x => x.AvgScore)
                .ToList();

            // Map cluster IDs to labels based on average score (lowest score = lowest label)
            var labels = _config.ClusterLabels.GetValueOrDefault(k) 
                ?? Enumerable.Range(1, k).Select(i => $"Cluster {i}").ToArray();

            var labelMap = new Dictionary<int, string>();
            for (int i = 0; i < clusterAvgs.Count && i < labels.Length; i++)
            {
                labelMap[clusterAvgs[i].ClusterId] = labels[i];
            }

            // Assign labels
            foreach (var page in clustered)
            {
                page.ClusterLabel = labelMap.GetValueOrDefault(page.ClusterId, $"Cluster {page.ClusterId}");
            }
        }

        private List<ClusterStatistics> CalculateClusterStatistics(List<ClusteredPage> clustered, int k)
        {
            return clustered
                .GroupBy(p => p.ClusterId)
                .Select(g => new ClusterStatistics
                {
                    ClusterId = g.Key,
                    Label = g.First().ClusterLabel,
                    PageCount = g.Count(),
                    AvgPositionScore = (float)g.Average(p => p.PositionScore),
                    AvgReferenceScore = (float)g.Average(p => p.ReferenceScore),
                    AvgSpecialistScore = (float)g.Average(p => p.SpecialistScore),
                    AvgCredibilityScore = (float)g.Average(p => p.CredibilityScore),
                    AvgTotalScore = (float)g.Average(p => p.TotalScore),
                    MinTotalScore = g.Min(p => p.TotalScore),
                    MaxTotalScore = g.Max(p => p.TotalScore)
                })
                .OrderBy(s => s.AvgTotalScore)
                .ToList();
        }

        private void LogClusterStatistics(List<ClusterStatistics> stats, string sourceName, int k)
        {
            Log.Information("[{src}] K={k} Clustering Results:", sourceName, k);
            
            foreach (var s in stats)
            {
                Log.Information("  Cluster {id} ({label}): {count} pages, AvgScore={avg:F3} [{min:F3}-{max:F3}]",
                    s.ClusterId, s.Label, s.PageCount, s.AvgTotalScore, s.MinTotalScore, s.MaxTotalScore);
            }
        }

        private void SaveClusteringResults(List<ClusteredPage> clustered, string sourceName, int k)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_clusters_k{k}.csv");

            using var writer = new StreamWriter(csvPath, false, new UTF8Encoding(true));

            // Header
            writer.WriteLine("ClusterId,ClusterLabel,OriginalRank,Url,Title,TotalScore,PositionScore,ReferenceScore,SpecialistScore,CredibilityScore,DistanceToCenter");

            // Sort by cluster, then by score
            var sorted = clustered
                .OrderBy(p => p.ClusterId)
                .ThenByDescending(p => p.TotalScore);

            foreach (var p in sorted)
            {
                writer.WriteLine(string.Join(",",
                    p.ClusterId,
                    EscapeCsv(p.ClusterLabel),
                    p.OriginalRank,
                    EscapeCsv(p.Url),
                    EscapeCsv(p.Title),
                    p.TotalScore.ToString("F4", CultureInfo.InvariantCulture),
                    p.PositionScore.ToString("F4", CultureInfo.InvariantCulture),
                    p.ReferenceScore.ToString("F4", CultureInfo.InvariantCulture),
                    p.SpecialistScore.ToString("F4", CultureInfo.InvariantCulture),
                    p.CredibilityScore.ToString("F4", CultureInfo.InvariantCulture),
                    p.DistanceToCenter.ToString("F4", CultureInfo.InvariantCulture)));
            }

            Log.Information("[{src}] Saved K={k} clustering: {path}", sourceName, k, csvPath);
        }

        private void SaveClusteringReport(Dictionary<int, List<ClusteredPage>> allResults, string sourceName)
        {
            var reportPath = Path.Combine(_dataPath, $"{sourceName}_clustering_report.txt");
            var sb = new StringBuilder();

            sb.AppendLine($"=== CLUSTERING REPORT: {sourceName} ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("=== ALGORITHM CONFIGURATION ===");
            sb.AppendLine("Algorithm: K-Means (ML.NET)");
            sb.AppendLine($"Max Iterations: {_config.MaxIterations}");
            sb.AppendLine($"Number of Threads: {_config.NumberOfThreads}");
            sb.AppendLine($"Random Seed: 42 (for reproducibility)");
            sb.AppendLine();
            sb.AppendLine("Features used:");
            sb.AppendLine("  - PositionScore (weight in search results)");
            sb.AppendLine("  - ReferenceScore (external links diversity)");
            sb.AppendLine("  - SpecialistScore (expert terminology density)");
            sb.AppendLine("  - CredibilityScore (absence of propaganda)");
            sb.AppendLine();

            foreach (var k in allResults.Keys.OrderBy(x => x))
            {
                var clustered = allResults[k];
                var stats = CalculateClusterStatistics(clustered, k);

                sb.AppendLine($"=== K={k} CLUSTERING ===");
                sb.AppendLine($"Total pages: {clustered.Count}");
                sb.AppendLine();
                sb.AppendLine("Cluster Statistics:");
                sb.AppendLine("?????????????????????????????????????????????????????????????????????????");
                sb.AppendLine($"{"ID",-4} {"Label",-15} {"Count",-7} {"AvgScore",-10} {"Range",-15} {"AvgPos",-8} {"AvgRef",-8} {"AvgSpec",-8} {"AvgCred",-8}");
                sb.AppendLine("?????????????????????????????????????????????????????????????????????????");

                foreach (var s in stats)
                {
                    sb.AppendLine($"{s.ClusterId,-4} {s.Label,-15} {s.PageCount,-7} {s.AvgTotalScore,-10:F4} " +
                        $"[{s.MinTotalScore:F2}-{s.MaxTotalScore:F2}]  {s.AvgPositionScore,-8:F3} {s.AvgReferenceScore,-8:F3} " +
                        $"{s.AvgSpecialistScore,-8:F3} {s.AvgCredibilityScore,-8:F3}");
                }

                sb.AppendLine();

                // Show top 3 pages per cluster
                sb.AppendLine("Top 3 pages per cluster:");
                foreach (var s in stats)
                {
                    sb.AppendLine($"\n  {s.Label} (Cluster {s.ClusterId}):");
                    var topPages = clustered
                        .Where(p => p.ClusterId == s.ClusterId)
                        .OrderByDescending(p => p.TotalScore)
                        .Take(3);

                    foreach (var p in topPages)
                    {
                        sb.AppendLine($"    - [{p.TotalScore:F3}] {Shorten(p.Title, 50)}");
                        sb.AppendLine($"      {Shorten(p.Url, 70)}");
                    }
                }

                sb.AppendLine();
            }

            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            Log.Information("[{src}] Clustering report saved: {path}", sourceName, reportPath);
        }

        #region Helper Methods

        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
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

            return result.ToArray();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";

            return "\"" + value + "\"";
        }

        private static string Shorten(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen - 3) + "...";
        }

        #endregion
    }
}
