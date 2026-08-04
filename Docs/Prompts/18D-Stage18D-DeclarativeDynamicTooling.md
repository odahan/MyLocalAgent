# STAGE 18D — Declarative Dynamic Tooling

> Run only after **Stage 18C** is finished and before **Stage 19**. Stage 18A remains the
> foundation for collection-driven UI projections, but this stage supersedes its explicit
> runtime-registration boundary. This is a compiled-product refactor: it discovers only tool
> classes compiled into the Core assembly. It does not load external assemblies, discover DLLs,
> install plug-ins, execute manifests, or add a tool, alias, group, setting, package, permission,
> provider call, persisted field, migration, page, or external service.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` in full, especially §8.3, §14, §16, §20, §21A,
   and §21C.
2. `Docs/ARCHITECTURE-LEDGER.md` in full. Existing Tool Map, task compatibility, contextual
   filesystem authorization, Safety, authoring, privacy, MVVM, and active-theme decisions bind
   this refactor.
3. The post-Stage-18C implementations of `ToolMap`, `ToolDefinition`,
   `ToolArgumentDefinition`, `LocalPathArgumentDefinition`, `ToolGroups`,
   `ToolGroupDefinition`, `ToolRegistry`, `IContextualToolFactory<TTool>`,
   `ToolExecutionContext`, `ScheduledTaskValidator`, `WorkflowRequirementAnalyzer`, workflow
   execution, Core dependency injection, Runner command construction, Task editor, Tool catalog,
   Tool tester, AI Playground, AI Workflow Helper, tests, manuals, samples, and publishing.
4. Existing tests for Tool Map reflection, direct invocation, whitelist enforcement, contextual
   target isolation, catalog completeness/order, task round trips, task-editor selection,
   authoring metadata, Tool tester discovery, and AI Playground all-tools behavior.
5. `Docs/Prompts/19-Stage19-SQLiteTool.md` and
   `Docs/Prompts/22-Stage22-ArchiveOfficePdfTools.md`, because their integration instructions
   must be rewritten to consume this contract rather than extend concrete registry constructors.

## 2. Mission

Make a compiled tool class and its attributed methods the authoritative source for generic tool
integration. Once a concrete tool and its implementation-specific dependencies exist, compiling
that attributed class into `OD.MyLocalAgent.Core` must automatically provide:

- its ordered group metadata and task-whitelist membership;
- its workflow aliases and agent function names;
- method and ordered argument binding metadata;
- descriptions, examples, optionality, enumeration values, placeholder support, local-path
  semantics, and Safety references;
- singleton or execution-context-bound activation;
- runtime registry dispatch and whitelist filtering;
- task validation, serialization order, workflow validation, requirement analysis, snippet
  generation, AI Helper context, Tool catalog, Tool tester, AI Playground all-tools projection,
  and the Task editor whitelist checkbox.

Adding a future built-in tool group must not require editing `ToolGroups`, `ToolMap`, the
`ToolRegistry` constructor, a target-type dictionary, a contextual-factory list, a UI group list,
XAML, a ViewModel Boolean, a load/save branch, or a group-order array.

Tool-specific implementation, packages, options, parser/helper services, secrets, connector
configuration, Safety option persistence, and specialized settings controls remain explicit.
Metadata may describe that an alias references a Safety flag; metadata never creates, persists,
enables, or renders that flag by itself.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S18D-D1 | **Compiled Core discovery only.** Discover attributed tool classes only from the fixed `OD.MyLocalAgent.Core` assembly. Never enumerate all loaded assemblies, probe directories, load a DLL, accept an assembly path, or execute external registration code. |
| S18D-D2 | **Attributes are authoritative.** Group, alias, description, activation, argument, path, and Safety metadata for invocable tools come from attributes on the concrete class, methods, and parameters. No parallel production `ToolMap` entry is maintained. |
| S18D-D3 | **One immutable catalog snapshot.** Discovery and validation produce one ordered immutable catalog during bootstrap. Core runtime, Runner, workflows, and WPF consume the same snapshot or a compatibility projection over that exact snapshot. No consumer rescans independently. |
| S18D-D4 | **Fail at startup.** Invalid or conflicting attributed metadata fails deterministically before the UI or Runner accepts work. The application never silently omits a malformed tool. |
| S18D-D5 | **Group order is metadata.** Every group owns a unique positive integer display order. No central order array remains. Existing order is preserved and future Stage 19/22 positions are assigned by their own class attributes. |
| S18D-D6 | **One concrete owner per runtime group.** Stage 18D permits exactly one attributed concrete tool class per invocable group. Splitting one group across several implementation classes is out of scope and must fail closed. Existing groups that gain aliases add attributed methods to their existing class. |
| S18D-D7 | **Alias order and agent names are metadata.** Each method declares a unique positive order within its group so the current Tool Map sequence remains exact. By default, the agent function name is derived deterministically from the alias by replacing the group separator and camel-case word boundaries with lower snake case. An explicit override exists only for compatibility and is validated for uniqueness. |
| S18D-D8 | **Descriptions are not duplicated.** `ToolAttribute` and `ToolArgumentAttribute` derive from `DescriptionAttribute`, so the catalog and `AIFunctionFactory` observe the same English text. A second `[Description]` on the same member or parameter is invalid. |
| S18D-D9 | **Reflection describes; it does not authorize.** Reflection reads compiled metadata and method signatures only. Discovery invokes no tool constructor or method and grants no task group, local path, Safety flag, connector credential, network access, Windows permission, or model call. |
| S18D-D10 | **Activation remains explicit and safe.** Each class declares `Singleton` or `Contextual`. Contextual targets are created from the immutable `ToolExecutionContext` for each registry operation; no ambient or mutable current-task state is introduced. |
| S18D-D11 | **Generic DI activation.** `ToolRegistry` depends on the catalog and one generic target activator, never on concrete tools or `IContextualToolFactory<TTool>` instances. Adding a tool type cannot change its constructor. |
| S18D-D12 | **Implementation dependencies remain explicit.** A tool's connectors, options, parsers, native initialization, secret handling, and helper services are registered normally by the owning stage. The generic tooling layer registers only the discovered concrete target and its optional public service contract. |
| S18D-D13 | **Taskless projection is explicit metadata.** A contextual tool may expose its declared public interface as a taskless service for Tool tester or direct consumers. Runtime task-associated calls still receive newly context-bound targets and never reuse that taskless instance. |
| S18D-D14 | **AI workflow entries remain separate.** The synthetic `ai` group and built-in AI workflow-step definitions remain an explicit internal catalog contribution because they are workflow primitives/skills, not invocable attributed tool methods. They join the same immutable catalog and remain non-whitelistable. Refactoring skill metadata is out of scope. |
| S18D-D15 | **Dynamic generic UI, explicit specialized UI.** Task whitelist, Tool catalog, Tool tester, helper, and Playground projections are catalog-driven. Connector credentials, provider health controls, tool-specific settings, per-task SQLite Guardrails, and Safety switches remain deliberately implemented by their owning stages. |
| S18D-D16 | **Unknown data fails closed.** Unknown, blank, duplicate, non-whitelist, and `ai` task groups remain invalid. Unknown aliases and mismatched method metadata remain invalid. |
| S18D-D17 | **Compatibility façades may remain.** Existing group-name constants and static `ToolMap`/`ToolGroups` public members may remain as source-compatible read-only façades, but their collections and lookups must delegate to the canonical discovered catalog. They may not own another list. |
| S18D-D18 | **No behavior change.** Stage 18D preserves the exact existing groups, aliases, function names, arguments, descriptions, ordering, whitelist defaults, task JSON, runtime results, Safety behavior, path authorization, logging, and provider behavior. |
| S18D-D19 | **No runtime plug-in claim.** Documentation must call this attributed built-in discovery or compiled extensibility. It must not call it a plug-in system or imply that copying a DLL installs a tool. |
| S18D-D20 | **English and MVVM.** Attribute text, identifiers, validation errors, XML documentation, and UI text are English. Existing collection-driven MVVM and presentation-only code-behind rules remain mandatory. |

## 4. Declarative attribute contract

### 4.1 ToolActivationKind

Add a public enum with exact members:

```csharp
public enum ToolActivationKind
{
    Singleton,
    Contextual
}
```

`Singleton` is valid only for a target whose behavior does not depend on a task-specific
`ToolExecutionContext`. `Contextual` creates a target bound to the supplied immutable context.
The enum describes target lifetime only; it does not describe method concurrency, thread safety,
authorization, or DI scope.

### 4.2 ToolGroupAttribute

Add one sealed, inherited-false, class-only attribute equivalent in capability to:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ToolGroupAttribute : Attribute
{
    public ToolGroupAttribute(
        string name,
        string displayName,
        string description,
        int displayOrder);

    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public int DisplayOrder { get; }
    public bool IsTaskWhitelistGroup { get; set; } = true;
    public ToolActivationKind Activation { get; set; }
    public Type? ServiceContract { get; set; }
    public bool RegisterTasklessProjection { get; set; } = true;
}
```

