using System.CommandLine;
using System.Text.Json;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Runner;

/// <summary>
/// Builds the runner command surface using System.CommandLine 2.0.
/// </summary>
public static class RunnerCommandFactory
{
    /// <summary>
    /// Creates the root command. Service resolution remains lazy so parser tests do not
    /// construct the application dependency-injection container.
    /// </summary>
    /// <param name="serviceProviderAccessor">
    /// An accessor for the started application service provider.
    /// </param>
    /// <param name="output">The normal command output writer.</param>
    /// <param name="error">The command error writer.</param>
    /// <returns>The configured root command.</returns>
    public static RootCommand CreateRootCommand(
        Func<IServiceProvider>? serviceProviderAccessor = null,
        TextWriter? output = null,
        TextWriter? error = null)
    {
        serviceProviderAccessor ??= static () =>
            throw new InvalidOperationException(
                "The runner service provider is unavailable during parser-only use.");
        output ??= Console.Out;
        error ??= Console.Error;

        var root = new RootCommand(
            "Executes OD.MyLocalAgent tasks and maintenance commands.");

        var runTaskOption = CreateTaskOption();
        var runCommand = new Command(
            "run",
            "Executes a task and rolls its Task Scheduler registration.");
        runCommand.Options.Add(runTaskOption);
        runCommand.SetAction(
            (parseResult, cancellationToken) =>
                RunnerCommandHandlers.RunAsync(
                    serviceProviderAccessor(),
                    parseResult.GetRequiredValue(runTaskOption),
                    output,
                    error,
                    cancellationToken));

        var listCommand = new Command(
            "list",
            "Lists stored tasks and their next runs.");
        listCommand.SetAction(
            (_, cancellationToken) =>
                RunnerCommandHandlers.ListAsync(
                    serviceProviderAccessor(),
                    output,
                    cancellationToken));

        var testPromptTaskOption = CreateTaskOption();
        var testPromptCommand = new Command(
            "test-prompt",
            "Executes only a task's prompt path without scheduling.");
        testPromptCommand.Options.Add(testPromptTaskOption);
        testPromptCommand.SetAction(
            (parseResult, cancellationToken) =>
                RunnerCommandHandlers.TestPromptAsync(
                    serviceProviderAccessor(),
                    parseResult.GetRequiredValue(testPromptTaskOption),
                    output,
                    error,
                    cancellationToken));

        var aliasOption = new Option<string>("--alias")
        {
            Description = "The directly invocable Tool Map alias.",
            Required = true,
        };
        aliasOption.Validators.Add(
            result =>
            {
                var alias = result.GetValueOrDefault<string>();
                if (string.IsNullOrWhiteSpace(alias))
                {
                    result.AddError("--alias requires a non-empty value.");
                    return;
                }

                var definition = ToolMap.Find(alias);
                if (definition is null)
                {
                    result.AddError($"Unknown Tool Map alias '{alias}'.");
                }
                else if (!definition.IsAgentTool)
                {
                    result.AddError(
                        $"Tool Map alias '{alias}' is not directly invocable.");
                }
            });

        var argsOption = new Option<string>("--args")
        {
            Description = "A JSON object containing tool arguments.",
            Required = true,
        };
        argsOption.Validators.Add(
            result =>
            {
                var json = result.GetValueOrDefault<string>();
                if (string.IsNullOrWhiteSpace(json))
                {
                    result.AddError("--args requires a JSON object.");
                    return;
                }

                try
                {
                    using var document = JsonDocument.Parse(json);
                    if (document.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        result.AddError("--args must contain a JSON object.");
                    }
                }
                catch (JsonException exception)
                {
                    result.AddError($"--args contains invalid JSON: {exception.Message}");
                }
            });

        var testToolCommand = new Command(
            "test-tool",
            "Invokes one Tool Map alias directly.");
        testToolCommand.Options.Add(aliasOption);
        testToolCommand.Options.Add(argsOption);
        testToolCommand.SetAction(
            (parseResult, cancellationToken) =>
                RunnerCommandHandlers.TestToolAsync(
                    serviceProviderAccessor(),
                    parseResult.GetRequiredValue(aliasOption),
                    parseResult.GetRequiredValue(argsOption),
                    output,
                    error,
                    cancellationToken));

        var resyncCommand = new Command(
            "resync-schedules",
            "Repairs Task Scheduler registrations from the task store.");
        resyncCommand.SetAction(
            (_, cancellationToken) =>
                RunnerCommandHandlers.ResyncSchedulesAsync(
                    serviceProviderAccessor(),
                    output,
                    cancellationToken));

        root.Subcommands.Add(runCommand);
        root.Subcommands.Add(listCommand);
        root.Subcommands.Add(testPromptCommand);
        root.Subcommands.Add(testToolCommand);
        root.Subcommands.Add(resyncCommand);
        return root;
    }

    private static Option<string> CreateTaskOption()
    {
        var option = new Option<string>("--task")
        {
            Description = "The task id.",
            Required = true,
        };
        option.Validators.Add(
            result =>
            {
                var id = result.GetValueOrDefault<string>();
                if (string.IsNullOrWhiteSpace(id))
                {
                    result.AddError("--task requires a non-empty value.");
                }
                else if (!IsValidTaskId(id))
                {
                    result.AddError("Task id must match [a-z0-9-]+.");
                }
            });
        return option;
    }

    private static bool IsValidTaskId(string id) =>
        id.Length > 0 &&
        id.All(
            static character =>
                (character >= 'a' && character <= 'z') ||
                char.IsAsciiDigit(character) ||
                character == '-');
}
