using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies the exact declarative Stage 22 catalog surface.</summary>
public sealed class Stage22CatalogContractTests
{
    /// <summary>Verifies the four groups are independent contextual whitelist groups in stable order.</summary>
    [Fact]
    public void GroupsHaveExactDeclarativeMetadata()
    {
        var expected = new[]
        {
            (ToolGroups.Archive, 600, typeof(IArchiveTool)),
            (ToolGroups.Document, 700, typeof(IDocumentTool)),
            (ToolGroups.Spreadsheet, 800, typeof(ISpreadsheetTool)),
            (ToolGroups.Pdf, 900, typeof(IPdfTool)),
        };

        foreach (var (name, order, contract) in expected)
        {
            var group = ToolCatalog.Default.FindGroup(name);
            group.Should().NotBeNull();
            group!.IsTaskWhitelistGroup.Should().BeTrue();
            group.DisplayOrder.Should().Be(order);
            var aliases = ToolMap.Definitions.Where(definition => definition.ToolGroup == name).ToArray();
            aliases.Should().NotBeEmpty();
            aliases.Should().OnlyContain(definition => definition.Activation == ToolActivationKind.Contextual);
            aliases.Should().OnlyContain(definition => contract.IsAssignableFrom(definition.DeclaringType));
        }
    }

    /// <summary>Verifies aliases, functions, Safety flags, and local-path access are exact.</summary>
    [Fact]
    public void AliasesHaveExactFunctionsSafetyAndPathAccess()
    {
        var expected = new Dictionary<string, (string Function, TaskCapability? Capability, string[] Paths)>(StringComparer.Ordinal)
        {
            ["archive.list"] = ("archive_list", null, ["archivePath:File:ReadOnly"]),
            ["archive.extract"] = ("archive_extract", TaskCapability.ArchiveWrite, ["archivePath:File:ReadOnly", "destinationPath:File:ReadWrite"]),
            ["archive.extractAll"] = ("archive_extract_all", TaskCapability.ArchiveWrite, ["archivePath:File:ReadOnly", "destinationDirectory:Directory:ReadWrite"]),
            ["archive.createZip"] = ("archive_create_zip", TaskCapability.ArchiveWrite, ["archivePath:File:ReadWrite", "sourcePath:FileOrDirectory:ReadOnly"]),
            ["archive.addToZip"] = ("archive_add_to_zip", TaskCapability.ArchiveWrite, ["archivePath:File:ReadWrite", "sourcePath:FileOrDirectory:ReadOnly"]),
            ["document.readDocx"] = ("document_read_docx", null, ["documentPath:File:ReadOnly"]),
            ["spreadsheet.listSheets"] = ("spreadsheet_list_sheets", null, ["workbookPath:File:ReadOnly"]),
            ["spreadsheet.readRange"] = ("spreadsheet_read_range", null, ["workbookPath:File:ReadOnly"]),
            ["pdf.getInfo"] = ("pdf_get_info", null, ["pdfPath:File:ReadOnly"]),
            ["pdf.readText"] = ("pdf_read_text", null, ["pdfPath:File:ReadOnly"]),
        };

        var actual = ToolMap.Definitions.Where(definition =>
            definition.ToolGroup is ToolGroups.Archive or ToolGroups.Document or ToolGroups.Spreadsheet or ToolGroups.Pdf).ToArray();
        actual.Select(definition => definition.Alias).Should().BeEquivalentTo(expected.Keys);
        foreach (var definition in actual)
        {
            var contract = expected[definition.Alias];
            definition.AgentFunctionName.Should().Be(contract.Function);
            definition.RequiredCapability.Should().Be(contract.Capability);
            definition.LocalPathArguments.Select(path => $"{path.ArgumentName}:{path.TargetKind}:{path.RequiredAccess}")
                .Should().Equal(contract.Paths);
        }
    }
}
