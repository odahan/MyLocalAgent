# STAGE 22 — Archive, Office, and PDF Tools

> Run only after **Stage 21** is finished, including the **Stage 18A** foundation. Stage 17 V2 remains the implementation contract for
> Tool Map metadata and workflow authoring, Stage 19 remains the contract for contextual local-file
> tools, and Stages 20–21 remain the placeholder contracts. This stage adds four task-whitelist
> groups for bounded local archive, DOCX, spreadsheet, and PDF operations. It does not add OCR,
> Office automation, COM interop, a document editor, arbitrary archive conversion, encrypted-file
> passwords, or a general binary-file parser.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§24. This
   standalone prompt is the complete Stage 22 implementation contract and must be reflected in a
   concise new shared-specification addendum during implementation.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read. Recorded package, Tool Map, task serialization,
   file-system sandbox, per-task authorization, reparse-point, Safety, Guardrails, privacy,
   cancellation, active-theme, and documentation decisions are binding.
3. The post-Stage-21 implementations of `ToolMap`, `ToolDefinition`, `ToolArgumentDefinition`,
   `LocalPathArgumentDefinition`, `WorkflowArgumentKind`, `ToolGroupDefinition`, `ToolGroups`,
   `ToolGroupSelectionItem`, `ToolRegistry`,
   `ToolExecutionContext`, `IContextualToolFactory<TTool>`, `IPathSandbox`, `PathSandbox`,
   `WorkflowRequirementAnalyzer`, `WorkflowSnippetBuilder`, task creation/editing and validation,
   Settings persistence, Tool tester, AI Playground, AI Workflow Helper, Tool catalog, Runner,
   logging, incidents, tests, publishing, root README, and both standalone user manuals.
4. The official documentation and release notes for SharpCompress 0.50.1,
   DocumentFormat.OpenXml 3.5.1, ExcelDataReader 3.9.0, and PdfPig 0.1.15. Verify their actual
   restored APIs and transitive dependency graphs before coding; do not implement from memory.

## 2. Mission

Add safe, bounded tools that let an agent or deterministic workflow:

- inspect and extract common local archive formats;
- create a ZIP and add a local file or directory tree to a ZIP;
- extract readable text from `.docx` files;
- inspect and read bounded ranges from `.xls`, `.xlsx`, and `.xlsb` workbooks;
- inspect and extract readable text from `.pdf` files.

The exact new whitelist groups are `archive`, `document`, `spreadsheet`, and `pdf`. They appear as
independent, unchecked choices whenever a new task is created and in the existing task editor. A
task receives none of these capabilities unless the corresponding group is explicitly saved in its
`tools` array. Every local path remains independently subject to the Stage 11 sandbox and per-task
ReadOnly/ReadWrite authorization rules.

Archive mutations are deliberately limited to ZIP. Common archive formats may be listed and
extracted, but Stage 22 does not update, create, convert, or recompress RAR, 7z, TAR, or compressed
TAR variants. Office and PDF operations are deliberately read-only even though the selected SDKs
contain some write APIs. Package capability does not automatically become product capability.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S22-D1 | **Four independent whitelist groups.** Add exactly `archive`, `document`, `spreadsheet`, and `pdf` as task-whitelist entries in the Stage 18A central catalog. `ToolGroups.All` and every catalog-driven UI projection derive them automatically. They are visible and unchecked by default in new-task creation and remain independently selectable when editing a task. |
| S22-D2 | **One authoritative Tool Map.** Every new alias, argument, example, path access, Safety flag, function name, description, snippet, helper projection, and catalog entry comes from the existing Core Tool Map. No UI-only or workflow-only alias table is allowed. |
| S22-D3 | **Permissively licensed packages.** Pin SharpCompress 0.50.1, DocumentFormat.OpenXml 3.5.1, ExcelDataReader 3.9.0, and PdfPig 0.1.15 centrally and reference them from Core only. Record the resolved package and license inventory in the architecture ledger. |
| S22-D4 | **No commercially constrained document stack.** Do not add NPOI, EPPlus, iText, Aspose, Syncfusion, GemBox, Office Interop, LibreOffice automation, a command-line archiver, or a second library for the same format. NPOI is specifically excluded because its current binary distribution carries a revenue-related maintenance EULA despite older Apache-2.0 expectations. |
| S22-D5 | **Stable releases only.** Do not replace PdfPig 0.1.15 with a newer alpha/nightly build. The shared stable-first package policy applies even when a prerelease has a later date. |
| S22-D6 | **Read allowlist for archives.** Archive list/extract operations accept only ZIP, 7z, RAR, TAR, TAR.GZ/TGZ, TAR.BZ2/TBZ2, TAR.XZ/TXZ, TAR.ZST/TZST, GZIP, BZIP2, XZ, and Zstandard inputs that SharpCompress 0.50.1 actually opens in the restored build. Unsupported or ambiguous formats fail closed. |
| S22-D7 | **ZIP-only writes.** `archive.createZip` and `archive.addToZip` produce or replace only `.zip` files. No alias writes any other archive format, deletes an entry, renames an entry, or edits an archive in place. |
| S22-D8 | **Archive write Safety gate.** Every archive operation that creates filesystem content — extract one, extract all, create ZIP, or add to ZIP — requires `Safety:AllowArchiveWrite`, shipped `false`, in addition to the task group and path authorization. Listing never requires this flag. |
| S22-D9 | **Explicit overwrite.** The default is never overwrite. `extract`, `createZip`, and `addToZip` reject an existing destination or entry unless their explicit overwrite argument is `true`. An overwrite still requires the archive Safety flag and ReadWrite authorization. |
| S22-D10 | **No partial archive replacement.** ZIP create/add builds a complete temporary sibling, validates it, flushes it, and atomically moves or replaces the destination. It never updates the live ZIP in place. A failure or cancellation preserves the prior archive and cleans up only temporary files owned by the call. |
| S22-D11 | **Zip Slip and link defense.** Archive entry names are untrusted. Absolute, rooted, drive-relative, UNC, device, alternate-data-stream, traversal, empty-segment, trailing-dot/space, reserved Windows-device, and escaping paths are rejected. Symbolic links, hard links, junction-like entries, devices, FIFOs, and other non-regular entries are never materialized. |
| S22-D12 | **Archive-bomb limits are enforced while streaming.** Metadata preflight is necessary but never sufficient. Entry count, compressed input size, per-entry output, total output, and effective compression ratio are counted during extraction and abort before a configured limit is exceeded. |
| S22-D13 | **No archive passwords.** Password-protected or encrypted archive entries are rejected with a typed, content-free error. No password argument, setting, secret-store key, prompt value, or interactive password dialog is introduced. |
| S22-D14 | **Extraction is non-recursive.** An archive contained inside another archive is extracted as an ordinary file only. The tool never automatically opens or recursively expands nested archives. |
| S22-D15 | **DOCX only for Word documents.** `document.readDocx` accepts `.docx` only. Legacy `.doc`, macro-enabled `.docm`, templates, RTF, ODT, embedded packages, and encrypted documents are out of scope. |
| S22-D16 | **Read-only Office and PDF surface.** No public tool creates, updates, signs, fills, converts, repairs, or saves a Word, spreadsheet, or PDF file. Open every supported Office package in read-only mode and never call a package save API. |
| S22-D17 | **Excel read formats.** Spreadsheet aliases accept `.xls`, `.xlsx`, and `.xlsb`. Use ExcelDataReader's low-level streaming reader; do not add `ExcelDataReader.DataSet`, materialize an entire workbook, or evaluate formulas. |
| S22-D18 | **Stored spreadsheet values.** Return the value stored or cached in the workbook. Do not run formulas, macros, external links, data connections, Power Query, pivot refresh, or volatile recalculation. Formula text is not exposed in Stage 22. |
| S22-D19 | **PDF text, not OCR.** PdfPig uses layout-aware text extraction; do not use `Page.Text` as the user-facing extraction algorithm. Image-only pages return an explicit no-extractable-text marker. Stage 22 adds no OCR, rasterizer, JavaScript execution, attachment extraction, or external-link fetch. |
| S22-D20 | **No PDF passwords.** Encrypted or password-protected PDFs are rejected. No password enters a tool argument, log, incident, setting, or secret store. |
| S22-D21 | **Bounded, structured output.** All list/read results are compact JSON with deterministic order, explicit counts and truncation metadata, and one program-owned `untrustedContent` marker. Text and cell data never enter exception messages, normal logs, incidents, or telemetry. |
| S22-D22 | **Existing Guardrails contract is unchanged.** Stage 22 does not add a new per-task Guardrails field or silently summarize local documents. The existing agent system instruction continues to treat file/tool content as untrusted data. Limits are applied before content enters agent or workflow context. |
| S22-D23 | **Context-bound local tools.** New tool instances are created through the existing contextual factory so path grants and task identity remain immutable per execution. No singleton current-task, current-path, open archive, open workbook, or open PDF state is introduced. |
| S22-D24 | **One validated configuration section.** Add one `FileContent` settings section for archive and document limits, with one matching Settings UI section. Do not scatter related limits across FileSystem, Guardrails, or provider settings. |
| S22-D25 | **Content-free observability.** Logs may contain alias, format, counts, byte totals, truncation flags, elapsed time, and outcome category. They never contain entry names, document text, cell values, sheet names, PDF metadata values, passwords, absolute paths, temporary names, or raw parser exceptions. |
| S22-D26 | **English implementation, bilingual guidance.** Code identifiers, XML documentation, UI labels, errors, Tool Map descriptions, and technical documentation are English. The standalone user guides explain the same behavior and examples in French and English. |

