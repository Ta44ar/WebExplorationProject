using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using WebExplorationProject.Models;

namespace WebExplorationProject.Analysis
{
    /// <summary>
    /// Service for generating page rankings based on multiple criteria.
    /// 
    /// RANKING FORMULA:
    /// TotalScore = w1*PositionScore + w2*ReferenceScore + w3*SpecialistScore + w4*CredibilityScore
    /// 
    /// Where:
    /// - PositionScore: Based on search result position (1st = 1.0, last = 0.0)
    /// - ReferenceScore: Based on outbound links to relevant domains
    /// - SpecialistScore: Based on density of domain-specific terminology
    /// - CredibilityScore: Based on absence of emotional/propaganda language
    /// 
    /// Default weights: w1=0.20, w2=0.25, w3=0.30, w4=0.25
    /// </summary>
    public class RankingService
    {
        private readonly string _dataPath;
        private readonly RankingConfiguration _config;
        private readonly HashSet<string> _specialistTerms;
        private readonly HashSet<string> _emotionalWords;
        private readonly HashSet<string> _propagandaPhrases;
        private readonly bool _usingGeneratedDictionaries;

        public RankingService(
            string dataPath, 
            RankingConfiguration? config = null,
            GeneratedDictionaries? generatedDictionaries = null)
        {
            _dataPath = dataPath;
            _config = config ?? new RankingConfiguration();
            
            Directory.CreateDirectory(_dataPath);

            // Use generated dictionaries if available, otherwise use defaults
            if (generatedDictionaries != null && generatedDictionaries.SpecialistTerms.Count > 0)
            {
                _specialistTerms = generatedDictionaries.SpecialistTerms;
                _emotionalWords = generatedDictionaries.EmotionalWords;
                _propagandaPhrases = generatedDictionaries.PropagandaPhrases;
                _usingGeneratedDictionaries = true;
                
                Log.Information("Using AI-generated dictionaries: {specialist} specialist, {emotional} emotional, {propaganda} propaganda",
                    _specialistTerms.Count, _emotionalWords.Count, _propagandaPhrases.Count);
            }
            else
            {
                // Initialize default term dictionaries
                _specialistTerms = InitializeDefaultSpecialistTerms();
                _emotionalWords = InitializeDefaultEmotionalWords();
                _propagandaPhrases = InitializeDefaultPropagandaPhrases();
                _usingGeneratedDictionaries = false;
                
                Log.Information("Using default dictionaries (no AI generation)");
            }

            if (!_config.ValidateWeights())
            {
                Log.Warning("Ranking weights do not sum to 1.0, results may be skewed");
            }
        }

        /// <summary>
        /// Generates ranking for pages from a specific source's crawl results.
        /// </summary>
        public async Task<List<PageRanking>> GenerateRankingAsync(string sourceName, string? searchQuery = null)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_BFS_graph.csv");
            
            // Try alternative naming patterns
            if (!File.Exists(csvPath))
            {
                csvPath = Path.Combine(_dataPath, $"{sourceName}_DFS_graph.csv");
            }
            if (!File.Exists(csvPath))
            {
                csvPath = Path.Combine(_dataPath, $"{sourceName}_graph.csv");
            }

            if (!File.Exists(csvPath))
            {
                Log.Error("[{src}] No crawl CSV found in: {path}", sourceName, _dataPath);
                return new List<PageRanking>();
            }

            Log.Information("[{src}] Loading crawl data from: {path}", sourceName, csvPath);

            var lines = File.ReadAllLines(csvPath, Encoding.UTF8).Skip(1).ToList();
            int totalPages = lines.Count;

            Log.Information("[{src}] Analyzing {count} pages for ranking", sourceName, totalPages);

            // Build link graph for reference analysis
            var linkGraph = BuildLinkGraph(lines);

            var rankings = new List<PageRanking>();
            int position = 0;

            foreach (var line in lines)
            {
                position++;
                
                var ranking = AnalyzePage(
                    line, 
                    position, 
                    totalPages, 
                    linkGraph, 
                    sourceName);
                
                if (!string.IsNullOrEmpty(ranking.Url))
                {
                    rankings.Add(ranking);
                }

                // Progress logging
                if (position % 100 == 0)
                {
                    Log.Information("[{src}] Analyzed {pos}/{total} pages", 
                        sourceName, position, totalPages);
                }
            }

            // Calculate final scores and assign ranks
            foreach (var ranking in rankings)
            {
                ranking.TotalScore = CalculateTotalScore(ranking);
            }

