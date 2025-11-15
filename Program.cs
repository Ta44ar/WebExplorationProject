using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Crawling;
using WebExplorationProject.Search;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            //.MinimumLevel.Warning()
            .CreateLogger();

        Log.Information("=== Web Exploration Project start ===");

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets<Program>(optional: true);

        var configuration = builder.Build();

        string? googleApiKey = configuration["GOOGLE_API_KEY"] ?? configuration["Secrets:GoogleApiKey"];
        string? googleCx = configuration["GOOGLE_CX"] ?? configuration["Secrets:GoogleCx"];
        string? braveToken = configuration["BRAVE_API_KEY"] ?? configuration["Secrets:BraveApiKey"];
        int maxResults = int.Parse(configuration["Search:MaxResults"] ?? "12");

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

        var http = new HttpClient();
        var googleProvider = new GoogleSearchProvider(http, googleApiKey, googleCx);
        var braveProvider = !string.IsNullOrEmpty(braveToken)
            ? new BraveSearchProvider(http, braveToken)
            : null;

        var crawler = new WebCrawlingService();

        Log.Information("\n=== GOOGLE ===");
        Log.Information("Fetching results from Google for query: '{query}'...", query);

        var googleResults = await googleProvider.GetResultsAsync(query, maxResults, useCache);
        Log.Information("[Google] Retrieved {count} links.", googleResults.Count);

        if (googleResults.Count > 0)
        {
            Log.Information("[Google] Starting crawling...");
            await crawler.CrawlUrlsAsync(googleResults, googleProvider.ProviderName);
        }

        if (braveProvider != null)
        {
            Log.Information("\n=== BRAVE ===");
            Log.Information("Fetching results from Brave for query: '{query}'...", query);

            var braveResults = await braveProvider.GetResultsAsync(query, maxResults, useCache);
            Log.Information("[Brave] Retrieved {count} links.", braveResults.Count);

            if (braveResults.Count > 0)
            {
                Log.Information("[Brave] Starting crawling...");
                await crawler.CrawlUrlsAsync(braveResults, braveProvider.ProviderName);
            }
        }

        Log.Information("\nProgram execution completed.");
    }
}