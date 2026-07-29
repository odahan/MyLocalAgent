namespace OD.MyLocalAgent.Core;

/// <summary>
/// Adapts process-level exception hooks to the single incident reporter.
/// </summary>
public static class AppExceptionHandlers
{
    private static readonly object SyncRoot = new();
    private static IIncidentReporter? _reporter;
    private static bool _processEventsWired;

    /// <summary>
    /// Initializes the adapter with the application incident reporter.
    /// </summary>
    /// <param name="reporter">The incident reporter.</param>
    public static void Initialize(IIncidentReporter reporter)
    {
        ArgumentNullException.ThrowIfNull(reporter);
        lock (SyncRoot)
        {
            _reporter = reporter;
        }
    }

    /// <summary>
    /// Directly reports an exception and returns its correlation identifier.
    /// </summary>
    /// <param name="exception">The exception to report.</param>
    /// <param name="source">The source page, task, or process hook.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated correlation identifier.</returns>
    public static string Handle(
        Exception exception,
        string source,
        CancellationToken cancellationToken = default) =>
        HandleAsync(exception, source, cancellationToken).GetAwaiter().GetResult();

    /// <summary>
    /// Directly reports an exception asynchronously.
    /// </summary>
    /// <param name="exception">The exception to report.</param>
    /// <param name="source">The source page, task, or process hook.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The generated correlation identifier.</returns>
    public static Task<string> HandleAsync(
        Exception exception,
        string source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        IIncidentReporter reporter;
        lock (SyncRoot)
        {
            reporter = _reporter
                ?? throw new InvalidOperationException(
                    "AppExceptionHandlers.Initialize must be called before reporting an incident.");
        }

        return reporter.ReportAsync(
            exception,
            source,
            IncidentSeverity.Critical,
            cancellationToken);
    }

    /// <summary>
    /// Handles a UI dispatcher exception and reports that it was handled.
    /// </summary>
    /// <param name="exception">The dispatcher exception.</param>
    /// <param name="source">The current page or command source.</param>
    /// <returns><see langword="true"/> after the incident is recorded.</returns>
    public static bool HandleDispatcherException(Exception exception, string source)
    {
        _ = Handle(exception, source);
        return true;
    }

    /// <summary>
    /// Wires the AppDomain and unobserved-task defense-in-depth hooks once.
    /// </summary>
    public static void WireProcessEvents()
    {
        lock (SyncRoot)
        {
            if (_processEventsWired)
            {
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            _processEventsWired = true;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception
            ?? new InvalidOperationException(
                $"An unhandled non-exception object was raised: {args.ExceptionObject}");
        _ = Handle(exception, "AppDomain.CurrentDomain.UnhandledException");
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        _ = Handle(args.Exception, "TaskScheduler.UnobservedTaskException");
        args.SetObserved();
    }
}
