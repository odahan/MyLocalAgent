# STAGE 18A — Extensible Tool Integration Foundation

> Run only after **Stage 18** is finished and before **Stage 19**. This stage refactors the
> integration path used by existing tools so later stages can add tool groups without duplicating
> lists across Core and WPF. It adds no tool, alias, task permission, package, setting, Safety flag,
> provider call, persisted field, migration, page, or external service.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` in full, including addendum §21A.
2. `Docs/ARCHITECTURE-LEDGER.md` in full. Existing Tool Map, Stage 11 authorization, Stage 13
   helper, Stage 17 authoring, task compatibility, MVVM, privacy, and active-theme decisions bind
   this refactor.
3. The post-Stage-18 implementations of `ToolGroups`, `ToolMap`, `ToolDefinition`,
   `LocalPathArgumentDefinition`, `ToolRegistry`, `ScheduledTaskValidator`,
   `WorkflowRequirementAnalyzer`, `TaskEditorViewModel`, `TaskEditorPage`,
   `WorkflowToolCatalogViewModel`, `ToolTesterViewModel`, AI Playground, AI Workflow Helper,
   task serialization, dependency injection, tests, README, and both user manuals.
4. Existing tests for Tool Map reflection, whitelist enforcement, task round trips, Task editor
   Apply/Undo, Tool catalog ordering and opt-in, local-path proposals, tester aliases, and AI
   Playground all-tools behavior.

## 2. Mission

Make one ordered Core catalog describe every tool group and drive every generic integration
surface. Adding a future group must require one catalog entry plus its Tool Map definitions and
runtime registration, but must not require another Boolean property, another hard-coded XAML
checkbox, another catalog order array, or another load/save branch.

The task editor projects its whitelist choices from catalog metadata. It discovers whether a group
needs the Authorized locations editor from that group's Tool Map `LocalPathArguments`. The Tool
catalog and Tool tester use the same group order. Task validation and runtime filtering use the
same task-whitelist projection.

All current behavior and persisted data remain exact. The six existing task groups remain gmail,
calendar, dropbox, filesystem, http, and web. The synthetic ai group remains authoring-only. No
new checkbox, capability, authorization, or task JSON value appears in Stage 18A.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S18A-D1 | **Metadata refactor only.** The runtime alias set, public tool methods, task JSON shape, settings, prompts, Safety behavior, authorization semantics, and provider behavior remain unchanged. |
| S18A-D2 | **One ordered group catalog.** Core owns immutable metadata for stable name, English display name, English description, and task-whitelist eligibility. Declaration order is display and serialization order. |
| S18A-D3 | **Constants remain compatible.** Existing `ToolGroups.Gmail`, `Calendar`, `Dropbox`, `FileSystem`, `Http`, `Web`, and `Ai` constants remain available. `All` is derived from whitelistable metadata rather than another handwritten name list. |
| S18A-D4 | **AI stays non-whitelistable.** The synthetic ai metadata participates in authoring order but is excluded from `TaskWhitelist` and `All`; task JSON containing ai remains invalid. |
| S18A-D5 | **Bidirectional completeness.** Every Tool Map definition names a catalog group, and every task-whitelist group owns at least one non-AI Tool Map definition. Case-insensitive duplicate group names are forbidden. |
| S18A-D6 | **Collection-driven task UI.** Task editor exposes one ordered collection of selection items. It has no per-group `UseGmail`/`UseFileSystem`-style state and no XAML checkbox per known group. |
| S18A-D7 | **Metadata-driven local authorization visibility.** A selection item requires local-path authorization exactly when one of its Tool Map definitions contains local-path metadata. No group-name special case such as filesystem-or-dropbox is allowed. |
| S18A-D8 | **Generic load/save/apply/undo.** Task load, save, helper Apply, catalog opt-in, and assisted Undo iterate the selection collection and preserve catalog order. No switch or conditional branch names a concrete group. |
| S18A-D9 | **Shared display order.** Tool catalog and Tool tester sort groups through the Core catalog. They do not own local `GroupOrder` arrays or alphabetical group ordering. Aliases remain sorted within a group as previously documented. |
| S18A-D10 | **Unknown groups still fail closed.** Task validation and runtime registry continue rejecting blank, unknown, duplicate, or non-whitelist groups. Error messages list known groups in catalog order. |
| S18A-D11 | **AI Playground remains dynamic.** “All tools” continues to use the derived `ToolGroups.All`; Stage 18A adds no separate playground list and no capability escalation. |
| S18A-D12 | **No speculative generic tool runtime.** Tool implementation registration, contextual factories, Safety evaluation, argument binding, and invocation remain explicit and type-safe. This stage removes duplicated catalog integration, not concrete tool behavior. |
| S18A-D13 | **English and MVVM.** Metadata, UI labels, descriptions, identifiers, comments, and XML documentation are English. Selection items use MVVM Toolkit observability; code-behind remains presentation-only. |
| S18A-D14 | **No documentation drift.** Technical and user documentation describe the catalog-driven behavior without claiming that third-party or future tools are dynamically loaded at runtime. |

## 4. Core contract

### 4.1 ToolGroupDefinition

Add an immutable public Core record equivalent to:

```csharp
public sealed record ToolGroupDefinition(
    string Name,
    string DisplayName,
    string Description,
    bool IsTaskWhitelistGroup);
