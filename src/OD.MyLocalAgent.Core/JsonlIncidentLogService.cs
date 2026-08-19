using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Stores incidents in a process-safe JSON Lines file.
/// </summary>
public sealed class JsonlIncidentLogService : IIncidentLogService
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance using an explicit path.
    /// </summary>
    /// <param name="path">The incident JSONL path.</param>
    /// <param name="timeProvider">The time provider used for lock retries.</param>
    public JsonlIncidentLogService(string path, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task AppendAsync(
        IncidentRecord incident,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);
        var line = JsonSerializer.Serialize(incident, SerializerOptions) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(line);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await WithExclusiveWriterAsync(
                async stream =>
                {
                    stream.Seek(0, SeekOrigin.End);
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IncidentRecord>> ReadAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var incidents = new List<IncidentRecord>();
            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var incident = JsonSerializer.Deserialize<IncidentRecord>(
                        line,
                        SerializerOptions);
                    if (incident is not null)
                    {
                        incidents.Add(incident);
                    }
                }
                catch (JsonException)
                {
                    // A partially written final line must not hide earlier valid incidents.
                }
            }

            return incidents;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var fullDestinationPath = Path.GetFullPath(destinationPath);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullDestinationPath)!);
            await using var destination = new FileStream(
                fullDestinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);

            if (!File.Exists(_path))
            {
                return;
            }

            await using var source = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await WithExclusiveWriterAsync(
                stream =>
                {
                    stream.SetLength(0);
                    return Task.CompletedTask;
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WithExclusiveWriterAsync(
        Func<FileStream, Task> operation,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    _path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.Read,
                    4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                await operation(stream).ConfigureAwait(false);
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt),
                    _timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
