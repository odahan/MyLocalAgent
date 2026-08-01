namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes the run-log session of the current runner invocation to the output tee.
/// </summary>
public sealed class RunLogSessionHolder
{
    private volatile RunLogSession? _session;

    /// <summary>
    /// Gets or sets the active session, or <see langword="null"/> outside a task run.
    /// </summary>
    public RunLogSession? Session
    {
        get => _session;
        set => _session = value;
    }
}
