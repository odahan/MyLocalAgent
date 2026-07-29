using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace OD.MyLocalAgent.UI.Services;

internal sealed class DesktopInteractionService : IDesktopInteractionService
{
    public bool Confirm(string title, string message) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

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

    public void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Clipboard.SetText(text);
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
}
