# STAGE 28 — Reflection-Driven Workflow Step Editor

> This stage makes ordinary workflow construction and maintenance possible without hand-writing
> JSON. It adds caret-aware insertion before or after the current step, typed editing of one
> existing step, and clean deletion of one step. The step detail form is created on demand from
> the declarative Tool Map metadata discovered from compiled Core types.
>
> JSON remains the only persisted and runtime workflow format. The raw workflow editor, AI
> Workflow Helper, Tool catalog, and focused workflow-text editor remain complementary authoring
> surfaces. This stage does not add another workflow language, an LLM-generated form, or runtime
> plug-ins.

## 0. Read first (mandatory)

Read these files completely before changing code:

- `AGENTS.md`;
- `Docs/Prompts/00-Shared-Specification.md`, especially the workflow and Stage 17 addenda;
- `Docs/Prompts/17-Stage17-WorkflowAuthoringAids.V2.md`;
- `Docs/Prompts/18D-Stage18D-DeclarativeDynamicTooling.md`;
- `Docs/Prompts/26-Stage26-EmbeddedKnowledgeAndDeclarativePromptTools.md`;
- `Docs/Prompts/27-Stage27-HybridKnowledgeRetrievalAndContextSelection.md`;
- `Docs/ARCHITECTURE-LEDGER.md`;
- the workflow sections of `README.md`;
- current implementations and tests for `ToolCatalog`, `ToolDefinition`,
  `ToolArgumentDefinition`, `WorkflowStepDefinition`, `WorkflowDefinitionSerializer`,
  `WorkflowStepDefinitionJsonConverter`, `WorkflowSnippetBuilder`,
  `WorkflowCaretInsertionService`, `WorkflowRequirementAnalyzer`,
  `WorkflowPromptEditingService`, `ScheduledTaskValidator`, `TaskEditorViewModel`,
  `TaskEditorPage`, `WorkflowToolCatalogViewModel`, and `WorkflowPromptEditorViewModel`.

The repository state and architecture ledger are authoritative when an older prompt differs from
implemented behavior. In particular, preserve Stage 26 exact prompt-tool aliases and Knowledge
scope, Stage 27's unchanged `knowledge.search` input, the Stage 17 canonical serialization and
assisted-undo rules, and the current task capability/path boundaries.

## 1. Mission

Add a primary, deterministic **Workflow step editor** to the Task editor with these flows:

1. use the workflow editor caret to identify the current top-level step;
2. insert a new step immediately before or after that step;
3. choose the new step's tool/alias from the current Tool Map;
4. construct the selected step's detail form on demand from catalog metadata;
5. edit all applicable typed arguments and step-level options, including `ai.prompt` model,
   `includeContext`, exact `toolAliases`, and Knowledge tag narrowing;
6. edit an existing step without permitting its tool/alias to change;
7. delete the current step structurally, with confirmation and dependency warnings;
8. apply each successful insertion, edit, or deletion as one atomic Task-editor draft change.

The feature edits an in-memory task draft only. **Save task** remains the sole persistence and
scheduling boundary.

## 2. Locked product decisions

