# STAGE 19 — SQLite Tool

> Run only after **Stage 18D** is finished. Stage 17 V2 is the implementation contract for
> workflow authoring. Stages 9 and 10 remain abandoned proposals and are neither prerequisites
> nor in scope. This stage adds one local, task-authorized SQLite tool group; it does not add a
> general database abstraction, ORM, remote database connector, or connection-string surface.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§22. §22 is
   the normative shared contract for this stage.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read. Recorded package versions, Tool Map metadata,
   task serialization, local-path authorization, Safety, Guardrails, privacy, active-theme,
   and publishing decisions are binding.
3. The post-Stage-18D implementations of `IToolCatalog`, the declarative tool attributes,
   `ToolMap`, `ToolDefinition`, `ToolArgumentDefinition`, `WorkflowArgumentKind`,
   `ToolGroupDefinition`, `ToolGroups`, `ToolGroupSelectionItem`, `ToolRegistry`,
   `ToolExecutionContext`, the generic target activator, `IPathSandbox`, `PathSandbox`,
   `LocalPathArgumentDefinition`, `WorkflowRequirementAnalyzer`, `ScheduledTask`,
   `ScheduledTaskSerializer`, `ScheduledTaskValidator`, `ToolCallLog`, `SafetyOptions`,
   `IContentTrustService`, compact batch Guardrails contracts, `GuardrailDelivery`, options
   registration, task execution, Runner cancellation, Settings, Task editor, Tool tester,
   AI Playground, AI Workflow Helper, Tool catalog, tests, manuals, samples, and publishing
   scripts.
4. The Microsoft.Data.Sqlite 10.0 documentation for connection modes, parameters, data types,
   transactions, synchronous I/O limitations, and native handles, plus the SQLite documentation
   for prepared-statement read-only classification and `sqlite3_interrupt`.

## 2. Mission

Add a bounded local SQLite capability to agents and deterministic workflows. A task may inspect
schema, query rows, and — only after an independent global Safety opt-in — execute one modifying
statement against an authorized database file. The four aliases are:

- `sqlite.listObjects`;
- `sqlite.describeTable`;
- `sqlite.query`;
- `sqlite.execute`.

Every call uses a file path, never a caller-supplied connection string. Relative paths remain
under `FileSystem:RootPath`; absolute paths require the task's existing directory authorization.
Each call owns and closes one private, non-pooled connection. There is no mutable current-task
singleton, saved connection, session, transaction handle, attached database, extension loading,
or background database worker.

SQLite read output is deterministically bounded before delivery. Each task persists whether its
SQLite read results use Guardrails. Settings supplies only the default copied into newly created
tasks; changing that default does not mutate existing tasks. When task Guardrails are enabled,
the complete bounded result is screened without summarization before it can enter workflow or
agent context.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S19-D1 | **Four aliases, one group.** Add exactly `sqlite.listObjects`, `sqlite.describeTable`, `sqlite.query`, and `sqlite.execute` in the new whitelist group `sqlite`. No generic database provider or alias synonym is introduced. |
| S19-D2 | **Microsoft.Data.Sqlite only.** Pin `Microsoft.Data.Sqlite` 10.0.10 centrally and reference it from Core. Do not add EF Core, Dapper, System.Data.SQLite, a migration framework, or a second SQLite bundle. |
| S19-D3 | **Paths, not connection strings.** Every alias accepts `databasePath`. Connection strings, URI filenames, `:memory:`, named in-memory databases, passwords, VFS names, cache modes, and arbitrary provider options are forbidden inputs. |
| S19-D4 | **Existing authorization model.** Read aliases require `ReadOnly` local-path access; `sqlite.execute` requires `ReadWrite`. Relative paths use `FileSystem:RootPath`; absolute paths use immutable task grants. Reparse-point checks remain authoritative. |
| S19-D5 | **Context-bound instances.** `SqliteTool` declares Stage 18D `Contextual` activation and is created by the generic target activator for each execution context. Singleton mutable task id, current path, current Guardrails flag, connection, or transaction state is forbidden. |
| S19-D6 | **One statement per call.** `query` and `execute` accept exactly one prepared SQLite statement, allowing only trailing whitespace/comments and one optional terminating semicolon. No multi-statement or migration batch alias belongs to Stage 19. |
| S19-D7 | **Read and write are structurally separate.** `sqlite.query` accepts a result-producing statement classified read-only by SQLite plus the additional deny rules in §7. `sqlite.execute` accepts a non-result-producing modifying statement and rejects read-only statements. |
| S19-D8 | **Writes require explicit Safety opt-in.** `sqlite.execute`, including file creation, requires `Safety:AllowSqliteWrite`, shipped `false`. Tool-group whitelisting and read-write path authorization remain independently required. |
| S19-D9 | **Creation is explicit.** A missing file is rejected unless `sqlite.execute.createIfMissing` is `true`. Read aliases never create a file. No parent directory is created implicitly. |
| S19-D10 | **Atomic modifying call.** `sqlite.execute` runs its single statement in an application-owned transaction, commits once on success, and rolls back on failure or cancellation. Caller transaction-control statements are forbidden. |
| S19-D11 | **Conservative SQLite features.** Write connections enable foreign-key enforcement. Connections are private and non-pooled. The tool never changes journal mode, enables WAL, attaches another database, loads an extension, uses encryption, or exposes in-memory databases. |
| S19-D12 | **Real JSON parameters.** `parameters` is an optional JSON object of named scalar values. Stage 19 extends `WorkflowArgumentKind` with `Object`; double-encoded JSON parameter strings are not the normative contract. |
| S19-D13 | **Bounded exact results.** Normal scalar values are not summarized or rewritten. Row count, complete serialized result size, SQL length, schema-object count, and delivered BLOB size are bounded by validated SQLite settings. Truncation occurs at object or row boundaries and is explicit. |
| S19-D14 | **Stable ordered row shape.** Query output carries ordered column metadata and rows as arrays, not dictionaries, preserving duplicate column names and declaration order. SQLite storage classes map deterministically to JSON. |
| S19-D15 | **Per-task Guardrails snapshot.** `ScheduledTask.SqliteGuardrailsEnabled` is a persisted boolean. `SQLite:GuardrailsEnabledByDefault` initializes a new task only. Changing Settings never changes a saved task silently. |
| S19-D16 | **Existing tasks fail safe.** A task JSON that predates Stage 19 and omits `sqliteGuardrailsEnabled` deserializes as `true`. Canonical serialization writes the property explicitly. |
| S19-D17 | **Screen complete bounded read data, never summarize it.** When enabled for the execution, list, describe, and query serialize their complete bounded data, split it into bounded screening chunks, batch-screen every chunk, and apply one effective trust decision to the whole result. No SQLite result is sent to the Guardrails summarizer. |
| S19-D18 | **Explicit task opt-out.** When a task has `sqliteGuardrailsEnabled: false`, SQLite read data is returned as plaintext without an LLM Guardrails call. This opt-out affects SQLite only and does not change Gmail, HTTP, Web Search, or the global Guardrails configuration. |
| S19-D19 | **Fail closed when protection was requested.** If task SQLite Guardrails are enabled but global screening is disabled, unavailable, cancelled by timeout, malformed, or fails, raw structured data is not exposed. Existing conservative Guardrails delivery semantics apply. Caller cancellation still propagates instead of returning a fallback. |
| S19-D20 | **Taskless default.** Tool tester and AI Playground use `SQLite:GuardrailsEnabledByDefault` because they have no persisted task. Task-associated workflow and agent calls always use the task's persisted value. |
| S19-D21 | **Responsive cancellation.** Database work runs away from the WPF dispatcher. Cancellation uses the connection's native handle and `sqlite3_interrupt`; `SqliteCommand.Cancel` and nominal async ADO.NET methods are not treated as effective cancellation. |
| S19-D22 | **Content-free observability.** SQL, parameters, database values, schema names, identifiers, BLOB bytes, absolute paths, protected payloads, and raw SQLite exception messages never enter normal logs, incidents, telemetry, or Guardrails failure descriptors. |
| S19-D23 | **Stage 17 remains the authoring source.** Attributed aliases and the `Object` argument kind extend the single discovered Core catalog, snippet builder, Tool catalog, helper prompt, validation, and assisted authoring. No SQLite-only alias table or workflow editor is permitted. |
| S19-D24 | **All SQLite settings are visible together.** Settings adds one SQLite section containing the write Safety switch, new-task Guardrails default, row limits, result limit, BLOB limit, SQL limit, and command timeout. No separate SQLite page is introduced. |
| S19-D25 | **English and MVVM.** UI text, code identifiers, comments, XML documentation, result markers, and errors are English. MVVM Toolkit remains mandatory and database logic does not enter code-behind. |