## 4. Critical constraints

- R1, R2, R4, R5, R7, R8, R9, R10 and all post-v1 shared addenda remain binding. Every public
  API has English XML documentation; complete files are warning-clean and maintainable.
- File extension is not trusted as format detection. Require an allowlisted extension and verify
  the detected container/signature agrees before reading. A renamed executable, Office package,
  or unsupported polyglot must not become an archive merely because SharpCompress can probe it.
- Every input and output path is resolved separately with its declared access. Read access to an
  archive never implies write access to its destination; write access to a ZIP never implies read
  access to an arbitrary source.
- Reparse-point and containment validation must occur at resolution and again immediately before
  every commit. A preflight-only path check is insufficient against directory swaps.
- Parser work may be synchronous internally. Run it away from the WPF dispatcher, check caller
  cancellation between bounded units, dispose every stream/package/reader, and never claim native
  cancellation that a package does not provide.
- Do not expose package exceptions directly. Convert expected corrupt/unsupported/limit failures to
  typed, sanitized tool errors. Unexpected exceptions follow the incident pipeline without raw
  content or paths.
- Every `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` command in this stage
  explicitly uses `-m:1`.

## 5. Package and configuration contract

### 5.1 Central package versions

Add exactly these direct central versions:

```xml
<PackageVersion Include="DocumentFormat.OpenXml" Version="3.5.1" />
<PackageVersion Include="ExcelDataReader" Version="3.9.0" />
<PackageVersion Include="PdfPig" Version="0.1.15" />
<PackageVersion Include="SharpCompress" Version="0.50.1" />
```

Add one Core package reference for each. Do not reference these packages from UI, Runner,
Workflows, or Tests directly; those projects consume Core abstractions. Do not add
`ExcelDataReader.DataSet`.

The chosen packages are compatible with net10 and have permissive licenses at specification time:

| Package | Product use | License expectation | Reason for selection |
|---|---|---|---|
| SharpCompress 0.50.1 | Common archive read/extract and ZIP creation | MIT | Managed implementation, net10 asset, async-capable APIs, broad common-format read support, no external executable. |
| DocumentFormat.OpenXml 3.5.1 | DOCX package traversal | MIT | Official .NET Foundation/Microsoft Open XML SDK and read-only package access. |
| ExcelDataReader 3.9.0 | Streaming XLS/XLSX/XLSB values | MIT | Small read-focused API with legacy BIFF support and no Office installation. |
| PdfPig 0.1.15 | PDF metadata and layout-aware text extraction | Apache-2.0 | Read-focused PDF parsing with document-layout extraction and no commercial runtime. |

At implementation time, run restore, inspect the lock/assets files, verify exact direct/transitive
licenses and known vulnerability results, and record them in the ledger. If a listed version is no
longer obtainable or has a newly published vulnerability, choose the closest newer stable,
permissively licensed compatible version, compile against its real API, and record the deviation.
Do not silently substitute a commercially constrained package.

Legacy BIFF code pages require `CodePagesEncodingProvider`. Register it once during Core bootstrap
before ExcelDataReader is used. Add a direct `System.Text.Encoding.CodePages` package only if the
restored net10 compile/runtime requires it; if added, pin the matching stable .NET 10 servicing
version centrally and record why it is direct rather than transitive.

