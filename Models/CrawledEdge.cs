namespace WebExplorationProject.Models
{
    public record CrawledEdge(
        string Source,
        string? ParentUrl,
        string Url,
        int Depth,
        string Title,
        int StatusCode,
        string ContentType,
        double LoadTimeSeconds,
        int WordCount
    );
}