## 4. Critical constraints

- R1, R2, R4, R5, R7, R9, R10 and shared addenda remain binding. Every public API has English
  XML documentation; complete files are warning-clean and maintainable.
- Guardrails protect data delivered to a later model; they do not authorize the SQL, path, tool
  group, Safety flag, or database mutation. Those checks happen independently and first.
- A read-only connection is necessary but insufficient SQL confinement. Microsoft.Data.Sqlite
  exposes no SQLite authorizer callback, and SQLite classifies `ATTACH` and `DETACH` as read-only.
  Implement the complete layered inspection contract in §7; do not rely on string prefix tests.
- SQLite ADO.NET async calls execute synchronously and `SqliteCommand.Cancel()` is ineffective.
  Do not block the WPF dispatcher or claim cancellation without the native interrupt tests.
- Never log through the current exception-bearing `ToolCallLog` failure path if doing so would
  serialize a raw SQLite exception, SQL text, path, parameter, or database value. Add a
  content-free sensitive-operation path or an equivalent SQLite-specific logger.
- Every `dotnet restore`, `dotnet build`, `dotnet test`, and `dotnet publish` command in this
  stage explicitly uses `-m:1`.

## 5. Package, configuration, and persisted task contract

### 5.1 Package and native runtime

Add centrally:

```xml
<PackageVersion Include="Microsoft.Data.Sqlite" Version="10.0.10" />
```

Add one Core package reference to `Microsoft.Data.Sqlite`. The package's compatible
`SQLitePCLRaw.bundle_e_sqlite3` and `SQLitePCLRaw.core` dependencies provide the native SQLite
runtime and interrupt API. Do not add separate direct SQLitePCLRaw package references or version
entries; use the transitive API version resolved by the pinned provider package.

Both the UI and Runner publish outputs must contain the correct Windows native asset. Tests that
run only against the build output do not replace a published-output smoke test. Record the
resolved Microsoft.Data.Sqlite, SQLitePCLRaw, and native SQLite versions in the architecture
ledger.

### 5.2 SQLiteOptions

Add one validated Core options class:

```csharp
public sealed class SQLiteOptions
{
    public const string SectionName = "SQLite";

    public int DefaultMaxRows { get; set; } = 100;
    public int MaxRows { get; set; } = 1_000;
    public int MaxResultCharacters { get; set; } = 64_000;
    public int MaxBlobBytes { get; set; } = 4_096;
    public int MaxSqlCharacters { get; set; } = 16_000;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public bool GuardrailsEnabledByDefault { get; set; } = true;
}
```

Use the exact section spelling `SQLite` in configuration and `SQLiteOptions` in code. Shipped
`appsettings.json` gains:

```json
"SQLite": {
  "DefaultMaxRows": 100,
  "MaxRows": 1000,
  "MaxResultCharacters": 64000,
  "MaxBlobBytes": 4096,
  "MaxSqlCharacters": 16000,
  "CommandTimeoutSeconds": 30,
  "GuardrailsEnabledByDefault": true
}
```

Validate on startup and before Settings persistence:

- `DefaultMaxRows` is 1–`MaxRows`;
- `MaxRows` is 1–10,000;
- `MaxResultCharacters` is 4,096–1,000,000;
- `MaxBlobBytes` is 0–1,000,000; zero means BLOB content is never delivered;
- `MaxSqlCharacters` is 256–1,000,000;
- `CommandTimeoutSeconds` is 1–300.

These outer validation bounds are program-owned and not additional user settings. A configured
maximum is an authorization limit, not a target the tool must fill.

### 5.3 SafetyOptions and user settings persistence

`SafetyOptions` gains `bool AllowSqliteWrite`, default `false`, stored at
`Safety:AllowSqliteWrite`. Extend `UserSettingsData`, `UserSettingsService`, validation,
configuration reload, dirty tracking, and settings tests for every SQLite property and the new
Safety value. Saving SQLite settings preserves unknown and unrelated sections and never changes
task files.

### 5.4 ScheduledTask

Add:

```csharp
/// <summary>
/// Gets a value indicating whether SQLite read results require Guardrails protection.
/// </summary>
public bool SqliteGuardrailsEnabled { get; init; } = true;
```

The canonical JSON property is `sqliteGuardrailsEnabled` and is always written, including when
`false`. It is independent of the `tools` array: a task may preserve its preference while the
`sqlite` group is temporarily disabled. Validation accepts either boolean and rejects null or a
non-boolean JSON value through normal strict deserialization.

