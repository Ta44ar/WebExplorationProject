using Abot2.Core;
using Abot2.Crawler;
using Abot2.Poco;
using Serilog;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using WebExplorationProject.Helpers;
using WebExplorationProject.Models;

namespace WebExplorationProject.Crawling
{
    public class WebCrawlingService
    {
        private readonly CrawlConfiguration _config;
        private readonly int _maxWidth;
        private readonly List<CrawledEdge> _edges = new();
        private string _currentSource = "Unknown";
        private readonly string _dataPath;
        private readonly CrawlMode _crawlMode;
        private const int BatchSize = 50;

        public WebCrawlingService(int maxDepth = 3, int maxWidth = 30, CrawlMode crawlMode = CrawlMode.BFS)
        {
            _config = new CrawlConfiguration
            {
                MaxPagesToCrawl = maxWidth,
                MaxCrawlDepth = maxDepth,
                IsUriRecrawlingEnabled = false,
                DownloadableContentTypes = "text/html",
                MaxConcurrentThreads = 10,
                CrawlTimeoutSeconds = 300,
                MinCrawlDelayPerDomainMilliSeconds = 1000,
                IsRespectRobotsDotTextEnabled = true
            };

            _maxWidth = maxWidth;
            _crawlMode = crawlMode;
            _dataPath = DataPaths.CrawlingPath;
        }

        public async Task CrawlUrlsAsync(IList<string> urls, string sourceName)
        {
            _currentSource = sourceName;

            int processed = 0;

            Log.Information("[{src}] Using {mode} crawling strategy", sourceName, _crawlMode);

            foreach (var url in urls.Take(_maxWidth))
            {
                try
                {
                    // Create crawler with appropriate scheduler based on mode
                    IScheduler scheduler = _crawlMode == CrawlMode.BFS
                        ? new BfsScheduler()
                        : new DfsScheduler();

                    using var crawler = new PoliteWebCrawler(_config, null, null, scheduler, null, null, null, null, null);

                    crawler.PageCrawlCompleted += PageCrawlCompleted;
                    crawler.PageCrawlDisallowed += (s, e) =>
                    {
                        Log.Warning("[{src}] Disallowed: {url} (reason: {reason})", sourceName, e.PageToCrawl.Uri, e.DisallowedReason);
                    };

                    Log.Information("[{src}] Crawling root: {url} (mode: {mode})", sourceName, url, _crawlMode);
                    var result = await crawler.CrawlAsync(new Uri(url));

                    Log.Information("[{src}] Finished: {url}", sourceName, url);
                    processed++;
                }
                catch (Exception ex)
                {
                    Log.Warning("[{src}] Error crawling {url}: {msg}", sourceName, url, ex.Message);
                }

                await Task.Delay(500);
            }

            Log.Information("[{src}] Crawling complete ({mode}). Total seeds processed: {count}", sourceName, _crawlMode, processed);

            if (_edges.Any())
            {
                SavePartialResults(sourceName);
            }

            ExportGraphvizFromCsv(sourceName);
            GeneratePngGraphs(sourceName);
        }

        private void PageCrawlCompleted(object? sender, PageCrawlCompletedArgs e)
        {
            var page = e.CrawledPage;
            var doc = page.AngleSharpHtmlDocument;

            var metaDescription = doc?.Head?
                .QuerySelector("meta[name=description]")?
                .GetAttribute("content") ?? "";

            doc?.QuerySelectorAll("nav, header, footer, script, style, noscript")
                .ToList()
                .ForEach(n => n.Remove());

            var rawText = doc?.Body?.TextContent ?? "";
            var cleaned = Regex.Replace(rawText, @"\s+", " ").Trim();
            var wordCount = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            _edges.Add(new CrawledEdge(
                Source: _currentSource,
                ParentUrl: page.ParentUri?.AbsoluteUri ?? "(root)",
                Url: page.Uri.AbsoluteUri,
                Depth: page.CrawlDepth,
                Title: doc?.Title ?? "(no title)",
                StatusCode: (int)(page.HttpResponseMessage?.StatusCode ?? 0),
                ContentType: page.HttpResponseMessage?.Content?.Headers?.ContentType?.MediaType ?? "unknown",
                Content: cleaned,
                Description: metaDescription,
                LoadTimeSeconds: page.Elapsed,
                WordCount: wordCount
            ));

            if (_edges.Count % BatchSize == 0)
            {
                SavePartialResults(_currentSource);
                _edges.Clear();
            }

            // Enhanced logging to show BFS/DFS behavior
            var parentInfo = page.ParentUri != null ? $"from {page.ParentUri.Host}" : "ROOT";
            Log.Information("[{mode}] Crawled depth={depth} | {url} | {parent}",
                _crawlMode, page.CrawlDepth, page.Uri.AbsoluteUri, parentInfo);
        }

