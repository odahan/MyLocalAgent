# STAGE 20 — Agent Prompt Placeholders

> Run only after **Stage 19** is finished. Stage 17 V2 remains the implementation contract for
> workflow authoring, and Stage 19 remains the contract for SQLite. This stage adds deterministic
> task/date placeholder expansion to prompt-only agent executions. It does not change workflow
> placeholder semantics, add workflow data to an agent prompt, introduce a general templating
> engine, or add a persisted task field, setting, package, tool, skill, page, or provider call.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§22. This
   standalone prompt is the complete Stage 20 implementation contract and must be reflected in a
   concise new shared-specification addendum during implementation.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read. Recorded placeholder, `TimeProvider`, prompt
   execution, workflow precedence, Runner reporting, run-log privacy, Prompt editor, active-theme,
   and Stage 17 authoring decisions are binding.
3. The post-Stage-19 implementations of `WorkflowPlaceholderCatalog`,
   `WorkflowPlaceholderResolver`, `ScheduledTask`, `ScheduledTaskValidator`,
   `IMyLocalAgentService`, `MyLocalAgentService`, `AgentRunResult`, `WorkflowResult`,
   `SequentialWorkflowEngine`, `RunnerCommandHandlers`, `MarkdownReportWriter`, run-log services,
   `PromptEditorViewModel`, `PromptEditorPage`, dependency injection, tests, README, both standalone
   manuals, and the current documentation-validation scripts or tests.
4. Existing tests for workflow `Today`/`Now` formats, prompt-only Runner generation-policy
   propagation, Prompt editor streaming, workflow precedence, reports, run logs, privacy, and
   concurrent task execution.

## 2. Mission

Allow a prompt-only saved task to use deterministic values that already exist before the model is
called:

- `{{Today}}` and `{{Today[format]}}`;
- `{{Now}}` and `{{Now[format]}}`;
- `{{task.id}}`;
- `{{task.name}}`.

Resolve these placeholders in the application before sending the prompt to the configured agent.
The model must receive the resolved prompt, so a filename such as
`C:\Reports\Sort-{{Today[yyyyMMdd]}}.md` becomes a concrete dated path without asking a local model
to calculate or copy the date correctly.

An unknown, malformed, invalidly formatted, or workflow-only placeholder must not fail, skip, or
disable an otherwise runnable prompt-only task. Preserve its exact original text, send it unchanged
to the model, and emit a non-blocking warning. A warning does not claim that the model will reproduce
the marker in its final response or tool arguments; it states only that the application did not
resolve the occurrence.

