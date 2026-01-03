using Abot2.Core;
using Abot2.Poco;
using Serilog;

namespace WebExplorationProject.Crawling
{
    /// <summary>
    /// BFS (Breadth-First Search) scheduler using FIFO queue.
    /// Explores pages level by level, preventing cycles with visited tracking.
    /// </summary>
    public class BfsScheduler : IScheduler
    {
        private readonly Queue<PageToCrawl> _queue = new();
        private readonly HashSet<string> _visited = new();
        private readonly object _lock = new();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _queue.Count;
                }
            }
        }

        public void Add(PageToCrawl page)
        {
            if (page == null)
                return;

            lock (_lock)
            {
                var url = page.Uri.AbsoluteUri;
                
                // Prevent cycles: only add if not visited
                if (_visited.Contains(url))
                {
                    Log.Debug("[BFS] Skipping already visited URL: {url}", url);
                    return;
                }

                _visited.Add(url);
                _queue.Enqueue(page);
                Log.Debug("[BFS] Added to queue (depth {depth}): {url}", page.CrawlDepth, url);
            }
        }

        public void Add(IEnumerable<PageToCrawl> pages)
        {
            if (pages == null)
                return;

            foreach (var page in pages)
            {
                Add(page);
            }
        }

        public PageToCrawl? GetNext()
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                    return null;

                var page = _queue.Dequeue(); // FIFO - First In, First Out
                Log.Debug("[BFS] Dequeued (depth {depth}): {url}", page.CrawlDepth, page.Uri);
                return page;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
                _visited.Clear();
                Log.Debug("[BFS] Scheduler cleared");
            }
        }

        public void AddKnownUri(Uri uri)
        {
            if (uri == null)
                return;

            lock (_lock)
            {
                _visited.Add(uri.AbsoluteUri);
            }
        }

        public bool IsUriKnown(Uri uri)
        {
            if (uri == null)
                return false;

            lock (_lock)
            {
                return _visited.Contains(uri.AbsoluteUri);
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
}