### 5.2 FileContentOptions

Add one validated Core options class equivalent to:

```csharp
public sealed class FileContentOptions
{
    public const string SectionName = "FileContent";

    public long MaxInputFileBytes { get; set; } = 100 * 1024 * 1024;
    public int MaxArchiveEntries { get; set; } = 10_000;
    public long MaxArchiveEntryBytes { get; set; } = 250 * 1024 * 1024;
    public long MaxArchiveTotalBytes { get; set; } = 1024L * 1024 * 1024;
    public int MaxArchiveCompressionRatio { get; set; } = 200;
    public int MaxListedEntries { get; set; } = 1_000;
    public int MaxDocumentCharacters { get; set; } = 64_000;
    public int MaxSpreadsheetRows { get; set; } = 1_000;
    public int MaxSpreadsheetColumns { get; set; } = 256;
    public int MaxSpreadsheetCells { get; set; } = 25_000;
    public int MaxPdfPages { get; set; } = 100;
}
```

Shipped `appsettings.json` gains:

```json
"FileContent": {
  "MaxInputFileBytes": 104857600,
  "MaxArchiveEntries": 10000,
  "MaxArchiveEntryBytes": 262144000,
  "MaxArchiveTotalBytes": 1073741824,
  "MaxArchiveCompressionRatio": 200,
  "MaxListedEntries": 1000,
  "MaxDocumentCharacters": 64000,
  "MaxSpreadsheetRows": 1000,
  "MaxSpreadsheetColumns": 256,
  "MaxSpreadsheetCells": 25000,
  "MaxPdfPages": 100
}
```

Validate on startup and before Settings persistence:

- input file bytes: 1 MiB–2 GiB;
- archive entries: 1–100,000;
- per-entry bytes: 1 MiB–4 GiB;
- total extracted bytes: 1 MiB–16 GiB and not less than per-entry bytes;
- compression ratio: 1–10,000;
- listed entries: 1–10,000 and not greater than archive entries;
- document characters: 4,096–1,000,000;
- spreadsheet rows: 1–100,000;
- spreadsheet columns: 1–16,384;
- spreadsheet cells: 1–1,000,000 and not greater than rows multiplied by columns using checked
  arithmetic;
- PDF pages: 1–10,000.

These maxima are authorization ceilings. Caller-supplied smaller limits are allowed where defined;
zero and negative values are rejected rather than treated as unlimited.

### 5.3 Safety and settings persistence

Add `bool AllowArchiveWrite` to `SafetyOptions`, default `false`, bound to
`Safety:AllowArchiveWrite`. Extend user-settings DTOs, validation, atomic persistence, monitored
reload, dirty tracking, Settings UI, and tests for the Safety value and every `FileContent` value.
Saving Settings preserves unknown and unrelated sections and never mutates task JSON.

The Settings UI places all File content limits and **Allow archive writes and extraction** in one
section. The Safety control explains that task whitelisting and ReadWrite authorized locations are
still required. It must not imply that enabling the switch grants paths or adds a task tool group.

## 6. Tool groups, Tool Map, and public contracts

### 6.1 Groups and task whitelist

Add:

```csharp
public const string Archive = "archive";
public const string Document = "document";
public const string Spreadsheet = "spreadsheet";
public const string Pdf = "pdf";
```

Add all four as task-whitelist `ToolGroupDefinition` entries. Preserve the established display
order and insert the new groups after SQLite: gmail, calendar, dropbox, filesystem, sqlite, archive,
document, spreadsheet, pdf, http, web, then the non-whitelist `ai` authoring group. Do not maintain
a second UI, tester, or workflow-catalog ordering table. Update every hard-coded count or phrase
such as “seven tool groups.”

New-task creation presents four independent unchecked checkboxes. Loading a pre-Stage-22 task adds
nothing to its saved `tools` array. Saving a task retains only explicitly selected groups and the
existing validator rejects unknown groups as before.

### 6.2 Normative aliases

| Alias | Agent function | Ordered arguments | Optional | Path access | Safety |
|---|---|---|---|---|---|
| `archive.list` | `archive_list` | `archivePath`, `maxEntries` | `maxEntries` | archive ReadOnly | — |
| `archive.extract` | `archive_extract` | `archivePath`, `entryPath`, `destinationPath`, `overwrite` | `overwrite` | archive ReadOnly; destination ReadWrite | `AllowArchiveWrite` |
| `archive.extractAll` | `archive_extract_all` | `archivePath`, `destinationDirectory` | — | archive ReadOnly; destination ReadWrite | `AllowArchiveWrite` |
| `archive.createZip` | `archive_create_zip` | `archivePath`, `sourcePath`, `entryPath`, `overwriteArchive` | `entryPath`, `overwriteArchive` | source ReadOnly; archive ReadWrite | `AllowArchiveWrite` |
| `archive.addToZip` | `archive_add_to_zip` | `archivePath`, `sourcePath`, `entryPath`, `overwriteEntry` | `entryPath`, `overwriteEntry` | archive ReadWrite; source ReadOnly | `AllowArchiveWrite` |
| `document.readDocx` | `document_read_docx` | `documentPath`, `maxCharacters` | `maxCharacters` | ReadOnly | — |
| `spreadsheet.listSheets` | `spreadsheet_list_sheets` | `workbookPath` | — | ReadOnly | — |
| `spreadsheet.readRange` | `spreadsheet_read_range` | `workbookPath`, `sheetName`, `startRow`, `startColumn`, `maxRows`, `maxColumns` | last five | ReadOnly | — |
| `pdf.getInfo` | `pdf_get_info` | `pdfPath` | — | ReadOnly | — |
| `pdf.readText` | `pdf_read_text` | `pdfPath`, `startPage`, `maxPages`, `maxCharacters` | last three | ReadOnly | — |

All numeric arguments use `WorkflowArgumentKind.Integer`; paths, entry/sheet names, and optional
values use `String`; overwrite flags use `Boolean`. Cancellation tokens are excluded from Tool Map
metadata. Use the same ordered signatures for public interfaces, concrete methods, Tool Map
metadata, `DescriptionAttribute`, agent functions, workflow validation, snippets, helper prompts,
and tests.

### 6.3 Public interfaces

Add public interfaces and sealed contextual implementations with signatures equivalent to:

```csharp
public interface IArchiveTool
{
    Task<string> ListAsync(
        string archivePath,
        int? maxEntries = null,
        CancellationToken cancellationToken = default);

    Task<string> ExtractAsync(
        string archivePath,
        string entryPath,
        string destinationPath,
        bool overwrite = false,
        CancellationToken cancellationToken = default);

    Task<string> ExtractAllAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken = default);

    Task<string> CreateZipAsync(
        string archivePath,
        string sourcePath,
        string? entryPath = null,
        bool overwriteArchive = false,
        CancellationToken cancellationToken = default);

    Task<string> AddToZipAsync(
        string archivePath,
        string sourcePath,
        string? entryPath = null,
        bool overwriteEntry = false,
        CancellationToken cancellationToken = default);
}

public interface IDocumentTool
{
    Task<string> ReadDocxAsync(
        string documentPath,
        int? maxCharacters = null,
        CancellationToken cancellationToken = default);
}

public interface ISpreadsheetTool
{
    Task<string> ListSheetsAsync(
        string workbookPath,
        CancellationToken cancellationToken = default);

    Task<string> ReadRangeAsync(
        string workbookPath,
        string? sheetName = null,
        int startRow = 1,
        int startColumn = 1,
        int? maxRows = null,
        int? maxColumns = null,
        CancellationToken cancellationToken = default);
}

public interface IPdfTool
{
    Task<string> GetInfoAsync(
        string pdfPath,
        CancellationToken cancellationToken = default);

    Task<string> ReadTextAsync(
        string pdfPath,
        int startPage = 1,
        int? maxPages = null,
        int? maxCharacters = null,
        CancellationToken cancellationToken = default);
}
```

The concrete tools may delegate parsing to internal services for focused testing, but the public
runtime surface remains the four tool interfaces. Public types and members require English XML
documentation.

### 6.4 Typed errors

Add one shared sanitized error contract or narrowly scoped typed exceptions with stable categories:

- `UnsupportedFormat`;
- `FormatMismatch`;
- `CorruptFile`;
- `EncryptedFile`;
- `EntryNotFound`;
- `UnsafeEntryPath`;
- `UnsupportedEntryType`;
- `DuplicateEntry`;
- `LimitExceeded`;
- `DestinationExists`;
- `SheetNotFound`;
- `NoExtractableText` where a successful empty result is not more appropriate;
- existing authorization and Safety exceptions remain their current types.

Agent-facing errors use program-authored English messages and stable categories only. They do not
include parser messages, paths, entry names, sheet names, cell contents, text fragments, document
metadata, or temporary file names.

## 7. Archive contract

### 7.1 Format validation

Recognize extensions case-insensitively, with longest compound extension first. The read allowlist
is `.zip`, `.7z`, `.rar`, `.tar`, `.tar.gz`, `.tgz`, `.tar.bz2`, `.tbz2`, `.tar.xz`, `.txz`,
`.tar.zst`, `.tzst`, `.gz`, `.bz2`, `.xz`, and `.zst`. Do not treat `.docx`, `.xlsx`, `.jar`,
`.nupkg`, `.epub`, or arbitrary ZIP-based containers as archive-tool inputs in Stage 22.

The implementation verifies container type from content and rejects disagreement with the declared
extension. For single-stream compression formats, synthesize at most one logical regular entry using
a safe derived leaf name; never accept a header-provided absolute or traversing output name.

RAR is read/extract only. If a supported extension maps to a feature the restored SharpCompress
build cannot safely read, remove it from both implementation and documentation and record the exact
deviation rather than routing through an external executable.

### 7.2 Canonical entry names

Tool input and JSON output use `/` as the archive separator. A canonical entry path:

- is relative and contains at least one non-empty segment;
- contains no `.` or `..` segment;
- contains no NUL, control character, `:`, backslash, drive/UNC/device prefix, or rooted form;
- contains no segment that Windows trims to a different name because of trailing spaces/dots;
- contains no case-insensitive DOS device segment such as `CON`, `PRN`, `AUX`, `NUL`, `COM1`, or
  `LPT1`, including a device name followed by an extension;
- remains within normal configured/OS path-length constraints after destination combination.

Do not Unicode-normalize names. Detect destination collisions using the actual Windows
case-insensitive semantics. If two entries map to the same destination, or a file conflicts with a
directory prefix, reject the complete operation before committing output.

### 7.3 Listing

`archive.list` opens read-only and returns entries in archive declaration order, bounded by the
smaller of `maxEntries` and `MaxListedEntries`. Each entry contains only:

- canonical `path`;
- `entryType` (`file` or `directory`);
- nullable declared compressed and uncompressed byte counts;
- nullable modified UTC timestamp when reliably available;
- `encrypted` boolean;
- top-level `format`, total/delivered entry counts, `hasMoreEntries`, and `truncated`.

An unsafe, duplicate, encrypted, or unsupported special entry is reported through content-free
aggregate counts and a safe status; its raw name is not returned. Listing never extracts payload
bytes and never mutates the archive.

### 7.4 Extract one

`archive.extract` selects exactly one canonical regular-file entry by ordinal case-sensitive path.
Directories and special entries are rejected. `destinationPath` is an explicit output file path;
its parent must already exist. The tool streams to an application-owned temporary sibling, counts
actual output bytes and ratio, flushes, revalidates destination containment/reparse state, then
moves or atomically replaces according to `overwrite`.

On cancellation or failure, remove only the temporary file owned by the call. Never truncate an
existing destination before validation completes.

### 7.5 Extract all

`archive.extractAll` requires `destinationDirectory` not to exist. Its parent directory must exist
and be ReadWrite-authorized. Preflight the complete entry graph, create one hidden/random sibling
staging directory, stream every regular entry under it, and count actual per-entry and total bytes.
Create ordinary directories only. After successful validation and flush/disposal, revalidate the
destination parent and rename the staging directory to the exact destination atomically.

If destination creation loses a race, fail without merging. On cancellation or failure, delete only
the staging directory created by the call after verifying its resolved path remains inside the
authorized parent. No partially populated final destination is allowed.

### 7.6 Create ZIP

`archive.createZip` accepts one source regular file or directory tree. The source must exist and be
ReadOnly-authorized. Directory traversal is recursive but skips nothing silently: any reparse point,
special file, inaccessible item, cycle, or limit violation rejects the call. The source itself is
never modified.

