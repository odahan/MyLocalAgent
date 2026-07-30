# STAGE 13 — AI Workflow Helper

> Run only after **Stage 12** is finished. Stages 9 and 10 remain abandoned proposals and are
> neither prerequisites nor in scope. The task-owned sequential workflow JSON remains the
> persisted source of truth.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§16.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs, package versions, and security
   boundaries are binding.
3. The current Tool Map, workflow serializer and validator, placeholder resolver, filesystem
   authorization code, LLM provider abstraction, Task editor, desktop-interaction service,
   theme audit, tests, README, manuals, and samples.

## 2. Mission

Add one **AI Workflow Helper** window to the existing Task editor. It has two entry modes:

- **AI Helper** turns an English description into exactly the workflow-array JSON currently
  typed by hand in the Task editor. It also supports modification of an existing workflow
  through a deterministic, editable step-by-step description.
- **AI Explain** turns one valid existing workflow into an English explanation covering its
  purpose, ordered steps, prompts, tools, skills, placeholders, data flow, permissions,
  Safety gates, external effects, and likely execution failures.

The helper uses the provider currently selected in Settings and streams its response. It
never executes the proposed workflow, invokes a workflow tool or skill, reads a referenced
file, changes a global Safety setting, or persists assistant-session content.

Generated JSON must pass application validation before it can leave the helper. Required
tool groups and statically identifiable absolute-path grants are derived by the application,
presented for explicit approval, and applied atomically with the workflow. Refusing a required
proposal keeps the user in the helper, where the description can be changed or the operation
abandoned.

## 3. Product decisions (locked by the product owner — do not re-open)

| # | Decision |
|---|---|
| D1 | **One window, two modes**: use one reusable AI Workflow Helper window. The Task editor exposes **AI Helper** while creating or editing and **AI Explain** when non-empty workflow JSON is present. Do not add another page or separate assistant windows. |
| D2 | **Workflow array only**: generation returns exactly the JSON array accepted by the existing `WorkflowText` field. It never returns or replaces a complete `ScheduledTask`. |
| D3 | **Deterministic editable description**: existing workflow JSON is converted in C#, without an LLM call, into an ordered, lossless textual description containing every alias, effective id, argument, placeholder, error policy, retry value, and original string/object form. |
| D4 | **Iterative refinement without chat history**: the complete editable description is the temporary source of intent. After a valid generation, the user may edit it directly or submit one short refinement instruction. No visible message transcript is accumulated. The description is regenerated deterministically from each accepted candidate JSON. |
| D5 | **Current provider**: every generation and explanation uses `ILlmClientFactory.Create()` so the provider and model currently selected in Settings apply. There is no helper-specific provider, model, endpoint, credential, or cloud fallback. |
| D6 | **Internal prompt**: system instructions, Tool Map context, placeholder catalog, task authorization context, repair prompts, provider, and model remain internal. The user sees only their editable description or refinement, streamed output, validation messages, proposal review, and final explanation. |
| D7 | **No execution**: assistant calls are tool-less chat calls. The helper must not use `MyLocalAgentAI`, expose `AITool` instances, invoke built-in skills, resolve runtime outputs, inspect referenced content, or run the generated workflow. An `ai.*` alias in generated JSON is data, not an assistant invocation. |
| D8 | **Application validation wins**: the LLM is never trusted to identify validity, required tools, permissions, or Safety gates. Parsing, canonical serialization, validation, and requirement analysis are deterministic application operations. |
| D9 | **Three attempts total**: one initial model response plus at most two automatic correction responses are allowed per Generate, Refine, or Explain request. A correction receives the previous response and exact deterministic errors. After the third failure, preserve the last response and errors and offer manual **Retry**. |
| D10 | **Explicit Apply and one-level Undo**: only a valid candidate with every required tool/path proposal accepted enables **Apply**. Apply atomically updates workflow JSON, tool selections, and authorized locations. The Task editor retains the immediately preceding values for one-level **Undo** until a successful task save, reload, navigation away, or later helper application replaces that snapshot. |
| D11 | **No session persistence**: descriptions, refinements, raw responses, explanations, partial streams, errors, and proposal choices exist only in memory while editing. Only an applied workflow and accepted task settings enter the normal Task editor state. |
| D12 | **Invalid Explain stops locally**: AI Explain first parses and validates the current workflow against the current task draft. Invalid JSON or workflow rules are listed locally and no LLM call is made. |
| D13 | **English only**: helper UI, descriptions, generated workflow prompts, validation text, explanations, and documentation are English. Generation responses are pure JSON arrays without Markdown fences or commentary. |
| D14 | **Text and rendered Markdown**: explanations are held as Markdown and can be viewed as deterministic plain text or rendered Markdown. Text is the default. Markdig converts Markdown to HTML and WebView2 renders a local, restricted document. |
| D15 | **WebView2 degradation**: failure to detect or initialize the WebView2 Runtime never blocks Text mode or the helper. Show an English message and a user-initiated link to the official Microsoft WebView2 Runtime download page. Never download or install it automatically. |

