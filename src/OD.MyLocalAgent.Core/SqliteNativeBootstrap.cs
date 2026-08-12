namespace OD.MyLocalAgent.Core;

/// <summary>Initializes the packaged native SQLite provider exactly once.</summary>
internal static class SqliteNativeBootstrap
{
    private static readonly Lazy<bool> Initialization = new(
        static () =>
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Ensures that the native SQLite provider is ready.</summary>
    public static void Initialize() => _ = Initialization.Value;
}
