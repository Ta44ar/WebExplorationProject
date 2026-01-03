namespace WebExplorationProject.Models
{
    /// <summary>
    /// Represents the ranking analysis result for a single page.
    /// Contains scores for each ranking criterion and the final weighted score.
    /// </summary>
    public class PageRanking
    {
        // === Identification ===
        
        /// <summary>URL of the analyzed page.</summary>
        public string Url { get; set; } = "";
        
        /// <summary>Title of the page.</summary>
        public string Title { get; set; } = "";
        
        /// <summary>Search provider source (Google/Brave).</summary>
        public string Source { get; set; } = "";

        // === Criterion 1: Search Position ===
        
        /// <summary>Position in search results (1-based).</summary>
        public int SearchPosition { get; set; }
        
        /// <summary>Normalized score based on position (higher = better position).</summary>
        public double PositionScore { get; set; }

        // === Criterion 2: External References ===
        
        /// <summary>Number of outbound links to other domains on the topic.</summary>
        public int OutboundLinkCount { get; set; }
        
        /// <summary>Number of unique domains referenced.</summary>
        public int UniqueDomainsReferenced { get; set; }
        
        /// <summary>Normalized score for references (0-1).</summary>
        public double ReferenceScore { get; set; }

        // === Criterion 3: Specialist Terminology ===
        
        /// <summary>Count of specialist/expert terms found.</summary>
        public int SpecialistTermCount { get; set; }
        
        /// <summary>Total word count in content.</summary>
        public int TotalWordCount { get; set; }
        
        /// <summary>Density of specialist terms (terms/total words).</summary>
        public double SpecialistTermDensity { get; set; }
        
        /// <summary>Normalized score for specialist content (0-1).</summary>
        public double SpecialistScore { get; set; }

        // === Criterion 4: Emotional/Propaganda Language ===
        
        /// <summary>Count of emotional/loaded words found.</summary>
        public int EmotionalWordCount { get; set; }
        
        /// <summary>Count of propaganda/persuasion phrases found.</summary>
        public int PropagandaPhraseCount { get; set; }
        
        /// <summary>AI-based sentiment analysis score (-1 to 1, where 0 is neutral).</summary>
        public double SentimentScore { get; set; }
        
        /// <summary>AI-based credibility assessment (0-1).</summary>
        public double CredibilityScore { get; set; }
        
        /// <summary>Combined emotional/propaganda score (0-1, lower = more neutral/credible).</summary>
        public double EmotionScore { get; set; }

        // === Final Scores ===
        
        /// <summary>
        /// Final weighted score combining all criteria.
        /// Formula: w1*PositionScore + w2*ReferenceScore + w3*SpecialistScore + w4*(1-EmotionScore)
        /// </summary>
        public double TotalScore { get; set; }
        
        /// <summary>Final ranking position (1 = best).</summary>
        public int FinalRank { get; set; }

        // === Metadata ===
        
        /// <summary>When the analysis was performed.</summary>
        public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>Any notes or flags from analysis.</summary>
        public string Notes { get; set; } = "";
    }
}