using Dropbox.Api;
using Dropbox.Api.Files;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides Dropbox file operations with access-token or refresh-token authentication.
/// </summary>
public sealed class DropboxConnector : IDropboxConnector
{
    private readonly IOptions<DropboxOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DropboxConnector"/> class.
    /// </summary>
    /// <param name="options">The Dropbox credential configuration.</param>
    /// <param name="httpClientFactory">The managed HTTP client factory.</param>
    public DropboxConnector(
        IOptions<DropboxOptions> options,
        IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _options = options;
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public Task<DropboxItemDto> UploadAsync(
        string localPath,
        string dropboxPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(dropboxPath);
        return ExecuteApiOperationAsync(
            "Upload",
            async () =>
            {
                using var client = CreateClient();
                await using var input = new FileStream(
                    localPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 81920,
                    useAsync: true);
                var metadata = await client.Files
                    .UploadAsync(
                        dropboxPath,
                        WriteMode.Overwrite.Instance,
                        body: input)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return Map(metadata);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DropboxItemDto> DownloadAsync(
        string dropboxPath,
        string localPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dropboxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);
        return ExecuteApiOperationAsync(
            "Download",
            async () =>
            {
                using var client = CreateClient();
                using var response = await client.Files
                    .DownloadAsync(dropboxPath)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                var directory = Path.GetDirectoryName(Path.GetFullPath(localPath));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using var input = await response
                    .GetContentAsStreamAsync()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var output = new FileStream(
                    localPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true);
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                return Map(response.Response);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DropboxItemDto>> ListAsync(
        string folderPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folderPath);
        return ExecuteApiOperationAsync(
            "List",
            async () =>
            {
                using var client = CreateClient();
                var items = new List<DropboxItemDto>();
                var page = await client.Files
                    .ListFolderAsync(folderPath)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);

                while (true)
                {
                    items.AddRange(page.Entries.Select(Map));
                    if (!page.HasMore)
                    {
                        return (IReadOnlyList<DropboxItemDto>)items;
                    }

                    page = await client.Files
                        .ListFolderContinueAsync(page.Cursor)
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            },
            cancellationToken);
    }

    private DropboxClient CreateClient()
    {
        var options = GetValidatedOptions();
        var httpClient = _httpClientFactory.CreateClient(ConnectorHttpClientNames.Dropbox);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;
        var longPollClient = _httpClientFactory.CreateClient(ConnectorHttpClientNames.Dropbox);
        longPollClient.Timeout = Timeout.InfiniteTimeSpan;
        var configuration = new DropboxClientConfig("OD.MyLocalAgent/1.0")
        {
            HttpClient = httpClient,
            LongPollHttpClient = longPollClient,
        };

        return !string.IsNullOrWhiteSpace(options.AccessToken)
            ? new DropboxClient(options.AccessToken, configuration)
            : new DropboxClient(options.RefreshToken!, options.AppKey!, configuration);
    }

    private DropboxOptions GetValidatedOptions()
    {
        DropboxOptions options;
        try
        {
            options = _options.Value;
        }
        catch (OptionsValidationException exception)
        {
            throw new ConnectorConfigurationException(
                $"Dropbox connector configuration is invalid: {exception.Message}",
                exception);
        }

        var validation = ConnectorOptionsValidation.ValidateDropbox(options);
        return validation is null
            ? options
            : throw new ConnectorConfigurationException(validation);
    }

    private static DropboxItemDto Map(Metadata metadata)
    {
        var file = metadata.AsFile;
        return new DropboxItemDto(
            metadata.Name,
            metadata.PathDisplay ?? metadata.PathLower ?? metadata.Name,
            metadata.IsFolder,
            file?.Size,
            file is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(file.ClientModified, DateTimeKind.Utc)));
    }

    private static async Task<T> ExecuteApiOperationAsync<T>(
        string operation,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConnectorConfigurationException)
        {
            throw;
        }
        catch (HttpException exception)
        {
            throw new DropboxApiException(
                operation,
                exception.StatusCode,
                $"Dropbox operation '{operation}' failed: {exception.Message}",
                exception);
        }
        catch (Exception exception)
        {
            throw new DropboxApiException(
                operation,
                null,
                $"Dropbox operation '{operation}' failed: {exception.Message}",
                exception);
        }
    }
}
