using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace OD.MyLocalAgent.UI.Controls;

/// <summary>
/// Displays an in-memory HTML document while blocking navigation, pop-ups, downloads, and remote access.
/// </summary>
public sealed class SafeMarkdownView : WebView2
{
    /// <summary>
    /// Identifies the <see cref="Html"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.Register(
        nameof(Html),
        typeof(string),
        typeof(SafeMarkdownView),
        new PropertyMetadata(string.Empty, OnHtmlChanged));

    /// <summary>
    /// Identifies the <see cref="IsRuntimeAvailable"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IsRuntimeAvailableProperty =
        DependencyProperty.Register(
            nameof(IsRuntimeAvailable),
            typeof(bool),
            typeof(SafeMarkdownView),
            new FrameworkPropertyMetadata(
                true,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>
    /// Initializes a new instance of the <see cref="SafeMarkdownView"/> class.
    /// </summary>
    public SafeMarkdownView()
    {
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Gets or sets the complete restricted in-memory HTML document.
    /// </summary>
    public string Html
    {
        get => (string)GetValue(HtmlProperty);
        set => SetValue(HtmlProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the WebView2 runtime and control are available.
    /// </summary>
    public bool IsRuntimeAvailable
    {
        get => (bool)GetValue(IsRuntimeAvailableProperty);
        set => SetValue(IsRuntimeAvailableProperty, value);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!IsRuntimeAvailable || CoreWebView2 is not null)
        {
            return;
        }

        try
        {
            await EnsureCoreWebView2Async().ConfigureAwait(true);
            var core = CoreWebView2 ??
                throw new InvalidOperationException(
                    "WebView2 initialization did not provide a browser instance.");
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.DownloadStarting += OnDownloadStarting;
            Navigate();
        }
        catch (Exception)
        {
            IsRuntimeAvailable = false;
        }
    }

    private static void OnHtmlChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e) =>
        ((SafeMarkdownView)dependencyObject).Navigate();

    private void Navigate()
    {
        if (CoreWebView2 is not null && !string.IsNullOrEmpty(Html))
        {
            CoreWebView2.NavigateToString(Html);
        }
    }

    private static void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!string.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            e.Cancel = true;
        }
    }

    private static void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e) =>
        e.Handled = true;

    private static void OnDownloadStarting(
        object? sender,
        CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        e.Handled = true;
    }
}
