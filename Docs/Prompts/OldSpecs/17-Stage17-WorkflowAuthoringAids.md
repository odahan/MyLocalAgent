# STAGE 17 — Workflow Authoring Aids (Tool Catalog and Prompt Editor)

> Run only after **Stage 16** is finished. Stages 9 and 10 remain abandoned proposals and are
> neither prerequisites nor in scope. The task-owned sequential workflow JSON remains the
> persisted source of truth and the Task editor remains its only editor.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§20. §20 is the
   normative contract for this stage.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs, package versions, privacy
   boundaries, and Tool Map metadata decisions are binding.
3. The current `ToolMap`, `ToolDefinition`, `LocalPathArgumentDefinition`, `ToolGroups`,
   `WorkflowStepDefinition`, `WorkflowStepDefinitionJsonConverter`,
   `WorkflowDefinitionSerializer`, `ScheduledTaskSerializer`, `ScheduledTaskValidator`,
   `WorkflowPlaceholderCatalog`, the Stage 11 path normalization, the Stage 13 helper
   (`AIWorkflowHelperWindow`, `AIWorkflowHelperViewModel`, `AIWorkflowHelperDialogService`,
   `WorkflowAssistantPromptBuilder`), `TaskEditorPage.xaml` with its workflow card,
   `TaskEditorViewModel` (`AddWorkflowStep`, `FormatWorkflow`, `ValidateWorkflow`,
   `ApplyAIResult`, `AIEditorSnapshot`), `IDesktopInteractionService`, the theme resources,
   tests, README, manuals, and samples.

## 2. Mission

The workflow card currently offers alias-driven **Add step**, **Format JSON**, **Validate**,
and the Stage 13 **AI Helper** / **AI Explain** windows. Two deterministic authoring aids are
still missing, and both are pure application logic — **this stage introduces no new LLM call,
no new provider usage, and no new package**.

Add:

- a **Tool catalog** window: a browsable, searchable reference of every Tool Map alias with its
  purpose, tool group, ordered arguments, argument types, required/optional status, examples,
  Safety gate, filesystem-authorization needs, and AI-step status; selecting an entry inserts a
  correctly formatted step snippet **at the caret** of the workflow JSON editor, leaving real
  argument values to the user;
- a **Prompt editor** window: a left sidebar listing every `ai.prompt` step of the current
  workflow and, in the remaining space, a true multiline editor for the selected step's prompt,
  so long prompts can finally be read and written like text instead of one unreadable JSON
  line. Applying rewrites only the `prompt` argument of the edited steps.

Both windows are memory-only, never execute a step, never call a model, and never persist
anything by themselves; the existing **Save** command remains the only persistence path.

## 3. Product decisions (locked by the product owner — do not re-open)