New-task initialization snapshots the current monitored `SQLiteOptions.GuardrailsEnabledByDefault`.
Loading an existing task uses only the persisted value. Missing legacy input uses the property
initializer `true`; it does not consult live Settings while deserializing.

### 5.5 ToolExecutionContext

Extend the immutable execution context with a nullable SQLite Guardrails preference or an
equivalent explicit taskless representation. `FromTask` copies the task boolean. `Taskless`
contains no task override, causing `SqliteTool` to read the current
`GuardrailsEnabledByDefault`. Preserve source compatibility for existing test/context
construction where practical, but do not infer a task value from mutable singleton state.

## 6. Declarative tool metadata and public contracts

### 6.1 New group and enum

Decorate `SqliteTool` with the Stage 18D group attribute using stable name `sqlite`, English
display name **SQLite**, task-whitelist eligibility, display order 500, `Contextual` activation,
and `ISqliteTool` as its taskless public service contract. The discovered catalog, compatibility
projections, Task editor selection, validation, catalog, tester, and Playground must then include
it without editing `ToolGroups`, `ToolMap`, a registry constructor, target dictionary,
SQLite-specific Boolean, checkbox, or group-order table. A `ToolGroups.Sqlite = "sqlite"`
constant may be added for source convenience, but the constant is not registration. Update
comments and UI language that count supported groups.

Add:

```csharp
public enum SQLiteObjectType
{
    All,
    Table,
    View,
    Index,
    Trigger
}
```

Extend Stage 17's `WorkflowArgumentKind` with `Object`. The snippet builder emits an empty JSON
object for this kind. `Object` has no enumeration values and its top-level
`SupportsPlaceholders` flag is false because it cannot be replaced by one JSON string. Existing
recursive workflow resolution still resolves placeholders inside nested string parameter
values; document this distinction in the Tool catalog.

### 6.2 ISqliteTool

Add a public interface and a concrete sealed implementation with signatures equivalent to:

```csharp
public interface ISqliteTool
{
    Task<string> ListObjectsAsync(
        string databasePath,
        SQLiteObjectType objectType = SQLiteObjectType.All,
        string? namePattern = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default);

    Task<string> DescribeTableAsync(
        string databasePath,
        string tableName,
        CancellationToken cancellationToken = default);

    Task<string> QueryAsync(
        string databasePath,
        string sql,
        JsonElement parameters = default,
        int? maxRows = null,
        CancellationToken cancellationToken = default);

    Task<string> ExecuteAsync(
        string databasePath,
        string sql,
        JsonElement parameters = default,
        bool createIfMissing = false,
        CancellationToken cancellationToken = default);
}
```

Decorate each implementation method with `ToolAttribute`, every non-cancellation parameter with
`ToolArgumentAttribute`, and every path requirement with `ToolLocalPathAttribute`. The Stage 18D
tool attributes provide the effective English `DescriptionAttribute` metadata used by both the
discovered catalog and agent schema. Do not add a second `[Description]` or a handwritten Tool Map
entry. `CancellationToken` is excluded from catalog arguments as before.

### 6.3 Normative aliases

| Alias | Agent function | Arguments in order | Optional arguments | Path access | Safety |
|---|---|---|---|---|---|
| `sqlite.listObjects` | `sqlite_list_objects` | `databasePath`, `objectType`, `namePattern`, `maxResults` | `objectType`, `namePattern`, `maxResults` | file, ReadOnly | — |
| `sqlite.describeTable` | `sqlite_describe_table` | `databasePath`, `tableName` | — | file, ReadOnly | — |
| `sqlite.query` | `sqlite_query` | `databasePath`, `sql`, `parameters`, `maxRows` | `parameters`, `maxRows` | file, ReadOnly | — |
| `sqlite.execute` | `sqlite_execute` | `databasePath`, `sql`, `parameters`, `createIfMissing` | `parameters`, `createIfMissing` | file, ReadWrite | `Safety:AllowSqliteWrite` |

Tool Map descriptions must state what is returned, that queries and writes accept one statement,
that parameters are named scalar JSON values, and that `execute` may create a file only when
explicitly requested. Examples contain no personal path or secret.

### 6.4 Alias behavior

`sqlite.listObjects` queries application-owned schema metadata and returns objects ordered by
SQLite object type then name using explicit SQLite `BINARY` collation and a native
`LIMIT effectiveMax + 1`; it does not read an unbounded schema into memory for application-side
sorting.
`namePattern`, when supplied, uses SQLite `LIKE` semantics with a parameterized value; it is not
concatenated into SQL. `maxResults` defaults to `DefaultMaxRows` and is clamped to 1–`MaxRows`.
Internal SQLite objects whose names start with `sqlite_` are omitted, except the application may
include a bounded `sqlite_sequence` marker when it materially describes AUTOINCREMENT state;
never return its row contents. The returned object data contains `name`, `type`, and owning table
name where relevant, but not raw `sqlite_schema.sql` text.

`sqlite.describeTable` requires a non-empty exact table or view name. It returns bounded ordered
column metadata, primary-key order, nullability, declared type, default expression, hidden/
generated status when available, foreign keys, and indexes. It never executes schema SQL or
returns trigger bodies. Internal queries parameterize values or use one tested SQLite identifier
quoting helper that doubles embedded quotes; caller text is never concatenated as executable
syntax.

`sqlite.query` executes one approved read-only, result-producing statement and returns ordered
columns and bounded rows under §8. `maxRows` uses the same default and clamp rules. It does not
rewrite the caller SQL to add `LIMIT`, because doing so can change semantics; it stops reading
after the effective bound plus the single look-ahead needed to set `hasMoreRows`.

`sqlite.execute` checks the Safety flag before opening or creating the file, then executes one
approved non-result-producing modifying statement in one transaction. It returns only
application-authored metadata: `rowsAffected`, `lastInsertRowId` when meaningful,
`databaseCreated`, and `committed`. It never echoes SQL, parameters, schema, database values, or
the resolved path. `INSERT ... RETURNING` and other modifying statements with result columns are
rejected in Stage 19.

## 7. SQL and filesystem confinement

### 7.1 Path resolution

Call `IPathSandbox.Resolve` before building the connection string:

- list, describe, and query request `ReadOnly` and require an existing regular file;
- execute requests `ReadWrite`; a missing file additionally requires `createIfMissing=true`;
- a directory, reparse-point escape, device path, URI filename, empty path, and every SQLite
  memory spelling are rejected;
- only the resolved absolute path enters `SqliteConnectionStringBuilder`; the caller never
  supplies connection-string fragments;
- read connections use `Mode=ReadOnly`; existing write files use `Mode=ReadWrite`; explicit
  creation uses `Mode=ReadWriteCreate` only after the exclusive creation rules below;