## 4. Critical constraints (restated)

- R1, R2, R7, R9, and addenda §12–§15 remain binding. Selecting an OpenAI-compatible
  provider remains the user's existing cloud opt-in; the helper never silently falls back.
- Existing task serialization, sequential execution, Tool Map aliases, placeholder behavior,
  tool whitelisting, Safety gates, filesystem sandboxing, and per-task authorization remain
  unchanged.
- `ToolMap` is the single source of truth for aliases, tool groups, ordered arguments,
  optional arguments, AI-step status, and Safety flags. Extend its metadata for static local
  path analysis rather than creating a UI-only alias table.
- The workflow placeholder catalog used in model context and helper text must be produced from
  one shared definition consumed by the runtime resolver and assistant. Do not maintain a
  second handwritten placeholder list in the UI.
- The helper must not log or incident-report user descriptions, refinements, generated JSON,
  workflow prompts, explanations, absolute paths, model responses, or repair content.
  Exceptions may be reported with sanitized operation metadata and a correlation id.
- Cancellation propagates as cancellation, not as a failure, repair attempt, or incident.
- No task file, tool selection, authorized location, Safety setting, or editor text changes
  before explicit Apply.
- Markdig and `Microsoft.Web.WebView2` are the only new packages allowed by this stage. Pin
  current stable versions compatible with .NET 10 through Central Package Management and
  record the exact versions and justification in the architecture ledger.
- Warning-clean complete files, English XML documentation on every public API, MVVM Toolkit
  source generators only, and no business logic in window or page code-behind.
- Every `dotnet restore`, `dotnet build`, and `dotnet test` command explicitly uses `-m:1`.

## 5. Scope

### 5.1 Entry points and window states

Extend only the existing Task editor workflow card:

- **AI Helper** is available for a new or loaded task. An empty workflow starts with an empty
  description. A non-empty workflow must parse before opening authoring mode and initializes
  the description through §5.2.
- **AI Explain** is available when `WorkflowText` is non-empty. Clicking it runs local parse
  and validation first; errors are shown without opening a model-backed explanation.
- Keep **Add step**, **Translation example**, **Format JSON**, and **Validate** unchanged.

Use one window and one ViewModel with explicit `Author` and `Explain` modes. Author mode has:

1. an editable multiline **Workflow description**;
2. a single-use **Refinement** box enabled after a valid candidate exists;
3. Generate/Refine, Cancel, Retry, Apply, and Close commands;
4. a read-only streamed-response/JSON pane;
5. deterministic validation errors;
6. the proposal review of §5.6.

Explain mode has streamed progress, Cancel, Retry, Copy, Close, and a **Text / Rendered
Markdown** selector. Apply and proposal controls are absent.

The state machine must distinguish at least `Idle`, `Generating`, `Validating`,
`NeedsApproval`, `Ready`, `Failed`, and `Cancelled`. Disable conflicting commands while a
request is active. A busy indicator remains indeterminate when the provider supplies no
measurable progress.

### 5.2 Deterministic workflow description