If `entryPath` is omitted, a file uses its leaf name and a directory uses its leaf name as the ZIP
root. If supplied, `entryPath` is the canonical file entry name or directory root prefix. Directory
entries use `/`; entry order is deterministic ordinal order by canonical relative path. Preserve
file bytes and a normalized available last-write UTC timestamp, but do not store Windows ACLs,
alternate streams, owner data, reparse data, or absolute source paths.

Write to a temporary sibling ZIP. Reopen it through the same validation path before commit. Reject
an existing archive unless `overwriteArchive` is true. Atomic replacement must preserve the old ZIP
if construction, validation, cancellation, or commit fails.

### 7.7 Add to ZIP

`archive.addToZip` requires an existing valid `.zip` and one ReadOnly-authorized source file or
directory tree. Build a new temporary ZIP by copying safe existing entries and appending the new
deterministically ordered entries. Do not reuse unsafe, encrypted, duplicate, or special entries
from the original; reject the operation instead.

If `entryPath` is omitted, use the same default mapping as create. Existing canonical entry
collisions fail unless `overwriteEntry` is true. For a directory source, overwrite applies to each
colliding regular file under that prefix; file/directory structural conflicts always fail. Reopen
and validate the complete replacement ZIP before atomically replacing the original.

### 7.8 Archive limits

Apply all configured limits to listing, extraction, creation, and add/rewrite as relevant. During
extraction, effective ratio is actual uncompressed bytes divided by the best available positive
compressed-byte count; a zero/unknown compressed size is treated conservatively and remains bounded
by per-entry and total-byte ceilings. Checked arithmetic is mandatory.

Source traversal for create/add obeys the entry and total-byte limits before compression. Copying
the existing ZIP during add also counts toward entry and uncompressed ceilings. No setting disables
limits.

## 8. DOCX contract

`document.readDocx` verifies `.docx` extension plus a valid WordprocessingDocument package and opens
it read-only with autosave disabled. It reads user-visible main-document body content in document
order:

- paragraphs become lines;
- tabs become `\t`, explicit breaks become `\n`, and consecutive structural blocks have one
  normalized LF separator;
- tables are emitted row by row, cells separated by tabs and rows by LF at their position in the
  body;
- hyperlink display text is included but target URLs are not fetched or separately exposed;
- inserted/current text is included and deleted move/revision text is excluded;
- field display text already stored in the package may be included, but fields are never updated;
- headers, footers, comments, footnotes/endnotes, text boxes, images, alt text, embedded packages,
  macros, custom XML, and external relationships are not traversed in Stage 22.

Normalize CRLF/CR to LF and disallowed controls to safe spacing while preserving ordinary Unicode.
Apply the smaller of `maxCharacters` and `MaxDocumentCharacters` at Unicode scalar boundaries.
Return compact JSON with `format`, `text`, `paragraphCount`, `tableCount`, original/delivered
character counts when knowable, `truncated`, `omittedParts`, and `untrustedContent: true`.

Reject packages with external/invalid relationships that the parser would resolve outside the
package. No network request occurs. An empty valid body succeeds with empty text and explicit
counts; a corrupt, encrypted, wrong-format, or macro-enabled input fails with a sanitized category.

## 9. Spreadsheet contract

### 9.1 Common behavior

Use `ExcelReaderFactory.CreateReader` over one read-only file stream after extension/signature
validation. Enable single-pass behavior where the restored API permits it without losing required
metadata. Do not call `AsDataSet()` and do not hold the whole workbook in memory.

Workbooks are read-only. Macros are never executed; formulas are never evaluated; external links,
queries, refreshes, and embedded objects are never opened. Hidden rows/columns and cached values are
read as stored because hiding is presentation metadata, not access control. Documentation warns
that cached formula values may be stale.

### 9.2 List sheets

`spreadsheet.listSheets` returns declaration-order entries containing one-based `index` and exact
`name`, plus `format`, total/delivered count, and `untrustedContent: true`. Do not infer a header row,
scan cell values, or return workbook properties. Sheet names are untrusted content and are included
only in the bounded tool result, never logs or incidents.

### 9.3 Read range

`spreadsheet.readRange` defaults to the first sheet, row 1, column 1, and configured maximum rows
and columns. `sheetName` matches exactly using ordinal comparison. Start positions are one-based and
must be positive. Caller maxima must be positive and no larger than configured maxima.

Return rows as arrays rather than objects so duplicate/empty headers and exact column position are
preserved. Each delivered row includes its one-based source row number and exactly the selected
column span, including explicit JSON nulls for empty cells. Cell values map as:

- null → JSON null;
- string → JSON string with controls normalized;
- boolean → JSON boolean;
- finite numeric value → JSON number using invariant serialization;
- `DateTime` → invariant ISO 8601 string with `cellType: "dateTime"` metadata or an equivalent
  stable typed-cell representation;
- `TimeSpan` → invariant constant-format string with explicit type;
- unsupported/error/non-finite value → program-owned omission marker, never `NaN`/`Infinity` JSON.

Choose one stable typed-cell JSON representation and use it for every cell so type semantics cannot
drift between XLS and XLSX. The result includes sheet name/index, requested range, delivered row and
cell counts, `hasMoreRows`, `truncated`, and `untrustedContent: true`.

Stop at the first complete row that would exceed `MaxSpreadsheetCells` or
`MaxDocumentCharacters` after compact serialization. Never cut a JSON token or cell string. A cell
string longer than the remaining character budget is replaced wholly by a typed omission marker
with its character count.

## 10. PDF contract

### 10.1 Metadata

`pdf.getInfo` opens the file read-only and returns `format`, page count, PDF version when available,
encrypted flag, and bounded document-information fields `title`, `author`, `subject`, `keywords`,
`creator`, and `producer` when present. Normalize controls and cap the combined metadata payload
within `MaxDocumentCharacters`. Metadata is untrusted content and never logged.

Reject encrypted/password-protected files rather than returning partial metadata that may vary by
encryption scheme. Do not expose raw trailer dictionaries, JavaScript, attachments, form values,
signatures, or link targets.

### 10.2 Text extraction

`pdf.readText` defaults to page 1, configured `MaxPdfPages`, and configured
`MaxDocumentCharacters`. Caller values must be positive and may only reduce configured maxima.
Reject `startPage` beyond the document page count.

