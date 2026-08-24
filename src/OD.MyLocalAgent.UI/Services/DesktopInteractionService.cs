using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;
using System.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Encapsulates desktop-only dialogs, file pickers, and media interactions for view models.
/// </summary>
internal sealed class DesktopInteractionService : IDesktopInteractionService
{
    private SoundPlayer? _soundPlayer;
    private MemoryStream? _audioStream;
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

    public bool ConfirmDestructive(string title, string message, string confirmButtonText)
    {
        var dialog = CreateDialog(title, message, requiresConfirmation: true, primaryButtonText: confirmButtonText, isDestructive: true, defaultToCancel: true);
        return dialog.ShowDialog() == true;
    }

    public void ShowError(string title, string message)
    {
        var dialog = CreateDialog(title, message, requiresConfirmation: false);
        _ = dialog.ShowDialog();
    }

    public void ShowInformation(string title, string message)
    {
        var dialog = CreateDialog(
            title,
            message,
            requiresConfirmation: false,
            isInformational: true);
        _ = dialog.ShowDialog();
    }

    private static ConfirmationDialogWindow CreateDialog(
        string title,
        string message,
        bool requiresConfirmation,
        string? primaryButtonText = null,
        string? cancelButtonText = null,
        string? secondaryButtonText = null,
        bool isInformational = false,
        bool isDestructive = false,
        bool defaultToCancel = false)
    {
        var dialog = new ConfirmationDialogWindow(
            title,
            message,
            requiresConfirmation,
            primaryButtonText,
            cancelButtonText,
            secondaryButtonText,
            isInformational,
            isDestructive,
            defaultToCancel);
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

    public string? SelectOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = false, CheckFileExists = true };
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

    public void ShowImage(byte[] data, string title)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream(data, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        var image = new Image
        {
            Source = bitmap,
            Stretch = System.Windows.Media.Stretch.None,
        };
        var window = new Window
        {
            Title = title,
            Width = Math.Min(1200, bitmap.PixelWidth + 40),
            Height = Math.Min(900, bitmap.PixelHeight + 70),
            Content = new ScrollViewer
            {
                Content = image,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var owner = Application.Current?.MainWindow;
        if (owner?.IsVisible == true)
        {
            window.Owner = owner;
        }

        window.KeyDown += (_, args) =>
        {
            if (args.Key == System.Windows.Input.Key.Escape)
            {
                window.Close();
            }
        };
        window.Show();
    }

    public void PlayWaveAudio(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        StopAudio();
        _audioStream = new MemoryStream(data, writable: false);
        _soundPlayer = new SoundPlayer(_audioStream);
        _soundPlayer.Play();
    }

    public void StopAudio()
    {
        _soundPlayer?.Stop();
        _soundPlayer?.Dispose();
        _audioStream?.Dispose();
        _soundPlayer = null;
        _audioStream = null;
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
