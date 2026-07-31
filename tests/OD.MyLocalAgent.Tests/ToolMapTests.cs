using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
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
            "gmail_read_recent",
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
    /// Verifies Web Search is unavailable unless its cloud group is selected.
    /// </summary>
    [Fact]
    public async Task RegistryRejectsWebSearchOutsideWhitelist()
    {
        Directory.CreateDirectory(_rootPath);
        var registry = CreateRegistry();
        using var arguments = JsonDocument.Parse("""{"query":"test"}""");
        var action = () => registry.InvokeAsync(
            "web.search",
            arguments.RootElement,
            [ToolGroups.Http],
            CancellationToken.None);

        await action.Should().ThrowAsync<WorkflowValidationException>()
            .WithMessage("*whitelisted tool group 'web'*");
    }

    /// <summary>
    /// Verifies the optional Web Search result limit may be omitted.
    /// </summary>
    [Fact]
    public async Task RegistryInvokesWebSearchWithoutOptionalLimit()
    {
        Directory.CreateDirectory(_rootPath);
        var registry = CreateRegistry();
        using var arguments = JsonDocument.Parse("""{"query":"test"}""");

        var output = await registry.InvokeAsync(
            "web.search",
            arguments.RootElement,
            [ToolGroups.Web],
            CancellationToken.None);

        JsonDocument.Parse(output).RootElement
            .GetProperty("providerCount")
            .GetInt32()
            .Should()
            .Be(0);
    }

    /// <summary>
    /// Verifies direct registry invocation binds file tools to the supplied task context.
    /// </summary>
    [Fact]
    public async Task RegistryUsesContextForAbsoluteFileSystemInvocation()
    {
        Directory.CreateDirectory(_rootPath);
        var authorizedPath = Path.Combine(
            Path.GetTempPath(),
            "OD.MyLocalAgent.Registry",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(authorizedPath);
        try
        {
            var registry = CreateRegistry();
            var outputPath = Path.Combine(authorizedPath, "context.txt");
            using var arguments = JsonDocument.Parse(
                JsonSerializer.Serialize(
                    new { path = outputPath, content = "context" }));
            var context = new ToolExecutionContext(
                "registry-task",
                [
                    new FileSystemAccessRule
                    {
                        Path = authorizedPath,
                        Access = FileSystemAccessMode.ReadWrite,
                    },
                ]);

            _ = await registry.InvokeAsync(
                "filesystem.write",
                arguments.RootElement,
                [ToolGroups.FileSystem],
                context,
                CancellationToken.None);
            var tasklessAction = () => registry.InvokeAsync(
                "filesystem.write",
                arguments.RootElement,
                [ToolGroups.FileSystem],
                CancellationToken.None);

            File.ReadAllText(outputPath).Should().Be("context");
            await tasklessAction.Should().ThrowAsync<PathSandboxException>();
        }
        finally
        {
            Directory.Delete(authorizedPath, recursive: true);
        }
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
        var webSearchConnector = new Mock<IWebSearchConnector>(
            MockBehavior.Strict);
        webSearchConnector
            .Setup(
                instance => instance.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new WebSearchResult(
                    [],
                    WebSearchOptions.OllamaProvider));
        return new ToolRegistry(
            new GmailTool(
                Mock.Of<IGmailConnector>(),
                safetyOptions,
                new MutableOptionsMonitor<GuardrailsOptions>(new GuardrailsOptions()),
                new StubContentTrustService(),
                new StubIncidentReporter(),
                NullLogger<GmailTool>.Instance,
                TimeProvider.System),
            new CalendarTool(
                Mock.Of<ICalendarConnector>(),
                NullLogger<CalendarTool>.Instance,
                TimeProvider.System),
            new DelegateContextualToolFactory<DropboxTool>(
                context => new DropboxTool(
                    Mock.Of<IDropboxConnector>(),
                    sandbox,
                    NullLogger<DropboxTool>.Instance,
                    TimeProvider.System,
                    context)),
            new DelegateContextualToolFactory<FileSystemTool>(
                context => new FileSystemTool(
                    sandbox,
                    safetyOptions,
                    fileSystemOptions,
                    NullLogger<FileSystemTool>.Instance,
                    TimeProvider.System,
                    context)),
            new HttpTool(
                new StubHttpClientFactory(handler),
                Options.Create(new HttpOptions()),
                safetyOptions,
                new MutableOptionsMonitor<GuardrailsOptions>(new GuardrailsOptions()),
                new StubContentTrustService(),
                new StubIncidentReporter(),
                NullLogger<HttpTool>.Instance,
                TimeProvider.System),
            new WebSearchTool(
                webSearchConnector.Object,
                new MutableOptionsMonitor<WebSearchOptions>(
                    new WebSearchOptions()),
                new MutableOptionsMonitor<GuardrailsOptions>(
                    new GuardrailsOptions()),
                new StubContentTrustService(),
                new StubIncidentReporter(),
                NullLogger<WebSearchTool>.Instance,
                TimeProvider.System));
    }
}
