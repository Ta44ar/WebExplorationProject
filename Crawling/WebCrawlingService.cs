using Abot2.Crawler;
using Abot2.Poco;
using Serilog;
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

        public WebCrawlingService(int maxDepth = 3, int maxWidth = 30)
        {
            _config = new CrawlConfiguration
            {
                MaxPagesToCrawl = maxWidth,
                MaxCrawlDepth = maxDepth,
                IsUriRecrawlingEnabled = false,
                DownloadableContentTypes = "text/html",
                MaxConcurrentThreads = 5,
                CrawlTimeoutSeconds = 300,
                MinCrawlDelayPerDomainMilliSeconds = 1000
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
            SaveResults(sourceName);
            ExportGraphJson(sourceName);
            ExportGraphviz(sourceName);
            GeneratePngGraphs(sourceName);
        }

        private void PageCrawlCompleted(object sender, PageCrawlCompletedArgs e)
        {
            var page = e.CrawledPage;
            var html = page.Content.Text ?? "";
            var wordCount = html.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

            _edges.Add(new CrawledEdge(
                Source: _currentSource,
                ParentUrl: page.ParentUri?.AbsoluteUri ?? "(root)",
                Url: page.Uri.AbsoluteUri,
                Depth: page.CrawlDepth,
                Title: page.AngleSharpHtmlDocument?.Title ?? "(no title)",
                StatusCode: (int)(page.HttpResponseMessage?.StatusCode ?? 0),
                ContentType: page.HttpResponseMessage?.Content?.Headers?.ContentType?.MediaType ?? "unknown",
                LoadTimeSeconds: page.Elapsed,
                WordCount: wordCount
            ));

            Log.Information("Crawled: {url} | depth {depth} | from {parent}", page.Uri, page.CrawlDepth, page.ParentUri);
        }

        public void SaveResults(string sourceName)
        {
            var csvPath = Path.Combine(_dataPath, $"{sourceName}_graph.csv");
            var xlsxPath = Path.Combine(_dataPath, $"{sourceName}_graph.xlsx");

            using (var writer = new StreamWriter(csvPath))
            {
                writer.WriteLine("Source,ParentUrl,Url,Depth,Title,StatusCode,ContentType,LoadTimeSeconds,WordCount");
                foreach (var edge in _edges)
                {
                    writer.WriteLine($"\"{edge.Source}\",\"{edge.ParentUrl}\",\"{edge.Url}\",{edge.Depth},\"{edge.Title.Replace("\"", "'")}\",{edge.StatusCode},\"{edge.ContentType}\",{edge.LoadTimeSeconds:F2},{edge.WordCount}");
                }
            }

            using (var workbook = new ClosedXML.Excel.XLWorkbook())
            {
                var ws = workbook.AddWorksheet("GraphData");
                ws.Cell(1, 1).InsertTable(_edges);
                workbook.SaveAs(xlsxPath);
            }

            Log.Information("[{src}] Results saved to {csv} and {xlsx}", sourceName, csvPath, xlsxPath);
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

        public void ExportGraphviz(string sourceName)
        {
            var path = Path.Combine(_dataPath, $"{sourceName}_graph.dot");
            using var w = new StreamWriter(path);

            w.WriteLine("digraph CrawlGraph {");
            w.WriteLine("  rankdir=LR;"); // lewo→prawo zamiast pionowego layoutu
            w.WriteLine("  overlap=false;");
            w.WriteLine("  splines=true;");
            w.WriteLine("  node [shape=box, style=rounded, fontsize=10, color=gray50];");
            w.WriteLine("  edge [color=gray70, arrowsize=0.6];");

            foreach (var e in _edges)
            {
                string parent = Shorten(e.ParentUrl, 70);
                string child = Shorten(e.Url, 70);
                string color = e.Source.Equals("Google", StringComparison.OrdinalIgnoreCase)
                    ? "deepskyblue3"
                    : "darkorange3";

                w.WriteLine($"  \"{parent}\" -> \"{child}\" [color={color}];");
            }

            w.WriteLine("}");
            Log.Information("[{src}] Graph exported: {path}", sourceName, path);
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
