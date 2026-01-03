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

        // === AI Analysis Settings ===
        
        /// <summary>Whether to use AI for sentiment/credibility analysis.</summary>
        public bool UseAiAnalysis { get; set; } = true;
        
        /// <summary>AI provider to use (e.g., "OpenAI", "Azure").</summary>
        public string AiProvider { get; set; } = "OpenAI";
        
        /// <summary>Maximum content length to send to AI (chars).</summary>
        public int MaxContentForAi { get; set; } = 4000;
        
        /// <summary>Maximum number of pages to analyze with AI (to control costs/rate limits).</summary>
        public int MaxPagesForAiAnalysis { get; set; } = 50;
        
        /// <summary>Delay between AI requests in milliseconds (to avoid rate limiting).</summary>
        public int AiRequestDelayMs { get; set; } = 1000;

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