| # | Decision |
|---|---|
| S28-D1 | JSON remains the sole persisted workflow, runner input, import/export contract, and canonical serialization. No alternate workflow DSL or migration is added. |
| S28-D2 | The form is deterministic. No LLM, agent, skill, tool, connector, filesystem probe, or web call creates fields or values. |
| S28-D3 | Reflection remains owned by `ToolCatalog` in Core. The UI consumes the immutable `IToolCatalog` projection; it must not independently reflect tool methods or maintain a duplicate alias/argument table. |
| S28-D4 | A new step's tool is chosen only in insert mode. The selected Tool Map alias determines the generated form. |
| S28-D5 | An existing step's alias is immutable. Edit mode shows the alias and tool description read-only and exposes no tool picker. Core mutation rejects a replacement whose alias differs, even if a UI bug submits it. |
| S28-D6 | Changing an existing step to another tool requires two explicit operations: delete the old step, then insert a new step. No convert, morph, change-action, or argument-name guessing path exists. |
| S28-D7 | Delete removes exactly one top-level step. It never rewrites another step, renames an id, repairs a placeholder, reorders the array, or revokes task permissions automatically. |
| S28-D8 | The caret identifies steps by zero-based declaration position, never by id or serialized-text search. Duplicate ids in a repair draft therefore do not make selection ambiguous. |
| S28-D9 | Insert before/after, replace, and delete operate on a parsed top-level array and return one complete canonical JSON value. On success, all non-target step semantics are preserved. |
| S28-D10 | Invalid JSON has no typed-edit fallback. Insert/edit/delete commands report a deterministic repair message and leave the draft untouched. The Stage 17 Tool catalog may retain its explicit verbatim fallback, but the new primary editor never performs partial textual surgery on invalid JSON. |
| S28-D11 | Canonical formatting is accepted. An atomic step mutation may reformat the complete array through `WorkflowDefinitionSerializer`; it may not semantically change non-target steps. |
| S28-D12 | The detail panel is rebuilt on the fly when an insert-mode tool is selected. Argument order, type, requiredness, description, example, allowed values, placeholder support, defaults, and local-path semantics come from catalog metadata. |
| S28-D13 | Required arguments are always present in the form. Optional arguments have an explicit Include control; editing preserves whether each optional argument was present or absent. |
| S28-D14 | Examples are help text, not persisted defaults. A value is never silently copied from `Example`. Reflected CLR defaults may initialize an explicitly included optional argument; `ai.prompt` keeps its established authored defaults. |
| S28-D15 | Every JSON value is edited through a typed control: string, multiline string, integer, finite number, Boolean, enumeration, or structured JSON. Object/array values are independently parsed before Apply. |
| S28-D16 | `IsExcludedFromWorkflowTextEditor` does not hide an argument from the step editor. That flag scopes the focused long-text editor only; the full step editor must expose every catalog argument. |
| S28-D17 | Common step fields are first-class: id, arguments, optional error policy/retry values, and AI generation overrides. Existing omission versus explicit value is preserved in edit mode. |
| S28-D18 | `model`, `toolAliases`, and `knowledgeAccess` are exposed only for `ai.prompt`. `generationOptions` are exposed only for AI steps. Core validation remains authoritative. |
| S28-D19 | Prompt tools are selected as exact agent-callable Tool Map aliases. Missing or empty selection preserves no-tool prompt behavior; task groups are never implicitly inherited as prompt tools. |
| S28-D20 | A prompt Knowledge scope can only inherit or narrow the task's authorized tags and is available only while exact alias `knowledge.search` is selected. It can never broaden the task scope. |
| S28-D21 | Missing task tool groups, sensitive capabilities, and statically inferable absolute-directory grants are shown as authorization proposals. Every proposal starts unchecked and enters the Task-editor result only after explicit user opt-in. |
| S28-D22 | Global Safety settings, HTTP allowlists, connector configuration, filesystem sandbox rules, and Knowledge library access remain read-only runtime constraints. The dialog never changes them or claims execution will succeed merely because a step is structurally valid. |
| S28-D23 | Editing or deleting a step never automatically removes a task tool group, capability, or path grant. Other workflow steps or the task's agent prompt may still require it. |
| S28-D24 | Apply is all-or-nothing. A validation error changes neither workflow text nor task permissions and keeps the dialog open with field-level and summary diagnostics. Cancel discards the complete working copy. |
| S28-D25 | The existing shared one-level assisted undo covers insert, edit, and delete, including any explicitly accepted group, capability, or path changes. |
| S28-D26 | No authoring operation executes the workflow. Dialogs never call `ITaskStore`, `ITaskSchedulerService`, Runner services, providers, or runtime tool targets. |
| S28-D27 | Workflow content, prompts, argument values, paths, exact tool selections, and Knowledge tags are never logged, incident-reported, persisted, or copied by dialog logic. Only content-free operation summaries may be logged or shown. |
| S28-D28 | UI text, identifiers, XML documentation, comments, validation messages, and tests added by this stage are in English. |

## 3. User experience and command states

Replace the inline alias picker plus simple **Add step** primary path in the workflow card with:

- **Insert before**;
- **Insert after**;
- **Edit step**;
- **Delete step**.

Keep **Format JSON**, **Validate**, AI Helper, Tool catalog, and **Edit workflow text** available.
The existing Tool catalog remains useful as a complete reference and legacy quick-insertion aid;
the new Workflow step editor is the primary complete-form path. Do not expand the focused
workflow-text editor beyond its current long-text scope.

### 3.1 Current-step status

Show one compact status beside the commands:

- `Current step: #3 — filesystem.write (save-report)` when a step is located;
- `No current step — the workflow is empty.` for `[]`;
- a sanitized parse error when the JSON is invalid.

Recompute this projection whenever `WorkflowText` or `WorkflowCaretIndex` changes. It is a
projection only and must not set `IsDirty`.

### 3.2 Command availability

| Workflow state | Insert before | Insert after | Edit | Delete |
|---|---:|---:|---:|---:|
| Valid non-empty array with current step | Yes | Yes | Yes | Yes |
| Valid empty array | Yes, inserts at 0 | Yes, inserts at 0 | No | No |
| Invalid/non-array JSON | No | No | No | No |
| Known alias with repairable argument errors | Yes around it | Yes around it | Yes | Yes |
| Unknown alias | Yes around it | Yes around it | No | Yes |

Commands must defensively reparse when invoked programmatically. Disabled buttons are not the
only safety boundary.

