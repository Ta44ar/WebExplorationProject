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
        string Content,
        string Description,
        double LoadTimeSeconds,
        int WordCount
    );
}