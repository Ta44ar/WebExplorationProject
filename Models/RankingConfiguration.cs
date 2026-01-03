namespace WebExplorationProject.Models
{
    /// <summary>
    /// Configuration for the ranking formula weights and thresholds.
    /// </summary>
    public class RankingConfiguration
    {
        // === Weights (must sum to 1.0) ===
        
        /// <summary>Weight for search position score (default: 0.20)</summary>
        public double PositionWeight { get; set; } = 0.20;
        
        /// <summary>Weight for external references score (default: 0.25)</summary>
        public double ReferenceWeight { get; set; } = 0.25;
        
        /// <summary>Weight for specialist terminology score (default: 0.30)</summary>
        public double SpecialistWeight { get; set; } = 0.30;
        
        /// <summary>Weight for emotional/credibility score (default: 0.25)</summary>
        public double EmotionWeight { get; set; } = 0.25;

        // === Thresholds and Normalization ===
        
        /// <summary>Maximum expected outbound links for normalization.</summary>
        public int MaxExpectedOutboundLinks { get; set; } = 50;
        
        /// <summary>Maximum expected unique domains for normalization.</summary>
        public int MaxExpectedUniqueDomains { get; set; } = 20;
        
        /// <summary>Target specialist term density for max score.</summary>
        public double TargetSpecialistDensity { get; set; } = 0.05; // 5%
        
        /// <summary>Emotional word density threshold (above = penalty).</summary>
        public double EmotionalDensityThreshold { get; set; } = 0.02; // 2%

        /// <summary>
        /// Validates that weights sum to 1.0 (with tolerance).
        /// </summary>
        public bool ValidateWeights()
        {
            double sum = PositionWeight + ReferenceWeight + SpecialistWeight + EmotionWeight;
            return Math.Abs(sum - 1.0) < 0.001;
        }
    }
}
