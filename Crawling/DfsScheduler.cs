using Abot2.Core;
using Abot2.Poco;
using Serilog;

namespace WebExplorationProject.Crawling
{
    /// <summary>
    /// DFS (Depth-First Search) scheduler using LIFO stack.
    /// Explores as deep as possible before backtracking, preventing cycles with visited tracking.
    /// </summary>
    public class DfsScheduler : IScheduler
    {
        private readonly Stack<PageToCrawl> _stack = new();
        private readonly HashSet<string> _visited = new();
        private readonly object _lock = new();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _stack.Count;
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
                    Log.Debug("[DFS] Skipping already visited URL: {url}", url);
                    return;
                }

                _visited.Add(url);
                _stack.Push(page);
                Log.Debug("[DFS] Pushed to stack (depth {depth}): {url}", page.CrawlDepth, url);
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
                if (_stack.Count == 0)
                    return null;

                var page = _stack.Pop(); // LIFO - Last In, First Out
                Log.Debug("[DFS] Popped (depth {depth}): {url}", page.CrawlDepth, page.Uri);
                return page;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _stack.Clear();
                _visited.Clear();
                Log.Debug("[DFS] Scheduler cleared");
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