Workflow execution remains unchanged and strict. Workflow placeholders continue to resolve only
inside workflow step arguments and prompt templates with the established declaration-order context,
and an unknown workflow placeholder continues to fail the step under its error policy.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S20-D1 | **Prompt-only scope.** Expansion applies only when a saved task executes its agent prompt without a workflow: scheduled/CLI `run`, UI **Run now** through the Runner, CLI `test-prompt`, and Prompt editor **Test the prompt**. |
| S20-D2 | **Closed available-value set.** The only Stage 20 agent-prompt placeholders are `Today`, `Now`, `task.id`, and `task.name`, with the date/time formats in §6. No arbitrary variable, environment value, secret, connector value, database value, file value, or user-defined dictionary is introduced. |
| S20-D3 | **Shared date contract.** Agent prompts and workflows use one Core-owned date/time parser, alias normalizer, formatter, and validation rule. Stage 20 must not create a second interpretation of `Today`, `Now`, `YYYY`, `YY`, `DD`, or `hh`. |
| S20-D4 | **One local clock snapshot.** Capture `TimeProvider.GetLocalNow()` exactly once for one prompt expansion. Every `Today` and `Now` occurrence in that prompt uses the same value, including an execution crossing midnight. |
| S20-D5 | **Resolve before the provider.** Expansion completes before `IMyLocalAgentService` receives the user prompt and before any model or agent-tool round. Tool outputs, model text, and model-selected tool arguments are never passed back through the resolver. |
| S20-D6 | **Single pass, never recursive.** The resolver scans the original prompt once. Replacement text is not scanned again. A task name containing `{{Today}}` remains that literal text when inserted through `{{task.name}}`. |
| S20-D7 | **Preserve unresolved text exactly.** Unknown, workflow-only, malformed, and invalid-format occurrences remain character-for-character identical in the prompt sent to the model, including braces, casing, and internal whitespace. |
| S20-D8 | **Warn, do not fail.** An unresolved occurrence is a normal warning. It does not throw, create an incident, change Runner exit code, change report/index status, prevent scheduling, prevent Save, or prevent the model/tool execution. |
| S20-D9 | **Workflow-only markers remain inert in agent prompts.** `task.prompt`, `previous.output`, `workflow.context`, and every `<stepId>.output...` form are preserved and warned as unavailable in prompt-only agent context. They are not resolved to empty strings and do not acquire invented data. |
| S20-D10 | **Unknown markers remain inert.** A marker such as `{{customer_name}}` has no Stage 20 data source. It is preserved and warned as unknown; the application never guesses, prompts for, queries, or synthesizes its value. |
| S20-D11 | **Workflow behavior is unchanged.** `SequentialWorkflowEngine`, workflow declaration-order validation, structured output selection, `includeContext`, retry/error policy, and `WorkflowPlaceholderException` behavior retain their current semantics. |
| S20-D12 | **No prompt pre-expansion for workflow tasks.** When a task has both `prompt` and a non-empty `workflow`, workflow precedence remains authoritative. The persisted task prompt stays raw and is available only through the existing workflow `{{task.prompt}}` behavior; Stage 20 does not pre-resolve it. |
| S20-D13 | **Taskless surfaces are unchanged.** AI Playground, Tool tester, AI Workflow Helper, AI Explain, Guardrails, direct skills, workflow `ai.prompt`, and provider health operations do not use the Stage 20 agent-prompt resolver. |
| S20-D14 | **Unsaved Prompt editor text is tested.** Prompt editor **Test the prompt** resolves the current unsaved prompt text with the loaded task id/name and a fresh local-time snapshot, while retaining the existing unsaved generation-option behavior. |
| S20-D15 | **Content-free durable warnings.** Rolling logs, run logs, Markdown reports, report index, incidents, and telemetry never contain original/resolved prompt text, replacement values, or raw placeholder bodies. Durable warnings contain only program-owned codes/categories and aggregate occurrence counts. |
| S20-D16 | **Memory-only exact feedback.** Prompt editor may identify exact unresolved spans by projecting them from the prompt already visible in that editor. Such text is memory-only, bounded, never incident-reported, and never passed to a logger or persisted by the warning mechanism. |
| S20-D17 | **Success remains success.** A completed model execution with unresolved placeholders remains `Success`; its report contains a separate Warnings section. A provider or tool failure remains a failure independently of placeholder warnings. |
| S20-D18 | **No authorization change.** Expansion grants no tool group, filesystem path, SQLite permission, Safety flag, connector access, or Windows right. Resolved paths remain subject to the existing execution context and tool checks. |
| S20-D19 | **No new configuration.** There is no strict-mode toggle, placeholder dictionary, custom date culture, custom time zone, environment expansion switch, or task schema migration in Stage 20. |
| S20-D20 | **No new package or model call.** Expansion is local deterministic string processing. It never invokes an LLM, tool, skill, connector, filesystem operation, network call, or external templating package. |

## 4. Critical constraints

- R1, R2, R4, R5, R7, R9, R10 and all later shared addenda remain binding. Every public API has
  English XML documentation; code and UI text are English; implementation is warning-clean.
- Preserve the architecture dependency direction. Core cannot reference Workflows. Move the
  reusable date/time parsing and formatting behavior from Workflows into Core, or expose an
  equivalent Core-owned service used by both layers, without creating a dependency cycle.
- Do not resolve a prompt in `MyLocalAgentService` indiscriminately. That service is also used by
  taskless and interactive surfaces. The caller must explicitly select the task-owned prompt
  resolution path or pass an explicit immutable resolution context.
- Do not use `DateTime.Now`, `DateTimeOffset.Now`, or the current culture. Wall-clock access uses
  injected `TimeProvider`; formatting uses `CultureInfo.InvariantCulture`.
- Do not log a raw match as part of an exception, record `ToString()`, structured logging property,
  diagnostic object, or warning message. Privacy tests use canary placeholder bodies.
- Every `dotnet restore`, `dotnet build`, and `dotnet test` command explicitly uses `-m:1`.