Create a testable formatter over normalized `WorkflowStepDefinition` values. Its output is
English plain text intended for editing, not a second persisted workflow format. For every
step in declaration order it includes:

- one-based position;
- original string or object form;
- alias;
- explicit id and effective id;
- exact compact JSON for every argument, preserving value types and placeholder text;
- `onError`, `retryCount`, and `retryBackoffSeconds`, distinguishing omitted values from
  explicit values;
- the produced output name available to later placeholders.

The formatter must never summarize, omit, reorder, normalize, or resolve an argument.
Formatting the same deserialized workflow repeatedly is deterministic. After successful
generation or refinement, deserialize the canonical candidate and rebuild this complete
description in C#.

An initial Generate uses the complete current description. A Refine request uses only:

- the current canonical candidate JSON;
- its current deterministic description;
- the latest refinement text.

After a successful refinement, clear the one-use refinement field and rebuild the description.
Do not accumulate previous refinement messages or hidden conversation history.

### 5.3 Model context and tool-less streaming

Add a dedicated workflow-assistant service; do not reuse an agent execution path. Each model
call:

1. obtains the current `ILlmClientService` from `ILlmClientFactory`;
2. calls its `IChatClient` directly with exactly one system message and one user message;
3. supplies no tools, agent, skill, execution context, or side-effect callback;
4. uses streaming and accumulates the exact response for validation;
5. honors the provider's configured model and timeout and the UI cancellation token.

The versioned generation system prompt is built dynamically from:

- the canonical workflow array schema, including string and object forms;
- every `ToolMap.Definitions` entry with alias, tool group, ordered arguments, required versus
  optional arguments, AI-step status, and Safety flag;
- the shared placeholder catalog and structured-selector grammar;
- sequential execution, named-output, `includeContext`, and error-policy semantics;
- a requirement to return one pure JSON array and nothing else;
- a requirement not to invent aliases, arguments, placeholders, absolute permissions, or
  unavailable runtime values;
- a statement that referenced data is not available and must not be fabricated.

The user message contains the authoring description and, where applicable, current candidate
JSON plus the latest refinement. It may include current tool groups and authorization rules
for grounding, but never secrets, secret-configuration state, file contents, external tool
results, logs, or unrelated task data.

Explanation uses a separate versioned system prompt and receives canonical workflow JSON plus
the deterministic analysis of §5.5. It must explain:

- overall purpose and declaration-order execution;
- each alias, id, arguments, workflow `ai.prompt` text, and built-in skill;
- placeholders, named outputs, structured selections, context inclusion, and data flow;
- required tool groups and filesystem grants;
- relevant global Safety flags without claiming they are enabled;
- external calls, writes, destructive operations, Guardrails boundaries, and local-file
  classification limitations;
- statically visible conditions likely to fail, including missing runtime data, unresolved
  dynamic paths, large context, and disabled gated operations.

The helper's own system and repair prompts are never disclosed as part of an explanation.

### 5.4 Response parsing, validation, repair, and cancellation

For Generate and Refine:

1. Stream the current attempt into the read-only response pane.
2. Accept only a response whose entire non-whitespace content parses directly as a JSON array
   of `WorkflowStepDefinition`. Do not strip Markdown fences, prose, prefixes, or suffixes.
3. Canonically serialize it with `ScheduledTaskSerializer.Options`.
4. Validate workflow shape, ids, aliases, argument kinds, required arguments, placeholder
   grammar, `includeContext`, retries, and task-level rules.
5. Run requirement analysis (§5.5) and validate a provisional task containing existing plus
   proposed tool groups and filesystem grants.
6. If parsing or deterministic validation fails, issue a correction call containing the
   previous raw response and exact errors, while retaining the same authoritative catalog.
7. Stop after three model attempts total.

The correction prompt must request a complete replacement response, not a patch. Never ask
the model to waive, reinterpret, or repair application validation rules.

Use the existing Task-editor preview defaults for unrelated incomplete fields such as a new
task's id, name, schedule, or prompt. Candidate validation must still use the draft's real
tool selections, authorized locations, error policy, and workflow. An unfinished task header
must not prevent the user from authoring its workflow.

