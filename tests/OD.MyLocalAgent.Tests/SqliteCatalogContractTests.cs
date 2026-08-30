using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the declarative SQLite package, build-output, group, and Tool Map contracts.
/// </summary>
public sealed class SqliteCatalogContractTests
{
    /// <summary>
    /// Verifies SQLite provider packages are pinned centrally and referenced only by Core.
    /// </summary>
    [Fact]
    public void SqliteProviderPackagesArePinnedCentrallyAndReferencedByCore()
    {
        var root = FindRepositoryRoot();
        var packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        var versions = packages.Descendants("PackageVersion")
            .ToDictionary(element => (string)element.Attribute("Include")!, element => (string)element.Attribute("Version")!);

        versions["Microsoft.Data.Sqlite.Core"].Should().Be("10.0.10");
        versions["SQLitePCLRaw.bundle_winsqlite3"].Should().Be("2.1.11");

        var projectFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories);
        var references = projectFiles.Select(file => new
        {
            File = file,
            Packages = XDocument.Load(file).Descendants("PackageReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(static package => package is not null)
                .ToArray(),
        }).ToArray();

        references.Where(item => item.Packages.Contains("Microsoft.Data.Sqlite.Core", StringComparer.Ordinal))
            .Select(item => Path.GetFileNameWithoutExtension(item.File))
            .Should().Equal("OD.MyLocalAgent.Core");
        references.Where(item => item.Packages.Contains("SQLitePCLRaw.bundle_winsqlite3", StringComparer.Ordinal))
            .Select(item => Path.GetFileNameWithoutExtension(item.File))
            .Should().Equal("OD.MyLocalAgent.Core");
    }

    /// <summary>
    /// Verifies both application build outputs contain the SQLite provider and Windows-serviced provider asset.
    /// </summary>
    [Fact]
    public void UiAndRunnerBuildOutputsContainSqliteProviderAssets()
    {
        var root = FindRepositoryRoot();
        var outputRoots = new[]
        {
            (Path.Combine(root, "src", "OD.MyLocalAgent.UI", "bin"), "net10.0-windows"),
            (Path.Combine(root, "src", "OD.MyLocalAgent.Runner", "bin"), "net10.0"),
        };

        foreach (var (outputRoot, targetFramework) in outputRoots)
        {
            var outputs = Directory.EnumerateDirectories(outputRoot)
                .Select(configuration => Path.Combine(configuration, targetFramework, "win-x64"))
                .Where(Directory.Exists)
                .ToArray();

            outputs.Should().NotBeEmpty(outputRoot);
            outputs.Should().Contain(output =>
                File.Exists(Path.Combine(output, "Microsoft.Data.Sqlite.dll")) &&
                File.Exists(Path.Combine(output, "SQLitePCLRaw.provider.winsqlite3.dll")));
        }
    }

    /// <summary>
    /// Verifies task-whitelist groups include the Stage 22 local-content groups in metadata order.
    /// </summary>
    [Fact]
    public void TaskWhitelistContainsTheExpectedGroupsIncludingStage22()
    {
        ToolGroups.TaskWhitelist.Select(static group => group.Name).Should().Equal(
            "gmail", "calendar", "dropbox", "filesystem", "sqlite", "archive", "document", "spreadsheet", "pdf", "http", "web", "research", "knowledge");
        ToolGroups.Find("sqlite")!.DisplayOrder.Should().Be(500);
    }

    /// <summary>
    /// Verifies the SQLite aliases expose their exact declarative runtime metadata.
    /// </summary>
    [Fact]
    public void SqliteAliasesExposeTheirExactMetadata()
    {
        var expected = new Dictionary<string, (string AgentName, string Method, string[] Arguments, string[] Optional, FileSystemAccessMode Access, TaskCapability? Capability)>
        {
            ["sqlite.listObjects"] = ("sqlite_list_objects", "ListObjectsAsync", ["databasePath", "objectType", "namePattern", "maxResults"], ["objectType", "namePattern", "maxResults"], FileSystemAccessMode.ReadOnly, null),
            ["sqlite.describeTable"] = ("sqlite_describe_table", "DescribeTableAsync", ["databasePath", "tableName"], [], FileSystemAccessMode.ReadOnly, null),
            ["sqlite.query"] = ("sqlite_query", "QueryAsync", ["databasePath", "sql", "parameters", "maxRows", "storageSpaceId"], ["parameters", "maxRows", "storageSpaceId"], FileSystemAccessMode.ReadOnly, null),
            ["sqlite.execute"] = ("sqlite_execute", "ExecuteAsync", ["databasePath", "sql", "parameters", "createIfMissing"], ["parameters", "createIfMissing"], FileSystemAccessMode.ReadWrite, TaskCapability.SqliteWrite),
            ["sqlite.createProposalDatabase"] = ("sqlite_create_proposal_database", "CreateProposalDatabaseAsync", ["databasePath"], [], FileSystemAccessMode.ReadWrite, TaskCapability.SqliteWrite),
            ["sqlite.storeProposal"] = ("sqlite_store_proposal", "StoreProposalAsync", ["databasePath", "title", "summary", "sourceUrl", "used"], ["sourceUrl", "used"], FileSystemAccessMode.ReadWrite, TaskCapability.SqliteWrite),
        };

        var definitions = ToolMap.Definitions.Where(static definition => definition.ToolGroup == ToolGroups.Sqlite).ToArray();
        definitions.Select(static definition => definition.Alias).Should().Equal(expected.Keys);
        foreach (var definition in definitions)
        {
            var contract = expected[definition.Alias];
            definition.AgentFunctionName.Should().Be(contract.AgentName);
            definition.MethodName.Should().Be(contract.Method);
            definition.Arguments.Should().Equal(contract.Arguments);
            definition.OptionalArguments.Should().Equal(contract.Optional);
            definition.RequiredCapability.Should().Be(contract.Capability);
            definition.LocalPathArguments.Should().ContainSingle().Which.RequiredAccess.Should().Be(contract.Access);
        }
    }