### 3.3 Modal modes

Use one `WorkflowStepEditorWindow` and one ViewModel with explicit mode:

```text
InsertBefore | InsertAfter | Edit
```

Insert modes show a searchable, grouped Tool Map selector on the left and the generated form on
the right. Edit mode hides the selector and shows the immutable alias, group, and description in
the header. The window title and primary command are respectively **Insert workflow step** /
**Insert** and **Edit workflow step** / **Apply**.

Closing the window is equivalent to Cancel. If the working form has changed, ask for explicit
discard confirmation. A no-op edit returns `null` and creates no dirty state or undo snapshot.

## 4. Caret location and structural mutation

Refactor the private top-level UTF-8 span logic currently inside
`WorkflowCaretInsertionService` into one pure Core collaborator used by every workflow authoring
surface. Suggested types are:

```csharp
public sealed record WorkflowStepLocation(
    int Index,
    int Utf16Start,
    int Utf16End,
    string Alias,
    string EffectiveId);

public enum WorkflowStepInsertionPosition
{
    Before,
    After,
}

public static class WorkflowStepEditingService
{
    public static WorkflowStepLocationResult Locate(string text, int caretIndex);

    public static WorkflowStepMutationResult Insert(
        string text,
        int? anchorIndex,
        WorkflowStepInsertionPosition position,
        WorkflowStepDefinition step);

    public static WorkflowStepMutationResult Replace(
        string text,
        int targetIndex,
        string expectedAlias,
        WorkflowStepDefinition replacement);

    public static WorkflowStepMutationResult Delete(
        string text,
        int targetIndex,
        string expectedAlias);
}
```

Exact names may follow repository conventions, but the following behavior is normative.

### 4.1 Location rule

- Clamp the caret to the UTF-16 string bounds and never split a surrogate pair.
- Parse exactly one top-level JSON array and enumerate element byte spans with
  `Utf8JsonReader`.
- Translate UTF-8 spans to UTF-16 indices without assuming ASCII.
- Inside an element, including its opening token, selects that element.
- On a comma or whitespace between elements, select the element on the left.
- After the last element, select the last element.
- At or before the first element, including the opening `[`, select the first element. This
  permits intuitive editing and insertion at the beginning.
- An empty array has no current step but supports insertion at index 0.
- Return a typed parse/status result; deterministic parse errors are not incidents.

### 4.2 Insert, replace, and delete invariants

- Insert before uses the anchor index; insert after uses anchor index + 1; an empty array uses 0.
- Replace verifies the target index still exists and its alias equals `expectedAlias`
  case-insensitively. It also verifies `replacement.Step` equals `expectedAlias`. A mismatch is a
  `WorkflowValidationException`, not a conversion.
- Delete verifies the same index/alias precondition and removes one element only.
- Each operation reparses the source immediately before mutation, modifies a list of normalized
  `WorkflowStepDefinition`, and serializes through `WorkflowDefinitionSerializer`.
- The result contains final text, affected index, final caret at the affected/nearest element,
  alias, effective id when applicable, canonical-formatting flag, and content-free warnings.
- Mutation methods never accept or return task permissions. They are pure Core JSON operations.
- Reuse these operations from `WorkflowCaretInsertionService` where practical so insertion rules
  cannot drift.

## 5. Reflection-backed argument metadata

`ToolCatalog` already discovers public attributed methods and materializes immutable
`ToolArgumentDefinition` records. Extend that single projection only where the step editor needs
information currently lost after reflection.

### 5.1 Required metadata additions

Add stable Core metadata equivalent to:

- underlying non-cancellation CLR value type, including nullable element type information;
- whether the parameter has a reflected default;
- a JSON-compatible clone of that reflected default when present;
- optional finite numeric minimum and maximum authoring constraints;
- an optional editor hint only when `WorkflowArgumentKind`, `IsLongText`, `AllowedValues`, CLR
  type, and local-path metadata are insufficient.

Suggested additive shape:

```csharp
public sealed record ToolArgumentDefinition(...)
{
    public Type ValueType { get; init; } = typeof(string);
    public JsonElement? DefaultValue { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public WorkflowArgumentEditorHint EditorHint { get; init; }
}
```

Do not serialize `Type` or these authoring metadata into task JSON. The exact storage type for a
default may differ if it remains immutable and JSON-safe.

### 5.2 Discovery rules

- Agent tool metadata is derived from `ParameterInfo` plus `ToolArgumentAttribute` during the
  existing catalog scan.
- Nullable wrappers do not change the JSON kind; they affect optional/null handling only.
- Enum values continue to come from the CLR enum unless explicitly constrained.
- Non-null defaults must be finite, non-secret constants serializable by the shared JSON options.
  A reflected null or `default(T)` value with no persisted JSON representation is treated as no
  authored default, not emitted as an invalid value.