## 5. Scope and execution matrix

### 5.1 Expansion-enabled surfaces

| Surface | Prompt source | Expansion context | Required behavior |
|---|---|---|---|
| Scheduled `run --task` without workflow | persisted `ScheduledTask.Prompt` | persisted task id/name + one Runner local-time snapshot | Resolve before `RunPromptAsync`; warn without failing. |
| UI **Run now** without workflow | persisted task loaded by child Runner | same Runner path as scheduled execution | Identical to scheduled execution; no UI-only resolver. |
| CLI `test-prompt --task` | persisted `ScheduledTask.Prompt` | persisted task id/name + one Runner local-time snapshot | Resolve, execute, write/print the report, and include warnings. |
| Prompt editor **Test the prompt** | current unsaved `Prompt` | loaded task id/name + one UI local-time snapshot | Resolve before streaming; show non-blocking warning feedback and continue. |

The Runner must use one shared prompt-only execution helper for `run` and `test-prompt` so their
resolution, warning, generation-policy, result, and report behavior cannot diverge.

### 5.2 Explicitly unchanged surfaces

- a task with a non-empty workflow, including its raw persisted `prompt`;
- every workflow argument string and `ai.prompt` template;
- the AI Playground raw-provider and optional-agent paths;
- Tool tester and direct Tool Map invocation;
- AI Workflow Helper authoring, repair, and explanation;
- Guardrails screening and reduction;
- built-in skills and workflow skill inputs;
- connector, filesystem, SQLite, report-path, scheduler, and configuration strings.

Stage 20 is not a global `{{...}}` preprocessor.

## 6. Agent prompt placeholder contract

### 6.1 Supported placeholders

Matching is case-insensitive and permits surrounding whitespace inside the double braces, matching
the established workflow catalog behavior.

| Placeholder | Value |
|---|---|
| `{{Today}}` | Local date as `yyyy-MM-dd`. |
| `{{Today[format]}}` | Local date/time snapshot formatted with the supplied .NET custom date/time format. |
| `{{Now}}` | Local date/time as `yyyy-MM-ddTHH:mm:sszzz`. |
| `{{Now[format]}}` | Local date/time snapshot formatted with the supplied .NET custom date/time format. |
| `{{task.id}}` | Exact persisted task id. |
| `{{task.name}}` | Exact persisted task name. |

Bracket formats use `CultureInfo.InvariantCulture`, are non-empty and no longer than 100
characters, and reuse the existing filename-friendly aliases:

- `YYYY` → `yyyy`;
- `YY` → `yy`;
- `DD` → `dd`;
- `hh` → 24-hour `HH`.

The aliases are ordinal and case-sensitive exactly as today; placeholder names themselves remain
case-insensitive. Users remain responsible for selecting characters valid for the destination
filename. Stage 20 does not sanitize or authorize a resulting path.

### 6.2 Unavailable and unresolved placeholders

Classify unresolved occurrences into stable program-owned warning kinds equivalent to:

- `Unknown` — syntactically complete marker with no agent-prompt definition;
- `WorkflowOnly` — `task.prompt`, `previous.output`, `workflow.context`, or a syntactically
  recognizable `<stepId>.output` reference, including structured selectors;
- `InvalidDateTimeFormat` — recognizable `Today`/`Now` placeholder whose bracket format is empty,
  excessive, malformed, or rejected by .NET formatting;
- `MalformedBraces` — unmatched, nested, empty, or otherwise malformed placeholder braces.

Every unresolved source span remains exact. Warning detection must not normalize the preserved
text. Repeated occurrences may be grouped for display, but total occurrence count and category
counts must remain exact and deterministic. Ordering follows first source occurrence.

### 6.3 Resolution result

Add one pure, unit-testable Core resolver and an immutable result containing:

- the resolved prompt string;
- ordered unresolved-occurrence descriptors;
- total occurrence count by warning kind;
- enough source-span metadata for Prompt editor to project an exact marker from its already
  visible source text without storing that marker in durable warning messages.

The resolver accepts the prompt, task id, task name, and an explicit `DateTimeOffset localNow`.
It does not read the clock itself, log, perform I/O, or depend on provider/model state. Reject only
invalid API arguments such as null prompt/context; unresolved user template content is returned as
data and is never an exception.