The exact constructor/property layout may be adjusted for valid attribute syntax, but the
capabilities and defaults are normative. `ServiceContract`, when supplied, must be an interface
implemented by the attributed class. `RegisterTasklessProjection` is meaningful only when a
service contract exists. It does not expose the target as an agent tool without whitelist checks.

Existing display order is locked as follows:

| Group | Order |
|---|---:|
| `gmail` | 100 |
| `calendar` | 200 |
| `dropbox` | 300 |
| `filesystem` | 400 |
| `http` | 1000 |
| `web` | 1100 |
| synthetic `ai` contribution | 2000 |

Stage 19 uses 500 for `sqlite`. Stage 22 uses 600, 700, 800, and 900 for `archive`, `document`,
`spreadsheet`, and `pdf`. Stage 18D does not add those groups before their stages.

### 4.3 ToolAttribute

Add one sealed, inherited-false, public-instance-method-only attribute derived from
`System.ComponentModel.DescriptionAttribute`, equivalent in capability to:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ToolAttribute : DescriptionAttribute
{
    public ToolAttribute(string alias, string description, int order);

    public string Alias { get; }
    public int Order { get; }
    public string? AgentFunctionName { get; set; }
    public string? SafetyFlag { get; set; }
}
```

The alias remains the stable workflow name. `Order` is unique and positive within the owning
group and preserves the existing normative Tool Map sequence. The default derived function name must preserve all
current names, including examples such as `filesystem.markdownToHtml` becoming
`filesystem_markdown_to_html` and `sqlite.listObjects` becoming `sqlite_list_objects`.

`SafetyFlag` is authoring and diagnostic metadata referencing an existing global Safety option.
It does not perform the Safety check itself. The concrete method remains responsible for checking
the same flag before side effects, and consistency tests verify that the referenced flag is known.

### 4.4 ToolArgumentAttribute

Add one sealed, inherited-false parameter attribute derived from `DescriptionAttribute` with:

- `WorkflowArgumentKind Kind`;
- the English description inherited by the agent schema and Tool catalog;
- optional `Example`;
- optional ordered `AllowedValues`;
- tri-state or equivalent explicit/inferred placeholder support;
- tri-state or equivalent explicit/inferred requiredness when reflection alone is ambiguous.

Every public method parameter except `CancellationToken` has exactly one
`ToolArgumentAttribute`. Catalog order comes from method parameter order. Optionality normally
derives from the CLR optional/default contract; an explicit override may only confirm a supported
binding contract and may not make a required CLR parameter optional. `CancellationToken` is
excluded automatically and must be the final optional parameter.

The catalog builder validates that declared `WorkflowArgumentKind`, enum values, nullability,
default values, and CLR parameter types are compatible. It must support every existing kind and
Stage 19's `Object` kind without adding UI-specific type tables.

### 4.5 ToolLocalPathAttribute

Add one sealed, inherited-false, repeatable method attribute with equivalent capability to the
existing `LocalPathArgumentDefinition`:

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ToolLocalPathAttribute : Attribute
{
    public ToolLocalPathAttribute(
        string argumentName,
        LocalPathTargetKind targetKind,
        FileSystemAccessMode requiredAccess);

    public LocalPathDerivationKind Derivation { get; set; }
    public string? ValueArgumentName { get; set; }
    public string? DefaultValue { get; set; }
}
```

