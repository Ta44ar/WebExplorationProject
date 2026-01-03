namespace WebExplorationProject.Models
{
    /// <summary>
    /// Configuration for the clustering algorithm.
    /// </summary>
    public class ClusteringConfiguration
    {
        /// <summary>Number of clusters to generate (e.g., 2, 4, 6).</summary>
        public int[] ClusterCounts { get; set; } = { 2, 4, 6 };

        /// <summary>Maximum iterations for K-Means algorithm.</summary>
        public int MaxIterations { get; set; } = 100;

        /// <summary>Number of threads to use for training.</summary>
        public int NumberOfThreads { get; set; } = 4;

        /// <summary>
        /// Feature columns to use for clustering.
        /// Options: "All", "ScoresOnly", "Custom"
        /// </summary>
        public string FeatureMode { get; set; } = "All";

        /// <summary>
        /// Custom feature columns (when FeatureMode = "Custom").
        /// </summary>
        public string[] CustomFeatures { get; set; } = { "PositionScore", "ReferenceScore", "SpecialistScore", "CredibilityScore" };

        /// <summary>
        /// Labels for clusters based on K value.
        /// </summary>
        public Dictionary<int, string[]> ClusterLabels { get; set; } = new()
        {
            [2] = new[] { "Low Credibility", "High Credibility" },
            [4] = new[] { "Very Low", "Low", "Medium", "High" },
            [6] = new[] { "Very Low", "Low", "Below Average", "Above Average", "High", "Very High" }
        };
    }
}
