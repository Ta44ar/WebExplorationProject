using Microsoft.ML.Data;

namespace WebExplorationProject.Models
{
    /// <summary>
    /// Input data for ML.NET clustering algorithm.
    /// Features are the normalized ranking scores from Task 2.
    /// </summary>
    public class ClusteringInput
    {
        /// <summary>URL of the page (not used as feature).</summary>
        public string Url { get; set; } = "";

        /// <summary>Title of the page (not used as feature).</summary>
        public string Title { get; set; } = "";

        /// <summary>Source (Google/Brave) - not used as feature.</summary>
        public string Source { get; set; } = "";

        /// <summary>Original ranking position.</summary>
        public int OriginalRank { get; set; }

        /// <summary>Feature 1: Position score (0-1).</summary>
        [LoadColumn(0)]
        public float PositionScore { get; set; }

        /// <summary>Feature 2: Reference score (0-1).</summary>
        [LoadColumn(1)]
        public float ReferenceScore { get; set; }

        /// <summary>Feature 3: Specialist terminology score (0-1).</summary>
        [LoadColumn(2)]
        public float SpecialistScore { get; set; }

        /// <summary>Feature 4: Credibility score (0-1).</summary>
        [LoadColumn(3)]
        public float CredibilityScore { get; set; }

        /// <summary>Total score from Task 2.</summary>
        public float TotalScore { get; set; }
    }

    /// <summary>
    /// Output prediction from ML.NET clustering.
    /// </summary>
    public class ClusteringPrediction
    {
        /// <summary>Predicted cluster ID (0-based).</summary>
        [ColumnName("PredictedLabel")]
        public uint ClusterId { get; set; }

        /// <summary>Distances to all cluster centroids.</summary>
        [ColumnName("Score")]
        public float[]? Distances { get; set; }
    }

    /// <summary>
    /// Combined result with original data and cluster assignment.
    /// </summary>
    public class ClusteredPage
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
        public string Source { get; set; } = "";
        public int OriginalRank { get; set; }
        public float PositionScore { get; set; }
        public float ReferenceScore { get; set; }
        public float SpecialistScore { get; set; }
        public float CredibilityScore { get; set; }
        public float TotalScore { get; set; }
        public int ClusterId { get; set; }
        public string ClusterLabel { get; set; } = "";
        public float DistanceToCenter { get; set; }
    }

    /// <summary>
    /// Statistics for a single cluster.
    /// </summary>
    public class ClusterStatistics
    {
        public int ClusterId { get; set; }
        public string Label { get; set; } = "";
        public int PageCount { get; set; }
        public float AvgPositionScore { get; set; }
        public float AvgReferenceScore { get; set; }
        public float AvgSpecialistScore { get; set; }
        public float AvgCredibilityScore { get; set; }
        public float AvgTotalScore { get; set; }
        public float MinTotalScore { get; set; }
        public float MaxTotalScore { get; set; }
    }
}