Factor or extend the shared workflow-definition validation used by normal task Save/Validate
and by the helper. Required Tool Map arguments and statically checkable placeholder grammar
must not be enforced by a helper-only validator that can disagree with the normal editor.

Explanation requires valid input, but its Markdown response has no workflow-JSON validation.
An empty explanation or a failed provider call follows the same three-attempt limit. Preserve
the last raw response and sanitized error after exhaustion.

Cancel must immediately request cancellation of the active stream. Preserve received partial
text read-only for inspection, mark it incomplete, disable Apply, perform no repair, and
report no incident for the cancellation itself. Starting Retry or another generation clears
the previous partial stream only after the new operation begins.

### 5.5 Deterministic requirement and risk analysis

Create one application-owned analyzer that consumes the candidate workflow, current draft
tool groups, current `fileSystemAccess`, and current Safety configuration. It returns:

- required non-AI tool groups, derived only from Tool Map ownership;
- missing tool groups;
- literal local-path requirements and their required access;
- missing or insufficient filesystem grants;
- unresolved dynamic-path warnings;
- referenced Safety flags and their current enabled/disabled state;
- additional deterministic warnings used by explanation and proposal review.

Extend Tool Map metadata so local-path argument semantics are centralized and testable.
Metadata must distinguish read versus read-write access and file versus directory targets.
Cover:

| Alias | Static local-path requirement |
|---|---|
| `filesystem.list` | `path`: directory, ReadOnly |
| `filesystem.read` | `path`: file, ReadOnly |
| `filesystem.write` | `path`: file, ReadWrite |
| `filesystem.createDirectory` | `path`: directory, ReadWrite |
| `filesystem.delete` | `path`: file or directory, ReadWrite |
| `filesystem.move` | `from` and `to`: file or directory, ReadWrite |
| `filesystem.rename` | source plus derived same-directory destination, ReadWrite |
| `filesystem.setExtension` | source plus derived same-directory destination, ReadWrite |
| `dropbox.upload` | `localPath`: file, ReadOnly |
| `dropbox.download` | `localPath`: file, ReadWrite |

Relative paths remain covered by `FileSystem:RootPath` and produce no task grant proposal.
For a fully literal absolute file path, propose its containing directory. For a literal
absolute directory path, propose that directory. Normalize through the same Stage-11 path
logic, aggregate duplicate normalized proposals, and retain the strongest required access.
Existing most-specific-rule precedence determines whether the task already has sufficient
access. A literal file-or-directory target such as move or delete cannot be classified
without inspecting disk; propose its containing directory, label the broader scope clearly,
and require the normal explicit approval.

Do not infer a grant when the relevant path:

- contains any workflow placeholder;
- contains an unexpanded environment variable;
- is produced by a previous step or runtime model output;
- is malformed or cannot be classified safely.

Emit a readable warning and leave runtime authorization authoritative. Do not inspect disk
existence or resolve file contents. Reuse Stage-11 volume-root confirmation and UNC/S4U
warnings. Never propose remote Dropbox paths, URLs, task-store paths, reports, logs, secrets,
or other application-owned locations.

Safety flags are review warnings, not task-owned proposals. The helper never enables or
changes them. A disabled Safety flag does not prevent Apply when the workflow and task schema
are otherwise valid, but the review must state that execution will fail until the user
explicitly enables the global setting.

### 5.6 Proposal review, Apply, and Undo

After a candidate passes provisional validation, show one review surface containing:

- every missing tool group with an unchecked explicit approval control;
- every missing absolute directory, required `ReadOnly`/`ReadWrite` level, source alias, and
  any volume-root or UNC warning;
- unresolved dynamic-path warnings;
- referenced disabled Safety gates;
- confirmation that no workflow operation has been executed.

Required tool and path proposals must all be explicitly accepted before Apply. Declining or
unchecking one disables Apply but does not discard the candidate. The user may edit the
description, regenerate, retry, or close.

Apply returns one immutable result containing:

- canonical indented workflow-array JSON;
- the accepted union of current and required tool groups;
- normalized accepted filesystem rules, merging exact duplicates with the strongest access.

The Task editor applies all three values as one UI operation and stores a one-level snapshot
of the previous `WorkflowText`, tool selections, and authorized locations. **Undo AI changes**
restores that snapshot atomically. No task-store or scheduler write occurs until the existing
Save command succeeds.

### 5.7 Explanation rendering and WebView2 fallback

Store the model explanation as Markdown. The selector changes presentation without another
LLM call:

- **Text** uses a deterministic plain-text projection of the parsed Markdown and is the
  default and universal fallback.
- **Rendered Markdown** converts through Markdig and displays through the WPF WebView2
  control.

Rendered content is untrusted. The local HTML document must:

- disable or escape raw HTML from Markdown;
- contain no script;
- use only embedded dark-theme CSS;
- set a restrictive Content Security Policy blocking scripts, connections, frames, media,
  fonts, images, and all remote resources;
- intercept and block navigation, pop-ups, downloads, and new-window requests;
- never write a temporary HTML file when an in-memory document is sufficient.

Detect runtime availability before enabling Rendered Markdown and handle initialization
failure. If unavailable, remain in Text mode and show a normal WPF hyperlink to the official
Microsoft WebView2 Runtime download page:
`https://developer.microsoft.com/microsoft-edge/webview2/`. Opening it must be an explicit
user action routed through `IDesktopInteractionService`; do not start a download or installer.

### 5.8 Privacy, errors, and lifetime

- Keep assistant state in memory only and clear it when the window closes.
- Do not add assistant history, cache, task JSON properties, settings, report fields, or
  assistant transcript files.
- Do not place descriptions, refinements, prompts, responses, explanations, workflow JSON,
  or paths in Serilog events, incidents, exception messages, telemetry, or clipboard unless
  the user invokes an explicit Copy action.
- Provider/configuration failures enter the existing incident pipeline once with sanitized
  source `AI Workflow Helper` and surface a friendly message plus correlation id.
- Deterministic user-input and validation failures are normal UI errors and are not incidents.
- Closing the window while generation is active cancels it and applies nothing.

## 6. Required tests

Add deterministic tests, without a live Ollama or cloud call, for at least:

1. Task editor still deserializes and saves only the workflow array inside the complete task.
2. Deterministic description covers string/object form, order, ids, exact argument JSON,
   placeholders, error policy, retries, and omitted values.
3. Empty authoring, existing-workflow authoring, direct description editing, and one-use
   refinement state.
4. Generation context is produced from current Tool Map definitions and the shared
   placeholder catalog with no hard-coded UI alias list.
5. Helper model calls use the current factory result, system/user roles, streaming, and zero
   tools or skills.
6. Pure JSON succeeds; fenced JSON, prose, malformed JSON, non-array JSON, unknown aliases,
   missing arguments, invalid placeholders, and invalid retries fail.
7. Exactly three total attempts occur, correction prompts contain deterministic errors, and
   the last response/error is retained.
8. Cancellation stops streaming, makes no repair call, reports no incident, preserves partial
   output, and never enables Apply.
9. Tool requirements for every non-AI alias; AI steps require no task tool-group proposal.
10. Existing, missing, refused, and accepted tool-group proposals.
11. Relative paths produce no task grant; literal absolute file/directory paths produce the
    correct normalized grant and access.
12. Read versus read-write inference for every §5.5 alias, including both move endpoints,
    derived rename/extension destinations, and Dropbox local paths.
13. Existing rule sufficiency, nested-rule precedence, deduplication, and ReadOnly-to-
    ReadWrite elevation.
14. Placeholder, runtime-produced, malformed, and unexpanded-variable paths warn without
    inventing grants.
15. Volume-root confirmation and UNC/S4U warning behavior.
16. Safety-gated aliases display enabled/disabled status, never mutate settings, and disabled
    gates do not silently disappear.
17. Apply is disabled until every required proposal is accepted; refusal preserves state.
18. Atomic Apply and one-level Undo cover JSON, tools, and filesystem grants; Save remains the
    only persistence path.