| # | Decision |
|---|---|
| S17-D1 | **Deterministic aids only.** Neither window performs an LLM call, an agent run, a tool invocation, a skill call, a disk read of a referenced path, or a network request. Everything they display is derived from Tool Map metadata, the shared placeholder catalog, and the current editor text. |
| S17-D2 | **JSON stays the source of truth.** Both windows produce a new value for the existing `WorkflowText` (plus, for the catalog, an optional set of tool groups to enable). No new persisted field, file, setting, or page is introduced. |
| S17-D3 | **One catalog, in Core.** `ToolMap` is extended with an English description per alias and structured metadata per argument. The Stage 13 `WorkflowAssistantPromptBuilder` consumes the same metadata. A second, UI-only alias or argument table is forbidden. |
| S17-D4 | **Caret plumbing is view-only.** The workflow `TextBox` exposes its caret through a small attached behavior bound to the view model. All snippet building, offset mapping, insertion, and prompt rewriting live in non-UI, unit-testable classes. |
| S17-D5 | **Structured insertion first.** When the current JSON parses, insertion is performed on the parsed array at the step position determined by the caret, and the whole array is re-serialized canonically. When it does not parse, the canonical snippet text is inserted verbatim at the caret and the user is told that separators must be fixed manually. |
| S17-D6 | **Repeatable modal catalog.** The Tool catalog is a modal dialog that edits a working copy of the workflow text: **Insert** applies the snippet and keeps the window open for another insertion, **Insert and close** applies and closes, **Close** returns the accumulated result, **Cancel** discards every insertion made in the session. |
| S17-D7 | **Warn, never silently widen.** If an inserted step belongs to a tool group absent from the task whitelist, the catalog shows a warning and an unchecked *Enable this tool group* control; accepted groups are applied only when the session result is applied. A referenced disabled Safety flag is shown as a warning only: the catalog never changes a global Safety setting. |
| S17-D8 | **Prompt editor scope.** The Prompt editor lists exactly the steps whose alias is `ai.prompt` (case-insensitive), in declaration order, keyed by position. It edits only their `prompt` argument. It never adds, deletes, reorders, renames, or re-types a step and never edits other arguments, ids, error policies, or generation options. |
| S17-D9 | **Lossless multiline round trip.** The editor shows the prompt with real line breaks; applying stores the text with its real line breaks, which `System.Text.Json` writes as `\n` escapes, so each prompt still occupies exactly one physical line of JSON. No lossy flattening to spaces, no `\r`, no trailing-whitespace rewriting, no re-wrapping. Re-opening the editor must display exactly what was applied. |
| S17-D10 | **Batch edit, single apply.** Several prompts may be edited in one session. **Apply** rewrites all modified prompts at once; **Cancel** discards all of them. Closing the window with unsaved modifications requires an explicit confirmation. |
| S17-D11 | **One shared one-level undo.** The Stage 13 editor snapshot becomes the single assisted-change snapshot used by AI Helper, Tool catalog, and Prompt editor. The button is renamed **Undo assisted change** and restores workflow JSON, tool selections, and authorized locations atomically. The newest assisted change replaces the previous snapshot; a successful save, reload, or navigation away clears it. |
| S17-D12 | **Canonical formatting is accepted.** Any successful structured insertion or prompt application re-serializes the array with `ScheduledTaskSerializer.Options`, exactly like the existing **Format JSON** command. Users are told that manual formatting is normalized. |
| S17-D13 | **Privacy.** Prompt text, workflow JSON, argument values, and absolute paths are never logged, incident-reported, cached, or copied anywhere except through an explicit user Copy action. Exceptions are reported with sanitized operation metadata and a correlation id only. |
| S17-D14 | **No new packages.** Search, list virtualization, syntax highlighting, and text editing use existing WPF, Material Design, and MVVM Toolkit capabilities already pinned in `Directory.Packages.props`. |

## 4. Critical constraints (restated)

- R1, R2, R4, R5, R7, R9, R10 and addenda §12–§20 remain binding. Dark-only theme, English-only
  text, warning-clean complete files, English XML documentation on every public API, MVVM
  Toolkit source generators only, and no business logic in window or page code-behind.
- Existing task serialization, sequential execution, Tool Map aliases and semantics,
  placeholder behavior, tool whitelisting, Safety gates, filesystem sandboxing, per-task
  authorization, and the Stage 13 helper flows remain functionally unchanged.
- Extending `ToolDefinition` must not break its current consumers. Keep `Arguments` and
  `OptionalArguments` observably identical (derive them from the new argument metadata) so
  `ToolRegistry`, the workflow engine, the validator, the Stage 13 analyzer, and existing tests
  keep passing without behavioral change.
- Validation stays shared: the catalog and the prompt editor reuse `WorkflowDefinitionSerializer`,
  `ScheduledTaskValidator`, and `WorkflowPlaceholderCatalog`. No aid-only validator may disagree
  with Save/Validate.
- Every `dotnet restore`, `dotnet build`, and `dotnet test` command explicitly uses `-m:1`.

## 5. Scope

### 5.1 Tool Map metadata extension (Core)

Add one record and extend `ToolDefinition`:

```csharp
public enum WorkflowArgumentKind { String, Integer, Number, Boolean, Enumeration }

public sealed record ToolArgumentDefinition(
    string Name,
    WorkflowArgumentKind Kind,
    bool IsRequired,
    string Description)
{
    public string? Example { get; init; }
    public IReadOnlyList<string> AllowedValues { get; init; } = [];
    public bool SupportsPlaceholders { get; init; } = true;
}
```

`ToolDefinition` gains:

- `string Description` — one English sentence describing what the alias does and what its output
  contains;
- `IReadOnlyList<ToolArgumentDefinition> ArgumentDefinitions` — ordered, one entry per argument;
- `Arguments` and `OptionalArguments` become projections of `ArgumentDefinitions` preserving
  their current values and order.

Every entry of `ToolMap.Definitions` must be completed with a description, an argument kind, a
required flag, an argument description, and a realistic example. Enumeration arguments (such as
the calendar period) must list the exact accepted values of the underlying C# parameter type.
`ai.prompt` documents `prompt` (required string, placeholder-friendly) and `includeContext`
(optional boolean, default `false`, with the §15.2 context-size caveat).

