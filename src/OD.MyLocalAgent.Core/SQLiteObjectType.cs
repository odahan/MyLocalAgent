namespace OD.MyLocalAgent.Core;

/// <summary>Identifies the SQLite schema object categories returned by the tool.</summary>
public enum SQLiteObjectType
{
    /// <summary>Returns all supported object types.</summary>
    All,
    /// <summary>Returns tables.</summary>
    Table,
    /// <summary>Returns views.</summary>
    View,
    /// <summary>Returns indexes.</summary>
    Index,
    /// <summary>Returns triggers.</summary>
    Trigger,
}