- all modes use `Cache=Private`, `Pooling=false`, and the configured timeout.

If creation is requested, require an existing authorized parent directory and use an exclusive
create claim so two callers cannot both believe they created the file. Track whether this call
created the file. If opening, validation, execution, or commit fails before a successful commit,
close every handle and remove only the new file and sidecars provably owned by this call. Never
delete a pre-existing path or race-created file. Test this cleanup; do not implement a broad glob
or directory cleanup.

### 7.2 Statement inspection

Create one reusable, non-UI statement inspector/tokenizer. It must understand SQLite whitespace,
line comments, block comments, quoted strings, quoted identifiers, escaped quote characters,
bracket identifiers, parameters, and one optional final semicolon. It is not a general SQL
parser, but it must never discover control words by searching inside a quoted value or miss
control words hidden behind comments.

Inspect the statement on the already opened connection using the native prepare API or an
equivalent SQLite-owned parser. Require all of the following:

1. non-whitespace SQL no longer than `MaxSqlCharacters` Unicode characters;
2. exactly one successfully prepared statement;
3. no executable non-comment tail after the optional terminator;
4. a complete ordered native parameter catalog;
5. native read-only classification and result-column count;
6. no forbidden token or construct below.

Reject, in both query and execute, statement-level use of:

- `ATTACH` and `DETACH`;
- every `PRAGMA` form;
- `VACUUM`, including `VACUUM INTO`;
- `load_extension` and any extension-loading form;
- `BEGIN`, `COMMIT`, `END`, `ROLLBACK`, `SAVEPOINT`, and `RELEASE`;
- `CREATE VIRTUAL TABLE`;
- `TEMP` or `TEMPORARY` schema/object creation;
- a second statement hidden behind comments or unusual whitespace.

The connection never registers application-defined SQL functions, collations, virtual-table
modules, or extension entry points.

For `sqlite.query`, additionally require native read-only classification and at least one result
column. This allows ordinary `SELECT`, read-only `WITH`, `VALUES`, and read-only `EXPLAIN` forms
without trusting the first token alone. A statement such as `ATTACH`, even though SQLite may
classify it read-only, remains rejected by the explicit deny rules.

For `sqlite.execute`, require native non-read-only classification and zero result columns. The
application-owned transaction is the only transaction boundary. Read-only no-ops and statements
whose only effect is connection configuration are rejected.

### 7.3 Named parameter binding

`parameters` is omitted, undefined, null, or an object. Any other JSON kind is a deterministic
argument error. Each property name must exactly match one native named parameter including its
leading `$`, `:`, or `@`. Positional `?` parameters and anonymous parameters are forbidden.

Reject duplicate JSON property names, missing native parameters, and unused supplied parameters.
Do not compare or log parameter values. Bind JSON values as follows:

| JSON kind | SQLite value |
|---|---|
| null | NULL |
| true / false | INTEGER 1 / 0 |
| integer fitting Int64 | INTEGER |
| other finite number | REAL |
| string | TEXT |

Arrays, nested objects, non-finite numbers, and input BLOB wrappers are out of scope and fail
before execution. Nested string parameter values may contain workflow placeholders because the
existing resolver processes them before tool binding.

### 7.4 Write transaction and database behavior

Before starting the transaction, enable foreign-key enforcement on the write connection and
verify it is active. Do not change journal mode, synchronous mode, page size, auto-vacuum,
encoding, user version, application id, locking mode, or other persistent/configuration PRAGMA.

Use a serializable application-owned transaction. Associate the command explicitly with it.
Commit only after the command completes and result metadata has been captured. Cancellation,
constraint errors, busy/locked timeout, I/O failure, or any exception rolls back and sets no
successful result. `createIfMissing` changes file-open behavior only; it never relaxes SQL,
Safety, path, parameter, transaction, or limit checks.

## 8. Deterministic result contract and limits

### 8.1 Data payload

The unprotected logical payloads are compact JSON objects. Query uses this shape:

```json
{
  "columns": [
    { "name": "id", "declaredType": "INTEGER" },
    { "name": "name", "declaredType": "TEXT" }
  ],
  "rows": [
    [1, "Ada"],
    [2, "Grace"]
  ],
  "deliveredRowCount": 2,
  "effectiveMaxRows": 100,
  "hasMoreRows": false,
  "resultTruncated": false
}
```

Rows are arrays aligned to `columns`. Duplicate names are legal and preserved. Column names and
declared types are capped by a fixed 512-character application limit with explicit metadata;
they are never used as JSON property names. List and describe use similarly ordered arrays and
content-free count/truncation fields.

The complete returned tool JSON, including protection metadata, must not exceed
`MaxResultCharacters`. Reserve the bounded envelope overhead before admitting objects or rows.
When SQLite Guardrails are effective, the admission budget also reserves worst-case marked
Base64 expansion for the complete logical payload; a later caution/failure disposition must
never overflow the configured result limit. Explicit task opt-out may use the plaintext budget.
Never cut JSON, UTF-16 surrogate pairs, UTF-8 sequences, Base64, a scalar value, object, or row.
If the next complete item cannot fit, omit it, set `resultTruncated=true` and `hasMoreRows=true`
where applicable, then stop without scanning the remaining result solely to compute a total.
An oversized first row therefore yields zero delivered rows plus an explicit truncation marker;
the tool does not partially expose the row. Users can select fewer columns or narrower values.

### 8.2 SQLite storage classes

Map reader values by actual SQLite storage class, not declared column affinity:

- NULL → JSON `null`;
- INTEGER → JSON signed 64-bit integer;
- REAL → finite JSON number; non-finite values use an application-authored typed string marker;
- TEXT → JSON string with no normalization beyond JSON escaping;
- BLOB at or below `MaxBlobBytes` → `{ "$type": "blob", "byteLength": n,
  "base64": "...", "omitted": false }`;
- larger BLOB or any BLOB when the setting is zero → the same object with `base64: null` and
  `omitted: true`.

Do not guess dates, booleans, GUIDs, decimals, JSON, or encodings from declared types or text.
BLOB omission is deterministic minimization and must be represented before Guardrails.

### 8.3 Count bounds

`maxRows` and `maxResults` below one are deterministic argument errors. Values above configured
`MaxRows` are clamped and the effective value is returned. List, describe, and query all obey
both the item/row bound and the complete character bound. Schema enumeration never becomes an
unbounded exception to result limits.

## 9. SQLite Guardrails

### 9.1 Effective setting