Method-level placement is required because one public argument may imply several authorization
targets and a derived target may reference two parameters. All referenced argument names must
resolve case-insensitively to attributed non-cancellation parameters on that same method.

### 4.6 Representative shape

The following is illustrative API usage, not permission to change SQLite behavior before Stage 19:

```csharp
[ToolGroup(
    "sqlite",
    "SQLite",
    "Inspect and update authorized local SQLite databases.",
    500,
    Activation = ToolActivationKind.Contextual,
    ServiceContract = typeof(ISqliteTool))]
public sealed class SqliteTool : ISqliteTool
{
    [ToolLocalPath(
        "databasePath",
        LocalPathTargetKind.File,
        FileSystemAccessMode.ReadOnly)]
    [Tool("sqlite.query", "Executes one read-only SQLite statement and returns bounded ordered rows.", 300)]
    public Task<string> QueryAsync(
        [ToolArgument(WorkflowArgumentKind.String, "Authorized SQLite database file path.", Example = "data/app.db")]
        string databasePath,
        [ToolArgument(WorkflowArgumentKind.String, "One read-only SQLite statement.", Example = "SELECT id FROM items")]
        string sql,
        [ToolArgument(WorkflowArgumentKind.Object, "Named scalar SQLite parameters.")]
        JsonElement parameters = default,
        [ToolArgument(WorkflowArgumentKind.Integer, "Optional result row limit.", Example = "100")]
        int? maxRows = null,
        CancellationToken cancellationToken = default);
}
```