            // Sort and assign final ranks
            var sortedRankings = rankings.OrderByDescending(r => r.TotalScore).ToList();
            for (int i = 0; i < sortedRankings.Count; i++)
            {
                sortedRankings[i].FinalRank = i + 1;
            }

            // Save results
            SaveRankingToCsv(sortedRankings, sourceName);
            SaveRankingDetails(sortedRankings, sourceName);

            Log.Information("[{src}] Ranking complete. Top 5:", sourceName);
            foreach (var r in sortedRankings.Take(5))
            {
                Log.Information("  #{rank}: {url} (Score: {score:F3})", r.FinalRank, Shorten(r.Url, 60), r.TotalScore);
            }

            return await Task.FromResult(sortedRankings);
        }

        private PageRanking AnalyzePage(
            string csvLine, 
            int position, 
            int totalPages,
            Dictionary<string, HashSet<string>> linkGraph,
            string sourceName)
        {
            var fields = SplitCsvLine(csvLine);
            if (fields.Length < 8)
                return new PageRanking();

            string url = fields[2];
            string title = fields[4];
            string content = fields[7];

            var ranking = new PageRanking
            {
                Url = url,
                Title = title,
                Source = sourceName,
                SearchPosition = position,
                AnalyzedAt = DateTime.UtcNow
            };

            // === Criterion 1: Search Position ===
            ranking.PositionScore = CalculatePositionScore(position, totalPages);

            // === Criterion 2: External References ===
            AnalyzeReferences(ranking, url, linkGraph);

            // === Criterion 3: Specialist Terminology ===
            AnalyzeSpecialistContent(ranking, content);

            // === Criterion 4: Emotional/Propaganda Analysis ===
            AnalyzeEmotionalContent(ranking, content);

            return ranking;
        }

        /// <summary>
        /// Criterion 1: Position Score
        /// Formula: (N - position + 1) / N
        /// </summary>
        private double CalculatePositionScore(int position, int totalPages)
        {
            if (totalPages <= 1) return 1.0;
            return (double)(totalPages - position + 1) / totalPages;
        }

        /// <summary>
        /// Criterion 2: Reference Analysis
        /// </summary>
        private void AnalyzeReferences(PageRanking ranking, string url, Dictionary<string, HashSet<string>> linkGraph)
        {
            if (linkGraph.TryGetValue(url, out var outboundLinks))
            {
                ranking.OutboundLinkCount = outboundLinks.Count;
                ranking.UniqueDomainsReferenced = outboundLinks
                    .Select(ExtractDomain)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
            }

            double linkScore = Math.Min(1.0, (double)ranking.OutboundLinkCount / _config.MaxExpectedOutboundLinks);
            double domainScore = Math.Min(1.0, (double)ranking.UniqueDomainsReferenced / _config.MaxExpectedUniqueDomains);
            
            ranking.ReferenceScore = 0.4 * linkScore + 0.6 * domainScore;
        }

        /// <summary>
        /// Criterion 3: Specialist Terminology Analysis
        /// </summary>
        private void AnalyzeSpecialistContent(PageRanking ranking, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                ranking.SpecialistScore = 0;
                return;
            }

            var contentLower = content.ToLowerInvariant();
            var words = Regex.Matches(contentLower, @"\b\w+\b");
            ranking.TotalWordCount = words.Count;

            int specialistCount = 0;
            foreach (var term in _specialistTerms)
            {
                var pattern = @"\b" + Regex.Escape(term.ToLowerInvariant()) + @"\b";
                specialistCount += Regex.Matches(contentLower, pattern).Count;
            }

            ranking.SpecialistTermCount = specialistCount;
            ranking.SpecialistTermDensity = ranking.TotalWordCount > 0 
                ? (double)specialistCount / ranking.TotalWordCount 
                : 0;