- Cancellation tokens are never arguments or fields.
- Synthetic AI steps declare equivalent metadata in their existing Core definitions; they are
  not forced through fake reflection.
- Catalog construction fails fast on an impossible kind/type pair, a non-finite numeric bound,
  minimum greater than maximum, an invalid default, or an editor hint incompatible with the kind.
- `ScheduledTaskValidator` and authoring UI consume this projection rather than re-reflecting a
  method by name. Remove the validator's private method lookup if the new metadata fully replaces
  it without changing validation behavior.

### 5.3 Minimal hint policy

Prefer automatic mapping. Do not annotate every parameter merely to repeat its JSON kind.

| Metadata | Generated editor |
|---|---|
| String + `IsLongText=false` | Single-line text |
| String + `IsLongText=true` | Multiline text |
| Integer | Invariant integer text/numeric control |
| Number | Invariant finite-number text/numeric control |
| Boolean | CheckBox |
| Enumeration / non-empty allowed values | ComboBox |
| Object or array-like CLR type | Structured JSON editor |
| Local path definition | Text field plus access/target help; no disk browser is required |

Use hints for semantic exceptions such as SQL/JSON-as-string or a Knowledge tag picker. Do not
introduce WPF control types into Core metadata.

## 6. On-demand form model

Create the form from `ToolDefinition` when a tool is selected in insert mode or when the current
step is loaded in edit mode. A recommended UI model is one base
`WorkflowArgumentEditorItem` plus small typed subclasses or one discriminated editor-kind model
rendered through WPF DataTemplates.

Each row exposes:

- name and human-readable label;
- required/optional state;
- Include state for optional arguments;
- description, example, allowed values, placeholder support, and local-path requirement;
- original JSON value and current typed working value;
- validation error and modified state.

Rebuilding after an insert-mode tool change discards the previous tool's working fields only
after confirmation when the user entered data. It creates a fresh step id with
`WorkflowSnippetBuilder.CreateUniqueId` and does not attempt to map similarly named arguments
between tools.

### 6.1 Value rules

- Required strings start empty, except the established new `ai.prompt.prompt` default.
- Optional arguments start omitted unless their established authored behavior requires explicit
  emission (`ai.prompt.includeContext`) or the user turns on Include.
- Turning on an optional argument initializes its reflected runtime default when available;
  otherwise it starts as the kind's empty editing value and must validate before Apply.
- Turning off an optional argument removes it from generated `args`; it does not serialize null.
- Edit mode preserves property presence and values exactly until the user changes them.
- Multiline strings normalize CRLF/lone CR to LF on Apply, matching existing workflow text rules.
- Integer and number parsing uses invariant culture and rejects overflow, NaN, and infinity.
- Structured JSON accepts an object or array appropriate to metadata, rejects trailing content,
  and never accepts comments or JSON5 syntax.
- Placeholder insertion is offered only for strings whose metadata supports placeholders. It
  replaces the selected text and uses the shared `WorkflowPlaceholderCatalog`.

### 6.2 Common step fields

The form contains a **Step** section before arguments:

- immutable alias in edit mode; selected alias in insert mode;
- id with generated unique default and `[A-Za-z0-9._-]+` validation through the existing rule;
- optional error-policy override with Inherit/Override choice;
- `onError` selection when overridden;
- retry count and retry backoff only when effective `onError` is `Retry`;
- omission is preserved when editing.

Do not expose `IsStringForm` as a user setting. Editing a concise string-form step promotes it to
object form only when Apply creates step-specific data. A no-op edit retains its original text by
returning no result.

### 6.3 AI generation section

For every `ToolDefinition.IsAiStep`, offer an optional **Generation overrides** section with
independent inheritance controls for:

- Temperature;
- Top P;
- reasoning enabled.

Validate through the existing `GenerationPolicyValidation`. Omitting all three removes
`generationOptions`; edit mode preserves existing individual omission.

## 7. `ai.prompt` specialized section

The generic argument form continues to own `prompt`, `includeContext`, `timeout`, and `thinking`
from Tool Map metadata. Add a separate **Prompt execution** section for envelope properties.

### 7.1 Model

- **Inherit task/Settings model** is selected when `model` is absent.
- Clearing inheritance enables an editable model ComboBox/text field.
- Initially offer the configured provider model, task override, and existing step value without
  making a network call.
- The dialog never refreshes a provider catalog. Free text keeps the editor deterministic and
  avoids network/provider availability becoming an authoring dependency.
- A blank explicit model is invalid; whitespace is trimmed; model validation is shared.

### 7.2 Exact prompt tools

List only `IToolCatalog.Definitions` where `IsAgentTool` is true, grouped in catalog order. For
each entry show alias, description, group, sensitive capability, and whether model-generated local
paths remain runtime-authorized.