Extend `ToolMapTests` so the existing reflection consistency check also asserts, for every
definition: a non-empty description, one argument definition per real method parameter, a kind
compatible with the parameter's CLR type, `IsRequired` consistent with the previous
`OptionalArguments` content, and enumeration values equal to `Enum.GetNames` of the parameter
type. The Stage 13 generation system prompt must be rebuilt from the enriched metadata, and its
prompt version must be incremented and recorded.

### 5.2 Workflow editor caret plumbing (UI)

- Name the workflow JSON `TextBox` and attach a view-only behavior exposing `CaretIndex`,
  `SelectionStart`, and `SelectionLength` as two-way bindings to `TaskEditorViewModel`
  properties (`WorkflowCaretIndex`, …). The behavior contains no business logic.
- The view model clamps the caret to the current text length whenever `WorkflowText` changes
  programmatically, and requests focus plus caret placement after an insertion so the user lands
  on the inserted step.
- No other page or control is modified.

### 5.3 Tool catalog window (UI)

New modal window opened from the workflow card by a **Tool catalog…** button, always enabled,
through a dedicated dialog service following the Stage 13 pattern
(`IWorkflowToolCatalogDialogService.Show(request) → result?`). The request carries the current
workflow text, caret index, task tool groups, task filesystem rules, and the current
`SafetyOptions`. The result carries the final workflow text, the final caret index, and the set
of tool groups the user accepted to enable.

Layout:

- a left column with a **search box** (case-insensitive match over alias, group, description and
  argument names) and a list grouped by tool group in the fixed order gmail, calendar, dropbox,
  filesystem, http, web, ai; each entry shows the alias, a ⚠ marker when it is Safety-gated, a
  lock marker when its group is not whitelisted for the task, and its short description;
- a right pane for the selected alias showing: description, tool group, agent function name (or
  "workflow-only AI step"), the ordered argument table (name, kind, required/optional,
  description, example, accepted values, placeholder support), static filesystem-authorization
  requirements derived from `LocalPathArguments`, the Safety flag with its **current** enabled or
  disabled state read from `SafetyOptions`, and the whitelist state for the current task;
- insertion options: *Include optional arguments*, *Include error policy block*
  (`onError`, `retryCount`, `retryBackoffSeconds`), *Include generation options* (AI steps only,
  disabled otherwise), and a read-only preview of the exact snippet that will be inserted;
- a second tab **Placeholders** listing every entry of `WorkflowPlaceholderCatalog` with its
  meaning and an **Insert placeholder** action that inserts the placeholder text at the caret;
- a status line reporting the outcome of each insertion, and the commands **Insert**,
  **Insert and close**, **Close**, and **Cancel** per S17-D6.

Warnings shown before and after insertion, per S17-D7: missing tool group with an unchecked
*Enable this tool group* control, disabled Safety flag, and a note that any absolute path typed
later may still require a task authorization rule (Stage 11). None of these blocks insertion.

### 5.4 Snippet building and caret insertion (testable, non-UI)

Create two application classes with no WPF dependency.

**Snippet builder.** From a `ToolDefinition`, the insertion options, and the current workflow,
it produces a `WorkflowStepDefinition`:

- `step` = the exact alias as declared in the Tool Map;
- `id` = the existing `AddWorkflowStep` uniqueness algorithm (alias with `.` replaced by `-`,
  then `-2`, `-3`, … until unique among effective ids);
- required arguments always present, optional arguments only when requested, in Tool Map order;
- placeholder values by kind: empty string for `String`, `0` for `Integer` and `Number`, `false`
  for `Boolean`, the first accepted value for `Enumeration`; `ai.prompt.prompt` keeps the current
  editor default sentence and `ai.prompt.includeContext` keeps its current default of `true`;
- the error-policy block, when requested, with `onError` = the task's current error policy,
  `retryCount` = 1, `retryBackoffSeconds` = 2;
- `generationOptions`, when requested on an AI step, with the current effective task temperature
  and top-p values, never with context-window properties (§17).

**Insertion service.** Given the current text, the caret index, and the new step:

1. Try `WorkflowDefinitionSerializer.Deserialize`. On success, compute the top-level element
   spans of the array with a `Utf8JsonReader` (`TokenStartIndex` plus consumed bytes), translate
   the caret character offset to a byte offset, insert the new step **after** the element whose
   span ends at or before the caret (index `0` when the caret precedes the first element, append
   when it follows the last), re-serialize canonically, and return the new text plus a caret
   index positioned at the start of the inserted element.
