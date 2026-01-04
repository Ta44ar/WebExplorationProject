namespace WebExplorationProject.Helpers
{
    /// <summary>
    /// Centralized path management for all output folders.
    /// Structure:
    ///   data/
    ///   ??? 1_Crawling/      - Task 1: Graph CSV, DOT, PNG files
    ///   ??? 2_Ranking/       - Task 2: Ranking CSV and reports
    ///   ??? 3_Clustering/    - Task 3: Cluster assignments
    ///   ??? 4_Classification/ - Task 4: Classification results
    /// </summary>
    public static class DataPaths
    {
        private static string? _baseDataPath;

        /// <summary>
        /// Gets the base data directory (project/data).
        /// </summary>
        public static string BaseDataPath
        {
            get
            {
                if (_baseDataPath == null)
                {
                    var projectDir = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Parent?.Parent?.FullName
                        ?? AppContext.BaseDirectory;
                    _baseDataPath = Path.Combine(projectDir, "data");
                }
                return _baseDataPath;
            }
        }

        /// <summary>
        /// Task 1: Crawling output folder.
        /// </summary>
        public static string CrawlingPath
        {
            get
            {
                var path = Path.Combine(BaseDataPath, "1_Crawling");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// Task 2: Ranking output folder.
        /// </summary>
        public static string RankingPath
        {
            get
            {
                var path = Path.Combine(BaseDataPath, "2_Ranking");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// Task 3: Clustering output folder.
        /// </summary>
        public static string ClusteringPath
        {
            get
            {
                var path = Path.Combine(BaseDataPath, "3_Clustering");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// Task 4: Classification output folder.
        /// </summary>
        public static string ClassificationPath
        {
            get
            {
                var path = Path.Combine(BaseDataPath, "4_Classification");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        /// <summary>
        /// Cache folder for search results.
        /// </summary>
        public static string CachePath
        {
            get
            {
                var path = Path.Combine(BaseDataPath, "cache");
                Directory.CreateDirectory(path);
                return path;
            }
        }
    }
}
