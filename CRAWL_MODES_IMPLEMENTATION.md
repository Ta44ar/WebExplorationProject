# BFS/DFS Implementation Guide

## Overview
This project implements **Breadth-First Search (BFS)** and **Depth-First Search (DFS)** graph traversal strategies for web crawling, with cycle detection using visited URL tracking.

## Theory Compliance

### BFS (Breadth-First Search)
- **Data Structure**: FIFO Queue (`Queue<T>`)
- **Behavior**: Explores pages level by level
- **Implementation**: `BfsScheduler` class
- **Order**: Dequeues the oldest added page first

### DFS (Depth-First Search)
- **Data Structure**: LIFO Stack (`Stack<T>`)
- **Behavior**: Explores as deep as possible before backtracking
- **Implementation**: `DfsScheduler` class
- **Order**: Pops the most recently added page first

### Cycle Prevention
Both schedulers maintain a `HashSet<string>` of visited URLs to prevent infinite loops in cyclic web graphs.

## Architecture

### Files Created

1. **`Models/CrawlMode.cs`** - Enum defining BFS/DFS modes
2. **`Crawling/BfsScheduler.cs`** - FIFO queue-based scheduler
3. **`Crawling/DfsScheduler.cs`** - LIFO stack-based scheduler
4. **`HOW_TO_SEE_DIFFERENCES.md`** - Practical guide to observe BFS/DFS differences

### Modified Files

1. **`Crawling/WebCrawlingService.cs`** 
   - Added `CrawlMode` parameter to constructor
   - Injects custom scheduler into Abot2's `PoliteWebCrawler`
   - Enhanced logging with mode and depth information
   - File names include mode suffix (e.g., `Google_BFS_graph.csv`)
   
2. **`Program.cs`**
   - Reads crawl mode from configuration
   - Passes mode to `WebCrawlingService`

3. **`appsettings.json`**
   - Added `"Crawl": { "Mode": "BFS" }` section

## Usage

### Configuration
Edit `appsettings.json`:

```json
{
  "Crawl": {
    "Mode": "BFS"  // Options: "BFS" or "DFS"
  }
}
```

### Programmatic Usage
```csharp
// BFS mode (default)
var crawler = new WebCrawlingService(crawlMode: CrawlMode.BFS);

// DFS mode
var crawler = new WebCrawlingService(crawlMode: CrawlMode.DFS);
```

## How It Works

### 1. Scheduler Selection
When `CrawlUrlsAsync()` is called, the service creates the appropriate scheduler:

```csharp
IScheduler scheduler = _crawlMode == CrawlMode.BFS 
    ? new BfsScheduler() 
    : new DfsScheduler();
```

### 2. Integration with Abot2
The scheduler is injected into `PoliteWebCrawler`:

```csharp
using var crawler = new PoliteWebCrawler(_config, null, null, scheduler, ...);
```

### 3. URL Processing

**BFS (FIFO Queue)**:
```
Add: URL1 ? URL2 ? URL3
Get: URL1 (oldest) ? URL2 ? URL3
```

**DFS (LIFO Stack)**:
```
Add: URL1 ? URL2 ? URL3
Get: URL3 (newest) ? URL2 ? URL1
```

### 4. Cycle Detection
Before adding a URL, both schedulers check:

```csharp
if (_visited.Contains(url))
{
    Log.Debug("Skipping already visited URL: {url}", url);
    return;
}
_visited.Add(url);
```

## Observable Differences

### In Console Logs
```
[BFS] Crawled depth=0 | https://example.com | ROOT
[BFS] Crawled depth=1 | https://example.com/page1 | from example.com
[BFS] Crawled depth=1 | https://example.com/page2 | from example.com
[BFS] Crawled depth=2 | https://example.com/page1/sub1 | from example.com
```
vs
```
[DFS] Crawled depth=0 | https://example.com | ROOT
[DFS] Crawled depth=1 | https://example.com/page1 | from example.com
[DFS] Crawled depth=2 | https://example.com/page1/sub1 | from example.com
[DFS] Crawled depth=3 | https://example.com/page1/sub1/deep | from example.com
```

### Output Files
Files are named with the crawl mode:
- `Google_BFS_graph.csv` / `Google_DFS_graph.csv`
- `Google_BFS_graph.dot` / `Google_DFS_graph.dot`
- `Google_BFS_graph.png` / `Google_DFS_graph.png`

This allows direct comparison of results from different strategies.

## Comparison

| Feature | BFS | DFS |
|---------|-----|-----|
| Data Structure | Queue (FIFO) | Stack (LIFO) |
| Order | Level by level | Depth first |
| Depth Pattern | 0,1,1,1,2,2,2,3,3,3 | 0,1,2,3,2,1,2,3 |
| Memory | Stores all nodes at current depth | Stores path to current node |
| Use Case | Finding shortest path | Exploring deep hierarchies |
| Graph Handling | Handles cycles with visited set | Handles cycles with visited set |

## Practical Verification

1. **Run with BFS**: Set `"Mode": "BFS"` in appsettings.json, run project
2. **Run with DFS**: Set `"Mode": "DFS"` in appsettings.json, run project
3. **Compare outputs**:
   - CSV files show different crawl order
   - PNG graphs show different visual structure
   - Logs show different depth traversal patterns

See `HOW_TO_SEE_DIFFERENCES.md` for detailed comparison guide.

## Graph Theory Alignment

? **BFS**: Implements FIFO frontier expansion  
? **DFS**: Implements LIFO frontier expansion  
? **Cycle Handling**: Uses visited set to mark explored nodes  
? **Configurable**: Supports runtime strategy selection  
? **Observable**: Clear differences in logs and output files

This implementation satisfies academic requirements for graph traversal algorithms in web crawling context.
