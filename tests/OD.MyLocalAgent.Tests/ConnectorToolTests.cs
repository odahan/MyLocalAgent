using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies connector argument mapping, compact JSON, and email safety gating.
/// </summary>
public sealed class ConnectorToolTests : IDisposable
{
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies Gmail reads and blocks sends while the safety flag is disabled.
    /// </summary>
    [Fact]
    public async Task GmailToolMapsMessagesAndEnforcesSendGate()
    {
        var connector = new Mock<IGmailConnector>(MockBehavior.Strict);
        var message = new EmailMessageDto(
            "m1",
            "from@example.com",
            "to@example.com",
            "Subject",
            "Snippet",
            "Body",
            DateTimeOffset.Parse("2026-07-29T08:00:00Z"),
            true);
        connector.Setup(instance => instance.GetTodaysMessagesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([message]);
        var tool = new GmailTool(
            connector.Object,
            Options.Create(new SafetyOptions()),
            NullLogger<GmailTool>.Instance,
            TimeProvider.System);

        var json = await tool.ReadTodayAsync(CancellationToken.None);
        var send = () => tool.SendAsync(
            "to@example.com",
            "Subject",
            "Body",
            CancellationToken.None);

        JsonDocument.Parse(json).RootElement[0]
            .GetProperty("id").GetString().Should().Be("m1");
        await send.Should().ThrowAsync<SafetyException>()
            .Where(exception => exception.RequiredFlag == "Safety:AllowSendEmail");
    }

    /// <summary>
    /// Verifies allowed Gmail sends map all arguments to the connector.
    /// </summary>
    [Fact]
    public async Task GmailSendMapsArgumentsWhenEnabled()
    {
        var connector = new Mock<IGmailConnector>(MockBehavior.Strict);
        connector.Setup(instance => instance.SendMessageAsync(
                "to@example.com",
                "Subject",
                "Body",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new EmailMessageDto(
                    "sent",
                    "me@example.com",
                    "to@example.com",
                    "Subject",
                    string.Empty,
                    "Body",
                    DateTimeOffset.UtcNow,
                    false));
        var tool = new GmailTool(
            connector.Object,
            Options.Create(new SafetyOptions { AllowSendEmail = true }),
            NullLogger<GmailTool>.Instance,
            TimeProvider.System);

        var json = await tool.SendAsync(
            "to@example.com",
            "Subject",
            "Body",
            CancellationToken.None);

        JsonDocument.Parse(json).RootElement
            .GetProperty("id").GetString().Should().Be("sent");
        connector.VerifyAll();
    }

    /// <summary>
    /// Verifies Calendar arguments and result shape.
    /// </summary>
    [Fact]
    public async Task CalendarCreateMapsArgumentsAndReturnsEventJson()
    {
        var start = DateTimeOffset.Parse("2026-07-29T10:00:00+02:00");
        var end = start.AddHours(1);
        var connector = new Mock<ICalendarConnector>(MockBehavior.Strict);
        connector.Setup(instance => instance.CreateEventAsync(
                "Review",
                start,
                end,
                "Discuss stage 2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new CalendarEventDto(
                    "e1",
                    "Review",
                    "Discuss stage 2",
                    start,
                    end,
                    null));
        var tool = new CalendarTool(
            connector.Object,
            NullLogger<CalendarTool>.Instance,
            TimeProvider.System);

        var json = await tool.CreateAsync(
            "Review",
            start,
            end,
            "Discuss stage 2",
            CancellationToken.None);

        JsonDocument.Parse(json).RootElement
            .GetProperty("id").GetString().Should().Be("e1");
        connector.VerifyAll();
    }

    /// <summary>
    /// Verifies Dropbox local paths are sandboxed and connector results are compact JSON.
    /// </summary>
    [Fact]
    public async Task DropboxUploadResolvesSandboxedLocalPath()
    {
        Directory.CreateDirectory(_rootPath);
        var localFile = Path.Combine(_rootPath, "out.txt");
        await File.WriteAllTextAsync(localFile, "content");
        var connector = new Mock<IDropboxConnector>(MockBehavior.Strict);
        connector.Setup(instance => instance.UploadAsync(
                localFile,
                "/backup/out.txt",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new DropboxItemDto(
                    "out.txt",
                    "/backup/out.txt",
                    false,
                    7,
                    null));
        var fileSystemOptions = Options.Create(
            new FileSystemOptions { RootPath = _rootPath });
        var tool = new DropboxTool(
            connector.Object,
            new PathSandbox(fileSystemOptions),
            NullLogger<DropboxTool>.Instance,
            TimeProvider.System);

        var json = await tool.UploadAsync(
            "out.txt",
            "/backup/out.txt",
            CancellationToken.None);

        JsonDocument.Parse(json).RootElement
            .GetProperty("path").GetString().Should().Be("/backup/out.txt");
        connector.VerifyAll();
    }

    /// <summary>
    /// Deletes the isolated test directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