If no supported or unresolved marker exists, return the original prompt content unchanged and an
empty warning set. Avoid unnecessary semantic normalization: line endings, Unicode, whitespace,
and all non-placeholder characters remain identical.

### 6.4 Non-recursive examples

Given task name `Archive {{Today}}`, this source:

```text
Run {{task.name}} on {{Today[yyyyMMdd]}}.
```

resolves in the example date to:

```text
Run Archive {{Today}} on 20260803.
```

The `{{Today}}` introduced by `task.name` is not scanned again.

Given this unknown marker:

```text
Write a greeting for {{customer_name}}.
```

the model receives exactly the same marker and execution continues with one `Unknown` warning.
The application does not promise what the model will do with that unresolved text.

## 7. Shared placeholder architecture

### 7.1 Core-owned date/time behavior

Refactor the existing workflow-only date/time resolver so Core owns the common parsing,
normalization, format validation, and invariant formatting. Both the workflow resolver and the
new agent-prompt resolver call that one implementation.

Preserve every existing workflow-visible result and exception behavior. A workflow invalid format
still fails its step as before; an agent-prompt invalid format is preserved and warned. The shared
component may return a typed parse outcome so each caller applies its own failure policy without
duplicating date logic.

`WorkflowPlaceholderCatalog` remains the authoritative workflow authoring catalog. Add explicit
scope metadata or a separate Core-owned agent-prompt projection rather than showing workflow-output
markers as usable in a prompt-only agent. Do not maintain duplicate handwritten date syntax in UI.

### 7.2 Matching and preservation

The current workflow `Replace(string, Func<string,string>)` contract need not be broken. Add a
source-span-aware scan/overload or a dedicated agent scanner capable of returning the original
complete match when unresolved. Reconstructing a marker from a trimmed placeholder body is not
sufficient because it would lose casing or whitespace.

Matching must be linear or otherwise bounded for the task prompt size and resistant to pathological
brace input. Do not use repeated whole-string replacement, recursive substitution, or a loop that
can rescan inserted task names.

## 8. Runtime, warnings, reports, and UI

### 8.1 Runner prompt-only execution

Immediately before the prompt-only agent call:

1. capture one `TimeProvider.GetLocalNow()` value;
2. resolve the persisted prompt with task id/name;
3. emit one content-free warning summary when unresolved occurrences exist;
4. call the agent with the resolved prompt and the existing task tools, immutable execution
   context, and task-effective generation policy;
5. carry the warning summary into the prompt-path `WorkflowResult`, including when the later
   provider/tool execution fails.

The warning line uses program-owned text equivalent to:

```text
Warning: 2 agent prompt placeholder occurrences were left unchanged (1 unknown, 1 workflow-only).
```

It must not contain the prompt, raw markers, replacement values, source indexes, task name, file
path, or resolved output. The normal Runner/run-log infrastructure records that content-free line.
Do not report it through `IIncidentReporter`.

### 8.2 Execution result and Markdown report

Extend the execution result/report contract minimally so non-failing execution warnings can be
carried structurally. Existing workflow construction remains source-compatible where practical and
produces an empty warning collection unless another established path already supplies warnings.

`MarkdownReportWriter` adds `## Warnings` only when warnings exist. It lists content-free warning
codes/messages and aggregate counts. The final output remains unchanged. Report-index status remains
exactly `Success` or `Failure`; Stage 20 does not add a third persisted index status.

An unresolved placeholder plus a successful agent call yields:

- Runner exit code `0`;
- report status `Success`;
- a non-empty Warnings section;
- the ordinary final model output.

### 8.3 Prompt editor

Prompt editor adds concise active-theme help listing the four available forms and one filename
example. It does not add a new page, modal catalog, settings control, or general variable editor.

On **Test the prompt**:

1. capture one injected `TimeProvider` local snapshot;
2. resolve the current unsaved prompt using the loaded task id/name;
3. show a non-blocking warning summary before/during streaming;
4. optionally list bounded exact unresolved markers by slicing the already visible prompt using
   source spans; this projection stays memory-only and is cleared on the next test/load;
5. send only the resolved prompt to `RunPromptStreamingAsync` with the current task tool whitelist,
   immutable execution context, and effective unsaved task generation policy;
6. preserve cancellation, chunks, output text, and command-state behavior.

