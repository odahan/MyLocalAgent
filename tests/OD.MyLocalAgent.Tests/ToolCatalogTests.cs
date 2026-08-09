using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies ToolCatalog dynamic discovery, attributes, validation, and target activation.
/// </summary>
public sealed class ToolCatalogTests
{
    /// <summary>
    /// Tests that catalog discovery is restricted to explicit assemblies.
    /// </summary>
    [Fact]
    public void DiscoveryIsLimitedToExplicitAssembly()
    {
        var coreCatalog = ToolCatalog.FromAssembly(typeof(ToolCatalog).Assembly);
        coreCatalog.Groups.Should().NotBeEmpty();
        coreCatalog.Definitions.Should().NotBeEmpty();
        coreCatalog.Definitions.Should().AllSatisfy(def =>
            def.DeclaringType.Assembly.FullName.Should().Be(typeof(ToolCatalog).Assembly.FullName));
    }

    /// <summary>
    /// Verifies all built-in runtime groups and aliases are discovered with exact metadata.
    /// </summary>
    [Fact]
    public void CurrentRuntimeGroupsAndAliasesDiscoveredWithExactMetadata()
    {
        var catalog = ToolCatalog.Default;
        var whitelistGroupNames = catalog.TaskWhitelistGroups.Select(g => g.Name).ToList();

        whitelistGroupNames.Should().Equal("gmail", "calendar", "dropbox", "filesystem", "sqlite", "http", "web");

        catalog.FindGroup("gmail")!.DisplayName.Should().Be("Gmail");
        catalog.FindGroup("calendar")!.DisplayName.Should().Be("Calendar");
        catalog.FindGroup("dropbox")!.DisplayName.Should().Be("Dropbox");
        catalog.FindGroup("filesystem")!.DisplayName.Should().Be("File system");
        catalog.FindGroup("http")!.DisplayName.Should().Be("HTTP");
        catalog.FindGroup("web")!.DisplayName.Should().Be("Web search (web) (cloud)");

        catalog.Find("gmail.readToday").Should().NotBeNull();
        catalog.Find("gmail.readUnread").Should().NotBeNull();
        catalog.Find("gmail.readRecent").Should().NotBeNull();
        catalog.Find("gmail.send").Should().NotBeNull();

        catalog.Find("calendar.today").Should().NotBeNull();
        catalog.Find("calendar.read").Should().NotBeNull();
        catalog.Find("calendar.create").Should().NotBeNull();

        catalog.Find("dropbox.upload").Should().NotBeNull();
        catalog.Find("dropbox.download").Should().NotBeNull();
        catalog.Find("dropbox.list").Should().NotBeNull();

        catalog.Find("filesystem.list").Should().NotBeNull();
        catalog.Find("filesystem.read").Should().NotBeNull();
        catalog.Find("filesystem.write").Should().NotBeNull();
        catalog.Find("filesystem.move").Should().NotBeNull();
        catalog.Find("filesystem.moveToBin").Should().NotBeNull();
        catalog.Find("filesystem.rename").Should().NotBeNull();
        catalog.Find("filesystem.setExtension").Should().NotBeNull();
        catalog.Find("filesystem.delete").Should().NotBeNull();
        catalog.Find("filesystem.createDirectory").Should().NotBeNull();
        catalog.Find("filesystem.markdownToHtml").Should().NotBeNull();
        catalog.Find("filesystem.htmlToText").Should().NotBeNull();
        catalog.Find("filesystem.shellExecute").Should().NotBeNull();
        catalog.Find("filesystem.powerShell").Should().NotBeNull();

        catalog.FindGroup("sqlite")!.DisplayName.Should().Be("SQLite");
        catalog.Find("sqlite.listObjects").Should().NotBeNull();
        catalog.Find("sqlite.describeTable").Should().NotBeNull();
        catalog.Find("sqlite.query").Should().NotBeNull();
        catalog.Find("sqlite.execute").Should().NotBeNull();

        catalog.Find("http.get").Should().NotBeNull();
        catalog.Find("http.post").Should().NotBeNull();
        catalog.Find("http.getText").Should().NotBeNull();

        catalog.Find("web.search").Should().NotBeNull();

        catalog.Find("ai.summarize").Should().NotBeNull();
        catalog.Find("ai.extractActionItems").Should().NotBeNull();
        catalog.Find("ai.classifyEmail").Should().NotBeNull();
        catalog.Find("ai.prompt").Should().NotBeNull();
    }

