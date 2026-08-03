using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace OD.MyLocalAgent.UI.Services;

internal sealed class DesktopInteractionService : IDesktopInteractionService
{
    public UnsavedChangesChoice PromptUnsavedChanges(string title, string message)
    {
        var dialog = CreateDialog(
            title,
            message,
            requiresConfirmation: true,
            primaryButtonText: "Save and leave",
            cancelButtonText: "Stay on settings",
            secondaryButtonText: "Leave without saving");
        _ = dialog.ShowDialog();
        return dialog.Action switch
        {
            ConfirmationDialogAction.Primary => UnsavedChangesChoice.Save,
            ConfirmationDialogAction.Secondary => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Stay,
        };
    }

    public bool Confirm(string title, string message)
    {
        var dialog = CreateDialog(title, message, requiresConfirmation: true);
        return dialog.ShowDialog() == true;
    }

    public void ShowError(string title, string message)
    {
        var dialog = CreateDialog(title, message, requiresConfirmation: false);
        _ = dialog.ShowDialog();
    }

    private static ConfirmationDialogWindow CreateDialog(
        string title,
        string message,
        bool requiresConfirmation,
        string? primaryButtonText = null,
        string? cancelButtonText = null,
        string? secondaryButtonText = null)
    {
        var dialog = new ConfirmationDialogWindow(
            title,
            message,
            requiresConfirmation,
            primaryButtonText,
            cancelButtonText,
            secondaryButtonText);
        var owner = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(static window => window.IsActive) ??
            Application.Current?.MainWindow;
        if (owner?.IsVisible == true)
        {
            dialog.Owner = owner;
        }

        return dialog;
    }

    public string? SelectSavePath(
        string title,
        string filter,
        string defaultExtension,
        string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            FileName = suggestedFileName,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Clipboard.SetText(text);
    }

    public string CreateDesktopShortcut(
        string shortcutName,
        string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (!string.Equals(
                shortcutName,
                Path.GetFileName(shortcutName),
                StringComparison.Ordinal) ||
            shortcutName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException(
                "The shortcut name must be a valid file name.",
                nameof(shortcutName));
        }

        var targetPath = Path.GetFullPath(executablePath);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException(
                "The shortcut target executable was not found.",
                targetPath);
        }

        var desktopDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory) ||
            !Directory.Exists(desktopDirectory))
        {
            throw new DirectoryNotFoundException(
                "The Windows desktop directory could not be resolved.");
        }

        var shortcutPath = Path.Combine(
            desktopDirectory,
            $"{shortcutName}.lnk");
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID(
                "WScript.Shell",
                throwOnError: true) ??
                throw new PlatformNotSupportedException(
                    "Windows Script Host is unavailable.");
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            if (shortcut is null)
            {
                throw new InvalidOperationException(
                    "Windows did not create the shortcut object.");
            }

            var shortcutType = shortcut.GetType();
            SetComProperty(shortcutType, shortcut, "TargetPath", targetPath);
            SetComProperty(
                shortcutType,
                shortcut,
                "WorkingDirectory",
                Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory);
            SetComProperty(
                shortcutType,
                shortcut,
                "Description",
                "Launch OD.MyLocalAgent");
            SetComProperty(
                shortcutType,
                shortcut,
                "IconLocation",
                $"{targetPath},0");
            shortcutType.InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shortcut,
                args: null);
            return shortcutPath;
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        _ = Process.Start(
            new ProcessStartInfo
            {
                FileName = Path.GetFullPath(path),
                UseShellExecute = true,
            });
    }

    public void OpenUri(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Only absolute HTTP or HTTPS links can be opened.",
                nameof(uri));
        }

        _ = Process.Start(
            new ProcessStartInfo
            {
                FileName = parsed.AbsoluteUri,
                UseShellExecute = true,
            });
    }

    private static void SetComProperty(
        Type type,
        object target,
        string propertyName,
        object value)
    {
        _ = type.InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            binder: null,
            target,
            args: [value]);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }
}