2. On parse failure, return the canonically formatted snippet text inserted verbatim at the
   caret, indented with the leading whitespace of the caret's line, a caret index at the start of
   the inserted text, and an explicit warning that the JSON is currently invalid and separators
   must be corrected by hand.

Both paths are pure functions over strings and are covered by unit tests, including empty
arrays, `[]` with the caret inside, string-form steps, an in-string caret, and a caret at index
`0` or at the end of the text.

### 5.5 Prompt editor window (UI)

New modal window opened from the workflow card by an **Edit AI prompts…** button.

- The button is enabled only when `WorkflowText` parses and contains at least one `ai.prompt`
  step. Clicking it with unparseable JSON shows the deterministic parse error in the existing
  validation text area and opens nothing.
- Dialog service `IWorkflowPromptEditorDialogService.Show(request) → result?`. The request
  carries the parsed workflow and the task draft values needed for validation; the result
  carries the new canonical workflow text.
- Left sidebar: one entry per `ai.prompt` step, in declaration order, keyed by **position** (ids
  are display only, so duplicate ids cannot confuse the mapping). Each entry shows the one-based
  position, the effective id, an indicator when the id is implicit, a single-line preview of the
  prompt with control characters rendered visibly, and a modified marker.
- Right pane: a multiline editor (`AcceptsReturn`, `AcceptsTab`, wrapping enabled, monospace,
  vertical scrolling) bound to the selected entry's working text, a read-only metadata strip
  (`includeContext`, `onError`, retry values, per-step generation options), a character and line
  counter, and a **Placeholders** side list identical in content to §5.3 that also lists the
  effective ids of the steps preceding the selected step, with insertion at the editor caret.
- A string-form `ai.prompt` step or an object-form step without a `prompt` argument opens with
  an empty editor and is converted to object form with a `prompt` argument on apply.
- Commands: **Apply**, **Revert step**, **Cancel**, **Close**. Apply is disabled while any
  entry's prompt is empty or contains a placeholder rejected by the shared validation, and every
  such error names the offending step position and id.

### 5.6 Prompt read/write round trip (testable, non-UI)

Create one application class exposing two pure operations:

- **Extract** — from a parsed workflow, return the ordered `ai.prompt` entries with position,
  explicit id, effective id, prompt text exactly as deserialized (JSON escapes already decoded,
  so `\n` is already a real line break), `includeContext`, and the step's other display metadata.
- **Apply** — from the parsed workflow and the edited entries keyed by position, return a new
  workflow whose matching steps have their `prompt` argument replaced, every other argument,
  property, and step left byte-identical in meaning, and the array re-serialized canonically.

Newlines are preserved verbatim; the serializer emits them as `\n` escapes, so every prompt
still occupies a single physical JSON line (S17-D9). Round-tripping extract → apply without an
edit must return a workflow equal to the canonical serialization of the input.

### 5.7 Task editor integration, undo, and privacy

- `TaskEditorViewModel` gains `OpenToolCatalogCommand` and `OpenPromptEditorCommand`, both
  following the existing helper command shape: build the request, call the dialog service, and
  apply the returned result.
- Applying a catalog result sets `WorkflowText`, the caret index, and the union of current and
  accepted tool groups. Applying a prompt-editor result sets `WorkflowText` only.
- Both applications take the shared one-level snapshot of §S17-D11 before mutating state, so
  **Undo assisted change** restores workflow JSON, tool selections, and authorized locations
  atomically. Rename the existing button and its command accordingly and keep its visibility
  logic.
- The validation text area reports what happened: inserted alias and id, enabled tool groups,
  number of prompts updated, canonical reformatting, and any warning of §5.3 or §5.4.
- No prompt text, argument value, workflow JSON, or path enters Serilog, incidents, telemetry,
  or the clipboard except through an explicit user Copy action. Dialog-infrastructure failures
  are reported once through the existing incident pipeline with sanitized source
  `Workflow tool catalog` or `Workflow prompt editor`.

### 5.8 Worked examples (normative reference for §6)

These examples fix the exact expected shapes. Tests must assert them.

**Catalog insertion, structured path.** Current editor text, caret placed anywhere inside the
first element:

```json
[
  {
    "step": "filesystem.read",
    "id": "source",
    "args": {
      "path": "inbox/source.txt"
    }
  },
  "ai.summarize"
]
```

Inserting `filesystem.write` with optional arguments excluded and no error-policy block yields
the canonical text below, with the returned caret index at the start of the inserted element:

```json
[
  {
    "step": "filesystem.read",
    "id": "source",
    "args": {
      "path": "inbox/source.txt"
    }
  },
  {
    "step": "filesystem.write",
    "id": "filesystem-write",
    "args": {
      "path": "",
      "content": ""
    }
  },
  "ai.summarize"
]
```

Only the generated `id` replaces `.` with `-`; `step` always holds the real alias. Inserting the
same alias a second time produces the id `filesystem-write-2`.

**Catalog insertion with options.** `calendar.create` with the error-policy block requested, on
a task whose current policy is `ContinueOnError`:

```json
{
  "step": "calendar.create",
  "id": "calendar-create",
  "args": {
    "summary": "",
    "start": "",
    "end": "",
    "description": ""
  },
  "onError": "ContinueOnError",
  "retryCount": 1,
  "retryBackoffSeconds": 2
}
```

`ai.prompt` with optional arguments included and generation options requested:

```json
{
  "step": "ai.prompt",
  "id": "ai-prompt",
  "args": {
    "prompt": "Use the workflow context to complete this step.",
    "includeContext": true
  },
  "generationOptions": {
    "temperature": 0.4,
    "topP": 0.95
  }
}
```

**Catalog insertion, fallback path.** With the unparseable text `[ { "step": ` and the caret at
the end, the result is that text followed by the canonical snippet inserted verbatim, plus the
warning that the JSON is invalid and separators must be fixed manually. No reformatting of the
surrounding text occurs.

**Prompt round trip.** The persisted step keeps one physical JSON line:

```json
{
  "step": "ai.prompt",
  "id": "review",
  "args": {
    "prompt": "You are reviewing the working notes of the day.\n\nRules:\n- Use only the supplied context; never invent a fact.\n\nProduce a summary and the next actions.",
    "includeContext": true
  }
}
```

The Prompt editor displays it as:

```text
You are reviewing the working notes of the day.

Rules:
- Use only the supplied context; never invent a fact.

Produce a summary and the next actions.
```

Applying without editing must reproduce the JSON above byte for byte after canonical
serialization. `Docs/samples/multiline-prompt-report.json` is the shipped example of this shape
and must remain parseable by the stage gate.

## 6. Required tests

Add deterministic tests, without a live provider call, for at least:

1. Every Tool Map entry has a description, one argument definition per real method parameter,
   consistent kinds, consistent required flags, and correct enumeration values.
2. `Arguments` and `OptionalArguments` projections keep their pre-Stage-17 values for every
   alias.
3. The Stage 13 generation prompt is built from the enriched metadata, contains the new
   descriptions and argument kinds, and exposes an incremented prompt version.
4. Snippet building for a no-argument alias, a required-only alias, an alias with optional
   arguments included and excluded, a Safety-gated alias, an enumeration argument, `ai.prompt`
   defaults, the error-policy block, and AI generation options.
5. Snippet id uniqueness against existing string-form and object-form steps.
6. Structured insertion at the beginning, between two steps, at the end, into an empty array,
   with the caret inside a string literal, and with an out-of-range caret.
7. Raw-fallback insertion on unparseable JSON preserves the surrounding text, indents from the
   caret line, and returns the documented warning.
8. Inserted result is always canonical after structured insertion, and the returned caret index
   points at the inserted step.
9. Missing tool group produces a warning and an unaccepted proposal; accepting it yields the
   union of groups; declining changes nothing.
10. A Safety-gated alias reports the real enabled or disabled state and never mutates
    `SafetyOptions`.
11. Catalog session semantics: several insertions accumulate, **Cancel** discards them all,
    **Close** returns the accumulated text and caret.
12. Placeholder insertion uses `WorkflowPlaceholderCatalog` entries with no hard-coded UI list.
13. Prompt extraction returns only `ai.prompt` steps in declaration order, keyed by position,
    with duplicate ids handled unambiguously.
14. Prompt extraction decodes `\n` escapes into real line breaks and the preview renders control
    characters without altering stored text.
15. Applying a multiline prompt keeps real line breaks, produces exactly one physical JSON line
    per prompt, and round-trips without loss over extract → apply → extract.
16. Applying modifies only the targeted `prompt` arguments and leaves ids, other arguments,
    error policies, generation options, string-form steps, and step order unchanged.
17. A string-form or `prompt`-less `ai.prompt` step is promoted to object form with the entered
    prompt.
18. Apply is blocked by an empty prompt or an invalid placeholder, with an error naming the step
    position and id, and shared validation is used rather than an editor-only rule.
