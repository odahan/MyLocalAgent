namespace OD.MyLocalAgent.Runner;

/// <summary>
/// Provides the console application's process entry point.
/// </summary>
internal static class Program
{
    private static Task<int> Main(string[] args)
    {
        var (output, error) = ConsoleStreams.Acquire();
        return RunnerApplication.RunAsync(
            args,
            output,
            error,
            CancellationToken.None);
    }
}
