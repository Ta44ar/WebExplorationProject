using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WebExplorationProject.Search
{
    public class BraveSearchProvider : ISearchProvider
    {
        private readonly HttpClient _http;
        private readonly string _token;
        private readonly SearchCacheService _cache;

        public string ProviderName => "Brave";

        public BraveSearchProvider(HttpClient http, string token)
        {
            _http = http;
            _token = token;
            _cache = new SearchCacheService();
        }

        public async Task<IList<string>> GetResultsAsync(string query, int maxResults, bool useCache = false)
        {
            if (useCache && _cache.TryReadFromCache(ProviderName, query, out var cachedLinks))
            {
                Console.WriteLine($"[Cache] (Brave) Loaded {cachedLinks.Count} links from cache.");
                return cachedLinks.Take(maxResults).ToList();
            }

            var allResults = new List<string>();

            var requestUrl = $"https://api.search.brave.com/res/v1/web/search" +
                             $"?q={Uri.EscapeDataString(query)}" +
                             $"&count={maxResults}" +
                             $"&country=pl";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("X-Subscription-Token", _token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                using var resp = await _http.SendAsync(request);
                var content = await resp.Content.ReadAsStringAsync();

                if (resp.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    Console.WriteLine("[Brave] Limit of 2000 queries/month exceeded (HTTP 429).");
                    if (_cache.TryReadFromCache(ProviderName, query, out var fallback))
                    {
                        Console.WriteLine("[Brave] Using cached data.");
                        return fallback.Take(maxResults).ToList();
                    }
                    return allResults;
                }

                if (!resp.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Brave] API error ({resp.StatusCode})");
                    return allResults;
                }

                using var json = JsonDocument.Parse(content);

                if (json.RootElement.TryGetProperty("web", out var webSection) &&
                    webSection.TryGetProperty("results", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("url", out var urlElem))
                        {
                            var url = urlElem.GetString();
                            if (!string.IsNullOrWhiteSpace(url))
                                allResults.Add(url);
                        }
                    }
                }

                Console.WriteLine($"[Brave] API returned {allResults.Count} results.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Brave] Exception: {ex.Message}");
            }

            allResults = allResults
                .Distinct()
                .Take(maxResults)
                .ToList();

            Console.WriteLine($"[Brave] Collected {allResults.Count} results in total.");
            _cache.WriteToCache(ProviderName, query, allResults);

            return allResults;
        }
    }
}