For a task-associated execution, the only SQLite preference is the task's persisted
`SqliteGuardrailsEnabled`. For taskless Tool tester and AI Playground calls, use the current
`SQLiteOptions.GuardrailsEnabledByDefault`. The existing `GuardrailsOptions.ScreeningEnabled`
remains the master availability switch:

- SQLite preference false: bypass SQLite screening and deliver plaintext data;
- SQLite preference true and global screening enabled: screen under §9.2;
- SQLite preference true and global screening disabled/unavailable: apply the existing
  conservative disabled/failure delivery without exposing raw structured data.

Do not reinterpret `GuardrailsEnabledByDefault` as a live parent setting for saved tasks.

### 9.2 Screen-only compact batches

Extend `IContentTrustService` with a screen-only ordered batch operation, reusing the Stage 18
batch screener, strict id correlation, provider/model snapshot, fixed policy, nonce isolation,
per-item cache, partition limits, failure descriptors, and cancellation behavior. Do not call
the batch summarizer or final-summary screener.

After deterministic payload construction and before delivery:

1. serialize the complete logical payload canonically and compactly;
2. split it on Unicode scalar boundaries into ordered chunks no longer than
   `GuardrailsOptions.MaxContentChars`;
3. give chunks opaque invocation-local ids containing no path, table, SQL, or data;
4. screen all chunks through the screen-only batch operation;
5. compute one effective result using the minimum score and stable union of indicators;
6. apply one delivery decision to the complete original bounded payload.

Use a program-owned maximum of **36 screening chunks** for one SQLite result. When the configured
result limit and Guardrails content limit would require more, reduce the logical admission budget
before serialization and truncate only at the normal row/object boundary with explicit metadata.
This is not a new setting. Together with the Stage 18 item and character partition limits it
bounds one SQLite read to at most six full-size source-screening provider calls and prevents a
large Settings value from creating an unbounded LLM-call cascade.

Chunks are transport units only. No chunk is independently delivered, reordered, summarized,
or cached as a user-visible artifact. A failed or missing chunk makes the complete result use
the conservative failure disposition. A low-trust chunk withholds the complete result. Any
caution disposition protects the complete result as marked Base64. Only a complete clean result
is reconstructed as structured plaintext.

Screening applies to `listObjects`, `describeTable`, and `query`, because identifiers and stored
text can all contain untrusted instructions. It does not apply to `execute` acknowledgements,
which are application-authored metadata and contain no database content.

### 9.3 Stable protected envelope

All three read aliases return the same outer protection shape:

```json
{
  "data": { "columns": [], "rows": [] },
  "protectedData": null,
  "contentEncoding": "PlainText",
  "screened": true,
  "sqliteGuardrailsEnabled": true,
  "trustScore": 96,
  "trustStatus": "Clean",
  "requiresHumanReview": false,
  "screeningChunkCount": 1,
  "resultCharacterCount": 180
}
```

Exactly one of `data` and `protectedData` is non-null:

- clean or explicit task opt-out: `data` is the logical JSON object;
- caution, disabled screening, or screening failure: `data=null` and `protectedData` contains
  the complete bounded canonical payload encoded and marked by existing Guardrails delivery;
- low trust: `data=null` and `protectedData` is a fixed application-authored withholding marker,
  never source content.

When a task explicitly opts out, set `screened=false`, `sqliteGuardrailsEnabled=false`, trust
fields null, and `contentEncoding=PlainText`; do not pretend the data was assessed. Protection
metadata and markers use existing enum/string conventions rather than inventing conflicting
status names.

The envelope's structural/count metadata is application-authored and may remain outside
`protectedData`. Table names, column names, default expressions, SQL fragments, values, and BLOB
metadata belong inside the protected logical payload only.

### 9.4 Guardrails failures and incidents

Caller cancellation stops database or screening work immediately and produces no fallback or
incident. Non-cancellation Guardrails batch failures return conservative protected data and
report exactly one sanitized incident per failed batch phase using the Stage 18 descriptor.
Later screening batches may finish, but partial clean status never exposes the payload.

Never pass database content, chunk ids, SQL, parameters, paths, raw model output, nonces, hashes,
or provider exceptions containing them into incidents. Cache keys remain memory-only hashes and
are never logged.

## 10. Cancellation, timeouts, and concurrency

Microsoft.Data.Sqlite operations run synchronously. Execute the complete open/inspect/bind/read
or open/inspect/bind/transaction/write flow on a worker thread so Tool tester cannot block the
WPF dispatcher. Do not use fire-and-forget work.

Once the connection is open, register caller cancellation to invoke
`SQLitePCL.raw.sqlite3_interrupt(connection.Handle)` or the exact compatible API. Synchronize
registration disposal and connection closing so interrupt is never called on a closed or
closing handle. Map `SQLITE_INTERRUPT` caused by caller cancellation to
`OperationCanceledException` carrying the caller token. Dispose reader, command, transaction,
connection, registration, and native prepared handles on every path.

`CommandTimeoutSeconds` controls busy/locked retry behavior but is not claimed as a CPU execution
deadline. Task/Runner cancellation remains the outer deadline and native interrupt is its
enforcement mechanism. Do not automatically enable WAL to improve concurrency. Concurrent
readers and writers rely on the database's existing journal mode; busy/locked failures are
bounded, content-free tool failures.

No connection pooling or cross-call semaphore is added. SQLite and the filesystem remain the
concurrency authorities. The tool must not serialize operations across unrelated database
files globally.

## 11. Core, workflow, and authoring integration

### 11.1 Registration and registry

Register `SQLiteOptions` and SQLite-specific parser/native/helper services explicitly. Stage 18D
discovery registers the attributed `SqliteTool`, its taskless `ISqliteTool` projection, contextual
activation, registry target, aliases, and generic UI projections. Do not register
`IContextualToolFactory<SqliteTool>`, extend the `ToolRegistry` constructor, add a target-creation
branch, or add a parallel alias/group list. Agent tools and direct workflow invocation continue to
come exclusively from the discovered catalog.

### 11.2 Static requirement analysis

Attributed local-path metadata in the discovered catalog drives existing Stage 13/17 analysis:

- read aliases propose a containing-directory ReadOnly grant for a literal absolute file;
- execute proposes ReadWrite;
- relative paths require no task grant beyond `FileSystem:RootPath`;
- dynamic/placeholder paths produce a warning and no invented grant;
- `sqlite.execute` reports the current `Safety:AllowSqliteWrite` state without changing it.

Extend the shared Safety lookup; do not add a SQLite-only analyzer.

### 11.3 Tool catalog and snippet builder

The `SqliteTool` group attribute uses display order 500, placing it after `filesystem` (400) and
before `http` (1000). Every catalog-driven UI surface inherits that order without a central-list
edit. The workflow catalog shows
the path access, Safety warning, task Guardrails state, and the fact that the global SQLite
Guardrails setting applies only to new/taskless contexts.