Warnings do not prepend text to the model output and are not counted as provider chunks. Status text
states whether the completed or cancelled test had unresolved placeholders. A warning must remain
visible after completion until the prompt is edited, reloaded, or tested again.

### 8.4 Save, scheduling, and workflow precedence

Task Save remains valid with unresolved agent markers. Registration does not resolve the prompt,
because the date must be the execution date rather than the save or scheduling date. Task JSON
stores the original template exactly.

When `workflow.Count > 0`, the existing workflow path wins. Do not resolve agent placeholders first,
do not emit Stage 20 warnings for the raw task prompt, and do not change how a later workflow step
substitutes `{{task.prompt}}`.

## 9. Privacy, observability, and failure semantics

Allowed durable warning fields are limited to:

- program-owned warning code/category;
- total unresolved occurrence count;
- per-category occurrence counts;
- task id only where the existing logging scope already supplies it;
- surface (`run`, `test-prompt`, or Prompt editor) where existing logs require it;
- elapsed milliseconds for resolution if measured.

Never log, incident-report, index, or add to structured telemetry:

- original or resolved prompt text;
- raw or normalized placeholder bodies;
- task-name replacement values;
- date/time replacement strings;
- source spans or surrounding prompt text;
- a path or filename produced by expansion;
- model requests/responses beyond the existing explicit private Runner Debug policy.

The Stage 15/16 explicit Runner Debug channel may display the resolved LLM request because the user
opted into that existing private diagnostic behavior. Stage 20 adds no separate raw prompt channel
and does not persist Debug content.

Placeholder warnings are expected authoring/runtime feedback, not incidents. A resolver programming
failure is an ordinary unexpected infrastructure failure and follows the existing incident path, but
its exception/reporting boundary must sanitize prompt content.

## 10. Normative examples

### 10.1 Dated output filename

Agent prompt:

```text
Create the Markdown report with the filesystem tool at:
C:\Users\Example\Downloads\Sort-{{Today[yyyyMMdd]}}.md
Verify that the file exists before reporting success.
```

At local time `2026-08-03T14:30:25+02:00`, the agent receives:

```text
Create the Markdown report with the filesystem tool at:
C:\Users\Example\Downloads\Sort-20260803.md
Verify that the file exists before reporting success.
```

The filesystem tool still requires the task's normal ReadWrite authorization for the directory.

### 10.2 Collision-resistant timestamp

```text
Write the report to reports/Sort-{{Now[yyyyMMdd-HHmmss]}}.md.
```

At the example time above, the resolved leaf name is `Sort-20260803-143025.md`.

### 10.3 Task identity

```text
Include task id {{task.id}} and task name {{task.name}} in the report heading.
```

The exact saved id and name are inserted once before the model call.

### 10.4 Unknown marker

```text
Create a greeting for {{customer_name}}.
```

The source is saved and sent unchanged. Execution continues with one content-free `Unknown`
warning. MyLocalAgent does not look for customer data.

### 10.5 Workflow-only marker in an agent prompt

```text
Summarize {{previous.output}}.
```

A prompt-only task sends the marker unchanged and emits one `WorkflowOnly` warning. To consume a
previous result, the author must use a workflow where that output has a defined source.

## 11. Required automated tests

Add deterministic tests for at least:

### 11.1 Pure resolver

1. `Today` and `Now` defaults with a fixed non-UTC local offset.
2. `yyyyMMdd`, `yyyy-MM-dd`, `HHmmss`, and mixed literal custom formats.
3. Friendly `YYYY`, `YY`, `DD`, and `hh` aliases retaining existing semantics.
4. Case-insensitive placeholder names and surrounding internal whitespace.
5. Several date/time occurrences use exactly one supplied clock snapshot.
6. `task.id` and Unicode `task.name` exact insertion.
7. No recursive resolution when task name contains supported or unknown markers.
8. Unknown marker exact preservation including casing and whitespace.
9. Workflow-only detection for `task.prompt`, `previous.output`, `workflow.context`, named step
   output, and structured selectors.
10. Invalid/empty/excessive/malformed date format exact preservation and warning classification.
11. Empty, nested, unmatched, adjacent, and pathological brace sequences remain bounded and are
    classified without data loss.
