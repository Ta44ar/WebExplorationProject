using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Crawling;
using WebExplorationProject.Models;
using WebExplorationProject.Search;

namespace WebExplorationProject.Tasks
{
    /// <summary>
    /// Task 1: Web search and crawling.
    /// Searches for URLs using Google/Brave, crawls them with BFS/DFS,
    /// generates CSV, DOT, and PNG graph files.
    /// </summary>
    public class CrawlingTask : ITask
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public string Name => "Crawling Task";
        public string Description => "Search engines (Google/Brave) → Crawling (BFS/DFS) → Graph generation (CSV/DOT/PNG)";

        public CrawlingTask(IConfiguration configuration, HttpClient httpClient)
        {
            _configuration = configuration;
            _httpClient = httpClient;
        }

        public async Task ExecuteAsync()
        {
            Log.Information("=== TASK 1: Web Crawling ===");
            Log.Information(Description);

            // Read configuration
            string? googleApiKey = _configuration["GOOGLE_API_KEY"] ?? _configuration["Secrets:GoogleApiKey"];
            string? googleCx = _configuration["GOOGLE_CX"] ?? _configuration["Secrets:GoogleCx"];
            string? braveToken = _configuration["BRAVE_API_KEY"] ?? _configuration["Secrets:BraveApiKey"];

            string crawlModeStr = _configuration["Crawl:Mode"] ?? "BFS";
            CrawlMode crawlMode = Enum.TryParse<CrawlMode>(crawlModeStr, true, out var mode)
                ? mode
                : CrawlMode.BFS;

            string query = _configuration["Search:Query"] ?? "czy szczepionki powodują autyzm";
            bool useCache = bool.TryParse(_configuration["Search:UseCache"], out var cache) && cache;
            int maxResults = int.TryParse(_configuration["Search:MaxResults"], out var max) ? max : 20;

            Log.Information("Query: '{query}'", query);
            Log.Information("Crawl mode: {mode}", crawlMode);
            Log.Information("Max results: {max}, Use cache: {cache}", maxResults, useCache);

            if (string.IsNullOrEmpty(googleApiKey) || string.IsNullOrEmpty(googleCx))
            {
                Log.Error("Missing required: GOOGLE_API_KEY/GOOGLE_CX");
                return;
            }

            var providers = new List<ISearchProvider>();
            providers.Add(new GoogleSearchProvider(_httpClient, googleApiKey, googleCx));

            if (!string.IsNullOrEmpty(braveToken))
            {
                providers.Add(new BraveSearchProvider(_httpClient, braveToken));
            }
            else
            {
                Log.Warning("Brave API token not configured - skipping Brave provider");
            }

            var crawler = new WebCrawlingService(crawlMode: crawlMode);
            var crawledProviders = new List<string>();

            foreach (var provider in providers)
            {
                Log.Information("\n=== {Provider} ===", provider.ProviderName.ToUpper());

                var results = await provider.GetResultsAsync(query, maxResults, useCache);
                Log.Information("[{Provider}] Retrieved {count} links.", provider.ProviderName, results.Count);

                if (results.Count > 0)
                {
                    await crawler.CrawlUrlsAsync(results, provider.ProviderName);
                    crawledProviders.Add(provider.ProviderName);
                }
            }

            // Generate comparison if both sources were crawled
            if (crawledProviders.Contains("Google") && crawledProviders.Contains("Brave"))
            {
                Log.Information("\n=== COMPARISON ===");
                crawler.ExportComparisonArtifacts("Google", "Brave");
            }

            Log.Information("Task 1 completed. Check 'data' folder for results.");
        }
    }
}