`WorkflowSnippetBuilder` supports `Object` by emitting `{}`. SQLite snippets use `""` for path,
SQL, and table/pattern strings, `All` for the enum, `{}` for parameters, `0` for optional integer
limits when optional arguments are requested, and `false` for `createIfMissing`. The catalog
must label `0` limit placeholders as incomplete because runtime requires a positive value; it
does not silently substitute a configured snapshot for a generic integer placeholder.

### 11.4 Workflow validation and placeholders

Extend shared argument compatibility for JSON objects. Validate duplicate property names inside
the SQLite `parameters` object and its scalar-only shape deterministically before execution where
possible. Placeholder grammar remains centralized and recursive string values inside parameters
remain supported. Do not parse or validate SQL semantics at task Save time beyond type/size facts
available without opening the database; runtime inspection remains authoritative.

### 11.5 AI Workflow Helper

Rebuild generation/explanation context from the enriched Tool Map and increment the generation
prompt version from `workflow-generation-v2` to `workflow-generation-v3`. It must describe the
four aliases, one-statement rule,
parameters object, row/result limits, read/write authorization, Safety gate, task Guardrails
choice, explicit creation, and forbidden SQLite features. It never opens a database or claims a
table/path/statement exists or is authorized.

## 12. User interface

### 12.1 Settings SQLite section

Add one active-theme **SQLite** card/section to Settings containing all SQLite controls together:

- **Allow SQLite writes** — bound to `Safety:AllowSqliteWrite`, with a warning that one
  authorized call may change schema or many rows;
- **Use Guardrails for new SQLite tasks** — bound to
  `SQLite:GuardrailsEnabledByDefault`, explicitly stating that saved tasks are unchanged;
- **Default maximum rows**;
- **Maximum rows per call**;
- **Maximum result characters**;
- **Maximum BLOB bytes**;
- **Maximum SQL characters**;
- **Command timeout (seconds)**.

Use numeric validation with clear English inline errors and disable Save while invalid. Display
the valid ranges from §5.2. Saving follows current user-settings atomicity/reload behavior and
preserves secrets and unrelated sections. No database browser, path picker, test connection,
SQL editor, or live database inspection is added to Settings.

### 12.2 Task editor

The Stage 18D discovered catalog and collection-driven whitelist automatically render a
**SQLite** tool-group checkbox
from its catalog metadata and includes it in load/save, dirty tracking, validation, assisted group
application, and undo snapshots. Do not add a `UseSQLite` property or dedicated XAML checkbox.
Because SQLite aliases declare local-path arguments, selecting it automatically shows the existing
**Authorized locations** card alongside File system and Dropbox behavior.

Add a task-level **Protect SQLite read results with Guardrails** checkbox, visible when SQLite is
selected but persisted regardless of selection. For a new task, initialize it once from current
SQLite Settings. For an existing task, show the saved value. Its caption explains that disabling
it may pass database text directly to a later model and affects SQLite reads only.

The checkbox participates in dirty state, save, reload, navigation confirmation, clone/new-task
initialization, validation preview, and task JSON round trips. AI Helper and Tool catalog may
display its status but never change it.

### 12.3 Existing dynamic surfaces

Tool tester discovers all four attributed aliases from the catalog and uses taskless Guardrails
defaults. AI Playground's **Allow Tools** automatically includes the new group through the Stage
18D task-whitelist projection. Tools page counts and documentation must not retain six-group
wording. No SQLite-specific output renderer is required; the existing JSON viewer remains
authoritative.

## 13. Privacy, errors, and observability

Allowed sanitized telemetry/log fields are limited to:

- alias;
- read/write/create classification;
- requested/effective row or object cap;
- SQL character count, parameter count, column count, delivered row/object count;
- result character count, BLOB delivered/omitted aggregate counts and bytes;
- Guardrails enabled/applied status, aggregate chunk/batch/call/cache counts and disposition;
- SQLite primary/extended numeric error code and content-free category;
- elapsed milliseconds and outcome;
- correlation id.

Never log or incident-report SQL, parameter names or values, paths, filenames, database/table/
column/index/trigger names, declared types, defaults, row values, BLOB data, protected payloads,
Guardrails chunks, model output, hashes, or raw exception messages. Do not log a normalized or
relative path as a compromise.

Introduce content-free typed exceptions/categories for invalid statement, forbidden feature,
parameter mismatch, result limit, busy/locked, constraint, read-only, corrupt, I/O, interrupt,
and unexpected provider failure. User-visible deterministic errors may identify the argument or
forbidden SQLite feature but never echo the supplied SQL/value/path. Raw `SqliteException` may be
used locally for numeric classification, then must be discarded from logging/incident payloads.

Authorization, validation, Safety, busy/locked, constraint, missing file, invalid SQL, and
caller cancellation are expected operational outcomes and are not incidents. Unexpected
infrastructure failures and Guardrails provider failures use the existing sanitized incident
policy exactly once at the responsible boundary.

Runner Debug may display the final protected tool result under the Stage 16 explicit private
debug policy. It must not add a separate raw SQL, parameter, or pre-Guardrails SQLite channel.

## 14. Normative examples

### 14.1 List objects

```json
{
  "step": "sqlite.listObjects",
  "id": "tables",
  "args": {
    "databasePath": "data/app.db",
    "objectType": "Table",
    "maxResults": 100
  }
}
```

This requires the `sqlite` group. The relative path resolves beneath `FileSystem:RootPath` and
requires no absolute task grant. It never creates the file.

### 14.2 Parameterized query

```json
{
  "step": "sqlite.query",
  "id": "open-items",
  "args": {
    "databasePath": "data/app.db",
    "sql": "SELECT id, title FROM work_item WHERE status = $status ORDER BY id",
    "parameters": {
      "$status": "open"
    },
    "maxRows": 100
  }
}
```

The query is inspected as one read-only result-producing statement. With task SQLite Guardrails
enabled, its complete bounded logical result is delivered structurally only after every chunk is
clean; caution/failure uses protected data and low trust withholds it.

### 14.3 Placeholder parameter

```json
{
  "step": "sqlite.query",
  "id": "today-items",
  "args": {
    "databasePath": "data/app.db",
    "sql": "SELECT id, title FROM work_item WHERE due_date = $date",
    "parameters": {
      "$date": "{{Today[yyyy-MM-dd]}}"
    }
  }
}
```

The existing recursive placeholder resolver substitutes the nested string before scalar
parameter binding. The resolved value remains data and is never concatenated into SQL.