- Checked aliases serialize exactly to `toolAliases` in stable catalog order.
- Unchecked aliases are absent; an empty set omits `toolAliases`.
- Synthetic AI aliases are structurally excluded.
- A missing owning task group creates an unchecked authorization proposal; selecting an alias
  does not itself enable the group.
- Required capabilities and path behavior remain visible even when currently granted.
- Side-effecting tools are not forbidden, but their existing capability/Safety checks are stated
  plainly.

### 7.3 Knowledge tags

Show Knowledge scope only when exact alias `knowledge.search` is checked.

- **Inherit task Knowledge tags** means `knowledgeAccess` is omitted.
- **Narrow Knowledge tags** enables a multi-select sourced only from the task's current normalized
  tag scope.
- At least one tag is required when narrowing.
- Stale existing tags are displayed as unavailable repair items; Apply cannot broaden through
  them.
- Deselecting `knowledge.search` clears the working step scope only after confirmation when a
  scope existed.
- The resulting scope is validated with `KnowledgeTagPolicy.Narrow` before mutation.

For a direct `knowledge.search` step, its optional `args.tags` may use the same task-tag picker
through an editor hint. Omission means the complete task scope; an explicit list narrows the call.

## 8. Authorization review

Build a provisional workflow from the form and pass it through
`WorkflowRequirementAnalyzer` whenever relevant selections or values change. Debounce only if
needed for UI responsiveness; analysis remains deterministic and in memory.

Show three proposal groups:

1. missing non-AI task tool groups;
2. missing sensitive task capabilities;
3. missing statically inferred absolute-directory grants, including required ReadOnly/ReadWrite
   access and broader-scope/root/UNC warnings.

Rules:

- every proposal is unchecked by default, including values that were previously checked during a
  different tool selection in the same uncommitted insert session;
- Apply may succeed structurally without accepting a proposal, but the result carries a clear
  execution warning;
- accepted proposals are returned separately from workflow JSON and merged into the Task editor
  draft atomically;
- dynamic paths and prompt-tool model-generated arguments never invent a filesystem grant;
- relative paths need no task path grant and remain under the configured sandbox;
- Safety gates and connector/provider configuration are read-only warnings;
- editing/removing requirements never proposes revocation.

## 9. Dialog request, result, and Apply

Use `IWorkflowStepEditorDialogService.Show(request)` returning a nullable result. Recommended
request data:

- mode, workflow text, target/anchor index, and expected alias when editing;
- current task tool groups, capabilities, filesystem access, and Knowledge tag scope;
- task error policy and effective generation values;
- configured provider/default model and task model override;
- enough task draft data to run the same validation projection as Save without persistence.

Recommended result data:

- final canonical workflow text;
- final caret and affected index;
- operation kind, alias, and effective id;
- explicitly accepted task groups, capabilities, and path rules;
- content-free warnings and canonical-formatting flag.

Apply sequence is normative:

1. validate every included field locally;
2. materialize one `WorkflowStepDefinition` from the form;
3. for edit, prove the alias is unchanged;
4. call the pure Core Insert or Replace mutation;
5. build a complete provisional `ScheduledTask` from the Task editor request plus accepted
   proposals;
6. run `ScheduledTaskValidator.Validate` on that provisional task;
7. if valid, return one result and close;
8. if invalid, return no result, mutate nothing, keep the dialog open, focus the first relevant
   field when possible, and show the shared error without workflow content.

Do not catch unexpected exceptions as validation. The dialog host reports one sanitized incident
named `Workflow step editor`; cancellation and deterministic validation errors are not incidents.

## 10. Clean deletion

Deletion is initiated from the Task editor, not by changing the alias in the dialog.

### 10.1 Confirmation

Always confirm with content-free identity:

```text
Delete step #3 “save-report” (filesystem.write)?
This removes the step from the workflow draft. Task permissions are not removed.
```

The default focused action is Cancel. The destructive action is labelled **Delete step** and uses
the application's destructive styling.

### 10.2 Dependency analysis

Before confirmation, deterministically scan later steps with `WorkflowPlaceholderCatalog` for
references to the deleted step's effective id, including structured selectors below `.output`.
Return only dependent positions, ids, and argument names; never echo values.

When dependencies exist, the confirmation additionally states that the remaining workflow will
contain broken references and lists the dependent step identities. Do not automatically rewrite
or delete them. The user may explicitly choose **Delete anyway**; Save/Validate remains the final
guard until those references are repaired.

No dependency is inferred from arbitrary prose matching the id. Only syntactically recognized
workflow placeholders count.

### 10.3 Mutation result

After confirmation, call the pure Delete operation, apply the resulting canonical JSON as one
assisted change, and move the caret to:

- the step now occupying the deleted index;
- otherwise the preceding last step;
- otherwise the inside of the empty array.

Deletion never changes task permissions or authorization selections and never opens the step
editor. Cancel is a complete no-op.

## 11. Task editor integration and undo

Add `IWorkflowStepEditorDialogService` and its implementation to UI DI. Extend
`TaskEditorViewModel` with current-step projection and commands for insert before/after, edit, and
delete.

Before applying any non-null result, use the existing `AIEditorSnapshot`, which already captures:

- workflow text;
- task tool groups;
- sensitive capabilities;
- authorized filesystem locations.

Apply the workflow and explicitly accepted proposals under the existing loading/dirty-state guard,
then set one assisted undo snapshot. Update caret/focus through the existing selection binding.
Status text contains only operation, position, alias/id, accepted permission names, warnings, and
the reminder that Save is required.

`Undo assisted change` restores the complete snapshot atomically. Save, reload, navigation away,
or a newer assisted operation clears/replaces it under the existing rules. No-op and cancelled
operations create no snapshot and do not set `IsDirty`.

Keep raw JSON editing possible. A manual text edit after a step-editor result behaves like any
other manual draft edit; do not build a multi-level undo stack in this stage.

## 12. Reuse and compatibility

- Reuse `WorkflowDefinitionSerializer` for all parsing/serialization.
- Reuse `WorkflowSnippetBuilder.CreateUniqueId`; refactor its value construction only if the new
  form builder becomes the single source without changing Stage 17 snippet tests.
- Reuse Tool catalog item/search projections rather than copy group/description logic.
- Reuse `WorkflowPlaceholderCatalog`, `WorkflowRequirementAnalyzer`,
  `GenerationPolicyValidation`, `KnowledgeTagPolicy`, and `ScheduledTaskValidator`.
- Keep the AI Workflow Helper behavior and provider boundaries unchanged.
- Keep **Edit workflow text** focused on eligible existing long-form strings. The full step editor
  now owns model, context, exact tool aliases, Knowledge scope, typed short values, policies, and
  optional arguments.
- Existing task JSON and string-form steps load without migration.
- No package addition is expected. Use CommunityToolkit.Mvvm and existing WPF/theme resources.

## 13. Accessibility, theme, privacy, and performance

- Follow the active Light/Dark application theme and existing focus visuals.
- Provide accessible names/help text for tool search, every generated field, optional Include
  toggles, authorization proposals, and destructive confirmation.
- Tab order follows visual/catalog argument order. Enter must not accidentally apply from a
  multiline or structured JSON editor. Escape initiates Cancel/discard behavior.
- Validation messages are announced and associated with their fields; color is never the only
  signal.
- The dynamic argument list scrolls independently, supports keyboard-only use, and does not clip
  at 100%, 150%, or 200% Windows scaling.
- Tool selection/filtering and form rebuilding run in memory on the UI thread and remain
  responsive for the current catalog size. No general-purpose UI reflection or assembly scan
  occurs per keystroke.
- Never log or incident-report request/result objects. Unexpected errors use operation name,
  exception type, and correlation id only.

## 14. Required implementation sequence

The expected file delta is deliberately bounded. Exact record/result types may be grouped when
that improves readability, but do not place Core JSON logic in WPF classes.

Likely new Core files:

- `WorkflowStepEditingService.cs`;
- `WorkflowStepLocation.cs` and typed location/mutation result contracts;
- `WorkflowStepDependencyAnalyzer.cs`;
- `WorkflowArgumentEditorHint.cs` when an explicit hint enum is needed.

Likely new UI files:

- `Services/IWorkflowStepEditorDialogService.cs`;
- `Services/WorkflowStepEditorDialogService.cs`;
- request/result contracts under `Services/`;
- `ViewModels/WorkflowStepEditorViewModel.cs`;
- typed argument, tool-selection, authorization-proposal, and Knowledge-tag item ViewModels;
- `WorkflowStepEditorWindow.xaml` and its thin code-behind.

Primary modified files:

- Core Tool Map metadata/catalog, validator, snippet/caret services;
- `App.xaml.cs` DI registration;
- `TaskEditorViewModel.cs` and `TaskEditorPage.xaml`;
- existing Tool catalog projections only where reuse removes duplication;
- focused workflow/Core/Task-editor test files plus new dedicated step-editor tests;
- documentation listed in §16.

Implement in this order so Terra can keep each change reviewable:

1. Add failing Core tests for caret location, insert/replace/delete invariants, immutable aliases,
   Unicode spans, dependency discovery, and preservation of non-target steps.
2. Extract/refactor the shared top-level JSON span service and implement the pure Core editing
   service. Keep existing Stage 17 insertion tests green.
3. Extend argument metadata with reflected value type/default/bounds/hint and strengthen catalog
   contract tests. Populate synthetic AI metadata explicitly.
