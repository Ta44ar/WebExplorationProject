using System.Text.Json;

namespace WebExplorationProject.Search
{
    public class GoogleSearchProvider : ISearchProvider
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;
        private readonly string _cx;
        private readonly SearchCacheService _cache;

        public string ProviderName => "Google";

        public GoogleSearchProvider(HttpClient http, string apiKey, string cx)
        {
            _http = http;
            _apiKey = apiKey;
            _cx = cx;
            _cache = new SearchCacheService();
        }

        public async Task<IList<string>> GetResultsAsync(string query, int maxResults, bool useCache = false)
        {
            if (useCache && _cache.TryReadFromCache(ProviderName, query, out var cachedLinks))
            {
                Console.WriteLine($"[Cache] Loaded {cachedLinks.Count} results from cache file.");
                return cachedLinks.Take(maxResults).ToList();
            }

            var results = new List<string>();
            int startIndex = 1;

            while (results.Count < maxResults)
            {
                int num = Math.Min(10, maxResults - results.Count);
                string url = $"https://www.googleapis.com/customsearch/v1" +
                             $"?key={_apiKey}&cx={_cx}&q={Uri.EscapeDataString(query)}" +
                             $"&start={startIndex}&num={num}";

                using var resp = await _http.GetAsync(url);
                var content = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(content);
                        var msg = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
                        throw new InvalidOperationException($"Google API error: {msg}");
                    }
                    catch { resp.EnsureSuccessStatusCode(); }
                }

                using var json = JsonDocument.Parse(content);
                if (!json.RootElement.TryGetProperty("items", out var items))
                    break;

                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("link", out var link))
                    {
                        var linkVal = link.GetString();
                        if (!string.IsNullOrWhiteSpace(linkVal))
                            results.Add(linkVal);
                    }
                }

                startIndex += num;
                if (items.GetArrayLength() < num)
                    break;

                await Task.Delay(100);
            }

            _cache.WriteToCache(ProviderName, query, results);
            Console.WriteLine($"[Cache] Saved {results.Count} results to cache file.");

            return results;
        }
    }
}