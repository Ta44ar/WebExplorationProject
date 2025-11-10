namespace WebExplorationProject.Search
{
    public interface ISearchProvider
    {
        Task<IList<string>> GetResultsAsync(string query, int maxResults, bool useCachedResults);
        string ProviderName { get; }
    }
}