using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides Google Calendar operations without initiating interactive OAuth consent.
/// </summary>
public sealed class CalendarConnector : ICalendarConnector
{
    private const string PrimaryCalendar = "primary";
    private readonly IGoogleCredentialProvider _credentialProvider;
    private readonly GoogleHttpClientFactoryAdapter _googleHttpClientFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="CalendarConnector"/> class.
    /// </summary>
    /// <param name="credentialProvider">The silent Google credential provider.</param>
    /// <param name="httpMessageHandlerFactory">The managed HTTP handler factory.</param>
    /// <param name="timeProvider">The time source used to identify today's events.</param>
    public CalendarConnector(
        IGoogleCredentialProvider credentialProvider,
        IHttpMessageHandlerFactory httpMessageHandlerFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(credentialProvider);
        ArgumentNullException.ThrowIfNull(httpMessageHandlerFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _credentialProvider = credentialProvider;
        _googleHttpClientFactory =
            new GoogleHttpClientFactoryAdapter(httpMessageHandlerFactory);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CalendarEventDto>> GetTodaysEventsAsync(
        CancellationToken cancellationToken) =>
        GetEventsAsync(CalendarPeriod.Day, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(
        CalendarPeriod period,
        CancellationToken cancellationToken)
    {
        return ExecuteApiOperationAsync(
            "GetToday'sEvents",
            async () =>
            {
                using var service = await CreateServiceAsync(cancellationToken).ConfigureAwait(false);
                var today = DateOnly.FromDateTime(
                    _timeProvider.GetLocalNow().DateTime);
                var (startDate, endDate) = GetPeriodBounds(today, period);
                var start = GetStartOfLocalDay(startDate);
                var end = GetStartOfLocalDay(endDate);
                var request = service.Events.List(PrimaryCalendar);
                request.TimeMinDateTimeOffset = start;
                request.TimeMaxDateTimeOffset = end;
                request.SingleEvents = true;
                request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                var events = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                return (IReadOnlyList<CalendarEventDto>)(events.Items ?? [])
                    .Select(CalendarEventMapper.Map)
                    .ToArray();
            },
            cancellationToken);
    }

    private static (DateOnly Start, DateOnly End) GetPeriodBounds(
        DateOnly today,
        CalendarPeriod period)
    {
        return period switch
        {
            CalendarPeriod.Day => (today, today.AddDays(1)),
            CalendarPeriod.Week => GetWeekBounds(today),
            CalendarPeriod.Month =>
                (today.AddDays(1 - today.Day),
                 today.AddDays(1 - today.Day).AddMonths(1)),
            CalendarPeriod.Year =>
                (today.AddDays(1 - today.DayOfYear),
                 today.AddDays(1 - today.DayOfYear).AddYears(1)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(period),
                period,
                "Unsupported calendar period."),
        };
    }

    private static (DateOnly Start, DateOnly End) GetWeekBounds(DateOnly today)
    {
        var daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
        var start = today.AddDays(-daysSinceMonday);
        return (start, start.AddDays(7));
    }

    private DateTimeOffset GetStartOfLocalDay(DateOnly date)
    {
        var localDateTime = date.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Unspecified);
        return new DateTimeOffset(
            localDateTime,
            _timeProvider.LocalTimeZone.GetUtcOffset(localDateTime));
    }

    /// <inheritdoc />
    public Task<CalendarEventDto> CreateEventAsync(
        string summary,
        DateTimeOffset start,
        DateTimeOffset end,
        string description,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(description);
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                end,
                "The event end time must be later than its start time.");
        }

        return ExecuteApiOperationAsync(
            "CreateEvent",
            async () =>
            {
                using var service = await CreateServiceAsync(cancellationToken).ConfigureAwait(false);
                var calendarEvent = new Event
                {
                    Summary = summary,
                    Description = description,
                    Start = new EventDateTime { DateTimeDateTimeOffset = start },
                    End = new EventDateTime { DateTimeDateTimeOffset = end },
                };
                var request = service.Events.Insert(calendarEvent, PrimaryCalendar);
                var created = await request.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                return CalendarEventMapper.Map(created);
            },
            cancellationToken);
    }

    private async Task<CalendarService> CreateServiceAsync(CancellationToken cancellationToken)
    {
        var credential = await _credentialProvider
            .GetCredentialSilentAsync(cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            throw new ConnectorConfigurationException(
                "Google consent is required. Use the interactive Connect action before running Calendar operations.");
        }

        return new CalendarService(new BaseClientService.Initializer
        {
            ApplicationName = "OD.MyLocalAgent",
            HttpClientFactory = _googleHttpClientFactory,
            HttpClientInitializer = credential,
        });
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
        catch (GoogleApiException exception)
        {
            var statusCode = (int)exception.HttpStatusCode;
            throw new CalendarApiException(
                operation,
                statusCode == 0 ? null : statusCode,
                $"Calendar operation '{operation}' failed: {exception.Message}",
                exception);
        }
        catch (Exception exception)
        {
            throw new CalendarApiException(
                operation,
                null,
                $"Calendar operation '{operation}' failed: {exception.Message}",
                exception);
        }
    }
}
