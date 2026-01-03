using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.ML;
using Microsoft.ML.Data;
using Serilog;
using WebExplorationProject.Models;

namespace WebExplorationProject.Analysis
{
    /// <summary>
    /// Service for classifying pages using various ML algorithms with cross-validation.
    /// 
    /// ALGORITHMS:
    /// 1. FastTree (Decision Tree) - with different max depth settings
    /// 2. SDCA (Stochastic Dual Coordinate Ascent) - as alternative to KNN
    /// 
    /// VALIDATION: K-fold Cross-Validation (default 5 folds)
    /// </summary>
    public class ClassificationService
    {
        private readonly string _dataPath;
        private readonly MLContext _mlContext;
        private readonly int _crossValidationFolds;

        public ClassificationService(string dataPath, int crossValidationFolds = 5)
        {
            _dataPath = dataPath;
            _mlContext = new MLContext(seed: 42);
            _crossValidationFolds = crossValidationFolds;

            Directory.CreateDirectory(_dataPath);
        }

        /// <summary>
        /// Runs all classification experiments for given source and group counts.
        /// </summary>
        public async Task<List<ClassificationResult>> RunAllExperimentsAsync(string sourceName, int[] groupCounts)
        {
            var allResults = new List<ClassificationResult>();

            foreach (var k in groupCounts)
            {
                Log.Information("[{src}] Running classification experiments for K={k}", sourceName, k);

                // Load data
                var data = LoadClusteringData(sourceName, k);
                if (data.Count == 0)
                {
                    Log.Warning("[{src}] No data found for K={k}", sourceName, k);
                    continue;
                }

                Log.Information("[{src}] Loaded {count} samples for K={k}", sourceName, data.Count, k);

                // Define experiments
                var experiments = CreateExperiments(k);

                // Run each experiment
                foreach (var experiment in experiments)
                {
                    Log.Information("[{src}] Running: {algo} ({params}) for K={k}",
                        sourceName, experiment.AlgorithmName, experiment.ParameterDescription, k);

                    var result = await RunExperimentAsync(data, experiment);
                    allResults.Add(result);

                    Log.Information("[{src}] {algo}: Accuracy={acc:P2}, MacroAccuracy={macro:P2}",
                        sourceName, experiment.AlgorithmName, result.Accuracy, result.MacroAccuracy);
                }
            }

            // Save results
            SaveResultsToCsv(allResults, sourceName);
            SaveDetailedReport(allResults, sourceName);

            return allResults;
        }

        private List<ClassificationExperiment> CreateExperiments(int numberOfGroups)
        {
            return new List<ClassificationExperiment>
            {
                // Algorithm 1: FastTree (Decision Tree) - Parameter Set 1
                new ClassificationExperiment
                {
                    AlgorithmName = "FastTree",
                    ParameterDescription = "NumberOfLeaves=10, MinDatapointsInLeaves=5",
                    NumberOfGroups = numberOfGroups,
                    TrainerFactory = ml => ml.MulticlassClassification.Trainers
                        .OneVersusAll(ml.BinaryClassification.Trainers.FastTree(
                            numberOfLeaves: 10,
                            minimumExampleCountPerLeaf: 5,
                            numberOfTrees: 50))
                },

                // Algorithm 1: FastTree (Decision Tree) - Parameter Set 2
                new ClassificationExperiment
                {
                    AlgorithmName = "FastTree",
                    ParameterDescription = "NumberOfLeaves=20, MinDatapointsInLeaves=10",
                    NumberOfGroups = numberOfGroups,
                    TrainerFactory = ml => ml.MulticlassClassification.Trainers
                        .OneVersusAll(ml.BinaryClassification.Trainers.FastTree(
                            numberOfLeaves: 20,
                            minimumExampleCountPerLeaf: 10,
                            numberOfTrees: 100))
                },

                // Algorithm 2: SDCA (Stochastic Dual Coordinate Ascent) - Parameter Set 1
                new ClassificationExperiment
                {
                    AlgorithmName = "SdcaMaximumEntropy",
                    ParameterDescription = "L2Regularization=0.1",
                    NumberOfGroups = numberOfGroups,
                    TrainerFactory = ml => ml.MulticlassClassification.Trainers
                        .SdcaMaximumEntropy(l2Regularization: 0.1f)
                },

                // Algorithm 2: SDCA - Parameter Set 2
                new ClassificationExperiment
                {
                    AlgorithmName = "SdcaMaximumEntropy",
                    ParameterDescription = "L2Regularization=0.01",
                    NumberOfGroups = numberOfGroups,
                    TrainerFactory = ml => ml.MulticlassClassification.Trainers
                        .SdcaMaximumEntropy(l2Regularization: 0.01f)
                }
            };
        }