### 14.4 Gated write and explicit creation

```json
{
  "step": "sqlite.execute",
  "id": "create-table",
  "args": {
    "databasePath": "data/reporting.db",
    "sql": "CREATE TABLE report (id INTEGER PRIMARY KEY, title TEXT NOT NULL)",
    "parameters": {},
    "createIfMissing": true
  }
}
```

This requires the `sqlite` group, global `Safety:AllowSqliteWrite=true`, and effective read-write
authorization. A missing parent directory is not created. Failure before commit removes only a
new file exclusively owned by this call.

### 14.5 Forbidden statement

```json
{
  "step": "sqlite.query",
  "args": {
    "databasePath": "data/app.db",
    "sql": "ATTACH DATABASE 'other.db' AS other"
  }
}
```

This fails before execution even if SQLite reports the prepared statement as read-only. The
error names `ATTACH` as forbidden but does not echo SQL or a path.

### 14.6 Guardrails task property

```json
{
  "id": "daily-db-brief",
  "name": "Daily database brief",
  "schedule": "0 7 * * *",
  "prompt": "Inspect the authorized database and produce a short daily brief.",
  "tools": ["sqlite"],
  "fileSystemAccess": [],
  "sqliteGuardrailsEnabled": true,
  "workflow": [],
  "enabled": true,
  "errorPolicy": "StopOnError"
}
```

The property is saved with the task. Changing the Settings default later does not affect this
task.

## 15. Required automated tests

Add deterministic tests, using temporary authorized directories and generated synthetic SQLite
databases only, for at least:

### 15.1 Package, attributed discovery, and catalog

1. Exact central `Microsoft.Data.Sqlite` 10.0.10 version and one Core reference.
2. UI and Runner build outputs resolve the provider and native e_sqlite3 library.
3. Seven supported non-AI tool groups including exact `sqlite` spelling.
4. Four exact aliases, agent function names, method mappings, argument order, optionality,
   descriptions, object/enum kinds, Safety metadata, and local-path access metadata.
5. Reflection consistency including `JsonElement` → `Object` and CancellationToken exclusion.
6. Stage 18D contextual-activator isolation and taskless `ISqliteTool` registration.
7. Tool tester and AI Playground dynamic discovery without duplicate alias tables, registry
   branches, or generic SQLite-specific UI wiring.

### 15.2 Settings and task persistence

8. Every valid boundary and every invalid SQLiteOptions value.
9. Settings load/save/reload/dirty behavior for all SQLite fields and AllowSqliteWrite.
10. Settings persistence preserves unrelated sections and secrets.
11. New task snapshots true and false global Guardrails defaults.
12. Existing task retains its persisted value after global default changes.
13. Missing legacy `sqliteGuardrailsEnabled` becomes true and canonical serialization writes it.
14. Task false round trip, clone/edit/save/reload, dirty and navigation confirmation behavior.
15. SQLite selection shows authorized locations and task Guardrails controls.

### 15.3 Path and connection confinement

16. Relative read/write paths remain within FileSystem:RootPath.
17. Absolute ReadOnly and ReadWrite grants, insufficient grants, most-specific rule, sibling
    prefix rejection, and context concurrency isolation.
18. Reparse-point escape, URI filename, memory spelling, device/directory/empty path rejection.
19. Read aliases never create a missing file.
20. Execute missing-file behavior with createIfMissing false and true.
21. No implicit parent creation, exclusive create race, failed-create cleanup, and no deletion of
    any pre-existing/race-created file or unrelated sidecar.
22. Private cache, pooling disabled, correct open modes, foreign keys enabled for writes, and no
    journal-mode mutation.

### 15.4 SQL inspection and parameters

23. Ordinary SELECT, read-only CTE, VALUES, and EXPLAIN acceptance.
24. Query rejection of write, zero-column, invalid, empty, and overlong SQL.
25. Execute acceptance of representative INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, trigger,
    index, ANALYZE, and REINDEX statements where supported.
26. Execute rejection of read-only and result-producing modification/RETURNING statements.
27. Exactly one statement with optional semicolon/comments; rejection of every multi-statement
    variant.
28. Forbidden ATTACH, DETACH, every PRAGMA form, VACUUM/INTO, extension loading, transaction
    control, virtual table creation, and TEMP creation with mixed case/comments.
29. Forbidden words inside string literals, quoted identifiers, or comments do not false-match.
30. Native readonly classification is not treated as sufficient for ATTACH/DETACH.
31. Exact `$`, `:`, and `@` named parameters; missing, unused, duplicate, positional, and invalid
    parameter rejection.
32. Null, boolean, Int64 boundaries, finite real, Unicode text, nested object, array, BLOB-wrapper,
    and non-finite input behavior.
33. Nested workflow placeholders resolve before parameter binding without SQL concatenation.

### 15.5 Results and bounds

34. Ordered columns and array rows including duplicate aliases.
35. NULL, INTEGER, REAL, TEXT, Unicode, empty text, and BLOB output mapping.
36. BLOB exactly at, above, and under zero/configured limits with no partial Base64.
37. Default row limit, explicit limit, clamp, one-row look-ahead, and hasMoreRows.
38. Complete character limit, fixed-envelope reserve, oversized first row, row-boundary
    truncation, valid JSON, Unicode scalar integrity, and bounded schema results.
39. Column-name/type 512-character cap and explicit metadata.
40. Execute rowsAffected, meaningful lastInsertRowId, databaseCreated, committed, and absence of
    echoed content.
41. Transaction commit, constraint rollback, cancellation rollback, and created-file cleanup.

### 15.6 Guardrails

42. Task true, task false, legacy true, and taskless global-default resolution.
43. Explicit task opt-out performs zero Guardrails calls and labels output unscreened.
44. Complete payload chunking at scalar boundaries and exact MaxContentChars boundaries.
45. Stable screen-only batching, id correlation, cache behavior, provider/model snapshot, and no
    summarizer/final-summary calls.
46. Thirty-six-chunk ceiling, pre-serialization admission reduction, explicit truncation, and at
    most six full-size screening provider calls.
47. All-clean reconstruction to structured `data` with no `protectedData`.
48. Any caution chunk protects the complete payload as marked Base64.
49. Any low-trust chunk withholds the complete payload.
50. Disabled/failed/malformed/missing-chunk screening exposes no structured raw data.
51. Caller cancellation produces no fallback and no incident; other batch failures report once
    per failed batch with content-free descriptors.
52. Schema names, defaults, values, BLOB metadata, SQL, path, chunks, model output, and ids are
    absent from logs/incidents.
53. Execute acknowledgement never contacts Guardrails.

### 15.7 Cancellation, errors, and privacy