12. Repeated-marker occurrence counts and first-source ordering.
13. Prompts without markers remain byte-for-byte/character-for-character unchanged with no warning.
14. Line endings, Unicode scalar values, surrogate pairs, and non-placeholder braces remain intact.
15. Replacement output is not rescanned.

### 11.2 Shared workflow compatibility

16. Every existing workflow `Today`/`Now` default, custom format, alias, and invalid-format test
    remains green after the Core refactor.
17. Workflow unknown placeholders still fail with `WorkflowPlaceholderException`.
18. Workflow `task.prompt`, `previous.output`, named output, structured selection, and
    `workflow.context` remain unchanged.
19. A task with both prompt and workflow performs no Stage 20 prompt pre-expansion or warning.

### 11.3 Runner and report integration

20. Prompt-only Runner sends the resolved prompt to a fake agent and never the template.
21. `run` and `test-prompt` use identical resolution for the same frozen clock/task.
22. Tool whitelist, execution context, generation policy, cancellation, and loop limits remain
    unchanged after resolution.
23. A successful execution with unknown/workflow-only markers exits successfully and writes a
    Warnings section plus final output.
24. A later provider failure remains failure while preserving content-free warning metadata.
25. Report index retains only `Success`/`Failure` and its existing shape.
26. Run-log warning contains category/count but none of the raw canary marker, prompt, task name,
    replacement, or path.
27. No placeholder warning creates an incident.
28. Concurrent tasks with different ids/names/clock snapshots cannot observe each other's values.

### 11.4 Prompt editor and unchanged surfaces

29. Prompt editor Test resolves current unsaved text, not only persisted task text.
30. Prompt editor uses loaded task identity and a single injected local-time snapshot.
31. Warning feedback is non-blocking, memory-only, cleared/replaced deterministically, and does not
    alter streamed output or chunk count.
32. Cancellation after resolution retains the warning without converting cancellation to failure.
33. AI Playground, AI Workflow Helper, AI Explain, Guardrails, skills, workflow `ai.prompt`, and
    Tool tester do not invoke the agent-prompt resolver.
34. Prompt Save and task scheduling preserve the original placeholder template.
35. Active Light and Dark warning/help states use semantic palette resources and no inline colors.

Use fake agents/providers and frozen time. Tests never contact a real model, connector, filesystem
outside isolated temporary roots, or network endpoint.

## 12. Documentation

Update during Stage 20 implementation:

- `Docs/Prompts/00-Shared-Specification.md` with a concise new Stage 20 addendum covering scope,
  supported placeholders, warning/preservation behavior, privacy, workflow non-regression, and
  runtime surfaces;
- `Docs/ARCHITECTURE-LEDGER.md` with the exact public APIs, Core refactor, one-snapshot semantics,
  Runner/UI integration, warning/report behavior, privacy decisions, tests, manual evidence, and
  residual limitations;
- root `README.md` with an **Agent prompt placeholders** subsection clearly separated from workflow
  placeholders;
- `Docs/User-Manual.html` and `Docs/User-Manual.en.html`, maintaining structural and semantic
  parity between French and English;
- relevant `Docs/FirstRun.md` and `Docs/FirstRun-EN.md` prompt-authoring guidance;
- `Docs/theme-audit.md` for Prompt editor help/warning states in Light and Dark themes;
- `Docs/acceptance-report.md` and `Docs/Prompts/README.md` with Stage 20 scope and verification.

Both the French and English user guides must include complete, copyable examples for:

1. a dated filename using `Sort-{{Today[yyyyMMdd]}}.md`, showing a concrete resolved filename;
2. a collision-resistant filename using `{{Now[yyyyMMdd-HHmmss]}}`;
3. `{{task.id}}` and `{{task.name}}`;
4. unknown `{{customer_name}}` preservation with a non-failing warning;
5. `{{previous.output}}` being unavailable in a prompt-only agent task and the equivalent valid
   workflow concept;
6. the difference between save time, schedule time, and execution time;
7. the fact that resolved filesystem paths still require task authorization and that model/tool
   success is independent of placeholder expansion.

Both guides must state plainly:

- placeholders are expanded by MyLocalAgent before the prompt reaches the model;
- date/time comes from the injected Windows local clock at execution time;
- substitution is single-pass and non-recursive;
- an unresolved marker is sent unchanged, warning-only, and may be interpreted unpredictably by
  the model;