```

All public members require English XML documentation. Names are stable persisted identifiers;
display names and descriptions are UI metadata and never replace names in task JSON or runtime
authorization.

### 4.2 ToolGroups projections

`ToolGroups` owns one ordered catalog containing the six current task groups followed by ai. It
exposes:

- `Catalog`: every group in stable display order;
- `TaskWhitelist`: only groups legal in `ScheduledTask.Tools`, preserving catalog order;
- `All`: a case-insensitive set derived from `TaskWhitelist`;
- `Find(name)`: case-insensitive metadata lookup;
- `GetDisplayOrder(name)`: stable index, with unknown names sorting last.

Catalog construction must fail deterministically on case-insensitive duplicate names. Definitions
have nonblank names, display names, and descriptions. Stage 18A keeps exact existing labels,
including the cloud qualifier for Web Search.

### 4.3 Tool Map consistency

Do not move tool aliases out of `ToolMap`. Group metadata and tool metadata have separate concerns
but one tested relationship. Automated consistency checks prove:

- every `ToolMap.Definitions` group resolves through `ToolGroups.Find`;
- every `TaskWhitelist` group owns at least one non-AI definition;
- every AI step belongs to the non-whitelist ai group;
- `All` exactly equals the task-whitelist names;
- catalog and task-whitelist order remain deterministic.

## 5. Task editor contract

### 5.1 Selection projection

Add one observable UI item that wraps a `ToolGroupDefinition` and exposes:

- stable `Name`;
- `DisplayName`;
- `Description`;
- `RequiresLocalPathAuthorization`;
- observable `IsSelected`.

`TaskEditorViewModel` creates a read-only observable collection by iterating
`ToolGroups.TaskWhitelist`. `RequiresLocalPathAuthorization` is derived by finding at least one Tool
Map definition in the group with non-empty `LocalPathArguments`.

### 5.2 Dynamic XAML

Replace concrete checkboxes with one `ItemsControl` bound to the selection collection and a wrapping
items panel. Its item template binds label, checked state, and description tooltip. Existing active
theme resources and keyboard behavior remain. Do not introduce a custom control or code-behind list.

### 5.3 State transitions

Load sets each selection by membership in the saved task's `Tools`. Save returns selected names in
catalog order. Helper and Tool catalog results use the same setter. Assisted Undo snapshots names,
not UI item instances. The built-in translation example selects filesystem through the generic
selection API.

A selection change marks the draft dirty and raises `ShowAuthorizedLocations`. Loading suppresses
dirty state exactly as before. `ShowAuthorizedLocations` is true when any selected item whose group
contains local-path metadata is selected.

Expose small documented `SetToolGroupSelected` and `IsToolGroupSelected` helpers for deterministic
commands/tests. They accept only task-whitelist groups and reject unknown or ai names.

## 6. Other integration surfaces

- `WorkflowToolCatalogViewModel` orders group blocks using `ToolGroups.GetDisplayOrder` and retains
  alias ordering, search, whitelist opt-in, insertion, and session semantics.
- `ToolTesterViewModel` uses the same group order and continues deriving aliases from the runtime
  registry definitions.
- `ScheduledTaskValidator` lists expected names from `TaskWhitelist` in catalog order.
- AI Playground continues granting `ToolGroups.All` only after its existing explicit all-tools
  opt-in.
- AI Workflow Helper, workflow requirement analysis, Tool registry, argument binding, Safety,
  contextual factories, and DI keep their current APIs and behavior. Do not refactor them merely
  for stylistic consistency.

## 7. Compatibility and privacy

- Existing task files deserialize and serialize without migration or reordering beyond the already
  established canonical group order.
- No group becomes enabled by default. A new-task draft starts with every selection false.
- No selection grants a filesystem path, Safety flag, connector credential, Windows permission,
  model call, or runtime tool by itself.
- Display metadata is program-owned and safe for ordinary UI/log use. Task ids, paths, prompts,
  arguments, results, external content, credentials, and exceptions receive no new logging.
- Unknown future names remain invalid until both catalog and Tool Map/runtime implementation exist.
  This is extensibility for compiled product evolution, not a plug-in loader.

## 8. Required automated tests

Add deterministic coverage for at least:

1. unique, nonblank ordered catalog definitions;
2. exact `TaskWhitelist` and `All` projection;
3. case-insensitive `Find` and stable display order;
4. bidirectional Tool Map/group completeness;
5. ai excluded from task whitelist and selection;
6. task editor selection names/labels/order equal Core metadata;
7. new-task selections all false;
8. local-path relevance derived for filesystem and Dropbox while Gmail remains false;
9. selection dirty state and Authorized locations visibility;
10. generic task load/save round trip with several groups;
11. helper Apply and assisted Undo through generic selection state;
12. unknown and ai selection rejection;
13. Tool catalog group order equals the shared catalog;
14. Tool tester order equals the shared catalog;
15. existing whitelist, workflow, authoring, tester, Playground, task, and serialization suites
    remain green.

Tests must not add a fake production group or change the runtime alias set merely to demonstrate
extensibility. They prove that every generic projection equals the current shared metadata, so a
future compiled group causes a failing test wherever a stale hard-coded surface remains.

## 9. Documentation

Update during implementation:

- `Docs/Prompts/00-Shared-Specification.md` with concise Stage 18A normative rules;
- `Docs/ARCHITECTURE-LEDGER.md` with public APIs, removed hard-codings, compatibility, tests, gate,
  and residual boundaries;
- root `README.md` architecture/tool sections with the catalog ownership and future-group
  integration checklist;
- `Docs/User-Manual.html` and `Docs/User-Manual.en.html` in structural FR/EN parity, stating that
  Task editor choices are generated from the installed catalog and remain explicit opt-ins;
- `Docs/FirstRun.md` and `Docs/FirstRun-EN.md` if their whitelist guidance names a fixed set;
- `Docs/theme-audit.md` for the dynamic wrapping whitelist in Light and Dark themes;
- `Docs/acceptance-report.md` and `Docs/Prompts/README.md` with Stage 18A scope and evidence.

Documentation must distinguish a compiled extensible catalog from runtime plug-ins. Adding a new
group still requires implementation, registration, Tool Map metadata, tests, and documentation; it
no longer requires duplicating the generic UI/order/load/save wiring.

## 10. Stage gate

Run and show output:

1. `dotnet restore OD.MyLocalAgent.sln -m:1`
2. `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1`
3. `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1`
4. Validate both standalone manuals structurally and verify equal Stage 18A coverage.
5. Search production UI/ViewModel code for removed per-group `Use*` state and local group-order
   arrays.
6. `git diff --check`

Build must have zero warnings and zero errors. Tests must pass; the existing live Ollama probe may
remain skipped. No live connector, model, scheduled task, or filesystem outside isolated test roots
is required.

## 11. Acceptance addendum

- [ ] Stage 18A adds no tool, group, alias, package, setting, task field, permission, or behavior.
- [ ] One Core catalog owns ordered group names, labels, descriptions, and whitelist eligibility.
- [ ] `All`, task validation, Task editor, Tool catalog, Tool tester, and Playground projections
      derive from shared metadata.
- [ ] Task editor has no concrete per-group Boolean or checkbox and no concrete group load/save
      branch.
- [ ] Authorized locations visibility derives from Tool Map local-path metadata.
- [ ] AI remains authoring-only and unknown groups fail closed.
- [ ] Existing task JSON and runtime semantics remain compatible.
- [ ] Tests fail if catalog, Tool Map, UI selection, or display order diverge.
- [ ] Shared spec, ledger, README, prompt index, acceptance report, theme audit, and FR/EN manuals
      agree.
- [ ] Complete gate is green with `-m:1` on every dotnet command.

## 12. Before finishing implementation

1. Search `TaskEditorViewModel` and its XAML for every concrete task group and remove generic
   whitelist wiring that still names one.
2. Search catalog/tester UI for local group-order arrays and alphabetical group ordering.
3. Compare catalog, `All`, Tool Map groups, registry definitions, task editor items, and validation
   errors for exact case-insensitive set equality and stable order.
4. Load/save legacy tasks containing zero, one, and several groups; prove no permission is added or
   lost.
5. Exercise helper Apply/Undo and Tool catalog opt-in with existing groups.
6. Confirm local-path visibility comes only from Tool Map metadata and not display labels or names.
7. Validate EN/FR manual parity and active-theme wrapping behavior.
8. Run the complete gate and report changed files, public APIs, removed hard-codings, tests, GUI
   checks actually performed, and residual limitations.
