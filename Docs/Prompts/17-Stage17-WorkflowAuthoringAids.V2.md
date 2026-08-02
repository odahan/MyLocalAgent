# STAGE 17 V2 — Workflow Authoring Aids

> This V2 supersedes the Stage 17 authoring prompt for implementation purposes. It is a
> standalone specification. Do not modify or delete
> Docs/Prompts/17-Stage17-WorkflowAuthoringAids.md.
>
> Run only after Stage 16 is finished. Stages 9 and 10 remain abandoned proposals and are
> neither prerequisites nor in scope. The task-owned sequential workflow JSON remains the
> persisted source of truth and the Task editor remains its only editor.

## 1. Read first (mandatory)

1. Docs/Prompts/00-Shared-Specification.md — full read, including addenda §12–§20. Where the
   existing §20 differs from this V2, this V2 is the Stage 17 implementation contract and §20
   must be updated to match it.
2. Docs/ARCHITECTURE-LEDGER.md — full read. Recorded APIs, package versions, privacy
   boundaries, the Stage 15 active-theme decision, and Tool Map metadata decisions are binding.
3. The current ToolMap, ToolDefinition, LocalPathArgumentDefinition, ToolGroups,
   WorkflowStepDefinition, WorkflowStepDefinitionJsonConverter, WorkflowDefinitionSerializer,
   ScheduledTaskSerializer, ScheduledTaskValidator, WorkflowPlaceholderCatalog, Stage 11 path
   normalization, Stage 13 helper, TaskEditorPage workflow card, TaskEditorViewModel authoring
   commands and AIEditorSnapshot, IDesktopInteractionService, theme resources, tests, README,
   manuals, and samples.
4. The existing PromptEditorPage and PromptEditorViewModel, which edit the task-level prompt.
   The new dialog in this stage must be named Workflow Prompt Editor in the UI and use
   WorkflowPromptEditor-prefixed types to prevent ambiguity.

## 2. Mission

Add two deterministic, memory-only authoring aids to the Task editor workflow card:

- a Tool catalog dialog that documents every Tool Map alias and inserts correctly formatted
  object-form workflow steps at the workflow editor caret;
- a Workflow Prompt Editor dialog that lists all ai.prompt steps and provides a true multiline
  editor for each prompt, together with explicit control over whether that step also receives
  the complete accumulated workflow context.