- durable warnings intentionally omit the raw marker/prompt for privacy;
- workflows retain their larger data-bearing placeholder set and strict failure behavior;
- AI Playground and other taskless surfaces do not receive Stage 20 expansion.

Validate both standalone manuals for balanced tags, unique ids, valid internal targets, embedded
script syntax, zero unintended external resources, language-switch links, copy controls, and
matching example coverage. Documentation examples use synthetic task names and paths only.

## 13. Stage gate

Run and show output:

1. `dotnet restore OD.MyLocalAgent.sln -m:1`
2. `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1`
3. `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1`
4. Parse every JSON file under `Docs/samples`.
5. Validate `Docs/User-Manual.html` and `Docs/User-Manual.en.html` structurally and verify the
   required Stage 20 examples in both languages.
6. `git diff --check`

Build must have zero warnings and zero errors. Deterministic tests must pass. The existing local
Ollama integration probe may remain skipped. No live model, personal task, personal directory, or
real scheduled execution is required for the automated gate.

## 14. Acceptance addendum

- [ ] Exactly `Today`, `Now`, `task.id`, and `task.name` are resolved for prompt-only saved-task
      agent executions.
- [ ] `Today`/`Now` behavior is implemented once in Core and remains identical in workflows.
- [ ] One local-time snapshot covers every date/time occurrence in one prompt.
- [ ] Expansion occurs immediately before execution, never at Save or schedule-registration time.
- [ ] Resolution is single-pass and replacement text is never rescanned.
- [ ] Unknown, workflow-only, malformed, and invalid-format markers are preserved exactly and do
      not fail or skip execution.
- [ ] Workflow tasks and every workflow placeholder retain their previous strict behavior.
- [ ] AI Playground, Tool tester, helper/explanation, Guardrails, skills, and workflow `ai.prompt`
      remain outside Stage 20 expansion.
- [ ] Runner `run`, Run now, `test-prompt`, and Prompt editor Test use the intended prompt source,
      task identity, time snapshot, generation policy, tools, and execution context.
- [ ] Successful runs with warnings remain successful and reports contain a separate Warnings
      section without changing report-index status.
- [ ] Logs, run logs, reports, incidents, and telemetry contain no raw/resolved prompt,
      placeholder body, replacement, task-name value, or expanded path.
- [ ] Prompt editor exact-marker feedback is bounded, memory-only, active-theme, and does not alter
      the model output stream.
- [ ] No package, provider call, tool, skill, setting, task field, migration, page, authorization,
      Safety gate, or general templating engine was added.
- [ ] README, shared specification, ledger, First Run guides, both manuals, theme audit, acceptance
      report, and prompt index agree.
- [ ] Both EN and FR manuals contain all required copyable examples and pass structural validation.
- [ ] Complete gate is green with `-m:1` on every dotnet command.

## 15. Before finishing implementation

1. Re-read the complete Stage 20 diff against this prompt, shared placeholder rules, Stage 17 V2
   privacy, Stage 15/16 diagnostics, and the post-Stage-19 architecture ledger.
2. Search every `RunPromptAsync` and `RunPromptStreamingAsync` call site. Prove only the four
   task-owned prompt-only surfaces resolve and every excluded surface remains unchanged.
3. Search for `DateTime.Now`, `DateTimeOffset.Now`, duplicated `Today`/`Now` regexes, format
   normalization, and invariant formatting. Remove every new divergent clock/format path.
4. Prove workflow tasks do not pre-expand `ScheduledTask.Prompt` and workflow invalid placeholders
   still fail under the established error policy.
5. Use canary prompts, markers, task names, replacement dates, and paths to search rolling logs,
   run logs, reports, report index, incidents, and test diagnostics. Remove every unauthorized
   content leak.
6. Confirm warnings cannot change success, exit code, scheduling, Save, report index, or incident
   state.
7. Confirm the Prompt editor resolves unsaved text and preserves its existing task generation
   options, execution context, tool whitelist, cancellation, and chunk-count behavior.
8. Validate both manuals and compare their Stage 20 example inventory for EN/FR parity.
9. Run the complete gate with `-m:1`, then report changed files, public APIs, warning categories,
   tests, manual GUI checks actually performed, documentation validation, and residual limitations.
