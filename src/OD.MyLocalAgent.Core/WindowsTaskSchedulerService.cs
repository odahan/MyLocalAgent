using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskCreation = Microsoft.Win32.TaskScheduler.TaskCreation;
using TaskFolder = Microsoft.Win32.TaskScheduler.TaskFolder;
using TaskLogonType = Microsoft.Win32.TaskScheduler.TaskLogonType;
using TaskService = Microsoft.Win32.TaskScheduler.TaskService;
using TimeTrigger = Microsoft.Win32.TaskScheduler.TimeTrigger;
using ExecAction = Microsoft.Win32.TaskScheduler.ExecAction;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements rolling Windows Task Scheduler registrations from the JSON task store.
/// </summary>
public sealed class WindowsTaskSchedulerService : ITaskSchedulerService
{
    private const string RegistrationPrefix = "OD.MyLocalAgent.";

    private readonly ITaskStore _taskStore;
    private readonly IOptions<SchedulerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WindowsTaskSchedulerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsTaskSchedulerService"/> class.
    /// </summary>
    public WindowsTaskSchedulerService(
        ITaskStore taskStore,
        IOptions<SchedulerOptions> options,
        TimeProvider timeProvider,
        ILogger<WindowsTaskSchedulerService> logger)
    {
        ArgumentNullException.ThrowIfNull(taskStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _taskStore = taskStore;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(
        ScheduledTask task,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ScheduledTaskValidator.Validate(task);
        if (!task.Enabled)
        {
            await UnregisterAsync(task.Id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var nextOccurrence = CronSchedule.NextOccurrence(
            task.Schedule,
            _timeProvider.GetUtcNow(),
            TimeZoneInfo.Local)
            ?? throw new TaskValidationException(
                $"Task '{task.Id}' has no future cron occurrence.");
        var runnerPath = RunnerPathResolver.Resolve(_options.Value);

        await System.Threading.Tasks.Task.Run(
            () => RegisterCore(task, nextOccurrence, runnerPath),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UnregisterAsync(string id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        return System.Threading.Tasks.Task.Run(
            () =>
            {
                EnsureWindows();
                using var service = new TaskService();
                var folder = TryGetFolder(service);
                folder?.DeleteTask(GetRegistrationName(id), exceptionOnNotExists: false);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public bool IsRegistered(string id)
    {
        ValidateId(id);
        EnsureWindows();
        using var service = new TaskService();
        var path = $@"\{NormalizeFolderName()}\{GetRegistrationName(id)}";
        return service.GetTask(path) is not null;
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetNextRunAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        var task = await _taskStore.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return task is null || !task.Enabled
            ? null
            : CronSchedule.NextOccurrence(
                task.Schedule,
                _timeProvider.GetUtcNow(),
                TimeZoneInfo.Local);
    }

    /// <inheritdoc />
    public async Task<SchedulerResyncResult> ResyncAllAsync(
        CancellationToken cancellationToken)
    {
        var tasks = await _taskStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var registered = 0;
        var unregistered = 0;
        var failures = new List<string>();
        var expected = new HashSet<string>(
            tasks.Select(static task => task.Id),
            StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (task.Enabled)
                {
                    await RegisterAsync(task, cancellationToken).ConfigureAwait(false);
                    registered++;
                }
                else
                {
                    await UnregisterAsync(task.Id, cancellationToken).ConfigureAwait(false);
                    unregistered++;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(task.Id);
                _logger.LogError(
                    exception,
                    "Failed to resynchronize scheduled task {TaskId}.",
                    task.Id);
            }
        }

        try
        {
            var staleIds = await System.Threading.Tasks.Task.Run(
                () => GetStaleRegistrationIds(expected),
                cancellationToken).ConfigureAwait(false);
            foreach (var staleId in staleIds)
            {
                await UnregisterAsync(staleId, cancellationToken).ConfigureAwait(false);
                unregistered++;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failures.Add("<stale-registration-scan>");
            _logger.LogError(exception, "Failed to remove stale task registrations.");
        }

        return new SchedulerResyncResult(registered, unregistered, failures);
    }

    private void RegisterCore(
        ScheduledTask task,
        DateTimeOffset nextOccurrence,
        string runnerPath)
    {
        EnsureWindows();
        using var service = new TaskService();
        var folder = GetOrCreateFolder(service);
        var definition = service.NewTask();
        definition.RegistrationInfo.Description =
            $"Runs OD.MyLocalAgent task '{task.Name}' ({task.Id}).";
        definition.Settings.StartWhenAvailable = true;
        definition.Triggers.Add(new TimeTrigger(nextOccurrence.LocalDateTime));
        definition.Actions.Add(
            new ExecAction(
                runnerPath,
                $"run --task {task.Id}",
                Path.GetDirectoryName(runnerPath)));

        var userId = $@"{Environment.UserDomainName}\{Environment.UserName}";
        var logonType = _options.Value.RunWhenLoggedOff
            ? TaskLogonType.S4U
            : TaskLogonType.InteractiveToken;
        _ = folder.RegisterTaskDefinition(
            GetRegistrationName(task.Id),
            definition,
            TaskCreation.CreateOrUpdate,
            userId,
            password: null,
            logonType);
    }

    private IReadOnlyList<string> GetStaleRegistrationIds(
        IReadOnlySet<string> expectedTaskIds)
    {
        EnsureWindows();
        using var service = new TaskService();
        var folder = TryGetFolder(service);
        if (folder is null)
        {
            return [];
        }

        return folder.Tasks
            .Select(static task => task.Name)
            .Where(static name =>
                name.StartsWith(RegistrationPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(static name => name[RegistrationPrefix.Length..])
            .Where(id => !expectedTaskIds.Contains(id))
            .ToArray();
    }

    private TaskFolder GetOrCreateFolder(TaskService service) =>
        TryGetFolder(service) ??
        service.RootFolder.CreateFolder(
            NormalizeFolderName(),
            sddlForm: null,
            exceptionOnExists: false);

    private TaskFolder? TryGetFolder(TaskService service)
    {
        try
        {
            return service.GetFolder($@"\{NormalizeFolderName()}");
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private string NormalizeFolderName()
    {
        var folderName = _options.Value.TaskFolder
            .Trim()
            .Trim('\\', '/')
            .Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(folderName))
        {
            throw new OptionsValidationException(
                SchedulerOptions.SectionName,
                typeof(SchedulerOptions),
                ["Scheduler:TaskFolder is required."]);
        }

        return folderName;
    }

    private static string GetRegistrationName(string id) =>
        RegistrationPrefix + id;

    private static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Any(character =>
                (character < 'a' || character > 'z') &&
                !char.IsAsciiDigit(character) &&
                character != '-'))
        {
            throw new TaskValidationException("Task id must match [a-z0-9-]+.");
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Task Scheduler is available only on Windows.");
        }
    }
}
