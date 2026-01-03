using Microsoft.ML.Data;

namespace WebExplorationProject.Models
{
    /// <summary>
    /// Input data for classification algorithms.
    /// Features are the ranking scores, Label is the cluster assignment.
    /// </summary>
    public class ClassificationInput
    {
        /// <summary>URL of the page (not used as feature).</summary>
        public string Url { get; set; } = "";

        /// <summary>Cluster label (target for classification).</summary>
        [LoadColumn(0)]
        public uint Label { get; set; }

        /// <summary>Feature 1: Position score (0-1).</summary>
        [LoadColumn(1)]
        public float PositionScore { get; set; }

        /// <summary>Feature 2: Reference score (0-1).</summary>
        [LoadColumn(2)]
        public float ReferenceScore { get; set; }

        /// <summary>Feature 3: Specialist terminology score (0-1).</summary>
        [LoadColumn(3)]
        public float SpecialistScore { get; set; }

        /// <summary>Feature 4: Credibility score (0-1).</summary>
        [LoadColumn(4)]
        public float CredibilityScore { get; set; }
    }

    /// <summary>
    /// Output prediction from classification.
    /// </summary>
    public class ClassificationPrediction
    {
        [ColumnName("PredictedLabel")]
        public uint PredictedLabel { get; set; }

        [ColumnName("Score")]
        public float[]? Score { get; set; }
    }

    /// <summary>
    /// Result of a single classification experiment.
    /// </summary>
    public class ClassificationResult
    {
        public string Algorithm { get; set; } = "";
        public string Parameters { get; set; } = "";
        public int NumberOfGroups { get; set; }
        public int FoldCount { get; set; }
        
        // Metrics
        public double Accuracy { get; set; }
        public double MacroAccuracy { get; set; }
        public double MicroAccuracy { get; set; }
        public double LogLoss { get; set; }
        public double LogLossReduction { get; set; }
        
        // Per-class metrics
        public Dictionary<int, double> PerClassPrecision { get; set; } = new();
        public Dictionary<int, double> PerClassRecall { get; set; } = new();
        
        // Confusion matrix
        public int[,]? ConfusionMatrix { get; set; }
        
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
        public double TrainingTimeSeconds { get; set; }
    }

    /// <summary>
    /// Configuration for a classification experiment.
    /// </summary>
    public class ClassificationExperiment
    {
        public string AlgorithmName { get; set; } = "";
        public string ParameterDescription { get; set; } = "";
        public int NumberOfGroups { get; set; }
        public Func<Microsoft.ML.MLContext, Microsoft.ML.IEstimator<Microsoft.ML.ITransformer>> TrainerFactory { get; set; } = null!;
    }
}
