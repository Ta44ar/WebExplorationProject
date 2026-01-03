namespace WebExplorationProject.Analysis
{
    /// <summary>
    /// Result of AI-based content analysis.
    /// </summary>
    public class AiAnalysisResult
    {
        /// <summary>Sentiment score from -1 (very negative) to 1 (very positive). 0 = neutral.</summary>
        public double SentimentScore { get; set; }
        
        /// <summary>Credibility score from 0 (not credible) to 1 (highly credible).</summary>
        public double CredibilityScore { get; set; }
        
        /// <summary>Detected propaganda/persuasion techniques.</summary>
        public List<string> DetectedTechniques { get; set; } = new();
        
        /// <summary>Key emotional words found.</summary>
        public List<string> EmotionalWords { get; set; } = new();
        
        /// <summary>Brief explanation from AI.</summary>
        public string Explanation { get; set; } = "";
        
        /// <summary>Whether the analysis was successful.</summary>
        public bool Success { get; set; }
        
        /// <summary>Error message if analysis failed.</summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Interface for AI-based content analysis.
    /// </summary>
    public interface IAiAnalyzer
    {
        /// <summary>
        /// Analyzes content for sentiment, credibility, and propaganda techniques.
        /// </summary>
        /// <param name="content">Text content to analyze.</param>
        /// <param name="context">Optional context (e.g., search query topic).</param>
        /// <returns>Analysis result with scores and detected patterns.</returns>
        Task<AiAnalysisResult> AnalyzeContentAsync(string content, string? context = null);
    }
}