    /// <summary>
    /// Verifies snake_case agent function name derivation.
    /// </summary>
    [Fact]
    public void DerivedFunctionNamesEqualNormativeNames()
    {
        ToolCatalog.DeriveAgentFunctionName("gmail.readToday").Should().Be("gmail_read_today");
        ToolCatalog.DeriveAgentFunctionName("gmail.readUnread").Should().Be("gmail_read_unread");
        ToolCatalog.DeriveAgentFunctionName("gmail.readRecent").Should().Be("gmail_read_recent");
        ToolCatalog.DeriveAgentFunctionName("gmail.send").Should().Be("gmail_send");

        ToolCatalog.DeriveAgentFunctionName("calendar.today").Should().Be("calendar_today");
        ToolCatalog.DeriveAgentFunctionName("calendar.read").Should().Be("calendar_read");
        ToolCatalog.DeriveAgentFunctionName("calendar.create").Should().Be("calendar_create");

        ToolCatalog.DeriveAgentFunctionName("dropbox.upload").Should().Be("dropbox_upload");
        ToolCatalog.DeriveAgentFunctionName("dropbox.download").Should().Be("dropbox_download");
        ToolCatalog.DeriveAgentFunctionName("dropbox.list").Should().Be("dropbox_list");

        ToolCatalog.DeriveAgentFunctionName("filesystem.list").Should().Be("filesystem_list");
        ToolCatalog.DeriveAgentFunctionName("filesystem.read").Should().Be("filesystem_read");
        ToolCatalog.DeriveAgentFunctionName("filesystem.write").Should().Be("filesystem_write");
        ToolCatalog.DeriveAgentFunctionName("filesystem.move").Should().Be("filesystem_move");
        ToolCatalog.DeriveAgentFunctionName("filesystem.moveToBin").Should().Be("filesystem_move_to_bin");
        ToolCatalog.DeriveAgentFunctionName("filesystem.rename").Should().Be("filesystem_rename");
        ToolCatalog.DeriveAgentFunctionName("filesystem.setExtension").Should().Be("filesystem_set_extension");
        ToolCatalog.DeriveAgentFunctionName("filesystem.delete").Should().Be("filesystem_delete");
        ToolCatalog.DeriveAgentFunctionName("filesystem.createDirectory").Should().Be("filesystem_create_directory");
        ToolCatalog.DeriveAgentFunctionName("filesystem.markdownToHtml").Should().Be("filesystem_markdown_to_html");
        ToolCatalog.DeriveAgentFunctionName("filesystem.htmlToText").Should().Be("filesystem_html_to_text");
        ToolCatalog.DeriveAgentFunctionName("filesystem.shellExecute").Should().Be("filesystem_shell_execute");
        ToolCatalog.DeriveAgentFunctionName("filesystem.powerShell").Should().Be("filesystem_power_shell");

        ToolCatalog.DeriveAgentFunctionName("http.get").Should().Be("http_get");
        ToolCatalog.DeriveAgentFunctionName("http.post").Should().Be("http_post");
        ToolCatalog.DeriveAgentFunctionName("http.getText").Should().Be("http_get_text");

        ToolCatalog.DeriveAgentFunctionName("web.search").Should().Be("web_search");
        ToolCatalog.DeriveAgentFunctionName("sqlite.listObjects").Should().Be("sqlite_list_objects");
    }

    /// <summary>
    /// Verifies catalog facade equality.
    /// </summary>
    [Fact]
    public void CatalogAndCompatibilityFacadesAreExactlyEqual()
    {
        ToolGroups.Catalog.Should().Equal(ToolCatalog.Default.Groups);
        ToolGroups.TaskWhitelist.Should().Equal(ToolCatalog.Default.TaskWhitelistGroups);
        ToolGroups.All.Should().Equal(ToolCatalog.Default.TaskWhitelistNames);
        ToolMap.Definitions.Should().Equal(ToolCatalog.Default.Definitions);

        ToolGroups.Find("gmail").Should().Be(ToolCatalog.Default.FindGroup("gmail"));
        ToolMap.Find("gmail.send").Should().Be(ToolCatalog.Default.Find("gmail.send"));
        ToolGroups.GetDisplayOrder("gmail").Should().Be(ToolCatalog.Default.GetGroupDisplayOrder("gmail"));
    }

    /// <summary>
    /// Verifies stable ordering and reserved group positions.
    /// </summary>
    [Fact]
    public void OrderingIsStableAndReservedPositionsAreValid()
    {
        var groupOrders = ToolCatalog.Default.Groups.Select(g => g.DisplayOrder).ToList();
        groupOrders.Should().BeInAscendingOrder();

        // 100, 200, 300, 400, 1000, 1100, 2000
        ToolCatalog.Default.GetGroupDisplayOrder("gmail").Should().Be(100);
        ToolCatalog.Default.GetGroupDisplayOrder("calendar").Should().Be(200);
        ToolCatalog.Default.GetGroupDisplayOrder("dropbox").Should().Be(300);
        ToolCatalog.Default.GetGroupDisplayOrder("filesystem").Should().Be(400);
        ToolCatalog.Default.GetGroupDisplayOrder("http").Should().Be(1000);
        ToolCatalog.Default.GetGroupDisplayOrder("web").Should().Be(1100);
        ToolCatalog.Default.GetGroupDisplayOrder("ai").Should().Be(2000);
    }