19. The prompt-editor button is disabled for unparseable JSON and for a workflow without
    `ai.prompt`.
20. Task editor integration: caret clamping on programmatic text change, application of catalog
    and prompt results, single shared snapshot, atomic **Undo assisted change** across workflow,
    tools, and authorized locations, and the fact that neither window persists a task.
21. No aid performs a provider, agent, tool, skill, filesystem, or network call.
22. Every §5.8 worked example is asserted verbatim, including generated ids, canonical output,
    the fallback warning, and the `Docs/samples/multiline-prompt-report.json` round trip.

## 7. Documentation

Update:

- shared specification §20;
- `README.md`: the two new buttons, the catalog's content and insertion semantics, the caret
  behavior, canonical reformatting, tool-group warning and opt-in, the prompt editor's sidebar,
  multiline semantics with `\n` escapes, batch apply, and the renamed **Undo assisted change**;
- both standalone user manuals with the same user-facing behavior;
- `Docs/theme-audit.md` for both windows: search box, grouped list, detail pane, argument table,
  option check boxes, snippet preview, warning banners, sidebar, multiline editor, counters, and
  status line;
- `Docs/ARCHITECTURE-LEDGER.md` with the Stage 17 entry: enriched Tool Map contract, new public
  API surface, prompt-version increment, caret behavior rationale, insertion algorithm, prompt
  round-trip guarantee, privacy boundaries, exact gate output, GUI verification status, and
  residual risks;
- `Docs/Prompts/README.md` with the Stage 17 row;
- `Docs/samples/multiline-prompt-report.json` remains the reference example of a multiline
  prompt stored as `\n` escapes on one physical line; keep it parseable and referenced from the
  README's samples list;
- acceptance material with catalog insertion, invalid-JSON fallback, tool-group opt-in, multiline
  prompt editing, batch apply, and undo scenarios.
- User Guide, FR and EN

Document these residual risks honestly:

- inserted snippets are syntactically correct but semantically empty; the user must supply real
  values, and validation still runs at Save;
- canonical re-serialization discards the user's manual formatting;
- the fallback insertion path cannot repair invalid JSON;
- a very long prompt remains a single JSON line on disk and stays hard to review outside the
  prompt editor;
- static Tool Map metadata cannot predict runtime failures, authorization outcomes, or disabled
  Safety gates.

## 8. Stage gate (all must pass — show command output)

1. `dotnet restore OD.MyLocalAgent.sln -m:1`.
2. `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release` — zero warnings
   and zero errors.
3. `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release` — all
   deterministic tests green; the explicit local Ollama integration probe may remain skipped.
4. Parse every JSON file under `Docs/samples`.
5. `git diff --check`.

**Stage-17 acceptance addendum:**

- [ ] The Tool catalog lists every Tool Map alias with description, typed arguments, examples,
      Safety state, authorization needs, and whitelist state.
- [ ] Selecting an alias and inserting places a correctly formatted step at the caret position,
      with a unique id and empty typed argument placeholders.
- [ ] Several insertions in one catalog session accumulate; Cancel discards them all.
- [ ] Inserting a step of a non-whitelisted group warns and enables that group only after
      explicit opt-in; no global Safety setting ever changes.
- [ ] Insertion into invalid JSON falls back to verbatim text insertion with an explicit warning.
- [ ] The Prompt editor lists every `ai.prompt` step and displays each prompt as readable
      multiline text.
- [ ] A multiline prompt can be written naturally, applied, reopened, and shows exactly the same
      text, while the JSON keeps one physical line per prompt.
- [ ] Applying updates only the edited prompts; Cancel discards every pending edit.
- [ ] **Undo assisted change** restores workflow JSON, tools, and authorized locations for the
      AI Helper, the catalog, and the prompt editor alike.
- [ ] No LLM call, workflow execution, tool invocation, new package, new persisted field, or new
      page was introduced.

## 9. Before you finish

1. Append the Stage 17 entry to `Docs/ARCHITECTURE-LEDGER.md`.
2. Record the exact file-tree delta, the enriched Tool Map surface, the incremented assistant
   prompt version, and the gate results.
3. State whether the WPF application was actually launched and whether catalog insertion, the
   invalid-JSON fallback, tool-group opt-in, multiline prompt editing, batch apply, and undo were
   exercised.
4. Do not claim that an inserted snippet is a working step, or that static metadata predicts
   runtime authorization or Safety outcomes.
5. Do not claim GUI verification unless those flows were actually exercised.