Neither aid calls an LLM, agent, tool, skill, connector, filesystem path, or network resource.
Neither aid executes a workflow or persists a task. Applying a dialog result only changes the
current Task editor draft. The existing Save command remains the only persistence and
scheduling path.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S17V2-D1 | Deterministic aids only. Displayed content comes only from Tool Map metadata, the shared placeholder catalog, current configuration snapshots, and current editor text. |
| S17V2-D2 | JSON remains the source of truth. No new persisted task field, file, setting, page, workflow model, or migration is introduced. |
| S17V2-D3 | One catalog in Core. Tool Map metadata is consumed by the runtime registry, the Stage 13 prompt builder, the existing Add step command, and the new catalog. UI-only alias or argument tables are forbidden. |
| S17V2-D4 | Caret plumbing is view-only. Attached behavior exposes text positions; all id generation, snippet construction, UTF-16/UTF-8 mapping, insertion, prompt extraction, prompt rewriting, line-ending normalization, and preview projection live in non-WPF, unit-testable classes. |
| S17V2-D5 | Structured insertion first. Parseable JSON is modified as a parsed top-level array and canonically serialized. Invalid JSON receives a verbatim snippet at the clamped caret with an explicit repair warning. |
| S17V2-D6 | Sequential caret rule. Inside an element means insert after that element; between elements means insert after the element on the left; before the first means index 0; after the last means append. |
| S17V2-D7 | Repeatable catalog session. Insert accumulates and keeps the dialog open; Insert and close returns the accumulated result; Close returns it only when at least one effective change exists; Cancel discards the complete session. |
| S17V2-D8 | Warn, never silently widen. Only non-AI tool groups can be proposed for task whitelisting. Missing groups require explicit opt-in. Safety settings are displayed but never changed. |
| S17V2-D9 | Built-in AI is not whitelistable. The synthetic ai group is for catalog grouping only. AI entries never show a missing-whitelist lock and ai never appears in an accepted-group result. |
| S17V2-D10 | Workflow Prompt Editor scope. It edits only the prompt and includeContext arguments of existing ai.prompt steps, keyed by declaration position. It never adds, deletes, reorders, renames, or re-types a step and never changes ids, policies, retries, generation options, or unrelated arguments. |
| S17V2-D11 | Explicit context choice. includeContext is an independent AI-prompt option, defaulted to true for a newly built ai.prompt step. It is never controlled by the generic Include optional arguments option. |
| S17V2-D12 | No automatic context inference. A direct prior-output placeholder may be used with includeContext false to send only selected data. If a direct output placeholder and includeContext true coexist, show a duplication warning but never change either value automatically. |
| S17V2-D13 | Canonical LF prompts. Workflow prompt working text uses LF line endings. CRLF and lone CR are normalized to LF; no other whitespace, wrapping, indentation, or trailing whitespace is changed. |
| S17V2-D14 | Non-empty prompts everywhere. An ai.prompt prompt that is missing, null, non-string, empty, or whitespace-only is invalid in Save, Validate, AI Helper validation, and Workflow Prompt Editor Apply. |
| S17V2-D15 | Batch edit, one apply. Several prompt/includeContext pairs may be edited in one session. Apply validates, returns one result, and closes. Cancel discards the session. There is no Close command in this dialog. |
| S17V2-D16 | One shared one-level undo. AI Helper, Tool catalog, and Workflow Prompt Editor share one assisted-change snapshot restoring workflow JSON, selected tool groups, and authorized locations atomically. |
| S17V2-D17 | Canonical formatting is accepted. Structured catalog insertion and Workflow Prompt Editor Apply serialize with ScheduledTaskSerializer.Options and may replace manual formatting. |
| S17V2-D18 | Active application theme. Both dialogs follow the current Light or Dark application theme, matching the binding Stage 15 ledger decision. They do not introduce another theme or theme switcher. |
| S17V2-D19 | Privacy. Workflow JSON, prompts, argument values, paths, selections, and dialog working copies are never logged, incident-reported, placed in telemetry, persisted, or copied by application logic except through an explicit user clipboard action. |
| S17V2-D20 | No new packages. Existing WPF, Material Design, and MVVM Toolkit capabilities are sufficient. |

## 4. Critical constraints

- R1, R2, R4, R5, R7, R9, R10 and the shared addenda remain binding. UI text, identifiers,
  comments, and XML documentation are English. Public APIs have English XML documentation.
  MVVM Toolkit source generators are used; business logic does not enter code-behind.
- Existing task serialization, sequential execution, alias resolution, Safety gates,
  whitelisting, filesystem authorization, Guardrails, generation-policy resolution, and helper
  calls remain unchanged except for the explicit shared validation tightening for empty
  ai.prompt values and duplicate argument names in §8.
- Every restore, build, and test command explicitly uses -m:1.
- No deterministic validation error is reported as an incident. Unexpected dialog
  infrastructure failures are reported once with a content-free operation name and correlation
  id. Do not pass a data-bearing exception message, inner exception, workflow, prompt, argument,
  or path to the incident reporter.

## 5. Core metadata contracts

### 5.1 Argument metadata

Add:

    public enum WorkflowArgumentKind
    {
        String,
        Integer,
        Number,
        Boolean,
        Enumeration
    }

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

ToolDefinition gains:

- string Description: one English sentence describing the operation and its output;
- IReadOnlyList<ToolArgumentDefinition> ArgumentDefinitions: ordered runtime arguments.

Arguments is the ordered projection of ArgumentDefinitions.Name. OptionalArguments is the
ordered projection where IsRequired is false.

This stage intentionally corrects one inaccurate legacy representation: text is optional for
ai.summarize, ai.extractActionItems, and ai.classifyEmail because omission uses the previous
step output. Their OptionalArguments projection therefore becomes ["text"]. All non-AI aliases
retain their previous Arguments and OptionalArguments values exactly. ai.prompt keeps prompt
required and includeContext optional.

Every argument has a non-empty description and realistic non-secret example. Enumeration
arguments list the exact Enum.GetNames values in declaration order. Non-enumeration arguments
have no AllowedValues.

Kind mapping:

- string, DateTime, DateTimeOffset, Uri, and other textual JSON-bound values: String;
- byte through long and their unsigned equivalents: Integer;
- float, double, and decimal: Number;
- bool: Boolean;
- enum: Enumeration;
- nullable value types use the underlying kind.

SupportsPlaceholders is true only when the runtime can accept the argument as a JSON string
before placeholder substitution. It is false for numeric and boolean arguments. Enumeration
arguments may enable it when the runtime accepts the substituted enum name as a string.

For agent tools, reflection tests additionally assert:

- ToolDefinition.Description matches the method DescriptionAttribute;
- each argument description matches its parameter DescriptionAttribute;
- name, order, optionality, kind, and enumeration values match the reflected parameter;
- CancellationToken is excluded.

ToolRegistry uses ToolDefinition.Description as the top-level AIFunction description. Method
and parameter attributes remain because AIFunctionFactory uses reflected schema metadata; tests
prevent those descriptions from drifting from the Tool Map.

AI skills and ai.prompt have no directly reflected Tool Map method contract. Their metadata is
checked against explicit runtime-contract assertions rather than a fictitious MethodInfo.

### 5.2 Shared placeholder metadata

Replace the presentation-only string catalog with a structured Core catalog while retaining a
Definitions text projection for existing prompt-builder compatibility:

    public sealed record WorkflowPlaceholderDefinition(
        string InsertText,
        string Description)
    {
        public bool RequiresPriorStepId { get; init; }
        public bool SupportsStructuredSelector { get; init; }
    }

WorkflowPlaceholderCatalog exposes ordered Entries and derives Definitions from them. Runtime
recognition and IsValid remain the authority. The UI never parses display sentences to recover
placeholder text.

The Tool catalog provides an explicit Copy placeholder command using
IDesktopInteractionService.CopyText. It does not inject placeholder text into workflow JSON
because the catalog contains no editable argument target.

The Workflow Prompt Editor provides Insert placeholder at its prompt selection. For templates
requiring a prior step id, it creates concrete entries from the effective ids declared before
the selected ai.prompt step. It never inserts the generic word stepId when a concrete id is
required.

## 6. Snippet construction and insertion

### 6.1 WorkflowSnippetBuilder

Create one pure, non-WPF Core class. Given a ToolDefinition, insertion options, current parsed
workflow, current task error policy, and effective task Temperature and Top P, it creates one
object-form WorkflowStepDefinition.

Rules:

- step is the exact Tool Map alias;
- id uses one shared helper: replace "." by "-", then append -2, -3, and so on until the id is
  unique case-insensitively among all effective ids;
- required arguments are always included;
- optional arguments are included only when Include optional arguments is selected, except
  ai.prompt.includeContext, which is controlled solely by its dedicated option;
- argument order follows ArgumentDefinitions;
- placeholder values are "" for String, 0 for Integer and Number, false for Boolean, and the
  first AllowedValues entry for Enumeration;
- ai.prompt.prompt defaults to "Use the workflow context to complete this step.";
- a new ai.prompt step has Include accumulated workflow context selected by default and emits
  includeContext: true; clearing it emits includeContext: false rather than omitting the value,
  so the user's choice is explicit;
- Include error policy block emits onError equal to the current task policy, retryCount 1, and
  retryBackoffSeconds 2;
- Include generation options is enabled only for IsAiStep entries and emits the current
  effective task Temperature and Top P, never context-window or provider properties.

Refactor the existing TaskEditorViewModel AddWorkflowStep command to call this builder and append
the result. It keeps its current user-visible purpose but no longer owns a duplicate id,
required-argument, or AI-default algorithm.

### 6.2 WorkflowCaretInsertionService

Create one pure, non-WPF Core class returning:

- final text;
- final UTF-16 caret index;
- inserted top-level index;
- whether structured insertion succeeded;
- whether canonical serialization occurred;
- the exact fallback warning, when applicable.

Clamp input caret to [0, text.Length]. If it falls between the UTF-16 code units of one
surrogate pair, move it to the preceding valid scalar boundary.

Structured path:

1. Parse through WorkflowDefinitionSerializer.Deserialize.
2. Encode the original text as UTF-8 and enumerate top-level array element spans with
   Utf8JsonReader. TokenStartIndex is the element start and BytesConsumed after the complete
   value is its exclusive end.
