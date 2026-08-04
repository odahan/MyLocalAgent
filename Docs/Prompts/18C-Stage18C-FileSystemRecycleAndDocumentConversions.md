# STAGE 18C — File-system Recycle Bin and document conversions

> Run only after **Stage 18A**. This stage extends the existing `filesystem` group with one
> recoverable destructive operation and two deterministic local document conversions. It uses
> the Stage 18A compiled catalog foundation and adds no tool group, page, task field, migration,
> provider call, model call, connector, external service, or runtime plug-in system.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` in full, including §14, §20, §21A, and §21C.
2. `Docs/ARCHITECTURE-LEDGER.md` in full.
3. The post-Stage-18A implementations of `ToolMap`, `ToolDefinition`, `ToolGroups`,
   `LocalPathArgumentDefinition`, `WorkflowRequirementAnalyzer`, `ToolRegistry`,
   `FileSystemTool`, `IFileSystemTool`, `SafetyOptions`, shared DI, Settings persistence and WPF,
   Tool catalog, Tool tester, AI Workflow Helper, tests, README, and both user manuals.
4. The existing restricted Markdig renderer and `HttpContentProjector` HTML extraction rules.

## 2. Mission

Add exactly these aliases to the existing `filesystem` group:

- `filesystem.moveToBin(path)`;
- `filesystem.markdownToHtml(sourcePath, destinationPath?, theme?)`;
- `filesystem.htmlToText(sourcePath, format?)`.

All three aliases must flow through the Stage 18A metadata-driven Tool catalog, Tool tester,
Task editor whitelist, AI Workflow Helper, workflow validation, and agent exposure. Do not add a
parallel alias list, WPF checkbox, group-order table, or group-specific catalog branch.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S18C-D1 | **Existing group.** All three aliases belong to `filesystem`; Stage 18C adds no group or task JSON field. |
| S18C-D2 | **Dedicated recoverable-action gate.** `filesystem.moveToBin` requires effective ReadWrite path access and `Safety:AllowMoveToBin=true`, shipped false. It does not reuse `AllowFileDelete`. |
| S18C-D3 | **Files and directories.** Move to Bin accepts one existing file or directory and uses the Windows Recycle Bin without confirmation, progress, or error UI. It never performs permanent-delete fallback. |
| S18C-D4 | **Testable shell boundary.** Core owns `IRecycleBinService`; production uses a Windows implementation and tests use a fake. Automated tests never move user files into the real Recycle Bin. |
| S18C-D5 | **Markdown source and HTML destination.** Markdown conversion reads `sourcePath` and writes `destinationPath`. Omitting the destination replaces the source extension with `.html`. Source and destination must differ. |
| S18C-D6 | **Theme enum.** `theme` is `Dark` or `Light`, defaults to `Dark`, and selects complete standalone CSS rather than the live WPF theme. |
| S18C-D7 | **Restricted standalone HTML.** Raw Markdown HTML is disabled. Output is UTF-8, contains a restrictive CSP, embeds CSS only, performs no network request, and loads no script, image, font, frame, media, or remote resource. |
| S18C-D8 | **HTML extraction return value.** `htmlToText` reads one HTML file and returns compact JSON containing `sourcePath`, effective `format`, and `content`. It does not write a `.txt` or `.md` file implicitly; workflows may chain `filesystem.write`. |
| S18C-D9 | **Closed extraction formats.** `format` is `Text` or `Markdown`, defaults to `Text`, and is case-insensitively bound through the existing Tool Map enum path. |
| S18C-D10 | **Principal Markdown structure.** Markdown extraction preserves headings, paragraphs, ordered/unordered lists, bold, italics, links, block quotes, inline code, fenced code, and line breaks where present. Text extraction normalizes readable text and block boundaries. |
| S18C-D11 | **No active HTML.** Both extraction modes remove scripts, styles, noscript, SVG, canvas, forms, templates, and iframes and never execute HTML, CSS, JavaScript, links, or embedded resources. |
| S18C-D12 | **Existing read cap.** Both conversion sources are UTF-8 files subject to `FileSystem:MaxReadBytes`. Markdown HTML output uses the existing UTF-8 write semantics. |
| S18C-D13 | **Authorization metadata.** HTML extraction is ReadOnly. Markdown conversion independently requires ReadOnly for its source and ReadWrite for its explicit or derived destination. Dynamic destinations produce the existing unresolved-path warning and never an invented grant. |
| S18C-D14 | **Local content boundary unchanged.** Conversion is deterministic and local. Source and output remain outside Guardrails classification; filesystem authorization, the task whitelist, and the dedicated Safety gate remain the hard boundaries. |
| S18C-D15 | **Privacy.** Logs contain alias and sanitized path/format/theme metadata only. They never contain source Markdown, source HTML, extracted text, or generated HTML. |

## 4. Tool Map contract

| Alias | Agent function | Implementation | Ordered arguments | Optional | Path / Safety |
|---|---|---|---|---|---|
| `filesystem.moveToBin` | `filesystem_move_to_bin` | `FileSystemTool.MoveToBinAsync` | `path` | — | ReadWrite / `Safety:AllowMoveToBin` |
| `filesystem.markdownToHtml` | `filesystem_markdown_to_html` | `FileSystemTool.MarkdownToHtmlAsync` | `sourcePath`, `destinationPath`, `theme` | last two | source ReadOnly; destination ReadWrite |
| `filesystem.htmlToText` | `filesystem_html_to_text` | `FileSystemTool.HtmlToTextAsync` | `sourcePath`, `format` | `format` | ReadOnly |

Tool descriptions, argument examples, optionality, enum allowed values, local-path semantics, and
Safety metadata live in `ToolMap`. Reflection completeness and Stage 18A bidirectional group tests
remain authoritative.

## 5. Runtime behavior

### 5.1 Move to Bin

The tool checks `Safety:AllowMoveToBin` before resolving or touching the target. After the gate,
the sandbox resolves `path` with ReadWrite access. A missing target fails. The production recycle
service asks Windows Shell to move the target to the Recycle Bin with undo enabled and all UI
suppressed. A shell failure is surfaced; permanent deletion is forbidden. Success returns:

```json
{"path":"inbox/obsolete.txt","movedToBin":true}
```

### 5.2 Markdown to HTML

The source is resolved ReadOnly and checked against the existing byte cap. The effective
destination is the explicit value or `Path.ChangeExtension(sourcePath, ".html")`, then is resolved
independently with ReadWrite access. Parent creation follows `filesystem.write`. Repeated runs may
replace the destination. A source/destination identity is rejected.

Markdig uses advanced extensions with raw HTML disabled. The complete document declares UTF-8,
the selected color scheme, embedded theme CSS, and the restrictive CSP. Success returns source,
effective destination, theme, and UTF-8 byte count; it does not return the generated HTML.

### 5.3 HTML to text

HtmlAgilityPack parses in memory. Extraction prefers `main`, then `article`, then `body`, then the
document root. Active/non-content nodes are removed before traversal. Entity decoding and
whitespace normalization are deterministic. Success returns the extracted content in compact JSON.
The operation performs no write and no network access.

## 6. Settings and WPF

Add `AllowMoveToBin` to `SafetyOptions.IsEnabled`, shipped `appsettings.json`, user-settings
persistence, Settings dirty/load/save state, Task editor Safety snapshots, and the Settings Safety
card. The English UI label explains that it permits `filesystem.moveToBin` for authorized files
and directories.

No dedicated WPF Tool catalog update is allowed: the three aliases must appear because the
catalog consumes `ToolMap.Definitions`. The existing File system group remains the single explicit
task opt-in.

## 7. Required automated tests

Add deterministic coverage for at least:

1. Move to Bin blocked by its dedicated disabled Safety gate;
2. ReadWrite authorization required before recycle dispatch;
3. fake recycle service receives the resolved path and production shell is not called by tests;
4. missing file/directory behavior and compact success JSON;
5. Markdown default derived destination and default Dark theme;
6. explicit destination and Light theme;
7. raw Markdown HTML escaped/disabled and restrictive CSP emitted;
8. independent source/destination authorization and identical-path rejection;
9. conversion source byte cap;
10. HTML Text default and script/style removal;
11. HTML Markdown headings, lists, emphasis, links, quotes, and code;
12. Tool Map reflection, optionality, enums, local-path metadata, Safety analysis, registry binding,
    Tool catalog visibility, Tool tester visibility, and existing group order;
13. `AllowMoveToBin` settings load/save/default behavior;
14. existing filesystem, whitelist, helper, catalog, serialization, and privacy regressions.

## 8. Documentation

Update the shared specification, architecture ledger, prompt index, README tool/safety tables,
acceptance report, and both standalone user manuals in structural FR/EN parity. Manuals document
the exact arguments, defaults, authorization, Safety gate, return/write behavior, and chaining
`htmlToText` with `filesystem.write`. Documentation must not claim Guardrails classification,
cross-platform recycle behavior, HTML execution, network loading, or automatic `.txt`/`.md` writes.

## 9. Stage gate

Run and record:

1. `dotnet restore OD.MyLocalAgent.sln -m:1`
2. `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1`
3. `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1`
4. validate every sample JSON;
5. validate both standalone manuals structurally and verify semantic FR/EN Stage 18C parity;
6. confirm Tool catalog/tester derive the aliases from Tool Map metadata with no handwritten list;
7. `git diff --check`.

Build must have zero warnings and errors. Tests must pass; the existing local Ollama probe may
remain skipped. No real Recycle Bin mutation, live provider, connector, model, scheduled task, or
filesystem outside isolated test roots is required.

## 10. Acceptance addendum

- [ ] Exactly three aliases were added to the existing `filesystem` group.
- [ ] `Safety:AllowMoveToBin` is independent, persisted, visible in Settings, and false by default.
- [ ] Move to Bin handles files/directories through a testable, UI-free Windows boundary.
- [ ] Markdown conversion independently enforces source read and destination write access.
- [ ] Dark is the default HTML theme; Light is explicit.
- [ ] HTML output is standalone and restricted; raw Markdown HTML is disabled.
- [ ] HTML extraction defaults to Text and optionally preserves principal Markdown structure.
- [ ] Source content never enters logs, incidents, or Guardrails through Stage 18C.
- [ ] Tool catalog, Tool tester, helper, whitelist, and authorization proposals derive from metadata.
- [ ] Shared spec, ledger, README, prompt index, acceptance report, and EN/FR manuals agree.
- [ ] Complete Release gate is green with `-m:1` on every dotnet command.