54. Database work does not run on the captured WPF synchronization context.
55. Native interrupt stops a synthetic long recursive query and maps caller cancellation.
56. Interrupt/connection-close race is synchronized and leak-free.
57. Busy/locked timeout is bounded and does not globally serialize unrelated files.
58. Raw SqliteException message, SQL, parameter names/values, database identifiers, and paths are
    absent from application log, run log, and incident canaries.
59. Expected authorization, Safety, validation, missing-file, constraint, busy, and cancellation
    outcomes create no incident.
60. Every normative example and the Stage 19 sample workflow.

Tests never use a personal database, network, remote provider, real credential, or committed
binary `.db` fixture. Guardrails tests use fake providers and synthetic canary strings.

## 16. Documentation

Update during Stage 19 implementation:

- `Docs/Prompts/00-Shared-Specification.md` §22 if implementation reveals a discrepancy; do not
  silently weaken this prompt;
- `Docs/ARCHITECTURE-LEDGER.md` with exact APIs, package/native versions, SQL confinement,
  settings, task migration/default, output schema, Guardrails screen-only prompt version,
  cancellation, privacy, tests, publish evidence, and residual risks;
- root README Tool Map, group count, Settings, task JSON, local authorization, Safety,
  Guardrails, extension guidance, package/build, and troubleshooting sections;
- `Docs/User-Manual.html` and `Docs/User-Manual.en.html` with Settings and Task editor controls,
  all aliases/arguments/output shapes, examples, warnings, and troubleshooting;
- `Docs/FirstRun.md` and `Docs/FirstRun-EN.md` with safe first SQLite task guidance;
- `Docs/theme-audit.md` for every new SQLite Settings/Task editor state in Light and Dark themes;
- `Docs/Prompts/README.md`, `Docs/acceptance-report.md`, and one parseable synthetic SQLite
  workflow sample without a real database file.

Document plainly:

- SQLite Guardrails may send bounded database content to the configured cloud provider when
  OpenAI-compatible mode is selected;
- task opt-out can expose database text directly to a later model;
- Guardrails are probabilistic and Base64 is defense-in-depth, not isolation;
- result, row, and BLOB bounds can omit data and do not imply a total count;
- write authorization can affect many rows or schema objects with one statement;
- no SQL sandbox can prove semantic intent, and Stage 19 deliberately forbids several SQLite
  features;
- no encryption support is provided by the shipped SQLite bundle;
- busy timeout is not a CPU deadline; Runner cancellation uses native interrupt;
- no automatic WAL tuning, migration batch, backup, restore, attach, extension, or remote
  database support exists.

## 17. Stage gate

Run and show output:

1. `dotnet restore OD.MyLocalAgent.sln -m:1`
2. `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1`
3. `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1`
4. `dotnet publish src/OD.MyLocalAgent.UI/OD.MyLocalAgent.UI.csproj --no-restore --configuration Release -m:1`
5. `dotnet publish src/OD.MyLocalAgent.Runner/OD.MyLocalAgent.Runner.csproj --no-restore --configuration Release -m:1`
6. Parse every JSON file under `Docs/samples`.
7. Validate both standalone HTML manuals structurally.
8. `git diff --check`

Build and publish must have zero warnings and zero errors. Deterministic tests must pass. The
existing explicit local Ollama integration probe may remain skipped. Verify published UI and
Runner output can open a synthetic SQLite database and that the native SQLite asset resolves;
do not require a real LLM for this smoke test.

## 18. Acceptance addendum

- [ ] Exactly four attributed SQLite aliases exist in one `sqlite` group and the single discovered
      catalog.
- [ ] Microsoft.Data.Sqlite 10.0.10 and compatible native assets publish for UI and Runner.
- [ ] No ORM, remote connector, connection string, session, attached database, extension,
      encryption, memory database, WAL mutation, or migration batch was introduced.
- [ ] Read/write path access composes with task authorization and reparse-point defense.
- [ ] Execute requires both `sqlite` whitelist and `Safety:AllowSqliteWrite` plus ReadWrite access.
- [ ] Missing database creation is explicit and failure cleanup cannot delete prior data.
- [ ] Query and execute each accept one statement and every forbidden construct is tested through
      comments, casing, quoting, and native readonly edge cases.
- [ ] Parameters are named scalar JSON values and never concatenated into SQL.
- [ ] Results preserve ordered/duplicate columns and remain valid, bounded JSON at row/object
      boundaries.
- [ ] BLOB omission, row truncation, and schema truncation are explicit.
- [ ] Saved tasks own their SQLite Guardrails boolean; Settings changes only new/taskless defaults.
- [ ] Enabled SQLite Guardrails screen every bounded result chunk, never summarize, and fail closed
      for the complete payload.
- [ ] Explicit task opt-out is visible, SQLite-only, and makes zero Guardrails calls.
- [ ] Cancellation interrupts a real long SQLite operation and rolls back writes.
- [ ] SQL, values, identifiers, BLOBs, paths, protected data, and raw exceptions never enter logs
      or incidents.
- [ ] Settings exposes every requested SQLite control together and Task editor exposes group,
      local authorization, and per-task Guardrails state.
- [ ] Tool catalog, helper, Tool tester, AI Playground, manuals, samples, and ledger are consistent.
- [ ] Complete gate and published-output native smoke tests are green.

## 19. Before finishing implementation

1. Re-read the complete Stage 19 diff against shared §22 and Stage 17 V2 metadata rules.
2. Search production code for SQLite connection strings built from caller text; only the resolved
   sandbox path may enter `SqliteConnectionStringBuilder`.
3. Search for `ATTACH`, `DETACH`, `PRAGMA`, `VACUUM`, extension loading, transaction control,
   memory modes, shared cache, pooling, and journal-mode changes; every occurrence must be an
   application-owned test, deny rule, documentation statement, approved internal metadata query,
   or approved foreign-key setup.
4. Prove query classification does not rely only on a prefix or only on
   `sqlite3_stmt_readonly`.
5. Prove cancellation uses native interrupt and cannot race a closed connection.
6. Search logs and incident payloads with canary SQL, paths, parameters, identifiers, values,
   BLOBs, chunks, protected content, and exception messages; remove every leak.
7. Confirm the Settings default is copied only when creating a task and cannot live-change saved
   task behavior.
8. Confirm every read-output byte is either inside the Guardrails-protected logical payload or
   omitted deterministically with metadata; no duplicate raw field may bypass protection.
9. Run the complete gate with `-m:1`, then report changed files, package/native versions, alias
   surface, prompt-version changes, test counts, publish smoke evidence, GUI checks actually
   performed, and residual limitations.
