using System.Windows;
using System.Windows.Input;

namespace OD.MyLocalAgent.UI.Commands;

/// <summary>
/// Identifies a system-window operation exposed by the custom title bar.
/// </summary>
public enum WindowSystemAction
{
    /// <summary>
    /// Minimizes the window.
    /// </summary>
    Minimize,

    /// <summary>
    /// Maximizes the window.
    /// </summary>
    Maximize,

    /// <summary>
    /// Restores the window to its normal state.
    /// </summary>
    Restore,

    /// <summary>
    /// Closes the window.
    /// </summary>
    Close,
}

/// <summary>
/// Routes a custom title-bar action through the native WPF system command implementation.
/// </summary>
public sealed class WindowSystemCommand : ICommand
{
    /// <summary>
    /// Gets or sets the operation performed by the command.
    /// </summary>
    public WindowSystemAction Action { get; set; }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        return parameter is Window;
    }

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (parameter is not Window window)
        {
            return;
        }

        switch (Action)
        {
            case WindowSystemAction.Minimize:
                SystemCommands.MinimizeWindow(window);
                break;
            case WindowSystemAction.Maximize:
                SystemCommands.MaximizeWindow(window);
                break;
            case WindowSystemAction.Restore:
                SystemCommands.RestoreWindow(window);
                break;
            case WindowSystemAction.Close:
                SystemCommands.CloseWindow(window);
                break;
            default:
                throw new InvalidOperationException($"Unsupported window system action: {Action}.");
        }
    }
}