Implementation must format attributes readably and keep one attribute per line when several
attributes would otherwise obscure a method signature.

## 5. Discovery and immutable catalog

### 5.1 Fixed assembly boundary

Create one catalog builder that receives an explicit assembly. Production passes exactly the
assembly containing `ToolCatalog`/the built-in Core tool types. Tests may pass explicit test types
or a test assembly to verify failure cases. Production must not use `AppDomain.GetAssemblies`,
`Assembly.Load*`, `AssemblyLoadContext`, directory enumeration, configuration-supplied type names,
MEF, source manifests, or dependency-context probing.

Discovery selects non-abstract classes carrying `ToolGroupAttribute`, then their declared public
instance methods carrying `ToolAttribute`. It never instantiates a target. Reflection results are
validated, normalized into existing `ToolGroupDefinition`, `ToolDefinition`,
`ToolArgumentDefinition`, and `LocalPathArgumentDefinition` contracts, ordered, and frozen.

### 5.2 Canonical catalog API

Add an immutable catalog contract equivalent in capability to:

```csharp
public interface IToolCatalog
{
    IReadOnlyList<ToolGroupDefinition> Groups { get; }
    IReadOnlyList<ToolGroupDefinition> TaskWhitelistGroups { get; }
    IReadOnlySet<string> TaskWhitelistNames { get; }
    IReadOnlyList<ToolDefinition> Definitions { get; }
    ToolGroupDefinition? FindGroup(string name);
    ToolDefinition? Find(string alias);
    int GetGroupDisplayOrder(string name);
}
```

The production snapshot merges attributed invocable tools with the existing explicit built-in AI
workflow definitions, orders groups by `DisplayOrder`, and orders invocable aliases within each
group by their declared method `Order`. Existing AI workflow definitions retain their established
explicit order. This one rule is tested and used on every surface. Group and alias collections are
read-only and cannot be mutated after construction.

`ToolGroupDefinition` gains `DisplayOrder` if needed and `ToolDefinition` gains its group-local
order if needed. `ToolDefinition` continues exposing the
current public projections for compatibility, but its declaring type, method, ordered arguments,
description, optionality, local paths, and Safety flag are populated exclusively from reflection
for invocable tools. Holding the validated `MethodInfo` directly is allowed and preferred to a
later name-based method lookup, provided public compatibility is preserved.

