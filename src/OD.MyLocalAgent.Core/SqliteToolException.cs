using Microsoft.Data.Sqlite;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents a content-free SQLite tool failure carrying only a category and numeric SQLite codes.
/// The message never contains SQL, paths, parameters, database identifiers, or provider text.
/// </summary>
public sealed class SqliteToolException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteToolException"/> class.
    /// </summary>
    /// <param name="category">The content-free failure category.</param>
    /// <param name="primaryErrorCode">The optional SQLite primary result code.</param>
    /// <param name="extendedErrorCode">The optional SQLite extended result code.</param>
    public SqliteToolException(
        SqliteToolErrorCategory category,
        int? primaryErrorCode = null,
        int? extendedErrorCode = null)
        : base(MessageFor(category))
    {
        Category = category;
        PrimaryErrorCode = primaryErrorCode;
        ExtendedErrorCode = extendedErrorCode;
    }

    /// <summary>Gets the content-free failure category.</summary>
    public SqliteToolErrorCategory Category { get; }

    /// <summary>Gets the SQLite primary result code when the failure came from the provider.</summary>
    public int? PrimaryErrorCode { get; }

    /// <summary>Gets the SQLite extended result code when the failure came from the provider.</summary>
    public int? ExtendedErrorCode { get; }

    /// <summary>
    /// Classifies a provider exception into a content-free category using only its numeric codes.
    /// The raw exception must be discarded by callers after classification.
    /// </summary>
    /// <param name="exception">The provider exception to classify.</param>
    /// <returns>A content-free replacement exception.</returns>
    public static SqliteToolException FromSqliteError(SqliteException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var category = exception.SqliteErrorCode switch
        {
            5 or 6 => SqliteToolErrorCategory.BusyLocked,
            8 => SqliteToolErrorCategory.ReadOnly,
            9 => SqliteToolErrorCategory.Interrupted,
            10 or 13 or 14 => SqliteToolErrorCategory.InputOutput,
            11 or 26 => SqliteToolErrorCategory.Corrupt,
            19 => SqliteToolErrorCategory.Constraint,
            1 => SqliteToolErrorCategory.InvalidStatement,
            _ => SqliteToolErrorCategory.Unexpected,
        };
        return new SqliteToolException(category, exception.SqliteErrorCode, exception.SqliteExtendedErrorCode);
    }

    private static string MessageFor(SqliteToolErrorCategory category) => category switch
    {
        SqliteToolErrorCategory.InvalidStatement => "The SQLite statement is invalid for this alias.",
        SqliteToolErrorCategory.ForbiddenFeature => "The SQLite statement uses a forbidden feature.",
        SqliteToolErrorCategory.ParameterMismatch => "The SQLite parameters do not match the statement's named parameters.",
        SqliteToolErrorCategory.ResultLimit => "A SQLite result limit was violated.",
        SqliteToolErrorCategory.BusyLocked => "The SQLite database is busy or locked.",
        SqliteToolErrorCategory.Constraint => "A SQLite constraint rejected the modification.",
        SqliteToolErrorCategory.ReadOnly => "The SQLite database is read-only for this operation.",
        SqliteToolErrorCategory.Corrupt => "The SQLite database file is corrupt or not a database.",
        SqliteToolErrorCategory.InputOutput => "A SQLite file input/output failure occurred.",
        SqliteToolErrorCategory.Interrupted => "The SQLite operation was interrupted.",
        _ => "An unexpected SQLite provider failure occurred.",
    };
}
