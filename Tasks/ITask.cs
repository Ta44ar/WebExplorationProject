namespace WebExplorationProject.Tasks
{
    /// <summary>
    /// Interface for application tasks.
    /// </summary>
    public interface ITask
    {
        /// <summary>
        /// Name of the task for display purposes.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what the task does.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Executes the task asynchronously.
        /// </summary>
        Task ExecuteAsync();
    }
}