    /// <summary>
    /// Verifies object parameters and cancellation-token exclusion remain reflection-driven.
    /// </summary>
    [Fact]
    public void SqliteReflectionMapsJsonElementToObjectAndExcludesCancellationToken()
    {
        var query = ToolMap.Find("sqlite.query")!;
        query.ArgumentDefinitions.Single(argument => argument.Name == "parameters").Kind.Should().Be(WorkflowArgumentKind.Object);
        query.ArgumentDefinitions.Single(argument => argument.Name == "parameters").SupportsPlaceholders.Should().BeFalse();
        query.Arguments.Should().NotContain("cancellationToken");
        query.Method!.GetParameters().Single(parameter => parameter.ParameterType == typeof(CancellationToken)).Name.Should().Be("cancellationToken");
    }

    /// <summary>
    /// Verifies contextual SQLite targets are isolated and the taskless projection uses the declared service contract.
    /// </summary>
    [Fact]
    public void SqliteContextualActivationCreatesIsolatedTargetsAndTasklessProjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPathSandbox>(new PathSandbox(Options.Create(new FileSystemOptions { RootPath = Path.GetTempPath() })));
        services.AddSingleton<IOptions<SQLiteOptions>>(Options.Create(new SQLiteOptions()));
        services.AddSingleton<IOptionsMonitor<GuardrailsOptions>>(new MutableOptionsMonitor<GuardrailsOptions>(new GuardrailsOptions()));
        services.AddSingleton<IContentTrustService, StubContentTrustService>();
        services.AddSingleton<IIncidentReporter, StubIncidentReporter>();
        services.AddSingleton(Mock.Of<IProposalMemoryService>());
        services.AddSingleton(TimeProvider.System);
        services.AddLogging();
        services.AddSingleton<IToolCatalog>(ToolCatalog.Default);
        services.AddSingleton<IToolTargetActivator, ToolTargetActivator>();
        services.AddSingleton<SqliteTool>(serviceProvider =>
            (SqliteTool)serviceProvider.GetRequiredService<IToolTargetActivator>()
                .CreateTarget(typeof(SqliteTool), ToolExecutionContext.Taskless));
        services.AddSingleton<ISqliteTool>(serviceProvider =>
            serviceProvider.GetRequiredService<SqliteTool>());
        using var provider = services.BuildServiceProvider();
        var activator = provider.GetRequiredService<IToolTargetActivator>();

        var first = activator.CreateTarget(
            typeof(SqliteTool),
            new ToolExecutionContext("task-one", []));
        var second = activator.CreateTarget(
            typeof(SqliteTool),
            new ToolExecutionContext("task-two", []));

        ToolMap.Find("sqlite.query")!.Activation.Should().Be(ToolActivationKind.Contextual);
        first.Should().BeOfType<SqliteTool>();
        second.Should().BeOfType<SqliteTool>();
        first.Should().NotBeSameAs(second);
        provider.GetRequiredService<ISqliteTool>()
            .Should().BeSameAs(provider.GetRequiredService<SqliteTool>());

        var registry = new ToolRegistry(
            ToolCatalog.Default,
            provider.GetRequiredService<IToolTargetActivator>());
        var schemaProjection = () => registry.GetAgentTools(
            [ToolGroups.Sqlite],
            new ToolExecutionContext(
                "sqlite-schema-task",
                [],
                [TaskCapability.SqliteWrite]));

        schemaProjection.Should().NotThrow(
            "optional SQLite object parameters must have a serializable null default");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OD.MyLocalAgent.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
