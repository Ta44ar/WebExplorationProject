namespace WebExplorationProject.Models
{
    /// <summary>
    /// Defines the graph traversal strategy for web crawling.
    /// </summary>
    public enum CrawlMode
    {
        /// <summary>
        /// Breadth-First Search - uses FIFO queue (explores level by level).
        /// </summary>
        BFS,

        /// <summary>
        /// Depth-First Search - uses LIFO stack (explores as deep as possible first).
        /// </summary>
        DFS
    }
}
