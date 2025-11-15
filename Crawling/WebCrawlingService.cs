using Abot2.Crawler;
using Abot2.Poco;
using Serilog;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
        private const int BatchSize = 50;

        public WebCrawlingService(int maxDepth = 3, int maxWidth = 30)
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

            var projectDir = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName 
                ?? AppContext.BaseDirectory;
            _dataPath = Path.Combine(projectDir, "data");
            Directory.CreateDirectory(_dataPath);
        }

        public async Task CrawlUrlsAsync(IList<string> urls, string sourceName)
        {
            _currentSource = sourceName;

            int processed = 0;

            foreach (var url in urls.Take(_maxWidth))
            {
                try
                {
                    using var crawler = new PoliteWebCrawler(_config);

                    crawler.PageCrawlCompleted += PageCrawlCompleted;
                    crawler.PageCrawlDisallowed += (s, e) =>
                    {
                        Log.Warning("[{src}] Disallowed: {url} (reason: {reason})", sourceName, e.PageToCrawl.Uri, e.DisallowedReason);
                    };

                    Log.Information("[{src}] Crawling root: {url}", sourceName, url);
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

            Log.Information("[{src}] Crawling complete. Total seeds processed: {count}", sourceName, processed);

            if (_edges.Any())
            {
                SavePartialResults(sourceName);
            }
                
            ExportGraphJson(sourceName);
            ExportGraphvizFromCsv(sourceName);
            GeneratePngGraphs(sourceName);
        }

        private void PageCrawlCompleted(object sender, PageCrawlCompletedArgs e)
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

            Log.Information("Crawled: {url} | depth {depth} | from {parent}", page.Uri, page.CrawlDepth, page.ParentUri);
        }

        private void SavePartialResults(string sourceName)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_graph.csv");
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
                        EscapeCsv(edge.ParentUrl),
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

        public void ExportGraphJson(string sourceName)
        {
            var jsonPath = Path.Combine(_dataPath, $"{sourceName}_graph.json");
            var nodes = _edges.Select(e => new { id = e.Url, label = Shorten(e.Title ?? e.Url, 60) }).Distinct();
            var edges = _edges.Select(e => new { from = e.ParentUrl, to = e.Url });

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            var json = System.Text.Json.JsonSerializer.Serialize(new { nodes, edges }, options);
            File.WriteAllText(jsonPath, json);

            Log.Information("[{src}] JSON graph exported: {path}", sourceName, jsonPath);
        }

        public void ExportGraphvizFromCsv(string sourceName)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_graph.csv");
            var dotPath = Path.Combine(_dataPath, $"{sourceName}_graph.dot");

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
            var dotPath = Path.Combine(_dataPath, $"{sourceName}_graph.dot");
            var pngPath = Path.Combine(_dataPath, $"{sourceName}_graph.png");

            if (!File.Exists(dotPath))
            {
                Log.Warning("[{src}] DOT file not found: {path}", sourceName, dotPath);
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
                        Log.Error("[{src}] Failed to start Graphviz process ({engine})", sourceName, engine);
                        continue;
                    }

                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Log.Information("[{src}] PNG graph generated using {engine}: {path}", sourceName, engine, pngPath);
                        return;
                    }
                    else
                    {
                        var error = process.StandardError.ReadToEnd();
                        Log.Warning("[{src}] Graphviz ({engine}) failed (code {code}): {error}", sourceName, engine, process.ExitCode, error);
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    Log.Warning("[{src}] Graphviz not found or {engine} not in PATH", sourceName, engine);
                }
                catch (Exception ex)
                {
                    Log.Error("[{src}] Graphviz ({engine}) exception: {msg}", sourceName, engine, ex.Message);
                }
            }

            Log.Warning("[{src}] No PNG graph generated. Try converting manually: dot -Tpng {dotPath} -o {pngPath}", sourceName, dotPath, pngPath);
        }
    }
}