### 5.3 Compatibility façades

Keep existing stable `ToolGroups.Gmail`, `Calendar`, `Dropbox`, `FileSystem`, `Http`, `Web`, and
`Ai` constants. Future stages may add constants for source convenience, but constants are never
catalog registration.

If static `ToolGroups.Catalog`, `TaskWhitelist`, `All`, `Find`, `GetDisplayOrder`,
`ToolMap.Definitions`, and `ToolMap.Find` remain, each delegates to the same production
`ToolCatalog.Default` snapshot. Delete their handwritten group and invocable-alias collections.
Production code should prefer injected `IToolCatalog` where a constructor already participates in
DI; parser-only and compatibility code may use the immutable default snapshot.

`ToolMap.PromptAlias` may remain a stable AI-workflow constant. It is not tool registration.

## 6. Metadata validation and fail-fast rules

Catalog construction rejects, with deterministic content-free errors, at least:

1. blank names, labels, descriptions, aliases, argument descriptions, or invalid group/method order;
2. case-insensitive duplicate group names, group display orders, group-local method orders,
   aliases, or agent function names;
3. more than one attributed concrete class for the same runtime group;
4. abstract, open-generic, non-class, or unsupported target types;
5. non-public, static, generic, special-name, inherited-only, or overloaded ambiguity on a tool
   method;
6. a tool alias whose prefix does not exactly match its group name;
7. a task-whitelist group with no invocable method;
8. a runtime class attempting to own the reserved synthetic `ai` group;
9. an invalid or nonimplemented `ServiceContract`;
10. incompatible activation metadata or a contextual target that cannot be created with an
    immutable `ToolExecutionContext`;
11. missing, duplicate, or incompatible parameter attributes;
12. a non-final, multiple, or non-optional `CancellationToken`;
13. argument names that do not match the reflected camel-case parameter names;
14. optionality, default, nullability, kind, enum, or placeholder metadata inconsistent with the
    method signature;
15. local-path metadata referencing unknown/incompatible arguments or invalid derivation data;
16. a Safety reference unknown to the existing shared Safety lookup;
17. an invocable definition without an activatable target;
18. any mismatch between the immutable catalog and compatibility projections.

Errors may name program-owned group, alias, type, method, parameter, and Safety identifiers. They
must not contain task data, paths, prompts, arguments, tool results, credentials, external content,
or raw exceptions that could contain such values.

## 7. Generic dependency injection and runtime activation

### 7.1 Bootstrap

Core bootstrap builds the catalog once, validates it, registers that exact instance as
`IToolCatalog`, and registers discovered concrete targets generically according to their class
metadata. There is no concrete target list in `HostApplicationBuilderExtensions`.

When a `ServiceContract` exists, bootstrap registers it as an alias to the singleton target or to
an explicitly taskless contextual projection according to metadata. This preserves current direct
service behavior without allowing task-associated registry calls to reuse taskless targets.

Supporting services remain explicit. For example, Stage 19 still registers SQLite options and
statement/parser services, and Stage 22 still registers archive/document parser services and
one-time encoding initialization. The generic scanner must not infer or recursively register
arbitrary constructor dependencies.

### 7.2 Target activator

Replace the per-type `IContextualToolFactory<TTool>` wiring with one internal or public generic
runtime abstraction equivalent to:

```csharp
public interface IToolTargetActivator
{
    object CreateTarget(Type toolType, ToolExecutionContext context);
}
```

The implementation consults validated catalog activation metadata:

- `Singleton` resolves the registered singleton target;
- `Contextual` uses `ActivatorUtilities` or an equivalent compiled factory to create a fresh
  target with the supplied immutable execution context and registered dependencies.

It rejects a type absent from the catalog. It does not use a mutable current context, ambient
scope, reflection-based constructor guessing on each method call, or a dictionary naming concrete
production types.

### 7.3 ToolRegistry

`ToolRegistry` constructor depends only on `IToolCatalog` and `IToolTargetActivator` plus generic
infrastructure. Remove all concrete tool fields, concrete constructor parameters, and the
`Dictionary<Type, object>` target switch.