For each selected page, use PdfPig's supported layout-analysis extractor such as
`ContentOrderTextExtractor`, not raw `Page.Text`. Normalize line endings and controls. Return pages
in ascending number as `{ pageNumber, text, hasExtractableText, truncated }`, plus document page
count, requested/delivered page counts, delivered character count, `hasMorePages`, aggregate
`truncated`, and `untrustedContent: true`.

Admission is page-oriented. When a complete next page does not fit, admit a Unicode-scalar-safe
prefix of that page with an application-authored truncation marker, then stop. An image-only page
returns empty text and `hasExtractableText: false`; it does not trigger OCR or fail the whole call.
Malformed page content may fail the call with `CorruptFile`; do not silently omit a page whose
parser threw.

PdfPig has basic creation APIs, but Stage 22 exposes none. The technical and user documentation must
state that PDF support is text/metadata reading only and that layout, columns, ligatures, scanned
images, and unusual font encodings can make extracted order incomplete.

## 11. Authorization, Safety, runtime, and authoring

### 11.1 Authorization order

For every alias, enforce in this order:

1. workflow alias/agent function exists;
2. task whitelist contains the owning group;
3. every input/output path resolves under the sandbox and immutable task grants with the Tool Map's
   declared access;
4. archive write Safety flag where applicable;
5. extension/signature, format, structural, collision, and configured-limit validation;
6. parsing or mutation;
7. containment/reparse revalidation immediately before commit.

A failure at one layer never grants or bypasses another. Relative paths remain under
`FileSystem:RootPath`; absolute paths require matching task authorized locations. Tool tester and AI
Playground use their existing taskless authorization context and must not gain unrestricted local
file access.

### 11.2 Local-path metadata

Declare every path in `LocalPathArguments`, including dual-path aliases. Source/archive reads use
ReadOnly; destination/archive mutation uses ReadWrite. For a path that both reads and replaces an
existing ZIP, ReadWrite is sufficient under the established authorization model and must not be
downgraded to ReadOnly during preflight.

The workflow requirement analyzer proposes the narrowest directory grants for literal paths,
deduplicates them using existing path semantics, and cannot infer grants from unresolved
placeholders. AI Helper application remains opt-in and never silently authorizes a location.

### 11.3 Registration and contextual creation

Register parsing services and contextual factories in the existing Core DI extension. Archive,
document, spreadsheet, and PDF tool instances receive immutable `ToolExecutionContext`, options,
path sandbox, Safety monitor where needed, sanitized tool-call logging, and internal parser services.
Dispose every per-call resource; no open-document cache or static mutable reader is allowed.

Agent functions are exposed only for enabled groups. Workflow validation accepts aliases only when
their groups are saved. Existing prompt/model placeholder resolution applies recursively to string
arguments before path authorization exactly once and adds no new placeholder form.

### 11.4 Tool catalog, snippets, and helper

Stage 17 metadata drives:

- searchable Tool catalog descriptions, format matrices, argument types, examples, optionality,
  Safety and path requirements;
- valid JSON snippets with conservative synthetic paths and explicit `false` overwrite values;
- Add step and caret insertion;
- AI Helper generation/explanation and its tool/path/Safety proposals;
- task validation and required-group warnings.

Advance the helper/tool-catalog contract version once. Do not hard-code package-specific behavior in
the UI. Tool descriptions state read-only versus mutating behavior and never promise OCR, formula
evaluation, recursive extraction, archive encryption, or Office/PDF writing.

## 12. Results, privacy, cancellation, and failure semantics

- Tool result JSON is compact, UTF-8-safe, deterministic, and bounded before it reaches workflow or
  agent context. Every user-originated name/text/value field belongs under the existing untrusted
  tool-result boundary.
- Do not place document/entry/sheet/PDF content in top-level exception messages. Parser exceptions
  may echo content or paths and must be sanitized before logging or incident reporting.
- Allowed metrics: alias, detected format, input byte bucket, entry/page/row/cell counts, output
  byte/character counts, truncation, limit category, duration, success/failure category, provider
  package version in diagnostics.
- Forbidden durable fields: absolute/relative paths, archive entry names, sheet names, DOCX/PDF
  metadata values, extracted text, cell/formula values, embedded data, source bytes, temporary
  names, parser exception messages, and hashes of any of those values.
- Caller cancellation propagates as cancellation, creates no incident, and removes only artifacts
  provably owned by the current call. A timeout/package failure is not mislabeled cancellation.
- Expected unsupported, corrupt, encrypted, unsafe-entry, collision, destination-exists, and limit
  failures are normal typed tool failures, not incidents. Unexpected infrastructure faults use the
  existing single incident pipeline with sanitized data.
- Archive write acknowledgements contain only operation, format, aggregate counts/bytes, overwrite
  boolean, and success. They never echo paths or entry names.

## 13. User interface

### 13.1 Task creation and editing

The existing collection-driven tool-whitelist control gains four English checkboxes from the
central catalog; do not add four Boolean view-model properties or dedicated XAML controls:

- **Archives**;
- **Word documents**;
- **Spreadsheets**;
- **PDF documents**.

All are unchecked for a newly created task. Each has concise help explaining its formats and that
local path authorization is separate. Archives additionally shows the existing semantic Safety
warning when a workflow contains extract/create/add aliases while `AllowArchiveWrite` is false.

Authorized-locations visibility and validation derive from local-path metadata. A read-only DOCX,
workbook, PDF, or source directory proposes ReadOnly; extraction/ZIP destination proposes ReadWrite.
Do not add a page, wizard, file browser, password dialog, or package-specific editor.

### 13.2 Settings and test surfaces

Settings adds one active-theme File content section for all §5.2 limits and the archive Safety
switch. Tool tester and AI Playground discover the new groups/functions through the registry and
show the existing structured output/error UI. No surface bypasses taskless path authorization.

All Light and Dark states use semantic resources with no inline colors. Use MVVM Toolkit generated
properties/commands and no business logic in code-behind.

## 14. Required automated tests

Add deterministic tests for at least the following.

### 14.1 Package, Tool Map, whitelist, and settings

1. Exact direct package versions, expected licenses recorded in documentation, and no forbidden
   package/direct reference.
2. Restore assets resolve net10-compatible SharpCompress, Open XML, ExcelDataReader, and PdfPig
   assemblies in UI and Runner publish outputs.
3. All ten aliases match interface methods, function names, ordered/optional arguments,
   descriptions, Safety flags, and local-path access metadata.
4. `ToolGroups.All`, task validator, new-task UI, task round trip, registry, workflow validation,
   catalog, snippets, helper, tester, and playground agree on the four groups.