        private async Task<ClassificationResult> RunExperimentAsync(
            List<ClassificationInput> data,
            ClassificationExperiment experiment)
        {
            return await Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();

                // Convert to IDataView
                var dataView = _mlContext.Data.LoadFromEnumerable(data);

                // Define feature columns
                var featureColumns = new[] { "PositionScore", "ReferenceScore", "SpecialistScore", "CredibilityScore" };

                // Build pipeline
                var pipeline = _mlContext.Transforms
                    .Concatenate("Features", featureColumns)
                    .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label"))
                    .Append(experiment.TrainerFactory(_mlContext))
                    .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

                // Cross-validation
                var cvResults = _mlContext.MulticlassClassification.CrossValidate(
                    dataView,
                    pipeline,
                    numberOfFolds: _crossValidationFolds,
                    labelColumnName: "Label");

                stopwatch.Stop();

                // Aggregate metrics
                var avgAccuracy = cvResults.Average(r => r.Metrics.MacroAccuracy);
                var avgMicroAccuracy = cvResults.Average(r => r.Metrics.MicroAccuracy);
                var avgLogLoss = cvResults.Average(r => r.Metrics.LogLoss);
                var avgLogLossReduction = cvResults.Average(r => r.Metrics.LogLossReduction);

                // Get confusion matrix from best fold
                var bestFold = cvResults.OrderByDescending(r => r.Metrics.MacroAccuracy).First();
                var confusionMatrix = ExtractConfusionMatrix(bestFold.Metrics.ConfusionMatrix);

                // Extract per-class metrics
                var perClassPrecision = new Dictionary<int, double>();
                var perClassRecall = new Dictionary<int, double>();

                for (int i = 0; i < bestFold.Metrics.ConfusionMatrix.NumberOfClasses; i++)
                {
                    var classMetrics = bestFold.Metrics.ConfusionMatrix.PerClassPrecision;
                    if (i < classMetrics.Count)
                    {
                        perClassPrecision[i] = classMetrics[i];
                    }

                    var recallMetrics = bestFold.Metrics.ConfusionMatrix.PerClassRecall;
                    if (i < recallMetrics.Count)
                    {
                        perClassRecall[i] = recallMetrics[i];
                    }
                }

                return new ClassificationResult
                {
                    Algorithm = experiment.AlgorithmName,
                    Parameters = experiment.ParameterDescription,
                    NumberOfGroups = experiment.NumberOfGroups,
                    FoldCount = _crossValidationFolds,
                    Accuracy = avgAccuracy,
                    MacroAccuracy = avgAccuracy,
                    MicroAccuracy = avgMicroAccuracy,
                    LogLoss = avgLogLoss,
                    LogLossReduction = avgLogLossReduction,
                    PerClassPrecision = perClassPrecision,
                    PerClassRecall = perClassRecall,
                    ConfusionMatrix = confusionMatrix,
                    TrainingTimeSeconds = stopwatch.Elapsed.TotalSeconds
                };
            });
        }

        private List<ClassificationInput> LoadClusteringData(string sourceName, int k)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_clusters_k{k}.csv");

            if (!File.Exists(csvPath))
            {
                Log.Warning("Clustering file not found: {path}", csvPath);
                return new List<ClassificationInput>();
            }

            var data = new List<ClassificationInput>();
            var lines = File.ReadAllLines(csvPath, Encoding.UTF8).Skip(1);

            foreach (var line in lines)
            {
                var fields = SplitCsvLine(line);
                if (fields.Length < 10) continue;

                try
                {
                    data.Add(new ClassificationInput
                    {
                        Label = uint.Parse(fields[0]),  // ClusterId
                        Url = fields[3],
                        PositionScore = float.Parse(fields[6], CultureInfo.InvariantCulture),
                        ReferenceScore = float.Parse(fields[7], CultureInfo.InvariantCulture),
                        SpecialistScore = float.Parse(fields[8], CultureInfo.InvariantCulture),
                        CredibilityScore = float.Parse(fields[9], CultureInfo.InvariantCulture)
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning("Error parsing line: {error}", ex.Message);
                }
            }

            return data;
        }

        private int[,] ExtractConfusionMatrix(ConfusionMatrix cm)
        {
            var size = cm.NumberOfClasses;
            var matrix = new int[size, size];

            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j < size; j++)
                {
                    matrix[i, j] = (int)cm.GetCountForClassPair(i, j);
                }
            }

            return matrix;
        }

        private void SaveResultsToCsv(List<ClassificationResult> results, string sourceName)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_classification_results.csv");

            using var writer = new StreamWriter(csvPath, false, new UTF8Encoding(true));

            // Header
            writer.WriteLine("Algorithm,Parameters,NumberOfGroups,Folds,Accuracy,MacroAccuracy,MicroAccuracy,LogLoss,LogLossReduction,TrainingTime");

            foreach (var r in results)
            {
                writer.WriteLine(string.Join(",",
                    EscapeCsv(r.Algorithm),
                    EscapeCsv(r.Parameters),
                    r.NumberOfGroups,
                    r.FoldCount,
                    r.Accuracy.ToString("F4", CultureInfo.InvariantCulture),
                    r.MacroAccuracy.ToString("F4", CultureInfo.InvariantCulture),
                    r.MicroAccuracy.ToString("F4", CultureInfo.InvariantCulture),
                    r.LogLoss.ToString("F4", CultureInfo.InvariantCulture),
                    r.LogLossReduction.ToString("F4", CultureInfo.InvariantCulture),
                    r.TrainingTimeSeconds.ToString("F2", CultureInfo.InvariantCulture)));
            }

            Log.Information("[{src}] Classification results saved: {path}", sourceName, csvPath);
        }

        private void SaveDetailedReport(List<ClassificationResult> results, string sourceName)
        {
            var reportPath = Path.Combine(_dataPath, $"{sourceName}_classification_report.txt");
            var sb = new StringBuilder();

            sb.AppendLine($"???????????????????????????????????????????????????????????????????????????");
            sb.AppendLine($"                    CLASSIFICATION REPORT: {sourceName}");
            sb.AppendLine($"???????????????????????????????????????????????????????????????????????????");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("EXPERIMENT CONFIGURATION");
            sb.AppendLine("?????????????????????????????????????????????????????????????????????????????");
            sb.AppendLine($"Cross-Validation Folds: {_crossValidationFolds}");
            sb.AppendLine($"Random Seed: 42 (for reproducibility)");
            sb.AppendLine();
            sb.AppendLine("ALGORITHMS TESTED:");
            sb.AppendLine("  1. FastTree (Gradient Boosted Decision Trees)");
            sb.AppendLine("     - Fast, handles non-linear relationships");
            sb.AppendLine("     - Parameters: NumberOfLeaves, MinDatapointsInLeaves");
            sb.AppendLine();
            sb.AppendLine("  2. SdcaMaximumEntropy (Stochastic Dual Coordinate Ascent)");
            sb.AppendLine("     - Linear classifier with regularization");
            sb.AppendLine("     - Parameters: L2Regularization");
            sb.AppendLine();
            sb.AppendLine("FEATURES USED:");
            sb.AppendLine("  - PositionScore (search result position)");
            sb.AppendLine("  - ReferenceScore (external links)");
            sb.AppendLine("  - SpecialistScore (expert terminology)");
            sb.AppendLine("  - CredibilityScore (absence of propaganda)");
            sb.AppendLine();

            // Group results by number of groups
            var groupedResults = results.GroupBy(r => r.NumberOfGroups).OrderBy(g => g.Key);

            foreach (var group in groupedResults)
            {
                sb.AppendLine($"???????????????????????????????????????????????????????????????????????????");
                sb.AppendLine($"                         K = {group.Key} GROUPS");
                sb.AppendLine($"???????????????????????????????????????????????????????????????????????????");
                sb.AppendLine();

                // Summary table
                sb.AppendLine("RESULTS SUMMARY:");
                sb.AppendLine("?????????????????????????????????????????????????????????????????????????????");
                sb.AppendLine($"{"Algorithm",-25} {"Parameters",-35} {"Accuracy",-12} {"Time(s)",-10}");
                sb.AppendLine("?????????????????????????????????????????????????????????????????????????????");

                foreach (var r in group.OrderByDescending(r => r.Accuracy))
                {
                    sb.AppendLine($"{r.Algorithm,-25} {r.Parameters,-35} {r.Accuracy,10:P2} {r.TrainingTimeSeconds,8:F2}s");
                }

                sb.AppendLine();

                // Detailed results
                foreach (var r in group.OrderByDescending(r => r.Accuracy))
                {
                    sb.AppendLine($"?? {r.Algorithm} ({r.Parameters}) ??");
                    sb.AppendLine();
                    sb.AppendLine($"  Macro Accuracy:     {r.MacroAccuracy:P2}");
                    sb.AppendLine($"  Micro Accuracy:     {r.MicroAccuracy:P2}");
                    sb.AppendLine($"  Log Loss:           {r.LogLoss:F4}");
                    sb.AppendLine($"  Log Loss Reduction: {r.LogLossReduction:F4}");
                    sb.AppendLine($"  Training Time:      {r.TrainingTimeSeconds:F2}s");
                    sb.AppendLine();

                    // Per-class metrics
                    if (r.PerClassPrecision.Count > 0)
                    {
                        sb.AppendLine("  Per-Class Metrics:");
                        sb.AppendLine($"    {"Class",-8} {"Precision",-12} {"Recall",-12}");
                        foreach (var classId in r.PerClassPrecision.Keys.OrderBy(k => k))
                        {
                            var precision = r.PerClassPrecision.GetValueOrDefault(classId, 0);
                            var recall = r.PerClassRecall.GetValueOrDefault(classId, 0);
                            sb.AppendLine($"    {classId,-8} {precision,-12:P2} {recall,-12:P2}");
                        }
                        sb.AppendLine();
                    }

                    // Confusion Matrix
                    if (r.ConfusionMatrix != null)
                    {
                        sb.AppendLine("  Confusion Matrix:");
                        var size = r.ConfusionMatrix.GetLength(0);
                        sb.Append("         ");
                        for (int j = 0; j < size; j++)
                        {
                            sb.Append($"Pred{j,-5} ");
                        }
                        sb.AppendLine();

                        for (int i = 0; i < size; i++)
                        {
                            sb.Append($"  True{i}  ");
                            for (int j = 0; j < size; j++)
                            {
                                sb.Append($"{r.ConfusionMatrix[i, j],-8} ");
                            }
                            sb.AppendLine();
                        }
                        sb.AppendLine();
                    }
                }
            }

            // Overall summary
            sb.AppendLine("???????????????????????????????????????????????????????????????????????????");
            sb.AppendLine("                           OVERALL SUMMARY");
            sb.AppendLine("???????????????????????????????????????????????????????????????????????????");
            sb.AppendLine();

            var bestResult = results.OrderByDescending(r => r.Accuracy).First();
            var worstResult = results.OrderBy(r => r.Accuracy).First();

            sb.AppendLine($"Best Performer:  {bestResult.Algorithm} ({bestResult.Parameters})");
            sb.AppendLine($"                 K={bestResult.NumberOfGroups}, Accuracy={bestResult.Accuracy:P2}");
            sb.AppendLine();
            sb.AppendLine($"Worst Performer: {worstResult.Algorithm} ({worstResult.Parameters})");
            sb.AppendLine($"                 K={worstResult.NumberOfGroups}, Accuracy={worstResult.Accuracy:P2}");
            sb.AppendLine();

            // Analysis
            sb.AppendLine("ANALYSIS:");
            sb.AppendLine("?????????????????????????????????????????????????????????????????????????????");

            var k2Results = results.Where(r => r.NumberOfGroups == 2).ToList();
            var k4Results = results.Where(r => r.NumberOfGroups == 4).ToList();

            if (k2Results.Any() && k4Results.Any())
            {
                var avgK2 = k2Results.Average(r => r.Accuracy);
                var avgK4 = k4Results.Average(r => r.Accuracy);

                sb.AppendLine($"  Average accuracy for K=2: {avgK2:P2}");
                sb.AppendLine($"  Average accuracy for K=4: {avgK4:P2}");
                sb.AppendLine();

                if (avgK2 > avgK4)
                {
                    sb.AppendLine("  ? Classification is easier with fewer groups (K=2).");
                    sb.AppendLine("    This is expected as binary classification has simpler decision boundaries.");
                }
                else
                {
                    sb.AppendLine("  ? Interestingly, K=4 classification performed better.");
                    sb.AppendLine("    This may indicate that the data has natural 4-cluster structure.");
                }
            }

            var fastTreeResults = results.Where(r => r.Algorithm == "FastTree").ToList();
            var sdcaResults = results.Where(r => r.Algorithm == "SdcaMaximumEntropy").ToList();

            if (fastTreeResults.Any() && sdcaResults.Any())
            {
                var avgFastTree = fastTreeResults.Average(r => r.Accuracy);
                var avgSdca = sdcaResults.Average(r => r.Accuracy);

                sb.AppendLine();
                sb.AppendLine($"  FastTree average:           {avgFastTree:P2}");
                sb.AppendLine($"  SdcaMaximumEntropy average: {avgSdca:P2}");
                sb.AppendLine();

                if (avgFastTree > avgSdca)
                {
                    sb.AppendLine("  ? FastTree (decision tree) outperformed SDCA.");
                    sb.AppendLine("    This suggests non-linear relationships between features.");
                }
                else
                {
                    sb.AppendLine("  ? SDCA (linear classifier) outperformed FastTree.");
                    sb.AppendLine("    This suggests mostly linear separability between classes.");
                }
            }

            sb.AppendLine();
            sb.AppendLine("?????????????????????????????????????????????????????????????????????????????");
            sb.AppendLine("END OF REPORT");

            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            Log.Information("[{src}] Classification report saved: {path}", sourceName, reportPath);
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

        #endregion
    }
}
