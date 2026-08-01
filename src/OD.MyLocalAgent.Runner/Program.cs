namespace OD.MyLocalAgent.Runner;

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