    /// <summary>
    /// Verifies DescriptionAttribute derivation on ToolAttribute and ToolArgumentAttribute.
    /// </summary>
    [Fact]
    public void ToolAndArgumentAttributesProvideDescriptionAttributeForAgentSchema()
    {
        var toolAttr = new ToolAttribute("test.foo", "Test operation description", 10);
        (toolAttr is DescriptionAttribute).Should().BeTrue();
        toolAttr.Description.Should().Be("Test operation description");

        var argAttr = new ToolArgumentAttribute(WorkflowArgumentKind.String, "Arg description");
        (argAttr is DescriptionAttribute).Should().BeTrue();
        argAttr.Description.Should().Be("Arg description");
    }

    /// <summary>
    /// Verifies fail-fast validation for blank group name.
    /// </summary>
    [Fact]
    public void FailFastRule_BlankName_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(InvalidBlankGroupTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*blank ToolGroup name*");
    }

    /// <summary>
    /// Verifies fail-fast validation for duplicate group names.
    /// </summary>
    [Fact]
    public void FailFastRule_DuplicateGroupName_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(ValidGroup1Tool), typeof(ValidGroup1DuplicateTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate tool group name*");
    }

    /// <summary>
    /// Verifies fail-fast validation for duplicate group display order.
    /// </summary>
    [Fact]
    public void FailFastRule_DuplicateDisplayOrder_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(ValidGroup1Tool), typeof(DuplicateOrderTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate tool group display order*");
    }

    /// <summary>
    /// Verifies fail-fast validation for invalid service contract.
    /// </summary>
    [Fact]
    public void FailFastRule_InvalidServiceContract_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(InvalidContractTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*does not implement specified ServiceContract*");
    }

    /// <summary>
    /// Verifies fail-fast validation for alias prefix mismatch.
    /// </summary>
    [Fact]
    public void FailFastRule_AliasPrefixMismatch_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(PrefixMismatchTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*prefix does not match owning group name*");
    }

    /// <summary>
    /// Verifies fail-fast validation for missing parameter attributes.
    /// </summary>
    [Fact]
    public void FailFastRule_MissingParameterAttribute_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(MissingParamAttrTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*missing [ToolArgument] attribute*");
    }

    /// <summary>
    /// Verifies fail-fast validation for non-final CancellationToken parameters.
    /// </summary>
    [Fact]
    public void FailFastRule_CancellationTokenNotFinal_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(NonFinalCancellationTokenTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*must be the final parameter*");
    }

    /// <summary>
    /// Verifies fail-fast validation for unknown safety flags.
    /// </summary>
    [Fact]
    public void FailFastRule_UnknownSafetyFlag_Throws()
    {
        var act = () => ToolCatalog.FromTypes([typeof(UnknownSafetyFlagTool)]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown Safety flag*");
    }

    /// <summary>
    /// Verifies Singleton activation behavior in ToolTargetActivator.
    /// </summary>
    [Fact]
    public void SingletonAndContextualActivationBehaveCorrectly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ValidSingletonTool>();
        var catalog = ToolCatalog.FromTypes([typeof(ValidSingletonTool)]);
        services.AddSingleton<IToolCatalog>(catalog);
        services.AddSingleton<IToolTargetActivator, ToolTargetActivator>();

        var sp = services.BuildServiceProvider();
        var activator = sp.GetRequiredService<IToolTargetActivator>();

        var context1 = new ToolExecutionContext("task1", []);
        var context2 = new ToolExecutionContext("task2", []);

        var singleton1 = activator.CreateTarget(typeof(ValidSingletonTool), context1);
        var singleton2 = activator.CreateTarget(typeof(ValidSingletonTool), context2);

        singleton1.Should().BeSameAs(singleton2);
    }

    /// <summary>
    /// Verifies unselected groups do not activate target instances.
    /// </summary>
    [Fact]
    public void UnselectedGroupsDoNotActivateTargets()
    {
        var catalog = ToolCatalog.FromTypes([typeof(ValidGroup1Tool)]);
        var mockActivator = new Mock<IToolTargetActivator>(MockBehavior.Strict);
        var registry = new ToolRegistry(catalog, mockActivator.Object);

        var tools = registry.GetAgentTools([]);
        tools.Should().BeEmpty();
        mockActivator.Verify(a => a.CreateTarget(It.IsAny<Type>(), It.IsAny<ToolExecutionContext>()), Times.Never);
    }

