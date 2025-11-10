using System.Text;

namespace WebExplorationProject.Search
{
    public class SearchCacheService
    {
        private readonly string _basePath;

        public SearchCacheService(string basePath = "data")
        {
            if (!Path.IsPathRooted(basePath))
            {
                var projectDir = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName 
                    ?? AppContext.BaseDirectory;
                _basePath = Path.Combine(projectDir, basePath);
            }
            else
            {
                _basePath = basePath;
            }

            Directory.CreateDirectory(_basePath);
        }

        private string SanitizeFileName(string query, string provider)
        {
            var safeQuery = string.Join("_", query.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            safeQuery = safeQuery.Replace(' ', '_').ToLowerInvariant();
            return Path.Combine(_basePath, $"{provider.ToLowerInvariant()}_{safeQuery}.txt");
        }

        public bool TryReadFromCache(string provider, string query, out IList<string> links)
        {
            var file = SanitizeFileName(query, provider);
            links = new List<string>();

            if (!File.Exists(file))
                return false;

            links = File.ReadAllLines(file)
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Distinct()
                        .ToList();

            return links.Count > 0;
        }

        public void WriteToCache(string provider, string query, IList<string> links)
        {
            var file = SanitizeFileName(query, provider);
            File.WriteAllLines(file, links, Encoding.UTF8);
        }
    }
}