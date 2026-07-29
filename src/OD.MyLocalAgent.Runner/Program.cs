namespace OD.MyLocalAgent.Runner;

internal static class Program
{
    private static Task<int> Main(string[] args) =>
        RunnerApplication.RunAsync(
            args,
            Console.Out,
            Console.Error,
            CancellationToken.None);
}
