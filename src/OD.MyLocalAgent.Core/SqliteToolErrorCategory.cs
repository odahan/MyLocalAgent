namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies a content-free SQLite tool failure category safe for logging and telemetry.
/// </summary>
public enum SqliteToolErrorCategory
{
    /// <summary>The supplied SQL is empty, oversized, unparsable, or of the wrong statement class.</summary>
    InvalidStatement,

    /// <summary>The statement uses a SQLite feature forbidden by the Stage 19 confinement contract.</summary>
    ForbiddenFeature,

    /// <summary>The supplied parameters do not exactly match the statement's named parameters.</summary>
    ParameterMismatch,

    /// <summary>A deterministic result bound was violated by the request.</summary>
    ResultLimit,

    /// <summary>The database is busy or locked and the bounded retry window elapsed.</summary>
    BusyLocked,

    /// <summary>A database constraint rejected the modification.</summary>
    Constraint,

    /// <summary>The database or its storage is read-only for the attempted operation.</summary>
    ReadOnly,

    /// <summary>The database file is corrupt or is not a SQLite database.</summary>
    Corrupt,

    /// <summary>The underlying file could not be opened, read, or written.</summary>
    InputOutput,

    /// <summary>The native operation was interrupted.</summary>
    Interrupted,

    /// <summary>An unexpected provider failure occurred.</summary>
    Unexpected,
}
