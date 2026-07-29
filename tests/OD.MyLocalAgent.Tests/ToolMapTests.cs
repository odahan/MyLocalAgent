using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;
using Xunit.Abstractions;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies Tool Map reflection consistency and whitelist filtering.
/// </summary>
public sealed class ToolMapTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _rootPath =
        Path.Combine(Path.GetTempPath(), "OD.MyLocalAgent.Tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolMapTests"/> class.
    /// </summary>
    /// <param name="output">The test output sink.</param>
    public ToolMapTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Verifies every normative method, agent name, and argument list through reflection.
    /// </summary>
    [Fact]
    public void DefinitionsMatchRealMethodsAndParameters()
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var agentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedTools = 0;

        foreach (var definition in ToolMap.Definitions)
        {
            aliases.Add(definition.Alias).Should().BeTrue(
                $"alias '{definition.Alias}' must be unique");
            ToolMap.Find(definition.Alias.ToUpperInvariant()).Should().Be(definition);
            if (!definition.IsAgentTool)
            {
                continue;
            }

            agentNames.Add(definition.AgentFunctionName!).Should().BeTrue(
                $"agent function '{definition.AgentFunctionName}' must be unique");
            var method = definition.DeclaringType.GetMethod(
                definition.MethodName!,
                BindingFlags.Instance | BindingFlags.Public);
            method.Should().NotBeNull(
                $"{definition.DeclaringType.Name}.{definition.MethodName} must exist");
            var parameterNames = method!.GetParameters()
                .Where(static parameter => parameter.ParameterType != typeof(CancellationToken))
                .Select(static parameter => parameter.Name)
                .ToArray();
            parameterNames.Should().Equal(definition.Arguments);
            checkedTools++;
        }

        _output.WriteLine(
            $"Tool Map consistency: {ToolMap.Definitions.Count} aliases, {checkedTools} invocable tools, all methods/names/arguments matched.");
    }

    /// <summary>
    /// Verifies only selected groups are exposed and agent names come from the map.
    /// </summary>
    [Fact]
    public void RegistryFiltersAgentToolsByGroup()
    {
        Directory.CreateDirectory(_rootPath);
        var registry = CreateRegistry();

        var tools = registry.GetAgentTools([ToolGroups.Gmail]);

        tools.Select(static tool => tool.Name).Should().BeEquivalentTo(
            "gmail_read_today",
            "gmail_read_unread",
            "gmail_send");
        tools.Should().OnlyContain(static tool => tool is AIFunction);
    }

    /// <summary>
    /// Verifies alias invocation enforces the whitelist before dispatch.
    /// </summary>
    [Fact]
    public async Task RegistryRejectsAliasOutsideWhitelist()
    {
        Directory.CreateDirectory(_rootPath);
        var registry = CreateRegistry();
        var action = () => registry.InvokeAsync(
            "gmail.readToday",
            default,
            [ToolGroups.FileSystem],
            CancellationToken.None);

        await action.Should().ThrowAsync<WorkflowValidationException>()
            .WithMessage("*whitelisted tool group 'gmail'*");
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

    private ToolRegistry CreateRegistry()
    {
        var fileSystemOptions = Options.Create(
            new FileSystemOptions { RootPath = _rootPath });
        var safetyOptions = Options.Create(new SafetyOptions());
        var sandbox = new PathSandbox(fileSystemOptions);
        var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                }));
        return new ToolRegistry(
            new GmailTool(
                Mock.Of<IGmailConnector>(),
                safetyOptions,
                NullLogger<GmailTool>.Instance,
                TimeProvider.System),
            new CalendarTool(
                Mock.Of<ICalendarConnector>(),
                NullLogger<CalendarTool>.Instance,
                TimeProvider.System),
            new DropboxTool(
                Mock.Of<IDropboxConnector>(),
                sandbox,
                NullLogger<DropboxTool>.Instance,
                TimeProvider.System),
            new FileSystemTool(
                sandbox,
                safetyOptions,
                fileSystemOptions,
                NullLogger<FileSystemTool>.Instance,
                TimeProvider.System),
            new HttpTool(
                new StubHttpClientFactory(handler),
                Options.Create(new HttpOptions()),
                safetyOptions,
                NullLogger<HttpTool>.Instance,
                TimeProvider.System));
    }
}
