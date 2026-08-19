using System.Collections.Concurrent;
using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements a validated, watched, one-file-per-task JSON store.
/// </summary>
public sealed class JsonTaskStore : ITaskStore, IDisposable
{
    private readonly string _directoryPath;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance using an explicit directory.
    /// </summary>
    /// <param name="directoryPath">The task directory.</param>
    /// <param name="timeProvider">The application clock reserved for store coordination.</param>
    public JsonTaskStore(string directoryPath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(timeProvider ?? TimeProvider.System);
        _directoryPath = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(_directoryPath);
        _watcher = new FileSystemWatcher(_directoryPath, "*.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName |
                NotifyFilters.LastWrite |
                NotifyFilters.CreationTime |
                NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Created += OnExternalChange;
        _watcher.Changed += OnExternalChange;
        _watcher.Deleted += OnExternalChange;
        _watcher.Renamed += OnExternalRename;
    }

    /// <inheritdoc />
    public event EventHandler<TaskStoreChangedEventArgs>? TasksChanged;

    /// <inheritdoc />
    public async Task<ScheduledTask?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        ThrowIfDisposed();
        var gate = GetGate(id);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = GetPath(id);
            if (!File.Exists(path))
            {
                return null;
            }

            return await ReadTaskAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduledTask>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var paths = Directory.EnumerateFiles(_directoryPath, "*.json")
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var tasks = new List<ScheduledTask>(paths.Length);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileNameWithoutExtension(path);
            var gate = GetGate(id);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (File.Exists(path))
                {
                    tasks.Add(await ReadTaskAsync(path, cancellationToken).ConfigureAwait(false));
                }
            }
            finally
            {
                gate.Release();
            }
        }

        return tasks;
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        ScheduledTask task,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ThrowIfDisposed();
        ScheduledTaskValidator.Validate(task);
        var json = ScheduledTaskSerializer.Serialize(task);
        var gate = GetGate(task.Id);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var destinationPath = GetPath(task.Id);
            var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }

        TasksChanged?.Invoke(
            this,
            new TaskStoreChangedEventArgs(task.Id, TaskStoreChangeKind.Saved));
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        ThrowIfDisposed();
        var gate = GetGate(id);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var deleted = false;
        try
        {
            var path = GetPath(id);
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted = true;
            }
        }
        finally
        {
            gate.Release();
        }

        if (deleted)
        {
            TasksChanged?.Invoke(
                this,
                new TaskStoreChangedEventArgs(id, TaskStoreChangeKind.Deleted));
        }

        return deleted;
    }

    /// <summary>
    /// Releases the file watcher and task locks.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnExternalChange;
        _watcher.Changed -= OnExternalChange;
        _watcher.Deleted -= OnExternalChange;
        _watcher.Renamed -= OnExternalRename;
        _watcher.Dispose();
        foreach (var gate in _taskGates.Values)
        {
            gate.Dispose();
        }

        _disposed = true;
    }

    private static async Task<ScheduledTask> ReadTaskAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var task = ScheduledTaskSerializer.Deserialize(json);
        var fileId = Path.GetFileNameWithoutExtension(path);
        if (!string.Equals(fileId, task.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new TaskValidationException(
                $"Task file '{Path.GetFileName(path)}' contains id '{task.Id}'. File name and id must match.");
        }

        return task;
    }

    private SemaphoreSlim GetGate(string id) =>
        _taskGates.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));

    private string GetPath(string id) =>
        Path.GetFullPath(id + ".json", _directoryPath);

    private static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id.Any(character =>
                (character < 'a' || character > 'z') &&
                !char.IsAsciiDigit(character) &&
                character != '-'))
        {
            throw new TaskValidationException(
                "Task id must match [a-z0-9-]+.");
        }
    }

    private void OnExternalChange(object sender, FileSystemEventArgs args) =>
        RaiseExternalChange(args.Name);

    private void OnExternalRename(object sender, RenamedEventArgs args)
    {
        RaiseExternalChange(args.OldName);
        RaiseExternalChange(args.Name);
    }

    private void RaiseExternalChange(string? fileName)
    {
        if (_disposed ||
            string.IsNullOrWhiteSpace(fileName) ||
            !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var taskId = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        TasksChanged?.Invoke(
            this,
            new TaskStoreChangedEventArgs(taskId, TaskStoreChangeKind.External));
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
