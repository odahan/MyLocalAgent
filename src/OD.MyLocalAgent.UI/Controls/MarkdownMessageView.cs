using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfTable = System.Windows.Documents.Table;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdownTableRow = Markdig.Extensions.Tables.TableRow;

namespace OD.MyLocalAgent.UI.Controls;

/// <summary>
/// Displays selectable Markdown as native WPF flow content or selectable source text.
/// </summary>
public sealed class MarkdownMessageView : RichTextBox
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>Identifies the <see cref="Markdown"/> dependency property.</summary>
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownMessageView),
        new FrameworkPropertyMetadata(string.Empty, OnContentChanged));

    /// <summary>Identifies the <see cref="IsRendered"/> dependency property.</summary>
    public static readonly DependencyProperty IsRenderedProperty = DependencyProperty.Register(
        nameof(IsRendered),
        typeof(bool),
        typeof(MarkdownMessageView),
        new FrameworkPropertyMetadata(false, OnContentChanged));

    /// <summary>Initializes a selectable Markdown message view.</summary>
    public MarkdownMessageView()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        IsDocumentEnabled = true;
        AcceptsReturn = true;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "OnSurfaceBrush");
        SetResourceReference(CaretBrushProperty, "OnSurfaceBrush");
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        ContextMenu = CreateContextMenu();
        RebuildDocument();
    }

    /// <summary>Gets or sets the Markdown source.</summary>
    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Gets or sets whether Markdown formatting is rendered.</summary>
    public bool IsRendered
    {
        get => (bool)GetValue(IsRenderedProperty);
        set => SetValue(IsRenderedProperty, value);
    }

    private static void OnContentChanged(DependencyObject source, DependencyPropertyChangedEventArgs args) =>
        ((MarkdownMessageView)source).RebuildDocument();

    private void RebuildDocument()
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = FontFamily,
            FontSize = FontSize,
            LineHeight = FontSize * 1.5,
        };

        if (IsRendered)
        {
            RenderBlocks(Markdig.Markdown.Parse(Markdown ?? string.Empty, Pipeline), document.Blocks);
        }
        else
        {
            document.Blocks.Add(new Paragraph(new Run(Markdown ?? string.Empty))
            {
                Margin = new Thickness(0),
            });
        }

        Document = document;
    }

    private static void RenderBlocks(ContainerBlock source, BlockCollection target)
    {
        foreach (var block in source)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    target.Add(CreateHeading(heading));
                    break;
                case ParagraphBlock paragraph:
                    target.Add(CreateParagraph(paragraph.Inline));
                    break;
                case FencedCodeBlock fencedCode:
                    target.Add(CreateCodeBlock(fencedCode.Lines.ToString()));
                    break;
                case CodeBlock code:
                    target.Add(CreateCodeBlock(code.Lines.ToString()));
                    break;
                case ListBlock list:
                    target.Add(CreateList(list));
                    break;
                case QuoteBlock quote:
                    var section = new Section
                    {
                        Margin = new Thickness(14, 4, 0, 8),
                        FontStyle = FontStyles.Italic,
                    };
                    RenderBlocks(quote, section.Blocks);
                    target.Add(section);
                    break;
                case MarkdownTable table:
                    target.Add(CreateTable(table));
                    break;
                case ThematicBreakBlock:
                    target.Add(new BlockUIContainer(new Separator())
                    {
                        Margin = new Thickness(0, 8, 0, 8),
                    });
                    break;
                case ContainerBlock container:
                    RenderBlocks(container, target);
                    break;
            }
        }
    }

    private static Paragraph CreateHeading(HeadingBlock heading)
    {
        var paragraph = CreateParagraph(heading.Inline);
        paragraph.FontWeight = FontWeights.SemiBold;
        paragraph.FontSize = heading.Level switch
        {
            1 => 24,
            2 => 20,
            3 => 17,
            _ => 15,
        };
        paragraph.Margin = new Thickness(0, heading.Level <= 2 ? 12 : 8, 0, 5);
        return paragraph;
    }

    private static Paragraph CreateParagraph(ContainerInline? inline)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 8),
        };
        RenderInlines(inline, paragraph.Inlines);
        return paragraph;
    }

    private static Paragraph CreateCodeBlock(string code)
    {
        var paragraph = new Paragraph(new Run(code.TrimEnd('\r', '\n')))
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Margin = new Thickness(0, 4, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
        };
        paragraph.SetResourceReference(TextElement.BackgroundProperty, "SurfaceBrush");
        return paragraph;
    }

    private static System.Windows.Documents.List CreateList(ListBlock source)
    {
        var list = new System.Windows.Documents.List
        {
            MarkerStyle = source.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(20, 0, 0, 8),
            Padding = new Thickness(0),
        };

        foreach (var itemBlock in source.OfType<ListItemBlock>())
        {
            var item = new ListItem { Margin = new Thickness(0, 0, 0, 3) };
            RenderBlocks(itemBlock, item.Blocks);
            list.ListItems.Add(item);
        }

        return list;
    }

    private static WpfTable CreateTable(MarkdownTable source)
    {
        var table = new WpfTable
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 4, 0, 10),
        };
        var rows = source.OfType<MarkdownTableRow>().ToArray();
        var columnCount = rows.Select(static row => row.Count).DefaultIfEmpty(0).Max();
        for (var index = 0; index < columnCount; index++)
        {
            table.Columns.Add(new TableColumn());
        }

        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);
        foreach (var sourceRow in rows)
        {
            var row = new System.Windows.Documents.TableRow
            {
                FontWeight = sourceRow.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
            };
            rowGroup.Rows.Add(row);
            foreach (var sourceCell in sourceRow.OfType<MarkdownTableCell>())
            {
                var cell = new System.Windows.Documents.TableCell
                {
                    Padding = new Thickness(7, 5, 7, 5),
                    BorderThickness = new Thickness(0.5),
                };
                cell.SetResourceReference(System.Windows.Documents.TableCell.BorderBrushProperty, "DividerBrush");
                RenderBlocks(sourceCell, cell.Blocks);
                row.Cells.Add(cell);
            }
        }

        return table;
    }

    private static void RenderInlines(ContainerInline? source, InlineCollection target)
    {
        for (var inline = source?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case CodeInline code:
                    var codeRun = new Run(code.Content) { FontFamily = new FontFamily("Consolas") };
                    codeRun.SetResourceReference(TextElement.BackgroundProperty, "SurfaceBrush");
                    target.Add(codeRun);
                    break;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        target.Add(new LineBreak());
                    }
                    else
                    {
                        target.Add(new Run(" "));
                    }
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~')
                    {
                        span.TextDecorations = TextDecorations.Strikethrough;
                    }
                    else if (emphasis.DelimiterCount >= 2)
                    {
                        span.FontWeight = FontWeights.Bold;
                    }
                    else
                    {
                        span.FontStyle = FontStyles.Italic;
                    }
                    RenderInlines(emphasis, span.Inlines);
                    target.Add(span);
                    break;
                case TaskList task:
                    target.Add(new Run(task.Checked ? "☑ " : "☐ "));
                    break;
                case LinkInline link when !link.IsImage:
                    var hyperlink = new Hyperlink();
                    RenderInlines(link, hyperlink.Inlines);
                    if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) &&
                        uri.Scheme is "http" or "https")
                    {
                        hyperlink.NavigateUri = uri;
                        hyperlink.RequestNavigate += OnRequestNavigate;
                    }
                    target.Add(hyperlink);
                    break;
                case LinkInline image:
                    target.Add(new Run(image.Url ?? "Image"));
                    break;
                case ContainerInline container:
                    RenderInlines(container, target);
                    break;
            }
        }
    }

    private static void OnRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private static ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = "Copy selection",
            Command = ApplicationCommands.Copy,
        });
        menu.Opened += (_, _) =>
        {
            if (menu.Items[0] is MenuItem copy)
            {
                copy.CommandTarget = menu.PlacementTarget as IInputElement;
            }
        };
        return menu;
    }
}
