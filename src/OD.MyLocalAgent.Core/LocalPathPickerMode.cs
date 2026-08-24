namespace OD.MyLocalAgent.Core;

/// <summary>Defines the generic desktop picker used to author one local-path argument.</summary>
public enum LocalPathPickerMode
{
    /// <summary>Infers a safe picker from target kind and access metadata.</summary>
    Automatic,
    /// <summary>Selects an existing file.</summary>
    OpenFile,
    /// <summary>Selects a destination file that may not exist yet.</summary>
    SaveFile,
    /// <summary>Selects an existing directory.</summary>
    SelectFolder,
    /// <summary>Offers separate existing-file and directory choices.</summary>
    OpenFileOrFolder,
}
