using System.Collections.Concurrent;
using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements a validated, watched, one-file-per-task JSON store.
/// </summary>
public sealed class JsonTaskStore : ITaskStore, IDisposable
{
    private readonly string _directoryPath;
    private readonly string _archiveDirectoryPath;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _taskGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance using an explicit directory.
    /// </summary>
    /// <param name="directoryPath">The task directory.</param>
    /// <param name="timeProvider">The application clock reserved for store coordination.</param>
    /// <param name="archiveDirectoryPath">The directory that receives archived task JSON files.</param>
    public JsonTaskStore(
        string directoryPath,
        TimeProvider? timeProvider = null,
        string? archiveDirectoryPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _directoryPath = Path.GetFullPath(directoryPath);
        _archiveDirectoryPath = Path.GetFullPath(
            archiveDirectoryPath ?? Path.Combine(_directoryPath, "archives"));
        Directory.CreateDirectory(_directoryPath);
        Directory.CreateDirectory(_archiveDirectoryPath);
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
    public async Task<ScheduledTask> SaveAsync(
        ScheduledTask task,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        ThrowIfDisposed();
        ScheduledTaskValidator.Validate(task);
        var gate = GetGate(task.Id);
        ScheduledTask persistedTask;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var destinationPath = GetPath(task.Id);
            var now = _timeProvider.GetUtcNow();
            if (File.Exists(destinationPath))
            {
                var existing = await ReadTaskAsync(destinationPath, cancellationToken)
                    .ConfigureAwait(false);
                var createdUtc = existing.CreatedUtc ?? now;
                persistedTask = task with
                {
                    CreatedUtc = createdUtc,
                    UpdatedUtc = Latest(now, createdUtc, existing.UpdatedUtc),
                };
            }
            else
            {
                persistedTask = task with
                {
                    CreatedUtc = now,
                    UpdatedUtc = now,
                };
            }

            var json = ScheduledTaskSerializer.Serialize(persistedTask);
            var temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    json,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, destinationPath, overwrite: true);
                File.SetLastWriteTimeUtc(
                    destinationPath,
                    persistedTask.UpdatedUtc!.Value.UtcDateTime);
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
        return persistedTask;
    }

    /// <inheritdoc />
    public async Task<string?> ArchiveAsync(
        string id,
        CancellationToken cancellationToken)
    {
        ValidateId(id);
        ThrowIfDisposed();
        var gate = GetGate(id);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? archivePath = null;
        try
        {
            var sourcePath = GetPath(id);
            if (!File.Exists(sourcePath))
            {
                return null;
            }

            var task = await ReadTaskAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var json = ScheduledTaskSerializer.Serialize(task);
            Directory.CreateDirectory(_archiveDirectoryPath);
            archivePath = GetArchivePath(id);
            await File.WriteAllTextAsync(
                archivePath,
                json,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);
            File.Delete(sourcePath);
        }
        finally
        {
            gate.Release();
        }

        TasksChanged?.Invoke(
            this,
            new TaskStoreChangedEventArgs(id, TaskStoreChangeKind.Deleted));
        return archivePath;
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

        var file = new FileInfo(path);
        var fileCreatedUtc = new DateTimeOffset(file.CreationTimeUtc);
        var fileUpdatedUtc = new DateTimeOffset(file.LastWriteTimeUtc);
        var createdUtc = task.CreatedUtc ??
            (fileCreatedUtc <= fileUpdatedUtc ? fileCreatedUtc : fileUpdatedUtc);
        var updatedUtc = Latest(fileUpdatedUtc, createdUtc, task.UpdatedUtc);
        return task with
        {
            CreatedUtc = createdUtc,
            UpdatedUtc = updatedUtc,
        };
    }

    private static DateTimeOffset Latest(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset? third = null)
    {
        var latest = first >= second ? first : second;
        return third is not null && third > latest ? third.Value : latest;
    }

    private SemaphoreSlim GetGate(string id) =>
        _taskGates.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));

    private string GetPath(string id) =>
        Path.GetFullPath(id + ".json", _directoryPath);

    private string GetArchivePath(string id)
    {
        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd'T'HHmmss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var baseName = $"{id}--archived-{timestamp}";
        var path = Path.Combine(_archiveDirectoryPath, baseName + ".json");
        var suffix = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(_archiveDirectoryPath, $"{baseName}-{suffix:D2}.json");
            suffix++;
        }

        return path;
    }

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