For each operation, normalize the requested whitelist against
`IToolCatalog.TaskWhitelistNames`, select only matching invocable definitions, create at most one
target per required declaring type for that operation, and create `AIFunction` instances from the
validated reflected methods and attribute descriptions. Direct workflow invocation and agent
exposure continue sharing this path.

Target creation occurs only after alias and whitelist validation. An unselected group must not
construct its target or resolve its dependencies. Cancellation and result serialization remain
unchanged.

## 8. Catalog-driven consumers

Every generic consumer must obtain group and alias data from `IToolCatalog` or its exact immutable
compatibility façade:

- `ScheduledTaskValidator` and workflow-definition validation;
- `ToolRegistry` and `IToolRegistry.Definitions`;
- workflow step factory, sequential engine, descriptions, snippets, prompt editing, placeholder
  validation, and requirement analysis;
- Runner `test-tool` alias validation and execution;
- AI Workflow Helper generation/explanation context and Safety/path proposals;
- Workflow Tool catalog ordering, insertion, opt-in, and session state;
- Tool tester discovery, group order, arguments, and execution;
- AI Playground explicit all-tools projection;
- Task editor group selections, load/save, dirty state, Apply/Undo, and authorized-location
  visibility.

The Task editor retains the existing wrapping `ItemsControl`; no concrete group checkbox or XAML
branch is added. `RequiresLocalPathAuthorization` remains derived from discovered method path
attributes. New task selections remain unchecked.

Search production code for concrete group comparisons. A comparison is permitted only when the
behavior itself is product-specific, such as the built-in translation example selecting
`filesystem`, a SQLite-specific per-task Guardrails control, or connector settings. Generic
validation, ordering, selection, discovery, activation, and invocation must never name a group.

## 9. Migration of existing tools

Apply attributes to `GmailTool`, `CalendarTool`, `DropboxTool`, `FileSystemTool`, `HttpTool`, and
`WebSearchTool`, their invocable methods, and every non-cancellation parameter. Preserve exact
metadata and function names.

Use `Singleton` for tools whose current instances are shared and task-independent. Use
`Contextual` for Dropbox and File system because they bind local-path authorization through
`ToolExecutionContext`. Preserve their current taskless interface projections.

After equivalence tests pass:

- remove invocable production entries and helper builders from the handwritten `ToolMap`;
- remove handwritten runtime group definitions from `ToolGroups` while retaining constants and
  façades;
- remove concrete target registration and target dictionaries;
- remove `IContextualToolFactory<TTool>` and `ContextualToolFactory<TTool>` if no non-tool caller
  remains;
- retain explicit AI workflow definitions as the one documented non-invocable contribution;
- retain public tool interfaces/classes unless another binding requirement explicitly changes
  them.

Do not combine this metadata migration with method behavior, result shape, authorization,
Guardrails, Safety, connector, settings, task schema, or UI redesign changes.

## 10. Required amendments to Stage 19

Before implementing Stage 19, update `Docs/Prompts/19-Stage19-SQLiteTool.md` so it:

1. requires Stage 18D, not merely Stage 18A;
2. attributes `SqliteTool` with group order 500, contextual activation, task-whitelist metadata,
   and `ISqliteTool` as its service contract;
3. attributes the four methods and all arguments, including `Object`, enumeration, optionality,
   examples, Safety, and local-path metadata;
4. removes instructions to edit `ToolGroups`, `ToolMap`, the `ToolRegistry` constructor, target
   creation dictionaries, or `IContextualToolFactory<SqliteTool>`;
5. keeps explicit SQLite packages, options, native initialization, parser services, taskless
   projection, Safety persistence, and task-specific Guardrails UI;
6. verifies that compiling the attributed class automatically updates task validation, Task
   editor whitelist/local authorization visibility, catalog, tester, Playground, helper, and
   runtime dispatch;
7. adds a regression proving no SQLite-specific generic integration branch exists.

## 11. Required amendments to Stage 22

Before implementing Stage 22, update `Docs/Prompts/22-Stage22-ArchiveOfficePdfTools.md` so it:

1. requires Stage 18D;
2. attributes `ArchiveTool`, `DocumentTool`, `SpreadsheetTool`, and `PdfTool` with contextual
   activation and group orders 600, 700, 800, and 900;
3. attributes every alias, argument, path requirement, example, optional/enum value, and archive
   Safety reference;
4. removes instructions to edit the central group/alias lists, registry constructor, target
   dictionary, and four contextual-factory registrations;
5. keeps explicit package, parser, option, encoding-provider, Safety persistence, and specialized
   Settings work;
6. proves that the four independent unchecked Task editor groups and every other generic surface
   appear solely from attributed discovery;
7. adds a regression proving no archive/document/spreadsheet/pdf-specific generic UI or runtime
   integration branch exists.

## 12. Compatibility, security, and privacy

- Existing task files deserialize and serialize without migration or permission changes.
- Exact existing aliases, names, descriptions, examples, optional arguments, enum values, and
  local-path metadata remain stable.
- Discovery runs on program-owned metadata only and produces no normal telemetry containing a
  list of user activity. A startup summary may contain only counts of groups and aliases.
- Reflection or activation failures follow the existing sanitized configuration/incident
  boundary. Raw constructor exceptions are not copied into user-facing errors, logs, or incidents
  when they may contain sensitive configuration.
- Attribute metadata is not a substitute for runtime argument validation, method Safety checks,
  path sandboxing, Guardrails, connector authorization, limits, cancellation, or content-free
  observability.
- The catalog never enables a new group in an existing or new task. Users still opt in explicitly.
- A copied or untrusted DLL is never inspected or loaded by this stage.

## 13. Required automated tests

Add deterministic coverage for at least:

1. discovery is limited to the explicitly supplied/Core assembly;
2. all six current runtime groups and all existing aliases are discovered with exact metadata;
3. derived function names equal all current normative names;
4. catalog and compatibility façade references/projections are exactly equal;
5. group and alias ordering is stable and the reserved Stage 19/22 order positions are testable
   without adding production groups;
6. method, parameter, optionality, enum, placeholder, Safety, and local-path reflection
   consistency;
7. `ToolAttribute` and `ToolArgumentAttribute` provide the effective
   `DescriptionAttribute` observed by agent schema creation;
8. every fail-fast rule in §6 using isolated test fixture types/catalog builds;
9. singleton activation reuses one target while contextual activation creates isolated targets
   with the exact supplied execution context;
10. unselected groups do not activate targets;
11. registry dispatch needs no concrete target branch and preserves whitelist enforcement;
12. direct interface/taskless projections preserve current behavior and never leak into
    task-associated activation;
13. Task editor selection names, labels, descriptions, order, defaults, dirty/load/save,
    Apply/Undo, and local-path visibility equal catalog metadata;
14. Tool catalog, Tool tester, helper, validator, Runner, and Playground projections equal the
    same catalog;
15. unknown/duplicate/ai groups and unknown aliases still fail closed;
16. legacy task JSON round trips without migration or reordered permissions;
17. an attributed test tool can flow through catalog construction, DI activation, registry,
    validation, and a collection-driven test UI projection without editing a production list;
18. all existing tool behavior, workflow, authoring, task, Runner, UI, privacy, and serialization
    suites remain green.

Test-only attributed types must never enter the production Core assembly or production catalog.
Tests may construct a catalog from an explicit type set to prove extensibility and failure cases.

## 14. Documentation

Update during implementation:

- `Docs/Prompts/00-Shared-Specification.md` with a concise Stage 18D normative addendum that
  supersedes Stage 18A's explicit-registration boundary;
- `Docs/ARCHITECTURE-LEDGER.md` with attribute APIs, assembly boundary, activation model,
  compatibility, removed hard-codings, tests, and residual plug-in boundary;
- root `README.md` architecture/tool sections with the new built-in-tool authoring checklist;
- `Docs/Prompts/README.md` with Stage 18D and the new execution order;
- `Docs/User-Manual.html` and `Docs/User-Manual.en.html` only where their installed-catalog
  wording needs clarification; preserve FR/EN structural parity;
