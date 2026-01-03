using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Analysis;
using WebExplorationProject.Models;

namespace WebExplorationProject.Tasks
{
    /// <summary>
    /// Task 4: Classification with Cross-Validation.
    /// Uses clustering results from Task 3 as labels and trains classifiers.
    /// 
    /// Algorithms:
    /// - FastTree (Decision Tree) with 2 parameter sets
    /// - SdcaMaximumEntropy (Linear) with 2 parameter sets
    /// 
    /// Runs experiments for K=2 and K=4 groups.
    /// Total: 8 experiments (2 algorithms × 2 params × 2 group sizes)
    /// </summary>
    public class ClassificationTask : ITask
    {
        private readonly IConfiguration _configuration;

        public string Name => "Classification Task";
        public string Description => "Cross-validation classification (2 algorithms × 2 params × K=2,4) ? 8 results";

        public ClassificationTask(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task ExecuteAsync()
        {
            Log.Information("=== TASK 4: Classification with Cross-Validation ===");
            Log.Information(Description);

            // Get data path
            var projectDir = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName
                ?? AppContext.BaseDirectory;
            var dataPath = Path.Combine(projectDir, "data");

            if (!Directory.Exists(dataPath))
            {
                Log.Error("Data directory not found: {path}. Run Tasks 1-3 first.", dataPath);
                return;
            }

            // Load configuration
            int crossValidationFolds = 5;
            if (int.TryParse(_configuration["Classification:CrossValidationFolds"], out var folds))
            {
                crossValidationFolds = folds;
            }

            int[] groupCounts = { 2, 4 }; // K=2 and K=4
            var groupCountsStr = _configuration["Classification:GroupCounts"];
            if (!string.IsNullOrEmpty(groupCountsStr))
            {
                groupCounts = groupCountsStr.Split(',')
                    .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                    .Where(v => v > 0)
                    .ToArray();
            }

            Log.Information("Configuration:");
            Log.Information("  Cross-Validation Folds: {folds}", crossValidationFolds);
            Log.Information("  Group counts: {groups}", string.Join(", ", groupCounts));
            Log.Information("  Algorithms: FastTree, SdcaMaximumEntropy");
            Log.Information("  Parameter sets per algorithm: 2");
            Log.Information("  Total experiments: {count}", 4 * groupCounts.Length);

            var classificationService = new ClassificationService(dataPath, crossValidationFolds);

            // Find available clustering results
            var sources = FindAvailableSources(dataPath, groupCounts);

            if (sources.Count == 0)
            {
                Log.Error("No clustering data found. Run Task 3 first.");
                return;
            }

            Log.Information("Found {count} source(s) to classify: {sources}",
                sources.Count, string.Join(", ", sources));

            // Run classification for each source
            var allResults = new Dictionary<string, List<ClassificationResult>>();

            foreach (var source in sources)
            {
                Log.Information("\n=== Classifying: {source} ===", source);
                var results = await classificationService.RunAllExperimentsAsync(source, groupCounts);
                allResults[source] = results;

                // Log summary
                LogResultsSummary(results, source);
            }

            // Generate combined report if multiple sources
            if (allResults.Count > 1)
            {
                GenerateCombinedReport(allResults, dataPath);
            }

            Log.Information("\nTask 4 completed. Check 'data' folder for classification results.");
            Log.Information("Files generated:");
            Log.Information("  - {{source}}_classification_results.csv");
            Log.Information("  - {{source}}_classification_report.txt");
        }

        private void LogResultsSummary(List<ClassificationResult> results, string source)
        {
            Log.Information("\n[{src}] RESULTS SUMMARY:", source);
            Log.Information("?????????????????????????????????????????????????????????????????");
            
            foreach (var group in results.GroupBy(r => r.NumberOfGroups).OrderBy(g => g.Key))
            {
                Log.Information("  K={k}:", group.Key);
                foreach (var r in group.OrderByDescending(r => r.Accuracy))
                {
                    Log.Information("    {algo} ({params}): {acc:P2}",
                        r.Algorithm, r.Parameters, r.Accuracy);
                }
            }

            var best = results.OrderByDescending(r => r.Accuracy).First();
            Log.Information("\n  Best: {algo} ({params}) K={k} ? {acc:P2}",
                best.Algorithm, best.Parameters, best.NumberOfGroups, best.Accuracy);
        }

        private List<string> FindAvailableSources(string dataPath, int[] groupCounts)
        {
            var sources = new HashSet<string>();

            foreach (var k in groupCounts)
            {
                var clusterFiles = Directory.GetFiles(dataPath, $"*_clusters_k{k}.csv");

                foreach (var file in clusterFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var source = fileName.Replace($"_clusters_k{k}", "");

                    if (!source.Equals("CombinedData", StringComparison.OrdinalIgnoreCase))
                    {
                        sources.Add(source);
                    }
                }
            }

            return sources.ToList();
        }

        private void GenerateCombinedReport(
            Dictionary<string, List<ClassificationResult>> allResults,
            string dataPath)
        {
            var reportPath = Path.Combine(dataPath, "Combined_classification_summary.txt");
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("???????????????????????????????????????????????????????????????????????????");
            sb.AppendLine("                 COMBINED CLASSIFICATION SUMMARY");
            sb.AppendLine("???????????????????????????????????????????????????????????????????????????");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            // Compare sources
            sb.AppendLine("COMPARISON ACROSS SOURCES:");
            sb.AppendLine("?????????????????????????????????????????????????????????????????????????????");

            foreach (var k in new[] { 2, 4 })
            {
                sb.AppendLine($"\nK={k} Groups:");
                sb.AppendLine($"{"Source",-15} {"Best Algorithm",-25} {"Accuracy",-12}");
                sb.AppendLine(new string('-', 55));

                foreach (var kvp in allResults)
                {
                    var bestForK = kvp.Value
                        .Where(r => r.NumberOfGroups == k)
                        .OrderByDescending(r => r.Accuracy)
                        .FirstOrDefault();

                    if (bestForK != null)
                    {
                        sb.AppendLine($"{kvp.Key,-15} {bestForK.Algorithm,-25} {bestForK.Accuracy,-12:P2}");
                    }
                }
            }

            sb.AppendLine();
            sb.AppendLine("?????????????????????????????????????????????????????????????????????????????");

            File.WriteAllText(reportPath, sb.ToString(), System.Text.Encoding.UTF8);
            Log.Information("Combined summary saved: {path}", reportPath);
        }
    }
}