5. New tasks leave all four groups unchecked; legacy tasks do not gain them; explicitly selected
   groups round-trip exactly.
6. Every FileContent/Safety default, validation boundary, user-settings save/reload, unknown-section
   preservation, and monitored update.

### 14.2 Archive read and security

7. List and extract representative ZIP, 7z, RAR, TAR, and each supported compressed-TAR/single-
   stream format fixture that SharpCompress actually supports.
8. Extension/signature mismatch, unsupported extension, corrupt header, truncated payload,
   encrypted archive, and wrong ZIP-based container fail with sanitized categories.
9. Canonical entry order, counts, max-list truncation, directories, timestamps, and nullable sizes.
10. Zip Slip cases: `..`, rooted, drive, UNC, device, backslash, ADS, NUL/control, empty segment,
    trailing dot/space, DOS devices, long path, mixed separator, case collision, Unicode collision,
    file/directory prefix collision.
11. Symlink, hardlink, junction-like, device, FIFO, and other special-entry fixtures are never
    created.
12. Metadata lies about size/ratio but streaming limits still stop before configured ceilings.
13. Entry-count, per-entry, total-byte, ratio, checked-overflow, and cancellation boundaries.
14. Nested archives remain ordinary extracted files and are never recursively opened.

### 14.3 Archive mutation and atomicity

15. ReadOnly/ReadWrite path authorization and `AllowArchiveWrite` are independently required for
    every mutating alias.
16. Extract-one temporary sibling, default no-overwrite, explicit overwrite, containment recheck,
    cancellation cleanup, and preservation of an existing destination on failure.
17. Extract-all requires a missing destination, stages completely, handles destination races,
    commits by rename, and leaves no partial final directory.
18. Create ZIP from file and directory with default/custom canonical entry roots, deterministic
    order, preserved bytes, no reparse traversal, and atomic default/overwrite behavior.
19. Add to ZIP copies safe existing entries, rejects unsafe originals, handles file/directory
    collisions, obeys `overwriteEntry`, validates replacement, and preserves the old archive on
    every injected failure point.
20. Concurrent calls to the same archive have deterministic winner/failure behavior without
    corruption or shared mutable context.

### 14.4 DOCX

21. Paragraphs, tabs, breaks, Unicode, hyperlinks, tables, inserted/deleted revisions, empty body,
    LF normalization, scalar-safe truncation, counts, and omission metadata.
22. Headers/footers/comments/footnotes/images/embedded packages/external relationships are not
    traversed or fetched.
23. `.doc`, `.docm`, renamed ZIP, encrypted, corrupt, oversized, and signature-mismatch inputs fail
    safely without write access or package mutation.

### 14.5 Spreadsheets

24. XLS BIFF variants covered by fixtures, XLSX, and XLSB format detection and sheet-order listing.
25. Default/selected sheet, exact name matching, one-based ranges, empty cells, duplicate headers,
    hidden data, merged cells as returned by the library, and end-of-sheet behavior.
26. Stable typed JSON for null, Unicode string, boolean, finite number, date/time, time span,
    error/unsupported/non-finite values across formats and invariant cultures.
27. Cached formula result is returned without recalculation; macros, links, and connections never
    execute or fetch.
28. Row/column/cell/character/input limits stop at complete boundaries without whole-workbook
    materialization.

### 14.6 PDF

29. Metadata fields, page count, PDF version, empty metadata, control normalization, and bounds.
30. Layout-aware extraction order, multiple pages, start/max page selection, LF normalization,
    page-oriented truncation, Unicode scalar safety, and `hasMorePages`.
31. Image-only page succeeds with `hasExtractableText: false`; no OCR or rendering package is used.
32. Encrypted, corrupt, wrong-signature, oversized, invalid-page, malformed-page, attachment,
    JavaScript, and external-link fixtures perform no write, execution, extraction, or network call.

### 14.7 Integration, privacy, and regressions

33. Agent and workflow exposure obeys each group independently; a group never exposes another
    group's functions.
34. Workflow placeholder resolution, path requirement proposals, Safety warnings, cancellation,
    retries, and result propagation remain unchanged.
35. Tool tester and Playground cannot access an absolute path outside their established taskless
    authorization.
36. Canary paths, entry/sheet names, text, cells, PDF metadata, parser messages, and temporary names
    do not appear in rolling logs, run logs, reports, report index, incidents, or telemetry.
37. Content-result JSON includes untrusted markers but normal logs include only allowed aggregates.
38. Concurrent tasks with different grants and groups cannot observe each other's context or open
    resources.
39. Existing filesystem, SQLite, Dropbox local paths, Guardrails, Tool Map, authoring, task, and
    placeholder test suites remain green.

Fixtures must be synthetic, small, license-compatible, and committed under the test project or
generated deterministically in isolated temporary roots. Tests never invoke Microsoft Office,
LibreOffice, 7-Zip/WinRAR, OCR, a live model, connector, network endpoint, personal file, or path
outside an isolated authorized root.

## 15. Documentation

Update during Stage 22 implementation:

- `Docs/Prompts/00-Shared-Specification.md` with a concise Stage 22 addendum covering package
  choices, groups/aliases, format/write scope, authorization/Safety, bounded output, archive
  defenses, privacy, and documentation obligations;
- `Docs/ARCHITECTURE-LEDGER.md` with exact public APIs, direct/transitive package versions and
  licenses, rejected alternatives, configuration, Tool Map changes, atomicity, limits, tests,
  manual evidence, and residual limitations;
- root `README.md` as the English technical reference, including package/license rationale, exact
  format matrix, every alias and argument, result schemas, limits, error categories, authorization
  matrix, Safety/overwrite/atomicity behavior, cancellation, privacy, and known extraction limits;
- `Docs/User-Manual.html` and `Docs/User-Manual.en.html` with structurally and semantically
  equivalent French/English guidance;
- `Docs/FirstRun.md` and `Docs/FirstRun-EN.md` with task whitelist, path authorization, and archive
  Safety setup;
- `Docs/theme-audit.md` for the new whitelist and Settings states in Light and Dark themes;
- `Docs/acceptance-report.md` and `Docs/Prompts/README.md` with Stage 22 scope and evidence;
- XML documentation and Tool Map descriptions for every public type, member, option, and alias.

The README technical reference must make the deliberate product boundary unmistakable:

- common formats are readable/extractable, but only ZIP is writable;
- DOCX, XLS/XLSX/XLSB, and PDF support is read-only;
- archives and documents are never opened with an external desktop application;
- no password, OCR, macro, formula recalculation, external fetch, recursive extraction, Office/PDF
  conversion, or document editing exists;
- parser/library capability is not permission: group whitelist, path grants, and archive Safety are
  separate checks.

Both standalone user guides must include complete, copyable task/workflow examples for:

1. enabling **Archives** plus ReadOnly/ReadWrite locations and extracting one ZIP entry;
2. extracting a complete archive to a new destination directory and explaining why it must not
   already exist;
3. creating a ZIP, then adding a second file with overwrite disabled;
4. reading a DOCX and explaining which parts are omitted;
5. listing workbook sheets and reading a bounded range from an XLS/XLSX file;
6. reading selected PDF pages and explaining the image-only/no-OCR result;
7. diagnosing missing group, missing path grant, disabled archive Safety, existing destination,
   encrypted file, unsupported format, corrupt file, and configured-limit failures.

Both guides must state plainly:

- the four groups are unchecked by default when creating a task;
- group permission does not grant a path, and a path grant does not enable a group;
- archive extraction/create/add also requires the global Safety opt-in;
- overwrite defaults false and ZIP replacement is staged/atomic;
- extracted document/cell text is untrusted data and may contain misleading instructions;
- file size, entry, expansion, row/cell, page, and character limits prevent unbounded context or
  archive expansion;
- PDF extraction is text-only and does not OCR scans;
- spreadsheet formulas are not recalculated and cached values may be stale;
- Office and PDF tools never write the source file;
- archives with passwords and PDFs with passwords are not supported.

Validate both standalone manuals for balanced tags, unique ids, valid internal targets, embedded
script syntax, zero unintended external resources, language-switch links, copy controls, matching
format/alias tables, and equal example/error coverage. Use only synthetic paths and filenames.

## 16. Stage gate

Run and show output:

1. `dotnet restore OD.MyLocalAgent.sln -m:1`
2. Inspect resolved packages/licenses and run the repository's available vulnerable-package audit;
   fail on a known vulnerable direct or transitive package unless a documented false positive has
   explicit project approval.
3. `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1`
4. `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1`
5. Publish UI and Runner with their existing Windows target/runtime commands and `-m:1`; verify all
   four parser libraries and required runtime dependencies are present and run synthetic smoke
   reads from the published output.
6. Parse every JSON file under `Docs/samples`.
7. Validate `Docs/User-Manual.html` and `Docs/User-Manual.en.html` structurally and verify equal
   Stage 22 alias, format, Safety, limit, and example coverage.
8. Verify the README technical tool/argument table exactly matches Tool Map metadata.
9. `git diff --check`

Build and publish must have zero warnings and zero errors. Deterministic tests must pass. The local
Ollama integration probe may remain skipped. No Microsoft Office installation, external archive
executable, OCR engine, live model, personal document, personal path, or network call is required.

## 17. Acceptance addendum

- [ ] Exactly four new independent whitelist groups exist and are unchecked by default for new
      tasks.
- [ ] Exactly ten aliases exist with the names, arguments, path access, and Safety metadata in §6.
- [ ] SharpCompress, DocumentFormat.OpenXml, ExcelDataReader, and PdfPig are centrally pinned to the
      specified stable permissive versions or a documented approved stable correction.
- [ ] No forbidden commercial/EULA-sensitive alternative, Office/LibreOffice automation, external
      archiver, OCR, or duplicate parser package was added.
- [ ] Archive list/extract accepts only the documented allowlist and verifies extension plus
      signature/container.
- [ ] Archive writes are ZIP-only, Safety-gated, default-no-overwrite, staged, validated, and
      atomic.
- [ ] Zip Slip, Windows path aliases, collisions, reparse points, special entries, nesting, bombs,
      encryption, races, cancellation, and cleanup satisfy §§7 and 12.
- [ ] DOCX extraction is read-only, ordered, bounded, and limited to the documented body content.
- [ ] XLS/XLSX/XLSB reading is streaming, typed, range/cell bounded, and never recalculates or
      executes workbook content.
- [ ] PDF metadata/text reading is bounded and layout-aware, with no password, OCR, attachment,
      JavaScript, external fetch, or write surface.
- [ ] Every local path uses the existing sandbox and immutable per-task access independently.
- [ ] Tool result content is marked untrusted and no sensitive content reaches logs, incidents, or
      telemetry.
- [ ] Tool registry, workflows, Task editor, new-task initialization, Tool catalog, snippets,
      helper, tester, Playground, Settings, samples, and tests derive from shared metadata.
- [ ] README contains the complete English technical reference; both user manuals and First Run
      guides have French/English parity and copyable examples.
- [ ] Complete restore/audit/build/test/publish/documentation gate is green with `-m:1` on every
      dotnet command.

## 18. Before finishing implementation

1. Re-read the complete Stage 22 diff against this prompt, the shared Tool Map, Stage 11 path
   authorization, Stage 17 authoring, Stage 19 contextual tools, and the architecture ledger.
2. Search all parser package references; prove they exist only in Core and no public surface exposes
   package types.
3. Search every new local-path argument; prove its Tool Map access matches runtime authorization,
   helper proposals, catalog text, and tests.
4. Use adversarial archive fixtures to prove there is no write before full canonical-path preflight
   and no final commit before streamed limits and reparse revalidation pass.
5. Inject failure and cancellation at every temporary-file/directory and atomic commit boundary;
   prove prior user files survive and only owned temporary artifacts are removed.
6. Search for `Page.Text`, `AsDataSet`, formula evaluation, password arguments, Office Interop,
   process launches, shell commands, and network access in the new implementation; remove every
   prohibited path.
7. Use canary paths, archive/sheet names, DOCX/PDF text and metadata, spreadsheet values, parser
   messages, and temporary names to scan logs, reports, incidents, and test diagnostics.
8. Compare Tool Map, README, both manuals, Settings defaults, and generated snippets for exact alias,
   argument, group, format, limit, and Safety parity.
9. Confirm new-task checkboxes are present and unchecked, legacy tasks remain unchanged, and no
   enabled group grants a path implicitly.
10. Run the complete gate with `-m:1`, then report changed files, direct/transitive package and
    license inventory, alias surface, settings, test counts, publish smoke evidence, documentation
    validation, GUI checks actually performed, and residual limitations.