4. Add pure form-draft conversion tests covering every argument kind, optional presence, defaults,
   invariant parsing, objects/arrays, LF normalization, and no example-as-default behavior.
5. Implement request/result contracts, dialog service, ViewModel modes, generated field items, and
   DataTemplates without Task editor integration.
6. Add `ai.prompt` model, generation, exact tools, Knowledge scope, and authorization review.
7. Integrate caret status and insert/edit commands into `TaskEditorViewModel` and XAML, including
   focus/caret and assisted undo.
8. Add deletion confirmation, dependency warnings, Delete anyway behavior, and undo.
9. Reconcile the old Add-step/Tool-catalog entry points and remove duplicated UI-only construction
   logic where safe. Do not weaken the Tool catalog's reference function.
10. Complete ViewModel, UI contract, accessibility/theme, privacy, and Task-editor regression tests.
11. Update documentation and architecture ledger, then run the complete gate and manual UI matrix.

## 15. Required tests

Add deterministic non-integration coverage for at least the following.

### 15.1 Location and mutation

- empty array, one element, multiple elements, string-form and object-form steps;
- caret at zero, inside `[`, on element opening token, inside nested args/string, on comma,
  between elements, after last element, at end, and out of range;
- CRLF, LF, non-ASCII aliases/values, emoji/surrogate pairs, and UTF-8/UTF-16 returned positions;
- invalid JSON, non-array root, trailing content, and element-count/span mismatch;
- insert before/after exact indices and empty-array index 0;
- replace preserves all non-target semantic values and rejects changed/stale alias;
- delete first/middle/last/only element and deterministic returned caret;
- canonical serialization never locates an element by serialized string equality.

### 15.2 Metadata and generated fields

- every catalog argument has compatible value type/kind/editor mapping;
- reflected optional defaults match CLR defaults and are JSON-safe;
- synthetic AI arguments provide equivalent metadata;
- invalid bounds/default/hint catalog fixtures fail fast;
- every argument kind selects the expected DataTemplate/editor kind;
- all arguments appear, including those excluded only from the workflow-text editor;
- examples remain help only;
- optional omitted/present/null/default round trips;
- integer/number invariant parsing, overflow and non-finite rejection;
- enum allowed values, Boolean, structured object/array parsing, multiline LF normalization;
- placeholder support and insertion use shared metadata/catalog.

### 15.3 Step semantics

- unique id generation and duplicate-id rejection;
- common policy omission, Continue/Stop, Retry count/backoff visibility and validation;
- generation overrides independently inherit/persist and reject invalid ranges;
- model inherit/explicit/blank/trim behavior;
- insert mode tool change discards instead of maps old fields after confirmation;
- edit mode exposes no selector and Core rejects alias mutation;
- string-form no-op remains no-op; an effective edit promotes correctly.

### 15.4 Prompt tools, Knowledge, and authorization

- exact aliases include only agent-callable catalog tools in stable order;
- empty aliases preserve no-tool behavior;
- missing groups/capabilities/path rules start unchecked and are returned only when accepted;
- dynamic/model-generated paths create warnings and no invented grants;
- Knowledge scope hidden without `knowledge.search`, inherits by omission, and narrows only to task
  tags;
- stale/unauthorized/empty narrowing is rejected before Apply;
- direct `knowledge.search` tag picker preserves optional call narrowing;
- editing/deleting requirements never revokes permissions;
- Safety and connector/provider states are never mutated.

### 15.5 Task editor and deletion

- command CanExecute reacts to text/caret/current-step changes without dirtying the draft;
- insert/edit result application updates workflow and explicitly accepted permissions atomically;
- no-op, validation failure, Cancel, and window close leave draft/undo/dirty state unchanged;
- dependency scan recognizes valid later output placeholders and ignores prose/earlier steps;
- normal delete confirmation, dependent Delete anyway, and cancellation;
- deletion changes no permission selection;
- assisted undo restores workflow, groups, capabilities, and paths after every operation;
- Save/Validate still catches broken references left by Delete anyway;
- deterministic errors create no incident; unexpected host failures create one sanitized incident.

### 15.6 UI and manual matrix

- keyboard-only tool selection, generated fields, optional toggles, Apply/Cancel, and delete;
- screen-reader names and field-associated errors;
- active Light and Dark themes, wrapping, scrolling, and 100/150/200% scaling;
- long prompt/SQL/JSON values, many arguments, many exact prompt tools, and many Knowledge tags;
- current step and caret restored visibly after each mutation;
- no workflow values appear in normal logs or incidents.

Normal tests make no Ollama, OpenAI, connector, filesystem-content, or internet call.

## 16. Documentation and architecture ledger

Update at implementation time:

