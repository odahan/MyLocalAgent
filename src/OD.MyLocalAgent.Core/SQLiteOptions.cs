namespace OD.MyLocalAgent.Core;

/// <summary>
/// Configures bounded, local SQLite tool execution.
/// </summary>
public sealed class SQLiteOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "SQLite";

    /// <summary>Gets or sets the default row limit.</summary>
    public int DefaultMaxRows { get; set; } = 100;

    /// <summary>Gets or sets the hard row limit.</summary>
    public int MaxRows { get; set; } = 1_000;

    /// <summary>Gets or sets the hard serialized-result character limit.</summary>
    public int MaxResultCharacters { get; set; } = 64_000;

    /// <summary>Gets or sets the maximum delivered BLOB byte count.</summary>
    public int MaxBlobBytes { get; set; } = 4_096;

    /// <summary>Gets or sets the maximum SQL character count.</summary>
    public int MaxSqlCharacters { get; set; } = 16_000;

    /// <summary>Gets or sets the command timeout in seconds.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>Gets or sets whether taskless SQLite reads use Guardrails by default.</summary>
    public bool GuardrailsEnabledByDefault { get; set; } = true;

    /// <summary>Determines whether the configured values are within program-owned bounds.</summary>
    public static bool IsValid(SQLiteOptions value) =>
        value.DefaultMaxRows is >= 1 && value.DefaultMaxRows <= value.MaxRows &&
        value.MaxRows is >= 1 and <= 10_000 &&
        value.MaxResultCharacters is >= 4_096 and <= 1_000_000 &&
        value.MaxBlobBytes is >= 0 and <= 1_000_000 &&
        value.MaxSqlCharacters is >= 256 and <= 1_000_000 &&
        value.CommandTimeoutSeconds is >= 1 and <= 300;
}
