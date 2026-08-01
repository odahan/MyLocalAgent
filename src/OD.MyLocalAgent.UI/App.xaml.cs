using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Pages;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using OD.MyLocalAgent.Workflows;

namespace OD.MyLocalAgent.UI;

/// <summary>
/// Defines the WPF application entry point.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <inheritdoc />
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    ContentRootPath = AppContext.BaseDirectory,
                });
            builder.AddODServices();
            builder.Services.AddWorkflowServices();
            RegisterUserInterface(builder.Services);

            _host = builder.Build();
            var reporter = _host.Services.GetRequiredService<IIncidentReporter>();
            AppExceptionHandlers.Initialize(reporter);
            AppExceptionHandlers.WireProcessEvents();
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            _host.Services.GetRequiredService<IncidentMessengerBridge>().Start();
            _host.Services.GetRequiredService<IThemeService>().ApplyConfiguredTheme();
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            await _host.StartAsync().ConfigureAwait(true);
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            string? correlationId = null;
            if (_host is not null)
            {
                try
                {
                    correlationId = await _host.Services
                        .GetRequiredService<IIncidentReporter>()
                        .ReportAsync(
                            exception,
                            "Application startup",
                            CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch
                {
                    // The fallback message remains available when incident storage cannot start.
                }
            }

            var correlationText = correlationId is null
                ? string.Empty
                : $"{Environment.NewLine}Correlation id: {correlationId}";
            MessageBox.Show(
                $"OD.MyLocalAgent could not start.{Environment.NewLine}{exception.Message}{correlationText}",
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        try
        {
            if (_host is not null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            if (_host is not null)
            {
                try
                {
                    _ = await _host.Services
                        .GetRequiredService<IIncidentReporter>()
                        .ReportAsync(
                            exception,
                            "Application shutdown",
                            CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch
                {
                    // Process shutdown continues when the incident store is unavailable.
                }
            }
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }

    private static void RegisterUserInterface(IServiceCollection services)
    {
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);
        services.AddSingleton<ISnackbarMessageQueue>(
            _ => new SnackbarMessageQueue(TimeSpan.FromSeconds(6), Current.Dispatcher));
        services.AddSingleton<IDesktopInteractionService, DesktopInteractionService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<ILogManagementService, LogManagementService>();
        services.AddSingleton<IWorkflowAssistantService, WorkflowAssistantService>();
        services.AddSingleton<IMarkdownRenderingService, MarkdownRenderingService>();
        services.AddSingleton<IWebView2AvailabilityService, WebView2AvailabilityService>();
        services.AddSingleton<IAIWorkflowHelperDialogService, AIWorkflowHelperDialogService>();
        services.AddSingleton<IncidentMessengerBridge>();
#if !DEBUG
        // Debug builds skip the automatic startup resync so a debugging session never
        // rewrites Task Scheduler registrations that point at the deployed runner. The
        // Dashboard "Resync schedules" button remains available as an explicit action.
        services.AddHostedService<SchedulerStartupService>();
#endif

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<TasksViewModel>();
        services.AddSingleton<TaskEditorViewModel>();
        services.AddSingleton<PromptEditorViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<RunMonitorViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<ToolTesterViewModel>();
        services.AddSingleton<AIPlaygroundViewModel>();
        services.AddSingleton<TrustFilterViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AboutViewModel>();

        services.AddTransient<DashboardPage>();
        services.AddTransient<TasksPage>();
        services.AddTransient<TaskEditorPage>();
        services.AddTransient<PromptEditorPage>();
        services.AddTransient<LogsPage>();
        services.AddTransient<RunMonitorPage>();
        services.AddTransient<ToolsPage>();
        services.AddTransient<ToolTesterPage>();
        services.AddTransient<AIPlaygroundPage>();
        services.AddTransient<TrustFilterPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AboutPage>();
        services.AddSingleton(
            serviceProvider =>
            {
                var window = new MainWindow
                {
                    DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>(),
                };
                return window;
            });
    }

    private async void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        try
        {
            await AppExceptionHandlers.HandleAsync(
                    e.Exception,
                    "Application.DispatcherUnhandledException")
                .ConfigureAwait(true);
        }
        catch
        {
            // The dispatcher must stay alive even if durable incident storage is unavailable.
        }
    }
}
