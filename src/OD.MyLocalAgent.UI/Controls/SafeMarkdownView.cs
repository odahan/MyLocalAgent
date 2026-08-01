using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace OD.MyLocalAgent.UI.Controls;

/// <summary>
/// Displays an in-memory HTML document while blocking navigation, pop-ups, downloads, and remote access.
/// </summary>
public sealed class SafeMarkdownView : WebView2
{
    private Task? _initializationTask;
    private string? _lastNavigatedHtml;
    private bool _internalNavigationRequested;
    private ulong? _internalNavigationId;

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
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnRuntimeAvailabilityChanged));

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

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await InitializeAndNavigateAsync().ConfigureAwait(true);

    private async Task InitializeAndNavigateAsync()
    {
        if (!IsLoaded || !IsRuntimeAvailable)
        {
            return;
        }

        try
        {
            _initializationTask ??= InitializeCoreAsync();
            await _initializationTask.ConfigureAwait(true);
            await Dispatcher.InvokeAsync(
                Navigate,
                DispatcherPriority.Loaded);
        }
        catch (Exception)
        {
            IsRuntimeAvailable = false;
        }
    }

    private async Task InitializeCoreAsync()
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
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.DownloadStarting += OnDownloadStarting;
    }

    private static async void OnHtmlChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e) =>
        await ((SafeMarkdownView)dependencyObject)
            .InitializeAndNavigateAsync()
            .ConfigureAwait(true);

    private static async void OnRuntimeAvailabilityChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            await ((SafeMarkdownView)dependencyObject)
                .InitializeAndNavigateAsync()
                .ConfigureAwait(true);
        }
    }

    private void Navigate()
    {
        if (CoreWebView2 is not null &&
            !string.IsNullOrWhiteSpace(Html) &&
            !string.Equals(Html, _lastNavigatedHtml, StringComparison.Ordinal))
        {
            _lastNavigatedHtml = Html;
            _internalNavigationRequested = true;
            try
            {
                CoreWebView2.NavigateToString(Html);
            }
            catch
            {
                _internalNavigationRequested = false;
                _lastNavigatedHtml = null;
                throw;
            }
        }
    }

    private void OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (_internalNavigationRequested)
        {
            _internalNavigationRequested = false;
            _internalNavigationId = e.NavigationId;
            return;
        }

        e.Cancel = true;
    }

    private void OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_internalNavigationId != e.NavigationId)
        {
            return;
        }

        _internalNavigationId = null;
        if (e.IsSuccess)
        {
            return;
        }

        _lastNavigatedHtml = null;
        IsRuntimeAvailable = false;
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