3. Translate the clamped UTF-16 caret to a UTF-8 byte offset without splitting a scalar.
4. Select the insertion index using this exact rule:
   - caret before the first element start: 0;
   - caret inside an element span, including its first token: immediately after that element;
   - caret between elements: immediately after the element on the left;
   - caret after the last element: append;
   - empty array: 0.
5. Insert the new definition, serialize the entire array with
   WorkflowDefinitionSerializer.Serialize, then re-read the canonical spans and translate the
   inserted element's start byte back to its UTF-16 character index. Do not locate it by string
   search because two steps may serialize identically.

Fallback path:

- if parsing fails for any reason, including empty or whitespace-only input, insert the
  canonical object snippet verbatim at the clamped caret;
- preserve every surrounding character;
- prefix every continuation line of the snippet with the leading whitespace of the caret's
  source line;
- do not invent commas, brackets, quotes, or newlines around the snippet;
- return the caret at the first character of inserted text;
- return exactly:

    Workflow JSON is invalid. The snippet was inserted as text; add or repair JSON separators manually.

## 7. Workflow editor caret integration

- Name the workflow JSON TextBox.
- Add a small reusable attached behavior that exposes CaretIndex as a two-way binding.
- TaskEditorViewModel gains WorkflowCaretIndex and clamps it whenever WorkflowText changes
  programmatically.
- A catalog result requests workflow-editor focus and collapses the caret at the returned
  position. It does not delete or replace a pre-existing workflow-editor selection.
- All focus requests are view mechanics. No insertion logic enters the page code-behind.

The Workflow Prompt Editor may reuse the behavior with CaretIndex, SelectionStart, and
SelectionLength. In that editor, Insert placeholder replaces the selected prompt text, or
inserts at the caret when SelectionLength is zero, then places the caret after the inserted
placeholder.

## 8. Shared parsing and validation

Use WorkflowDefinitionSerializer consistently in Add step, Format JSON, Validate, AI Helper,
Tool catalog, and Workflow Prompt Editor. Remove or delegate the Task editor's private
deserializer so blank-input and root-shape behavior cannot diverge.

Extend ScheduledTaskValidator so:

- ai.prompt.prompt must exist and be a non-whitespace string;
- ai.prompt.prompt rejects CR characters; LF is the canonical workflow-prompt newline;
- argument property names are unique case-insensitively;
- placeholder validation remains centralized in WorkflowPlaceholderCatalog and the shared
  workflow validation path;
- all existing argument kinds and whitelist checks remain shared.

The Workflow Prompt Editor normalizes CRLF and lone CR to LF on extraction and whenever working
text changes. This is the only permitted prompt-text transformation. LF-canonical
extract/apply without a user edit reproduces canonical input exactly. A legacy prompt containing
CR can be opened and repaired by the dialog but is invalid until Apply normalizes it.
Extraction records a RequiresLineEndingNormalization flag for that entry; this flag counts as
an effective modification so Apply is available even when the user makes no additional edit.

The Prompt editor command is enabled only when WorkflowText parses and contains at least one
ai.prompt step. CanExecute state is recomputed when WorkflowText changes. Because a disabled
button cannot be clicked, the UI provides a tooltip explaining that valid workflow JSON with an
ai.prompt step is required. The command defensively re-parses if invoked programmatically and
writes the deterministic error to ValidationText without opening the dialog.

## 9. Tool catalog dialog

### 9.1 Request and result

Use IWorkflowToolCatalogDialogService.Show(request) returning a nullable result.

Request snapshot:

- current workflow text and UTF-16 caret;
- current non-AI task tool groups;
- current task filesystem authorization rules, displayed as task context only;
- current SafetyOptions snapshot;
- current task error policy;
- effective task Temperature and Top P.

Result:

- final workflow text and caret;
- accepted non-AI tool groups actually required by at least one inserted step;
- ordered insertion summaries containing alias, generated id, final position, and whether the
  fallback path was used;
- content-free warnings;
- whether canonical formatting occurred.

No insertion and no accepted group is a no-op and returns null. This prevents an unnecessary
dirty state or assisted-change snapshot.

### 9.2 Layout and search

The modal dialog follows the active application theme.

Left column:

- case-insensitive search over alias, group, description, and argument names;
- grouped, virtualized list in fixed order gmail, calendar, dropbox, filesystem, http, web, ai;
- alias, short description, Safety warning marker when applicable;
- missing-whitelist lock only for non-AI entries;
- built-in AI entries labelled as requiring no task tool-group permission.

Right pane:

- description, group, agent function name or "workflow-only AI step";
- ordered argument table: name, kind, required/optional, description, example, allowed values,
  and placeholder support;
- static local authorization requirements from LocalPathArguments;
- current task authorization rules as read-only context, without disk inspection or a claim
  that a future path is authorized;
- Safety flag and enabled/disabled snapshot;
- task-whitelist status for non-AI entries;
- note that relative paths use the configured sandbox and absolute paths may require a task
  authorization rule.

Insertion options:

- Include optional arguments;
- Include error policy block;
- Include generation options, enabled only for AI steps;
- for ai.prompt only, Include accumulated workflow context, checked by default and independent
  of Include optional arguments;
- read-only exact snippet preview.

Commands:

- Insert;
- Insert and close;
- Close;
- Cancel.

Insert mutates only the dialog working copy and updates its internal caret to the inserted
element start. Repeated Insert operations therefore accumulate deterministically.

For a missing non-AI group, show an unchecked Enable this tool group control. A group enters the
result only when it was explicitly accepted and an inserted step requires it. Deselecting it
before Close removes it from the result. AI never proposes a group.

Disabled Safety gates and unresolved future absolute-path authorization never block insertion.
They remain warnings and never mutate configuration or task authorization.

### 9.3 Placeholders tab

List WorkflowPlaceholderCatalog.Entries with description and Copy placeholder. Copy is the only
clipboard operation in either dialog and is always initiated explicitly by the user. The status
line confirms the copied template without echoing workflow or prompt content.

### 9.4 Session closing

- Close after effective insertions returns the accumulated result.
- Close without a change returns null.
- Cancel returns null and discards the complete working copy.
- The window close button behaves like Cancel. If the working copy differs from the request,
  require explicit confirmation before discarding it.

## 10. Workflow Prompt Editor

### 10.1 Pure prompt service

Create WorkflowPromptEditingService in Core with Extract and Apply operations.

Extract returns only ai.prompt entries, case-insensitively, in declaration order. Each entry
contains:

- zero-based workflow position and one-based display position;
- explicit id and effective id;
- whether the id is implicit;
- LF-normalized prompt text;
- includeContext, using runtime default false when omitted;
- onError, retry values, and generation options for display;
- prior effective ids available to placeholders.

String-form ai.prompt and object-form ai.prompt without prompt are extractable repair cases with
empty working text. They remain invalid until the user supplies a non-empty prompt and applies.

Apply receives edits keyed by zero-based workflow position. For each targeted entry:

- promote string form to object form when required;
- replace or add prompt;
- replace or add includeContext with the user's explicit boolean value;
- preserve every unrelated argument and all other step meaning;
- preserve declaration order;
- serialize the full array canonically.

Duplicate ids do not affect mapping because position is authoritative. Duplicate argument names
are rejected by shared validation before Apply.

### 10.2 Layout

Left sidebar:

- one entry per ai.prompt step;
- one-based position, effective id, implicit-id indicator;
- single-line preview;
- modified marker;
- context mode indicator: Full context or Selected placeholders only.

Preview projection is deterministic and display-only:

- LF becomes " ⏎ ";
- tab becomes " ⇥ ";
- other C0 controls use Unicode control pictures where available;
- consecutive display whitespace is not written back to the prompt;
- preview is truncated to 120 text elements with an ellipsis.

Right pane:

- multiline monospace TextBox with AcceptsReturn, AcceptsTab, wrapping, and vertical scrolling;
- editable Include accumulated workflow context checkbox;
- read-only policy metadata strip for onError, retry values, and generation options;
- character count based on UTF-16 text length and line count equal to one plus the number of LF
  characters, with an empty prompt displayed as 0 characters and 0 lines;
- Placeholders list from WorkflowPlaceholderCatalog plus concrete prior-step output entries;
- Insert placeholder replaces the current selection or inserts at the caret.

### 10.3 Context duplication warning

Show a non-blocking warning when includeContext is true and the prompt contains at least one of:

- previous.output, with or without a structured selector;
- a concrete prior step id followed by .output, with or without a structured selector;
- workflow.context.

Task, Today, and Now placeholders do not trigger this warning. The warning states that the
selected output and the complete accumulated context may both be sent to the model. It never
changes prompt text or includeContext.

To pass only selected prior data, the user inserts the desired placeholder and clears Include
accumulated workflow context.

### 10.4 Commands and closing

- Apply is enabled only when at least one prompt or includeContext value changed and every
  resulting ai.prompt is valid through shared validation.
- Apply validates, returns one result, and closes.
- Revert step restores the selected entry's initial prompt and includeContext.
- Cancel discards all changes and closes.
- There is no Close command.
- The window close button behaves like Cancel and requires explicit confirmation when any
  entry is modified.

Result:

- canonical workflow text;
- modified prompt count;
- modified includeContext count;
- ordered modified workflow positions and effective ids;
- canonical-formatting indicator;
- context-duplication warnings by position, without prompt content.

## 11. Task editor integration and assisted undo

Add Tool catalog… and Edit AI prompts… buttons to the workflow card.

TaskEditorViewModel gains:

- OpenToolCatalogCommand, always enabled when the dialog service exists;
- OpenWorkflowPromptEditorCommand, enabled under §8;
- assisted-change snapshot and HasAssistedUndoSnapshot;
- UndoAssistedChangeCommand;
- workflow caret and focus-request state.

Rename the UI button to Undo assisted change.

Before applying any non-null catalog or prompt result, replace the existing snapshot with:

- current WorkflowText;
- current selected non-AI tool groups;
- current authorized locations.

Catalog Apply:

- sets WorkflowText and returned caret;
- unions current groups with accepted non-AI groups;
- leaves authorized locations unchanged;
- requests workflow TextBox focus;
- reports inserted aliases/ids, accepted groups, canonical formatting, fallback warnings, and
  the reminder that Save is required.

Workflow Prompt Editor Apply:

- sets WorkflowText only;
- reports prompt and includeContext modification counts, canonical formatting, duplication
  warnings, and the reminder that Save is required.

Undo restores all three snapshotted values atomically even when the last aid changed only the
workflow. The newest assisted change replaces the older snapshot. Successful Save, Load,
navigation away, and creation of another applied assisted change clear or replace the snapshot
as appropriate.

Neither dialog calls ITaskStore, ITaskSchedulerService, runner services, tools, providers, or
connectors.

## 12. Privacy and incident handling

- Dialog working state is transient in-memory state, not a durable or shared cache.
- Do not log searches, aliases selected by the user, JSON, prompt text, argument values,
  placeholder selections, paths, authorized locations, or clipboard content.
- Standard TextBox selection and Ctrl+C remain explicit user clipboard actions. The application
  does not initiate them, observe their content, or log them.
- Deterministic parse and validation failures are shown locally and are not incidents.
- Unexpected infrastructure failures are reported once with source exactly
  "Workflow tool catalog" or "Workflow prompt editor".
- The reported exception must be content-free and must not retain the original data-bearing
  exception as InnerException. The user sees the correlation id through the existing incident
  notification.
- The application-owned Copy placeholder command copies only the catalog template explicitly
  selected by the user.

## 13. Normative examples

### 13.1 Structured insertion

Current text, caret anywhere inside the first element:

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

Insert filesystem.write without optional arguments or error policy:

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

The caret points at the opening brace of the inserted second element. A second insertion of the
same alias produces filesystem-write-2.

### 13.2 AI prompt with full context

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

### 13.3 AI prompt with selected prior output only

    {
      "step": "ai.prompt",
      "id": "review",
      "args": {
        "prompt": "Review only this result:\n\n{{source.output}}",
        "includeContext": false
      }
    }

This step receives the selected source output through the placeholder and does not receive the
complete accumulated workflow context.

### 13.4 Duplication warning

This shape remains valid but the Workflow Prompt Editor warns:

    {
      "step": "ai.prompt",
      "id": "review",
      "args": {
        "prompt": "Review {{source.output}}.",
        "includeContext": true
      }
    }

The application does not remove the placeholder or disable includeContext automatically.

### 13.5 Invalid JSON fallback