        private void SavePartialResults(string sourceName)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_{_crawlMode}_graph.csv");
            bool fileExists = File.Exists(csvPath);

            using (var writer = new StreamWriter(csvPath, append: true, new UTF8Encoding(true)))
            {
                if (!fileExists)
                {
                    writer.WriteLine("Source,ParentUrl,Url,Depth,Title,StatusCode,ContentType,Content,Description,LoadTimeSeconds,WordCount");
                }

                foreach (var edge in _edges.Where(e => e.Source == sourceName))
                {
                    writer.WriteLine(string.Join(",",
                        EscapeCsv(edge.Source),
                        EscapeCsv(edge.ParentUrl ?? "(root)"),
                        EscapeCsv(edge.Url),
                        edge.Depth,
                        EscapeCsv(edge.Title),
                        edge.StatusCode,
                        EscapeCsv(edge.ContentType),
                        EscapeCsv(edge.Content),
                        EscapeCsv(edge.Description),
                        edge.LoadTimeSeconds.ToString("F2", CultureInfo.InvariantCulture),
                        edge.WordCount));
                }
            }

            Log.Information("[{src}] Partial results appended: {count} entries", sourceName, _edges.Count);
        }


        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return "\"" + value + "\"";
        }

        public void ExportGraphvizFromCsv(string sourceName)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_{_crawlMode}_graph.csv");
            var dotPath = Path.Combine(_dataPath, $"{sourceName}_{_crawlMode}_graph.dot");

            if (!File.Exists(csvPath))
            {
                Log.Warning("[{src}] CSV not found: {path}", sourceName, csvPath);
                return;
            }

            var lines = File.ReadAllLines(csvPath, Encoding.UTF8)
                            .Skip(1)
                            .Where(line => !string.IsNullOrWhiteSpace(line));

            using var w = new StreamWriter(dotPath, false, new UTF8Encoding(false));

            w.WriteLine("digraph CrawlGraph {");
            w.WriteLine($"  label=\"{sourceName} - {_crawlMode} Mode\";");
            w.WriteLine("  labelloc=t;");
            w.WriteLine("  fontsize=16;");
            w.WriteLine("  rankdir=LR;");
            w.WriteLine("  overlap=false;");
            w.WriteLine("  splines=true;");
            w.WriteLine("  node [shape=box, style=rounded, fontsize=10, color=gray50];");
            w.WriteLine("  edge [color=gray70, arrowsize=0.6];");

            foreach (var line in lines)
            {
                var fields = SplitCsvLine(line);
                if (fields.Length < 3) continue;

                var parent = Shorten(fields[1], 70);
                var child = Shorten(fields[2], 70);
                var source = fields[0];

                string color = source.Equals("Google", StringComparison.OrdinalIgnoreCase)
                    ? "deepskyblue3"
                    : "darkorange3";

                w.WriteLine($"  \"{EscapeDot(parent)}\" -> \"{EscapeDot(child)}\" [color={color}];");
            }

            w.WriteLine("}");
            Log.Information("[{src}] Graph exported from CSV: {path}", sourceName, dotPath);
        }

        private static string[] SplitCsvLine(string line)
        {
            var matches = Regex.Matches(line, "(?<=^|,)(\"(?:[^\"]|\"\")*\"|[^,]*)");
            return matches.Select(m => m.Value.Trim().Trim('"').Replace("\"\"", "\"")).ToArray();
        }

        private static string EscapeDot(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "(null)";
            return text.Replace("\"", "'").Replace("\\", "\\\\");
        }


        private static string Shorten(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Length <= maxLen ? text : text.Substring(0, maxLen - 3) + "...";
        }

        private void GeneratePngGraphs(string sourceName)
        {
            var dotPath = Path.Combine(_dataPath, $"{sourceName}_{_crawlMode}_graph.dot");
            var pngPath = Path.Combine(_dataPath, $"{sourceName}_{_crawlMode}_graph.png");
            GeneratePngFromDot(dotPath, pngPath, sourceName);
        }

        private void GeneratePngFromDot(string dotPath, string pngPath, string logContext)
        {
            if (!File.Exists(dotPath))
            {
                Log.Warning("[{src}] DOT file not found: {path}", logContext, dotPath);
                return;
            }

            string[] engines = { "sfdp", "dot" };

            foreach (var engine in engines)
            {
                try
                {
                    var processInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = engine,
                        Arguments = $"-Gdpi=200 -Tpng \"{dotPath}\" -o \"{pngPath}\"",
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(processInfo);
                    if (process == null)
                    {
                        Log.Error("[{src}] Failed to start Graphviz process ({engine})", logContext, engine);
                        continue;
                    }

                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Log.Information("[{src}] PNG graph generated using {engine}: {path}", logContext, engine, pngPath);
                        return;
                    }
                    else
                    {
                        var error = process.StandardError.ReadToEnd();
                        Log.Warning("[{src}] Graphviz ({engine}) failed (code {code}): {error}", logContext, engine, process.ExitCode, error);
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Log.Warning("[{src}] Graphviz not found or {engine} not in PATH", logContext, engine);
                }
                catch (Exception ex)
                {
                    Log.Error("[{src}] Graphviz ({engine}) exception: {msg}", logContext, engine, ex.Message);
                }
            }

            Log.Warning("[{src}] No PNG graph generated. Try converting manually: dot -Tpng {dotPath} -o {pngPath}", logContext, dotPath, pngPath);
        }

        public void ExportComparisonArtifacts(string sourceA, string sourceB)
        {
            var compareName = $"Compare_{sourceA}_{sourceB}";
            
            // Find actual CSV files (with _BFS or _DFS suffix)
            var csvA = FindCsvFile(sourceA);
            var csvB = FindCsvFile(sourceB);
            
            if (csvA == null || csvB == null)
            {
                Log.Warning("[COMPARE] Missing CSV files for comparison. A={a}, B={b}", 
                    csvA ?? "not found", csvB ?? "not found");
                return;
            }
            
            ExportComparisonGraphvizFromCsv(csvA, csvB, sourceA, sourceB, compareName);
            
            var dotPath = Path.Combine(_dataPath, $"{compareName}_graph.dot");
            var pngPath = Path.Combine(_dataPath, $"{compareName}_graph.png");
            GeneratePngFromDot(dotPath, pngPath, compareName);
            
            WriteComparisonMetricsFromCsv(csvA, csvB, sourceA, sourceB, compareName);
        }

        private string? FindCsvFile(string sourceName)
        {
            // Try with crawl mode suffix first
            var withMode = Path.Combine(_dataPath, $"{sourceName}_{_crawlMode}_graph.csv");
            if (File.Exists(withMode))
                return withMode;
            
            // Try without suffix (legacy)
            var withoutMode = Path.Combine(_dataPath, $"{sourceName}_graph.csv");
            if (File.Exists(withoutMode))
                return withoutMode;
            
            // Try to find any matching file
            var pattern = $"{sourceName}_*_graph.csv";
            var matches = Directory.GetFiles(_dataPath, pattern);
            return matches.FirstOrDefault();
        }

        private void ExportComparisonGraphvizFromCsv(string csvA, string csvB, string sourceA, string sourceB, string outputName)
        {
            var dotPath = Path.Combine(_dataPath, $"{outputName}_graph.dot");

            if (!File.Exists(csvA) || !File.Exists(csvB))
            {
                Log.Warning("[COMPARE] Missing CSV(s): {a} exists={ea}, {b} exists={eb}",
                    csvA, File.Exists(csvA), csvB, File.Exists(csvB));
                return;
            }

            // Edges per source
            var edgesA = new HashSet<(string From, string To)>();
            var edgesB = new HashSet<(string From, string To)>();

            // Nodes per source
            var nodesA = new HashSet<string>();
            var nodesB = new HashSet<string>();

            ReadCsvEdgesIntoSets(csvA, edgesA, nodesA);
            ReadCsvEdgesIntoSets(csvB, edgesB, nodesB);

            var allNodes = new HashSet<string>(nodesA);
            allNodes.UnionWith(nodesB);

            // Map URL -> stable DOT id
            var nodeId = new Dictionary<string, string>(capacity: allNodes.Count);
            int idx = 0;
            foreach (var url in allNodes)
            {
                nodeId[url] = "n" + (++idx).ToString();
            }

            // Styling
            string colorA = "deepskyblue3";
            string colorB = "darkorange3";
            string colorBoth = "mediumpurple3";
            string colorRoot = "gray50";

            using var w = new StreamWriter(dotPath, false, new UTF8Encoding(false));

            w.WriteLine("digraph CompareGraph {");
            w.WriteLine("  rankdir=LR;");
            w.WriteLine("  overlap=false;");
            w.WriteLine("  splines=true;");
            w.WriteLine("  node [shape=box, style=\"rounded\", fontsize=10, color=gray50];");
            w.WriteLine("  edge [color=gray70, arrowsize=0.6];");

            // Nodes
            foreach (var url in allNodes)
            {
                bool inA = nodesA.Contains(url);
                bool inB = nodesB.Contains(url);

                string color = (url == "(root)") ? colorRoot
                             : (inA && inB) ? colorBoth
                             : inA ? colorA
                             : colorB;

                int penwidth = (inA && inB) ? 2 : 1;
                string label = EscapeDot(Shorten(PrettyUrlLabel(url), 60));

                w.WriteLine($"  {nodeId[url]} [label=\"{label}\", color={color}, penwidth={penwidth}];");
            }

            // Edges (union)
            var unionEdges = new HashSet<(string From, string To)>(edgesA);
            unionEdges.UnionWith(edgesB);

            foreach (var e in unionEdges)
            {
                bool inA = edgesA.Contains(e);
                bool inB = edgesB.Contains(e);

                string color = (inA && inB) ? colorBoth : inA ? colorA : colorB;
                int penwidth = (inA && inB) ? 2 : 1;
                string style = (inA && inB) ? "solid" : "dashed";

                if (!nodeId.ContainsKey(e.From) || !nodeId.ContainsKey(e.To))
                    continue;

                w.WriteLine($"  {nodeId[e.From]} -> {nodeId[e.To]} [color={color}, penwidth={penwidth}, style={style}];");
            }

            // Legend
            w.WriteLine("  subgraph cluster_legend {");
            w.WriteLine("    label=\"Legend\";");
            w.WriteLine("    fontsize=10;");
            w.WriteLine("    color=gray80;");
            w.WriteLine($"    la [label=\"{sourceA} only\", color={colorA}];");
            w.WriteLine($"    lb [label=\"{sourceB} only\", color={colorB}];");
            w.WriteLine($"    lab [label=\"both\", color={colorBoth}, penwidth=2];");
            w.WriteLine("  }");

            w.WriteLine("}");
            Log.Information("[COMPARE] DOT exported: {path}", dotPath);
        }

        private void ReadCsvEdgesIntoSets(string csvPath, HashSet<(string From, string To)> edges, HashSet<string> nodes)
        {
            using var reader = new StreamReader(csvPath, Encoding.UTF8);

            // header
            _ = reader.ReadLine();

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var fields = SplitCsvLine(line);
                if (fields.Length < 3) continue;

                var from = fields[1];
                var to = fields[2];

                edges.Add((from, to));
                nodes.Add(from);
                nodes.Add(to);
            }
        }

        private void WriteComparisonMetricsFromCsv(string csvA, string csvB, string sourceA, string sourceB, string outputName)
        {
            var metricsPath = Path.Combine(_dataPath, $"{outputName}_metrics.txt");

            if (!File.Exists(csvA) || !File.Exists(csvB))
            {
                Log.Warning("[COMPARE] Cannot write metrics - missing CSV files");
                return;
            }

            var edgesA = new HashSet<(string From, string To)>();
            var edgesB = new HashSet<(string From, string To)>();
            var nodesA = new HashSet<string>();
            var nodesB = new HashSet<string>();

            ReadCsvEdgesIntoSets(csvA, edgesA, nodesA);
            ReadCsvEdgesIntoSets(csvB, edgesB, nodesB);

            var commonNodes = new HashSet<string>(nodesA);
            commonNodes.IntersectWith(nodesB);

            var unionNodes = new HashSet<string>(nodesA);
            unionNodes.UnionWith(nodesB);

            var commonEdges = new HashSet<(string From, string To)>(edgesA);
            commonEdges.IntersectWith(edgesB);

            var unionEdges = new HashSet<(string From, string To)>(edgesA);
            unionEdges.UnionWith(edgesB);

            var domainsA = ExtractDomains(nodesA);
            var domainsB = ExtractDomains(nodesB);

            var commonDomains = new HashSet<string>(domainsA);
            commonDomains.IntersectWith(domainsB);

            var unionDomains = new HashSet<string>(domainsA);
            unionDomains.UnionWith(domainsB);

            double jNodes = unionNodes.Count == 0 ? 0 : (double)commonNodes.Count / unionNodes.Count;
            double jEdges = unionEdges.Count == 0 ? 0 : (double)commonEdges.Count / unionEdges.Count;
            double jDomains = unionDomains.Count == 0 ? 0 : (double)commonDomains.Count / unionDomains.Count;

            var sb = new StringBuilder();
            sb.AppendLine($"Comparison: {sourceA} vs {sourceB}");
            sb.AppendLine($"CSV A: {Path.GetFileName(csvA)}");
            sb.AppendLine($"CSV B: {Path.GetFileName(csvB)}");
            sb.AppendLine();
            sb.AppendLine("=== Nodes (URLs) ===");
            sb.AppendLine($"{sourceA}: {nodesA.Count}");
            sb.AppendLine($"{sourceB}: {nodesB.Count}");
            sb.AppendLine($"Common: {commonNodes.Count}");
            sb.AppendLine($"Union: {unionNodes.Count}");
            sb.AppendLine($"Jaccard(nodes): {jNodes:F3}");
            sb.AppendLine();
            sb.AppendLine("=== Edges (links) ===");
            sb.AppendLine($"{sourceA}: {edgesA.Count}");
            sb.AppendLine($"{sourceB}: {edgesB.Count}");
            sb.AppendLine($"Common: {commonEdges.Count}");
            sb.AppendLine($"Union: {unionEdges.Count}");
            sb.AppendLine($"Jaccard(edges): {jEdges:F3}");
            sb.AppendLine();
            sb.AppendLine("=== Domains ===");
            sb.AppendLine($"{sourceA}: {domainsA.Count}");
            sb.AppendLine($"{sourceB}: {domainsB.Count}");
            sb.AppendLine($"Common: {commonDomains.Count}");
            sb.AppendLine($"Union: {unionDomains.Count}");
            sb.AppendLine($"Jaccard(domains): {jDomains:F3}");
            sb.AppendLine();

            File.WriteAllText(metricsPath, sb.ToString(), Encoding.UTF8);
            Log.Information("[COMPARE] Metrics written: {path}", metricsPath);
        }

        private HashSet<string> ExtractDomains(HashSet<string> urls)
        {
            var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in urls)
            {
                if (Uri.TryCreate(u, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
                    domains.Add(uri.Host);
            }
            return domains;
        }

        private static string PrettyUrlLabel(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "(null)";
            if (url == "(root)") return "(root)";

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var path = uri.AbsolutePath;
                if (path.Length > 1 && path.EndsWith("/")) path = path.TrimEnd('/');
                var label = uri.Host + path;
                return string.IsNullOrWhiteSpace(label) ? url : label;
            }

            return url;
        }
    }
}
