namespace OD.MyLocalAgent.Core;

/// <summary>
/// Resolves the stable runner executable path shared by scheduling and Run now.
/// </summary>
public static class RunnerPathResolver
{
    /// <summary>
    /// Gets the published runner executable file name.
    /// </summary>
    public const string RunnerFileName = "OD.MyLocalAgent.Runner.exe";

    /// <summary>
    /// Resolves the configured path, or the sibling runner beside the current executable.
    /// </summary>
    /// <param name="options">The scheduler options.</param>
    /// <param name="requireExists">Whether a missing executable should fail resolution.</param>
    /// <returns>The absolute runner path.</returns>
    public static string Resolve(
        SchedulerOptions options,
        bool requireExists = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = string.IsNullOrWhiteSpace(options.RunnerPath)
            ? Path.Combine(AppContext.BaseDirectory, RunnerFileName)
            : Path.IsPathRooted(options.RunnerPath)
                ? Path.GetFullPath(options.RunnerPath)
                : Path.GetFullPath(options.RunnerPath, AppContext.BaseDirectory);

        if (requireExists && !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The runner executable was not found at '{path}'. Publish the runner beside the app or set Scheduler:RunnerPath.",
                path);
        }

        return path;
    }
}