- `Docs/theme-audit.md` and `Docs/acceptance-report.md` with evidence that a newly discovered
  group uses the existing dynamic wrapping whitelist in Light and Dark themes;
- Stage 19 and Stage 22 prompts as required by §§10–11.

The README contributor checklist must distinguish:

1. implement and attribute the concrete class/methods/parameters;
2. explicitly register implementation-specific dependencies, options, settings, and Safety
   persistence when applicable;
3. add behavior/security/privacy tests and documentation;
4. compile the class into Core;
5. do not edit generic catalog, registry, workflow, Runner, or WPF group wiring.

## 15. Stage gate

Run and show output:

1. `dotnet restore OD.MyLocalAgent.sln -m:1`
2. `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1`
3. `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1`
4. validate all JSON samples and standalone FR/EN manual structure/parity;
5. search production code for handwritten invocable Tool Map entries, concrete registry target
   fields/dictionaries, per-tool contextual factories, static group-order arrays, and concrete
   whitelist checkboxes/Booleans/branches;
6. prove the production scanner references only the fixed Core assembly and never a directory or
   loaded-assembly enumeration;
7. `git diff --check`.

Build must have zero warnings and zero errors. Tests must pass; the existing live Ollama probe may
remain skipped. Every `dotnet build` and `dotnet test` command includes `-m:1`.

## 16. Acceptance addendum

- [ ] Stage 18D adds no tool, alias, group, setting, package, permission, persisted field, page, or runtime behavior.
- [ ] Built-in runtime group and tool metadata come from attributes on concrete Core types and methods.
- [ ] Production discovery is limited to the fixed Core assembly and loads no DLL.
- [ ] One validated immutable catalog drives runtime, workflows, Runner, validation, authoring, and WPF.
- [ ] Adding an attributed built-in tool requires no edit to `ToolGroups`, `ToolMap`, `ToolRegistry`, generic DI target lists, or generic UI.
- [ ] Registry construction and activation contain no concrete tool type branch.
- [ ] Singleton and contextual lifetimes preserve current behavior and immutable task isolation.
- [ ] Task editor whitelist and local authorization visibility remain collection/metadata-driven.
- [ ] Specialized settings, credentials, Safety persistence, and tool behavior remain explicit and fail closed.
- [ ] AI remains a documented non-whitelist internal contribution.
- [ ] Existing task JSON and public aliases remain compatible.
- [ ] Stage 19 and Stage 22 prompts consume attributed discovery and contain no obsolete registry/catalog instructions.
- [ ] Documentation does not claim runtime plug-in or external-DLL support.
- [ ] Complete Release gate is green with `-m:1` on every build/test command.

## 17. Before finishing implementation

1. Compare the pre-18D and post-18D catalog snapshots field by field and prove exact equivalence.
2. Search every production `ToolMap` and `ToolGroups` consumer; confirm it reaches the canonical
   catalog and owns no stale collection.
3. Search Core bootstrap and `ToolRegistry` for every concrete tool type and remove generic
   integration wiring that names one.
4. Confirm discovery never instantiates a class and activation occurs only after whitelist
   validation.
5. Exercise singleton, taskless contextual, and two distinct task-associated contextual calls.
6. Load/save legacy tasks containing zero, one, and several groups; prove no permission changes.
7. Exercise helper Apply/Undo, Tool catalog opt-in, Tool tester, AI Playground, and Runner
   `test-tool` from the discovered catalog.
8. Confirm Safety and local-path attributes only describe requirements; runtime enforcement still
   occurs in the concrete method/path sandbox.
9. Validate Light/Dark wrapping behavior with the discovered current groups and record any manual
   GUI check not performed.
10. Re-read the updated Stage 19 and Stage 22 integration sections for obsolete `ToolMap`,
    `ToolGroups`, registry-constructor, target-dictionary, or contextual-factory instructions.
11. Run the complete gate and report changed files, public APIs, removed hard-codings, tests,
    GUI checks actually performed, and the residual external-DLL/plugin boundary.