19. Explain rejects invalid workflow locally and makes no model call.
20. Explanation context covers steps, workflow prompts, skills, placeholders, data flow,
    tools, permissions, Safety gates, effects, and deterministic failure warnings.
21. Text projection and Markdig rendering use one response without regeneration.
22. Rendered HTML disables raw HTML and remote resources and blocks navigation, pop-ups, and
    downloads.
23. Missing or failed WebView2 falls back to Text and exposes only the official
    user-initiated download link.
24. Closing the window clears in-memory state and no assistant content is logged or persisted.
25. ViewModel command enablement and busy/error states for both modes.

## 7. Documentation

Update:

- shared specification §16;
- `README.md` with both buttons, generation/refinement flow, proposal review, Apply/Undo,
  explanation coverage, provider/privacy behavior, Markdown rendering, and WebView2 fallback;
- both standalone user manuals with the same user-facing behavior;
- `Docs/theme-audit.md` for the modal window, streaming panes, proposal controls, selector,
  rendered surface, busy/error states, and fallback message;
- `Docs/ARCHITECTURE-LEDGER.md` with contracts, prompt versions, Tool Map metadata,
  package versions, privacy boundaries, WebView2 restrictions, exact verification output,
  GUI verification status, and residual risks;
- acceptance material to include authoring, refinement, refusal, cancellation, explanation,
  Markdown/Text, Undo, and missing-runtime scenarios.

Document these residual risks honestly:

- generated workflows can be semantically wrong despite valid JSON;
- literal static analysis cannot predict all runtime paths or values;
- disabled Safety gates can still make an applied workflow fail at runtime;
- model context and latency grow with workflow size;
- selecting a cloud provider sends the entered description and workflow context to that
  configured provider;
- Markdown rendering is best-effort and Text remains the dependable fallback.

## 8. Stage gate (all must pass — show command output)

1. `dotnet restore OD.MyLocalAgent.sln -m:1`.
2. `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release` — zero
   warnings and zero errors.
3. `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release` —
   all deterministic tests green; the explicit local Ollama integration probe may remain
   skipped.
4. Parse every JSON file under `Docs/samples`.
5. `git diff --check`.

**Stage-13 acceptance addendum:**

- [ ] AI Helper creates a valid workflow array from an English description without executing
      any step.
- [ ] A loaded workflow appears immediately as a complete deterministic editable description.
- [ ] Direct editing and one-use refinement both produce a new validated candidate without a
      persisted or visible chat transcript.
- [ ] Invalid model output is corrected at most twice after the initial attempt, then remains
      visible with errors and Retry.
- [ ] Missing tool groups and literal absolute-path grants require explicit approval; refusal
      keeps the user in the helper and disables Apply.
- [ ] Dynamic paths warn without invented authorization.
- [ ] Apply updates workflow, tools, and grants atomically; Undo restores all three before
      Save.
- [ ] AI Explain rejects invalid input locally and explains every valid step, prompt, skill,
      placeholder, dependency, permission, Safety gate, effect, and likely failure.
- [ ] Streaming can be cancelled without persistence, repair, execution, or incident.
- [ ] Text and safe Rendered Markdown views use one response; missing WebView2 has a functional
      Text fallback and an official user-initiated Microsoft download link.
- [ ] No graph, workflow library, workflow parameter, `ai.judge`, separate Workflows page,
      assistant history, workflow execution, or automatic Safety change was introduced.

## 9. Before you finish

1. Append the Stage 13 entry to `Docs/ARCHITECTURE-LEDGER.md`.
2. Record the exact file-tree delta, package versions, prompt versions, and gate results.
3. State whether the WPF application was actually launched and whether both modes, refusal,
   cancellation, Undo, Markdown rendering, and missing-runtime fallback were exercised.
4. Do not claim that valid JSON guarantees a correct workflow or that static authorization
   analysis covers dynamic runtime paths.
5. Do not claim GUI or WebView2 verification unless those flows were actually exercised.