- `README.md`: primary step editor flow, immutable existing alias, delete/recreate rule, clean
  deletion, exact prompt tools, Knowledge narrowing, authorization opt-ins, canonical formatting,
  raw JSON fallback, and Save/undo boundary;
- English and French user guides with screenshots or precise command walkthroughs;
- `Docs/WhatsWhere.md` with Core structure/mutation and UI form-generation ownership;
- `Docs/ARCHITECTURE-LEDGER.md` with metadata additions, caret rule, alias immutability, deletion
  dependency policy, public contracts, and gate evidence;
- `Docs/Prompts/00-Shared-Specification.md` with a concise Stage 28 addendum;
- `Docs/Prompts/README.md` with Stage 28 status and ordering.

Document clearly: an existing step cannot be changed to another tool. Delete it and insert a new
step instead.

## 17. Explicit non-goals

Do not add in Stage 28:

- alias mutation, step conversion, argument-name mapping between tools, or automatic data migration
  from a deleted step to a new one;
- drag/drop reorder, move up/down, copy/paste step, multi-selection, batch edit, workflow graph, or
  nested workflow structures;
- an alternative text/visual persistence format;
- LLM-generated forms, validation, permissions, values, ids, deletion repairs, or tool choices;
- arbitrary WPF control instantiation from attributes, runtime-loaded assemblies, DLL plug-ins,
  scripts, reflection invocation of tool targets, or a general property-grid package;
- automatic permission granting/revocation, Safety mutation, wildcard Knowledge scope, path probing,
  connector setup, or tool execution;
- automatic rewriting of downstream placeholders after id changes or deletion;
- a multi-level undo/redo system;
- package upgrades or unrelated workflow runtime changes.

## 18. Stage gate

Implementation is complete only when both commands pass exactly with repository-required serial
MSBuild execution:

```powershell
dotnet build OD.MyLocalAgent.sln --configuration Release -m:1
dotnet test OD.MyLocalAgent.sln --configuration Release --no-build --filter "Category!=Integration" -m:1
```

Also perform a clean self-contained single-file `win-x64` UI publication check and a manual Task
editor smoke test in both themes. Integration tests and live model catalogs are not required unless
the user explicitly requests them.

## 19. Acceptance checklist

- [ ] Caret location deterministically identifies the current top-level step with Unicode-safe
  UTF-8/UTF-16 mapping.
- [ ] Insert before and after choose a tool from the declarative Tool Map and open a complete typed
  form generated on demand.
- [ ] Edit shows the existing alias read-only and neither UI nor Core permits changing it.
- [ ] Changing tools is possible only by deleting the old step and inserting a new one.
- [ ] Delete removes exactly one parsed element, confirms explicitly, warns about recognized
  downstream references, and never rewrites other steps or permissions.
- [ ] Required/optional state, types, defaults, allowed values, examples, placeholders, and local
  path semantics come from the single catalog projection.
- [ ] Examples are not silently persisted as values; optional omission round-trips.
- [ ] `ai.prompt` exposes prompt arguments, includeContext, model, generation overrides, exact
  agent-callable aliases, and task-bounded Knowledge tags.
- [ ] Missing groups, capabilities, and statically inferred path grants are unchecked explicit
  proposals; no authorization or Safety state changes implicitly.
- [ ] Insert/edit Apply validates and mutates the complete Task-editor draft atomically; errors and
  cancellation are no-ops.
- [ ] Existing workflow JSON requires no migration and non-target step semantics are preserved.
- [ ] Assisted undo restores workflow, groups, capabilities, and paths after insert, edit, or
  delete.
- [ ] Raw JSON, AI Helper, Tool catalog, and focused long-text editing remain usable and coherent.
- [ ] No workflow content or authoring values leak into logs/incidents.
- [ ] Accessibility, Light/Dark themes, scaling, Release build, non-integration tests, publication,
  documentation, and architecture ledger are complete.

## 20. Before finishing implementation

Search production code and tests for all of the following and reconcile every result:

```text
WorkflowCaretInsertionService
TopLevelSpans
WorkflowSnippetBuilder
AddWorkflowStep
SelectedWorkflowAlias
WorkflowCaretIndex
WorkflowDefinitionSerializer
WorkflowStepDefinition
WorkflowStepDefinitionJsonConverter
ToolArgumentAttribute
ToolArgumentDefinition
WorkflowArgumentKind
IsExcludedFromWorkflowTextEditor
WorkflowRequirementAnalyzer
WorkflowPlaceholderCatalog
toolAliases
knowledgeAccess
includeContext
generationOptions
AIEditorSnapshot
```

Then prove by tests and inspection that no edit path can submit a different alias, no delete path
rewrites another element or removes a permission, no UI-local table duplicates Tool Map metadata,
and every successful result is applied to the Task editor as one undoable in-memory change before
Save.
