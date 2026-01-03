using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Crawling;
using WebExplorationProject.Models;
using WebExplorationProject.Search;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            //.MinimumLevel.Information()
            .MinimumLevel.Warning()
            .CreateLogger();

        Log.Information("=== Web Exploration Project start ===");

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets<Program>(optional: true);

        var configuration = builder.Build();

        string? googleApiKey = configuration["GOOGLE_API_KEY"] ?? configuration["Secrets:GoogleApiKey"];
        string? googleCx = configuration["GOOGLE_CX"] ?? configuration["Secrets:GoogleCx"];
        string? braveToken = configuration["BRAVE_API_KEY"] ?? configuration["Secrets:BraveApiKey"];

        // Read crawl mode from configuration (default: BFS)
        string crawlModeStr = configuration["Crawl:Mode"] ?? "BFS";
        CrawlMode crawlMode = Enum.TryParse<CrawlMode>(crawlModeStr, true, out var mode) 
            ? mode 
            : CrawlMode.BFS;

        Log.Information("Crawl mode selected: {mode} ({description})", 
            crawlMode, 
            crawlMode == CrawlMode.BFS ? "Breadth-First Search - FIFO queue" : "Depth-First Search - LIFO stack");

        if (string.IsNullOrEmpty(googleApiKey) || string.IsNullOrEmpty(googleCx))
        {
            Log.Error("Missing required environment variables: GOOGLE_API_KEY/GOOGLE_CX");
            return;
        }

        if (string.IsNullOrEmpty(braveToken))
        {
            Log.Warning("Missing Brave API token");
        }

        string query = "czy szczepionki powodują autyzm";
        bool useCache = true;
        int maxResults = 20;

        var http = new HttpClient();
        var googleProvider = new GoogleSearchProvider(http, googleApiKey, googleCx);
        var braveProvider = new BraveSearchProvider(http, braveToken);
        var crawler = new WebCrawlingService(crawlMode: crawlMode);

        bool googleCrawled = false;
        bool braveCrawled = false;

        var crawledProviders = new HashSet<string>();

        async Task CrawlProviderAsync(ISearchProvider provider)
        {
            Log.Information("\n=== {Provider} ===", provider.ProviderName.ToUpper());
            Log.Information("Fetching results from {Provider} for query: '{query}'...", provider.ProviderName, query);

            var results = await provider.GetResultsAsync(query, maxResults, useCache);
            Log.Information("[{Provider}] Retrieved {count} links.", provider.ProviderName, results.Count);

            if (results.Count > 0)
            {
                Log.Information("[{Provider}] Starting crawling...", provider.ProviderName);
                await crawler.CrawlUrlsAsync(results, provider.ProviderName);
                crawledProviders.Add(provider.ProviderName);
            }
        }

        ISearchProvider[] providers = [googleProvider, braveProvider];

        foreach (var provider in providers)
        {
            await CrawlProviderAsync(provider);
        }

        // Generate comparison artifacts if both sources were crawled
        if (googleCrawled && braveCrawled)
        {
            Log.Information("\n=== COMPARISON ===");
            Log.Information("Generating comparison artifacts...");
            crawler.ExportComparisonArtifacts(googleProvider.ProviderName, "Brave");
            Log.Information("Comparison complete. Check data folder for Compare_Google_Brave_* files.");
        }
        else
        {
            Log.Warning("Skipping comparison - not all sources were crawled (Google: {g}, Brave: {b})", 
                googleCrawled, braveCrawled);
        }

        Log.Information("\nProgram execution completed.");
    }
}