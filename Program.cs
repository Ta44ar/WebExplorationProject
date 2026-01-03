using Microsoft.Extensions.Configuration;
using Serilog;
using WebExplorationProject.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();

        Log.Information("=== Web Exploration Project ===");
        Log.Information("Analyzing web content credibility\n");

        var builder = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets<Program>(optional: true);

        var configuration = builder.Build();
        var httpClient = new HttpClient();

        // Available tasks
        var tasks = new Dictionary<int, ITask>
        {
            { 1, new CrawlingTask(configuration, httpClient) },
            { 2, new RankingTask(configuration, httpClient) },
            { 3, new ClusteringTask(configuration) },
            { 4, new ClassificationTask(configuration) }
        };

        // Parse command line arguments or show menu
        int selectedTask = 0;

        if (args.Length > 0 && int.TryParse(args[0], out var taskArg) && tasks.ContainsKey(taskArg))
        {
            selectedTask = taskArg;
        }
        else
        {
            selectedTask = ShowTaskMenu(tasks);
        }

        if (selectedTask == 0)
        {
            Log.Information("Exiting...");
            return;
        }

        if (tasks.TryGetValue(selectedTask, out var task))
        {
            try
            {
                await task.ExecuteAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Task execution failed: {message}", ex.Message);
            }
        }

        Log.Information("\nProgram completed. Press any key to exit...");
        if (!Console.IsInputRedirected)
        {
            Console.ReadKey();
        }
    }

    static int ShowTaskMenu(Dictionary<int, ITask> tasks)
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    SELECT A TASK                           ║");
        Console.WriteLine("╠════════════════════════════════════════════════════════════╣");
        
        foreach (var kvp in tasks)
        {
            Console.WriteLine($"║  [{kvp.Key}] {kvp.Value.Name,-50} ║");
            Console.WriteLine($"║      {kvp.Value.Description,-53} ║");
            Console.WriteLine("║                                                            ║");
        }
        
        Console.WriteLine("║  [0] Exit                                                  ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
        Console.Write("\nEnter your choice: ");

        if (int.TryParse(Console.ReadLine(), out var choice))
        {
            return choice;
        }

        return 0;
    }
}