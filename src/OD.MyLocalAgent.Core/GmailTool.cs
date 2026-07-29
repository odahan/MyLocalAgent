using System.ComponentModel;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Exposes compact-JSON Gmail connector operations.
/// </summary>
public sealed class GmailTool : IGmailTool
{
    private readonly IGmailConnector _connector;
    private readonly IOptions<SafetyOptions> _safetyOptions;
    private readonly ILogger<GmailTool> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GmailTool"/> class.
    /// </summary>
    public GmailTool(
        IGmailConnector connector,
        IOptions<SafetyOptions> safetyOptions,
        ILogger<GmailTool> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connector);
        ArgumentNullException.ThrowIfNull(safetyOptions);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _connector = connector;
        _safetyOptions = safetyOptions;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    [Description("Reads Gmail messages received today.")]
    public Task<string> ReadTodayAsync(CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "gmail.readToday",
            new { },
            async () =>
            {
                var messages = await _connector.GetTodaysMessagesAsync(cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(messages);
            });

    /// <inheritdoc />
    [Description("Reads unread Gmail messages.")]
    public Task<string> ReadUnreadAsync(CancellationToken cancellationToken) =>
        ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "gmail.readUnread",
            new { },
            async () =>
            {
                var messages = await _connector.GetUnreadMessagesAsync(cancellationToken)
                    .ConfigureAwait(false);
                return ToolJson.Serialize(messages);
            });

    /// <inheritdoc />
    [Description("Sends a plain-text Gmail message when Safety:AllowSendEmail is enabled.")]
    public Task<string> SendAsync(
        [Description("Recipient email address.")] string to,
        [Description("Message subject.")] string subject,
        [Description("Plain-text message body.")] string body,
        CancellationToken cancellationToken)
    {
        if (!_safetyOptions.Value.AllowSendEmail)
        {
            throw new SafetyException("gmail.send", "Safety:AllowSendEmail");
        }

        if (!MailAddress.TryCreate(to, out _))
        {
            throw new ArgumentException("A valid recipient email address is required.", nameof(to));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentNullException.ThrowIfNull(body);

        return ToolCallLog.ExecuteAsync(
            _logger,
            _timeProvider,
            "gmail.send",
            new { to, subjectLength = subject.Length, bodyLength = body.Length },
            async () =>
            {
                var message = await _connector.SendMessageAsync(
                    to,
                    subject,
                    body,
                    cancellationToken).ConfigureAwait(false);
                return ToolJson.Serialize(message);
            });
    }
}