            double densityRatio = ranking.SpecialistTermDensity / _config.TargetSpecialistDensity;
            ranking.SpecialistScore = Math.Min(1.0, densityRatio);
        }

        /// <summary>
        /// Criterion 4: Emotional/Propaganda Analysis (rule-based only)
        /// </summary>
        private void AnalyzeEmotionalContent(PageRanking ranking, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                ranking.EmotionScore = 0.5;
                ranking.CredibilityScore = 0.5;
                return;
            }

            var contentLower = content.ToLowerInvariant();

            // Count emotional words
            ranking.EmotionalWordCount = _emotionalWords
                .Sum(word => Regex.Matches(contentLower, @"\b" + Regex.Escape(word.ToLowerInvariant()) + @"\b").Count);

            // Count propaganda phrases
            ranking.PropagandaPhraseCount = _propagandaPhrases
                .Sum(phrase => Regex.Matches(contentLower, Regex.Escape(phrase.ToLowerInvariant())).Count);

            // Calculate credibility score
            double emotionalRatio = ranking.TotalWordCount > 0 
                ? (double)ranking.EmotionalWordCount / ranking.TotalWordCount 
                : 0;
            double propagandaRatio = ranking.TotalWordCount > 0 
                ? (double)ranking.PropagandaPhraseCount * 5 / ranking.TotalWordCount 
                : 0;

            ranking.CredibilityScore = Math.Max(0, 1.0 - emotionalRatio * 20 - propagandaRatio * 10);

            // EmotionScore: higher = more emotional (bad)
            double emotionalDensity = ranking.TotalWordCount > 0 
                ? (double)(ranking.EmotionalWordCount + ranking.PropagandaPhraseCount * 2) / ranking.TotalWordCount
                : 0;
            ranking.EmotionScore = Math.Min(1.0, emotionalDensity / _config.EmotionalDensityThreshold);
        }

        /// <summary>
        /// Calculates the final weighted score.
        /// </summary>
        private double CalculateTotalScore(PageRanking ranking)
        {
            return _config.PositionWeight * ranking.PositionScore
                 + _config.ReferenceWeight * ranking.ReferenceScore
                 + _config.SpecialistWeight * ranking.SpecialistScore
                 + _config.EmotionWeight * ranking.CredibilityScore;
        }

        private void SaveRankingToCsv(List<PageRanking> rankings, string sourceName)
        {
            var outputPath = Path.Combine(_dataPath, $"{sourceName}_ranking.csv");

            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(true));
            
            writer.WriteLine("FinalRank,Url,Title,TotalScore,PositionScore,ReferenceScore,SpecialistScore,CredibilityScore,EmotionScore,SearchPosition,OutboundLinks,SpecialistTerms,EmotionalWords,PropagandaPhrases,Notes");

            foreach (var r in rankings)
            {
                writer.WriteLine(string.Join(",",
                    r.FinalRank,
                    EscapeCsv(r.Url),
                    EscapeCsv(r.Title),
                    r.TotalScore.ToString("F4", CultureInfo.InvariantCulture),
                    r.PositionScore.ToString("F4", CultureInfo.InvariantCulture),
                    r.ReferenceScore.ToString("F4", CultureInfo.InvariantCulture),
                    r.SpecialistScore.ToString("F4", CultureInfo.InvariantCulture),
                    r.CredibilityScore.ToString("F4", CultureInfo.InvariantCulture),
                    r.EmotionScore.ToString("F4", CultureInfo.InvariantCulture),
                    r.SearchPosition,
                    r.OutboundLinkCount,
                    r.SpecialistTermCount,
                    r.EmotionalWordCount,
                    r.PropagandaPhraseCount,
                    EscapeCsv(r.Notes)));
            }

            Log.Information("[{src}] Ranking saved: {path}", sourceName, outputPath);
        }

        private void SaveRankingDetails(List<PageRanking> rankings, string sourceName)
        {
            var detailsPath = Path.Combine(_dataPath, $"{sourceName}_ranking_details.txt");
            var sb = new StringBuilder();

            sb.AppendLine($"=== RANKING REPORT: {sourceName} ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total pages analyzed: {rankings.Count}");
            sb.AppendLine($"Dictionary source: {(_usingGeneratedDictionaries ? "AI-generated" : "Default")}");
            sb.AppendLine();
            sb.AppendLine("=== FORMULA ===");
            sb.AppendLine("TotalScore = w1*PositionScore + w2*ReferenceScore + w3*SpecialistScore + w4*CredibilityScore");
            sb.AppendLine();
            sb.AppendLine("Weights used:");
            sb.AppendLine($"  w1 (Position):   {_config.PositionWeight:F2}");
            sb.AppendLine($"  w2 (Reference):  {_config.ReferenceWeight:F2}");
            sb.AppendLine($"  w3 (Specialist): {_config.SpecialistWeight:F2}");
            sb.AppendLine($"  w4 (Credibility):{_config.EmotionWeight:F2}");
            sb.AppendLine();
            sb.AppendLine("=== TOP 10 RESULTS ===");
            
            foreach (var r in rankings.Take(10))
            {
                sb.AppendLine($"\n#{r.FinalRank}: {r.Title}");
                sb.AppendLine($"  URL: {r.Url}");
                sb.AppendLine($"  Total Score: {r.TotalScore:F4}");
                sb.AppendLine($"  Position Score: {r.PositionScore:F4} (position: {r.SearchPosition})");
                sb.AppendLine($"  Reference Score: {r.ReferenceScore:F4} (links: {r.OutboundLinkCount}, domains: {r.UniqueDomainsReferenced})");
                sb.AppendLine($"  Specialist Score: {r.SpecialistScore:F4} (terms: {r.SpecialistTermCount}, density: {r.SpecialistTermDensity:P2})");
                sb.AppendLine($"  Credibility Score: {r.CredibilityScore:F4} (emotional: {r.EmotionalWordCount}, propaganda: {r.PropagandaPhraseCount})");
            }

            File.WriteAllText(detailsPath, sb.ToString(), Encoding.UTF8);
            Log.Information("[{src}] Ranking details saved: {path}", sourceName, detailsPath);
        }

        #region Helper Methods

        private Dictionary<string, HashSet<string>> BuildLinkGraph(List<string> csvLines)
        {
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in csvLines)
            {
                var fields = SplitCsvLine(line);
                if (fields.Length < 3) continue;

                var parent = fields[1];
                var child = fields[2];

                if (!graph.ContainsKey(parent))
                    graph[parent] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                graph[parent].Add(child);
            }

            return graph;
        }

        private string? ExtractDomain(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return uri.Host;
            return null;
        }

        private HashSet<string> InitializeDefaultSpecialistTerms()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // General scientific terms
                "badanie kliniczne", "peer-review", "metaanaliza", "randomizacja",
                "grupa kontrolna", "placebo", "skuteczność", "bezpieczeństwo",
                "epidemiologia", "immunologia", "patogen", "antygen", "przeciwciało",
                "efekt uboczny", "działanie niepożądane", "korelacja", "przyczynowość",
                "statystycznie istotny", "próba", "populacja", "badanie kohortowe",
                // Vaccine terms
                "szczepionka", "immunizacja", "odporność", "adiuwant",
                "mRNA", "wektor wirusowy", "odpowiedź immunologiczna", "tiomersal",
                "aluminium", "skuteczność szczepionki", "NOP", "VAERS", "kalendarz szczepień",
                "odporność zbiorowiskowa", "szczepienia ochronne", "dawka przypominająca",
                // Autism terms
                "autyzm", "spektrum autyzmu", "ASD", "zaburzenie rozwojowe",
                "diagnoza", "terapia behawioralna", "neurologia", "genetyka",
                "rozwój dziecka", "neuroróżnorodność", "Asperger",
                // English equivalents
                "clinical trial", "peer-review", "meta-analysis", "randomization",
                "control group", "placebo", "efficacy", "safety", "epidemiology"
            };
        }

        private HashSet<string> InitializeDefaultEmotionalWords()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "szokujący", "przerażający", "niesamowity", "niewiarygodny",
                "skandal", "afera", "tragedia", "katastrofa", "zbrodnia",
                "oszustwo", "kłamstwo", "spisek", "ukrywają", "cenzura",
                "prawda", "ujawniono", "sekret", "rewelacje", "bomba",
                "dramat", "horror", "masakra", "apokalipsa",
                "shocking", "unbelievable", "incredible", "amazing",
                "scandal", "conspiracy", "cover-up", "hidden", "secret",
                "truth", "revealed", "exposed", "bombshell", "outrageous"
            };
        }

        private HashSet<string> InitializeDefaultPropagandaPhrases()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "twoje dziecko", "ryzykujesz życie", "nie daj się oszukać",
                "przemysł farmaceutyczny", "big pharma", "rząd ukrywa",
                "naukowcy twierdzą", "badania wykazały", "eksperci mówią",
                "według badań", "udowodniono że",
                "wszyscy wiedzą", "każdy wie", "oczywiste jest",
                "nikt nie wierzy", "miliony ludzi",
                "ukryta prawda", "nie chcą żebyś wiedział", "mainstream media",
                "alternatywna medycyna", "naturalne metody",
                "musisz to zobaczyć", "nie uwierzysz", "szok",
                "pilne", "breaking", "ekskluzywne",
                "they don't want you to know", "hidden truth", "wake up",
                "do your own research", "mainstream media lies"
            };
        }

        private static string[] SplitCsvLine(string line)
        {
            var matches = Regex.Matches(line, "(?<=^|,)(\"(?:[^\"]|\"\")*\"|[^,]*)");
            return matches.Select(m => m.Value.Trim().Trim('"').Replace("\"\"", "\"")).ToArray();
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