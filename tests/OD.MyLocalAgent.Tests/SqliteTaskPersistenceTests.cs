using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Tests.TestDoubles;
using OD.MyLocalAgent.UI.Services;
using OD.MyLocalAgent.UI.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the persisted per-task SQLite Guardrails contract.
/// </summary>
public sealed class SqliteTaskPersistenceTests
{
    /// <summary>
    /// Keeps legacy tasks protected when their pre-Stage-19 JSON omits the preference.
    /// </summary>
    [Fact]
    public void MissingLegacySqliteGuardrailsPreferenceDefaultsToTrue()
    {
        var task = ScheduledTaskSerializer.Deserialize("""
        {
          "id": "legacy-task",
          "name": "Legacy task",
          "schedule": "0 7 * * *",
          "prompt": "Run safely.",
          "tools": [],
          "fileSystemAccess": [],
          "workflow": [],
          "enabled": true,
          "errorPolicy": "StopOnError"
        }
        """);

        task.SqliteGuardrailsEnabled.Should().BeTrue();
    }

    /// <summary>
    /// Writes an explicit false preference in canonical task JSON.
    /// </summary>
    [Fact]
    public void ExplicitSqliteGuardrailsOptOutIsSerialized()
    {
        var task = new ScheduledTask
        {
            Id = "sqlite-opt-out",
            Name = "SQLite opt-out",
            Schedule = "0 7 * * *",
            Prompt = "Run safely.",
            SqliteGuardrailsEnabled = false,
        };

        var json = ScheduledTaskSerializer.Serialize(task);

        json.Should().Contain("\"sqliteGuardrailsEnabled\": false");
    }

    /// <summary>
    /// Verifies a new task snapshots both possible SQLite Guardrails defaults.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewTaskSnapshotsCurrentSqliteGuardrailsDefault(bool defaultValue)
    {
        var options = new MutableOptionsMonitor<SQLiteOptions>(
            new SQLiteOptions { GuardrailsEnabledByDefault = defaultValue });
        var viewModel = CreateEditor(Mock.Of<ITaskStore>(), options);

        viewModel.Open(taskId: null);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.SqliteGuardrailsEnabled.Should().Be(defaultValue);
        viewModel.IsDirty.Should().BeFalse();
    }

    /// <summary>
    /// Verifies an existing task retains its persisted preference when the default changes.
    /// </summary>
    [Fact]
    public async Task ExistingTaskRetainsPersistedSqliteGuardrailsPreferenceAfterDefaultChanges()
    {
        var storedTask = new ScheduledTask
        {
            Id = "stored-sqlite-task",
            Name = "Stored SQLite task",
            Schedule = "0 7 * * *",
            Prompt = "Run safely.",
            SqliteGuardrailsEnabled = false,
        };
        var store = new Mock<ITaskStore>();
        store.Setup(instance => instance.GetAsync(storedTask.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(storedTask);
        var options = new MutableOptionsMonitor<SQLiteOptions>(
            new SQLiteOptions { GuardrailsEnabledByDefault = true });
        var viewModel = CreateEditor(store.Object, options);

        options.Set(new SQLiteOptions { GuardrailsEnabledByDefault = false });
        viewModel.Open(storedTask.Id);
        await (viewModel.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        viewModel.SqliteGuardrailsEnabled.Should().BeFalse();
        viewModel.IsDirty.Should().BeFalse();
    }

    private static TaskEditorViewModel CreateEditor(
        ITaskStore store,
        MutableOptionsMonitor<SQLiteOptions> sqliteOptions) =>
        new(
            store,
            Mock.Of<ITaskSchedulerService>(),
            Mock.Of<IIncidentReporter>(),
            Mock.Of<IDesktopInteractionService>(),
            Mock.Of<IMessenger>(),
            TimeProvider.System,
            sqliteOptions: sqliteOptions);
}