For input:

    [ { "step":

with the caret at the end, append the canonical object snippet exactly at that position,
preserve the surrounding text, and return:

    Workflow JSON is invalid. The snippet was inserted as text; add or repair JSON separators manually.

No separator repair or surrounding reformatting occurs.

### 13.6 Multiline LF round trip

The editor displays:

    You are reviewing the working notes of the day.

    Rules:
    - Use only the supplied context; never invent a fact.

    Produce a summary and the next actions.

The JSON stores the prompt on one physical line:

    "prompt": "You are reviewing the working notes of the day.\n\nRules:\n- Use only the supplied context; never invent a fact.\n\nProduce a summary and the next actions."

Reopening shows the same LF-normalized multiline text. No spaces, tabs, or trailing whitespace
are otherwise rewritten.

## 14. Required tests

Add deterministic tests, without live provider or external calls, for at least:

1. Complete Tool Map descriptions and argument metadata.
2. Reflection compatibility for every agent tool, including descriptions and enum names.
3. Explicit runtime-contract checks for AI skills and ai.prompt.
4. Exact non-AI Arguments and OptionalArguments compatibility.
5. Corrected optional text projection for built-in AI skills.
6. Stage 13 generation prompt built from enriched metadata with version increment to
   workflow-generation-v2.
7. Existing Add step and Tool catalog sharing the same builder and id algorithm.
8. No-argument, required-only, optional, Safety, enum, path, AI skill, and ai.prompt snippets.
9. ai.prompt includeContext true and false independently of Include optional arguments.
10. Error-policy and AI generation option blocks.
11. Unique ids against string and object forms, case-insensitively.
12. Structured insertion before the first, inside each element, between elements, and after the
    last.
13. Empty arrays, string-form elements, caret inside a JSON string, caret on element start,
    caret at zero, caret at end, and out-of-range caret.
14. UTF-16/UTF-8 mapping with accented text, CJK, emoji, and a guarded split-surrogate input.
15. CRLF source JSON mapping and canonical returned caret.
16. Fallback preservation, line indentation, clamping, and exact warning.
17. Returned structured caret obtained by reparsing the inserted index, not string search.
18. Missing non-AI group warnings, opt-in, decline, and accepted-group intersection with
    inserted aliases.
19. AI entries never locked and ai never returned as a tool group.
20. Safety enabled/disabled display with no SafetyOptions mutation.
21. Catalog multi-insert accumulation, no-op Close, changed Close, Cancel, and window-close
    discard confirmation.
22. Structured placeholder catalog with no UI duplicate.
23. Tool catalog Copy placeholder as the only catalog placeholder action.
24. Prompt extraction order and position identity with duplicate ids.
25. Repair extraction for string-form and prompt-less ai.prompt.
26. Empty and whitespace-only ai.prompt rejection through Save, Validate, helper validation,
    and Workflow Prompt Editor.
27. Duplicate argument-name rejection, including case variants of prompt.
28. CRLF and lone-CR normalization to LF with no other whitespace change.
29. Legacy CR input marks the entry as effectively modified so normalization can be applied
    without an additional user edit.
30. Multiline extract/apply/extract losslessness for LF-canonical text.
31. Exactly one physical JSON line per prompt value.
32. Targeted prompt and includeContext rewriting with every other property unchanged in meaning.
33. Explicit selected-output-only shape with includeContext false.
34. Duplication warning for previous.output, workflow.context, and concrete prior ids.
35. No duplication warning for task, Today, or Now placeholders.
36. No automatic mutation when duplication is warned.
37. Concrete prior-id placeholder insertion replacing a prompt selection.
38. Preview control rendering and 120-text-element truncation without stored-text mutation.
39. Apply disabled for no changes or any invalid prompt.
40. Apply closes with one batch result; Cancel and window close discard all pending changes.
41. Prompt button state for invalid JSON, no ai.prompt, and valid repairable ai.prompt shapes.
42. Result summaries contain enough content-free metadata for Task editor status text.
43. Shared assisted snapshot replacement and atomic undo across workflow, groups, and locations.
44. No-op dialog results create no dirty state and no undo snapshot.
45. Successful Save, Load, and navigation clear assisted undo.
46. No aid calls a provider, agent, tool, skill, connector, filesystem, scheduler, task store, or
    network service.
47. Deterministic errors create no incident; unexpected dialog failures create one sanitized
    incident without the original inner exception.
48. Every normative example above and Docs/samples/multiline-prompt-report.json.

## 15. Documentation

Update during Stage 17 implementation:

- shared specification §20 to match this V2, including the corrected caret rule,
  non-whitelistable ai group, explicit includeContext editing, LF policy, and global non-empty
  prompt validation;
- README with both dialogs, selected-output-only prompts, duplication warnings, canonical
  formatting, group opt-in, fallback, batch Apply, and Undo assisted change;
- Docs/User-Manual.html and Docs/User-Manual.en.html;
- Docs/FirstRun.md and Docs/FirstRun-EN.md where authoring guidance is summarized;
- Docs/theme-audit.md for both active themes and every new control/state;
- Docs/ARCHITECTURE-LEDGER.md with public APIs, metadata correction, prompt version,
  validation tightening, insertion algorithm, LF contract, privacy boundary, gate output,
  actual GUI verification, and residual risks;
- Docs/Prompts/README.md, identifying this V2 as the implementation prompt and retaining the
  original Stage 17 file as historical specification;
- Docs/acceptance-report.md;
- Docs/samples/multiline-prompt-report.json, kept parseable and LF-canonical.

Document residual risks honestly:

- generated snippets contain typed but semantically incomplete values;
- canonical serialization discards manual JSON formatting;
- fallback insertion cannot repair invalid JSON;
- long prompts remain one physical JSON line on disk;
- static metadata cannot predict runtime authorization, provider, connector, network, or Safety
  outcomes;
- includeContext plus explicit output placeholders may duplicate model input;
- selected placeholders may still expand to large runtime values.

## 16. Stage gate

Run and show output:

1. dotnet restore OD.MyLocalAgent.sln -m:1
2. dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release
3. dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
4. Parse every JSON file under Docs/samples.
5. Validate both standalone HTML manuals structurally.
6. git diff --check

Build must have zero warnings and zero errors. All deterministic tests must pass. The explicit
local Ollama integration probe may remain skipped.

## 17. Acceptance addendum

- [ ] Every Tool Map alias has one shared description and typed argument metadata.
- [ ] Built-in AI text arguments are represented as optional without UI or validator
      special-case lists.
- [ ] AI aliases never request an ai task tool group.
- [ ] Structured insertion follows the exact inside/left/before/after caret rule.
- [ ] Unicode caret mapping and returned caret placement are verified.
- [ ] Invalid JSON receives verbatim insertion and the exact repair warning.
- [ ] Catalog insertions accumulate; Cancel and window close discard them.
- [ ] Tool groups are enabled only after explicit opt-in and only when an inserted non-AI step
      requires them.
- [ ] Safety settings are never mutated.
- [ ] Tool catalog placeholders are copied, never injected into arbitrary JSON.
- [ ] Workflow Prompt Editor edits prompt and includeContext only.
- [ ] A specific prior output can be passed with includeContext false.
- [ ] Explicit output plus full context produces a warning without automatic mutation.
- [ ] Empty ai.prompt is invalid everywhere.
- [ ] Multiline prompt text is LF-canonical and otherwise lossless.
- [ ] Apply is one closing batch transaction; Cancel discards every pending edit.
- [ ] Undo assisted change restores workflow, groups, and locations atomically for all three
      assisted authoring flows.
- [ ] No LLM, execution, tool, skill, connector, filesystem, network, task-store, scheduler,
      package, persisted field, or page was introduced.

## 18. Before finishing implementation

1. Append the Stage 17 V2 entry to Docs/ARCHITECTURE-LEDGER.md.
2. Record the exact file-tree delta, enriched Tool Map surface, AI optional-argument correction,
   workflow-generation-v2 prompt contract, shared validation changes, and gate results.
3. State whether the WPF application was actually launched.
4. State separately whether catalog insertion, Unicode caret placement, invalid-JSON fallback,
   tool-group opt-in, placeholder copy, multiline editing, selected-output-only prompting,
   duplication warning, batch Apply, active Light/Dark themes, and Undo were exercised.
5. Do not claim that a snippet is operational merely because it is syntactically valid.
6. Do not claim runtime authorization, Safety, provider, connector, or network success from
   static metadata.
7. Do not claim GUI verification for flows that were not actually exercised.