    /// <summary>
    /// Verifies attributed tools flow through catalog discovery and registry function creation.
    /// </summary>
    [Fact]
    public void AttributedTestToolCanFlowThroughCatalogAndRegistry()
    {
        var catalog = ToolCatalog.FromTypes([typeof(ValidGroup1Tool)]);
        var services = new ServiceCollection();
        services.AddSingleton<ValidGroup1Tool>();
        services.AddSingleton<IToolCatalog>(catalog);
        services.AddSingleton<IToolTargetActivator, ToolTargetActivator>();

        var sp = services.BuildServiceProvider();
        var registry = new ToolRegistry(catalog, sp.GetRequiredService<IToolTargetActivator>());

        registry.Definitions.Should().ContainSingle(d => d.Alias == "testgroup1.doWork");
        var agentTools = registry.GetAgentTools(["testgroup1"]);
        agentTools.Should().HaveCount(1);
    }

    [ToolGroup("", "Blank", "Desc", 10)]
    private sealed class InvalidBlankGroupTool
    {
        [Tool("test.foo", "Foo", 10)]
        public void Foo([ToolArgument(WorkflowArgumentKind.String, "Arg")] string x) { }
    }

    /// <summary>
    /// Valid test tool stub for group 1.
    /// </summary>
    [ToolGroup("testgroup1", "Group 1", "Desc 1", 10)]
    public class ValidGroup1Tool
    {
        /// <summary>
        /// Test tool method.
        /// </summary>
        /// <param name="input">Input parameter.</param>
        /// <returns>Result.</returns>
        [Tool("testgroup1.doWork", "Do work", 10)]
        public Task<string> DoWorkAsync([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input) => Task.FromResult(input);
    }

    [ToolGroup("testgroup1", "Group 1 Dup", "Desc 1 Dup", 11)]
    private sealed class ValidGroup1DuplicateTool
    {
        [Tool("testgroup1.doWork2", "Do work 2", 10)]
        public void DoWork2([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input) { }
    }

    [ToolGroup("testgroup2", "Group 2", "Desc 2", 10)]
    private sealed class DuplicateOrderTool
    {
        [Tool("testgroup2.doWork", "Do work", 10)]
        public void DoWork([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input) { }
    }

    /// <summary>
    /// Custom test service interface.
    /// </summary>
    public interface ICustomService { }

    [ToolGroup("testgroup3", "Group 3", "Desc 3", 12, ServiceContract = typeof(ICustomService))]
    private sealed class InvalidContractTool
    {
        [Tool("testgroup3.doWork", "Do work", 10)]
        public void DoWork([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input) { }
    }

    [ToolGroup("testgroup4", "Group 4", "Desc 4", 13)]
    private sealed class PrefixMismatchTool
    {
        [Tool("wronggroup.doWork", "Do work", 10)]
        public void DoWork([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input) { }
    }

    [ToolGroup("testgroup5", "Group 5", "Desc 5", 14)]
    private sealed class MissingParamAttrTool
    {
        [Tool("testgroup5.doWork", "Do work", 10)]
        public void DoWork(string input) { }
    }

    [ToolGroup("testgroup6", "Group 6", "Desc 6", 15)]
    private sealed class NonFinalCancellationTokenTool
    {
        [Tool("testgroup6.doWork", "Do work", 10)]
        public void DoWork([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input, CancellationToken ct, [ToolArgument(WorkflowArgumentKind.String, "Extra")] string extra) { }
    }

    [ToolGroup("testgroup7", "Group 7", "Desc 7", 16)]
    private sealed class UnknownSafetyFlagTool
    {
        [Tool("testgroup7.doWork", "Do work", 10, SafetyFlag = "Safety:UnknownFlag")]
        public void DoWork([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input) { }
    }

    /// <summary>
    /// Valid test tool stub for singleton activation.
    /// </summary>
    [ToolGroup("testgroup8", "Group 8", "Desc 8", 17, Activation = ToolActivationKind.Singleton)]
    public sealed class ValidSingletonTool
    {
        /// <summary>
        /// Test tool method.
        /// </summary>
        /// <param name="input">Input parameter.</param>
        /// <returns>Result.</returns>
        [Tool("testgroup8.doWork", "Do work", 10)]
        public Task<string> DoWorkAsync([ToolArgument(WorkflowArgumentKind.String, "Input text")] string input) => Task.FromResult(input);
    }
}
