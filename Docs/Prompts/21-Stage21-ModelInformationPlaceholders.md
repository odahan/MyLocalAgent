# STAGE 21 — Model Information Placeholders

> Run only after **Stage 20** is finished. Stage 20 remains the contract for task/date
> placeholders and unresolved-marker warnings. This stage adds four deterministic model
> information placeholders to every user-authored execution surface, including task-owned
> prompts, workflows, agentic executions, and AI Playground. It also writes the resolved
> `ModelInfo` value at the beginning of every task run log. It does not add a provider call,
> model probe, persisted task field, setting, tool, skill, page, package, authorization, or
> general templating engine.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§23. This
   standalone prompt is the complete Stage 21 implementation contract and must be reflected in
   a concise new shared-specification addendum during implementation.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read. Recorded provider selection, generation-policy
   hierarchy, context-window semantics, workflow placeholders, prompt-only placeholder privacy,
   Runner execution, and run-log contracts are binding.
3. The post-Stage-20 implementations of `ILlmClientService`, `ILlmClientFactory`, provider
   services/options, `EffectiveGenerationPolicy`, `GenerationPolicyResolver`,
   `WorkflowPlaceholderCatalog`, both placeholder resolvers, `IMyLocalAgentService`,
   `MyLocalAgentService`, `SequentialWorkflowEngine`, `RunnerCommandHandlers`, `RunLogSession`,
   AI Playground, Prompt editor, workflow authoring services, dependency injection, tests,
   README, and both standalone manuals.
4. Existing tests for provider reload, hierarchical Temperature/Top P resolution, global context
   declaration, prompt-only placeholders, workflow step overrides, workflow validation,
   playground raw/agent modes, run-log ordering, privacy, and concurrent execution.

## 2. Mission

Expose the active model call configuration through exactly four application-resolved values:

- `{{ModelName}}` — active configured model identifier;
- `{{ModelSettings}}` — effective Temperature and Top P;
- `{{ModelContextSize}}` — declared context-window size in tokens;
- `{{ModelInfo}}` — combined active model and effective settings.

The values are computed locally from the already selected provider/model and the immutable
effective generation policy. They are substituted before user-authored text reaches the model or
a workflow tool/skill. They never require an additional LLM, health, network, connector, tool,
filesystem, or model-catalog call.

Every retained task run log starts with the exact resolved `ModelInfo` value for that task. This
header makes the model configuration visible during diagnosis without logging prompt content,
model output, secrets, paths, or external data.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S21-D1 | **Exactly four placeholders.** Stage 21 adds only `ModelName`, `ModelSettings`, `ModelContextSize`, and `ModelInfo`. Matching is case-insensitive and accepts surrounding whitespace inside double braces. |
| S21-D2 | **Global user-authored scope.** The four markers are available in prompt-only task execution, scheduled and Run now paths, agentic task execution, workflow string arguments and prompt/skill text, Prompt editor Test, AI Playground raw and agent modes, and user-authored prompts routed through `IMyLocalAgentService`. |
| S21-D3 | **Effective call values.** Temperature and Top P come from the immutable policy already resolved for the call. Workflow AI-step overrides win over task overrides, which win over Settings. Task calls use task-over-settings. Taskless/playground calls use Settings. |
| S21-D4 | **Declared context.** Context is the configured `ContextWindowTokens` declaration from Settings. It is not remaining capacity, prompt token count, output budget, provider telemetry, or a dynamically queried server limit. |
| S21-D5 | **Active model name.** Model name is the exact configured model identifier of the provider selected for the call. Provider display name and endpoint are not included. No model alias normalization or remote metadata lookup occurs. |
| S21-D6 | **Invariant deterministic format.** Numeric values use `CultureInfo.InvariantCulture` with general formatting and no thousands separators. The combined strings and separators are exact as defined in §6. |
| S21-D7 | **One immutable snapshot.** One model name and effective generation policy snapshot covers one dispatch or workflow step. Replacement text is not rescanned. Concurrent calls cannot observe each other's task/step settings. |
| S21-D8 | **Workflow-wide availability.** Workflow validation and authoring treat all four markers as valid in any string argument. An AI step uses its step-effective policy; a non-AI tool step uses the task-effective policy. Existing task/output/date placeholders remain unchanged. |
| S21-D9 | **Provider-bound final resolution.** Model-bound agent calls resolve at the last shared dispatch boundary as a safety net, so agentic and helper call sites cannot accidentally omit Stage 21 expansion. AI Playground raw-provider mode resolves explicitly before its direct provider call. |
| S21-D10 | **No Stage 20 warning.** The four markers are recognized as valid model placeholders in task-owned prompts. They never create `Unknown` or workflow-only placeholder warnings. Existing unknown-marker preservation and warning behavior remains unchanged. |
| S21-D11 | **Single pass and preservation.** Resolution is non-recursive. Unrelated placeholders and malformed brace text remain unchanged by the model resolver. A model name containing marker-like text is inserted literally. |
| S21-D12 | **Task-log header.** The first visible line of every retained task run log is the task-effective `ModelInfo` value. The structured start record remains first internally. The header is output metadata, not prompt content and not an incident. |
| S21-D13 | **Reload semantics.** Provider/model and global generation setting changes apply through existing monitored options at the next call. An in-flight call or workflow-step attempt keeps its captured values, including retries. |
| S21-D14 | **Privacy.** Model identifier, Temperature, Top P, and declared context size may appear in task logs and existing sanitized diagnostics. Prompts, tool arguments/results, external content, keys, endpoints, paths, and model responses are never added by this stage. |
| S21-D15 | **No authorization effect.** Placeholder expansion grants no tool group, file/database authorization, connector access, Safety permission, scheduler right, or Windows permission. |
| S21-D16 | **No new configuration or dependency.** Stage 21 reuses existing provider options and hierarchical generation policy. It adds no setting, task JSON property, migration, provider, package, model call, page, dialog, or external service. |

## 4. Critical constraints

- R1, R2, R4, R5, R7, R9, R10 and all later shared addenda remain binding. Public APIs use
  English XML documentation; UI/log text and code comments remain English; the build is
  warning-clean.
- Preserve dependency direction. Core may use the existing Ollama policy contracts; Workflows
  continues to depend on Core and must not introduce a reverse reference.
- Resolve model identity from the selected `ILlmClientService` or existing monitored provider
  options. Do not infer model name from provider display text and do not call provider health.
- Do not recompute Temperature/Top P independently from the policy passed to an AI call. The text
  shown to the model must describe the same policy applied to that call.
- Use invariant formatting. Do not use `CurrentCulture` for placeholder output.
- Every `dotnet restore`, `dotnet build`, and `dotnet test` command explicitly uses `-m:1`.

## 5. Scope and execution matrix

| Surface | Model source | Generation policy | Required behavior |
|---|---|---|---|
| Scheduled/CLI task `run` | selected provider model | task over Settings | Resolve all four; write task-log `ModelInfo` header. |
| UI **Run now** | child Runner selected provider model | task over Settings | Identical to scheduled Runner behavior. |
| CLI `test-prompt` | selected provider model | task over Settings | Resolve before dispatch; existing report behavior remains. |
| Prompt editor **Test the prompt** | selected provider model | unsaved task override snapshot over Settings | Resolve without Stage 20 warning and stream normally. |
| Agentic task/service calls | provider returned by factory | explicit effective call policy | Resolve at shared dispatch before trace/model call. |
| Workflow AI step | active model | step over task over Settings | Resolve in step strings with the same policy passed to the AI call. |
| Workflow tool step | active model | task over Settings | Resolve in every nested string argument before tool invocation. |
| AI Playground raw mode | active provider model | Settings | Resolve before direct provider streaming. |
| AI Playground agent mode | active provider model | Settings | Resolve before agent streaming; shared dispatch remains a safety net. |
| Workflow authoring catalog/helper | no runtime value during authoring | documented semantics only | Advertise the four forms; generated workflow JSON may contain them. |

Internal program-owned system prompts, Guardrails schemas/nonces, health requests, connector
payloads, model responses, and tool outputs are not retrospectively scanned. Stage 21 exposes
values in user-authored execution text; it is not a universal object-graph preprocessor.

## 6. Normative placeholder contract

Given model `qwen3:14b`, effective Temperature `0.25`, effective Top P `0.9`, and declared context
size `32768`, exact values are:

| Placeholder | Exact value |
|---|---|
| `{{ModelName}}` | `qwen3:14b` |
| `{{ModelSettings}}` | `Temp 0.25 - TopP 0.9` |
| `{{ModelContextSize}}` | `32768` |
| `{{ModelInfo}}` | `ModelName qwen3:14b - Temp 0.25 - TopP 0.9 - Context 32768` |

`ModelContextSize` has no suffix or grouping separator. `ModelSettings` and `ModelInfo` use the
English labels and ASCII ` - ` separators shown above. `ModelInfo` is composed from the other
three meanings and must never drift to a different numeric or model snapshot.

Replacement is case-insensitive:

```text
{{ modelname }} {{MODELSETTINGS}} {{modelcontextsize}} {{ModelInfo}}
```

Inserted model text is literal. For a synthetic model name `model-{{ModelContextSize}}`, resolving
`{{ModelName}}` produces exactly `model-{{ModelContextSize}}`; the introduced marker is not
expanded again.

## 7. Shared architecture

Add one immutable Core model-placeholder context containing the model name, effective Temperature,
effective Top P, and context-window tokens. It exposes the exact Stage 21 projections. Add one
pure Core resolver that replaces only the four supported names in one bounded, non-recursive pass
and preserves every unrelated complete marker exactly.

`ILlmClientService` exposes the current `ModelName` beside `ProviderName`. Ollama and
OpenAI-compatible implementations read their monitored provider options, retaining existing
per-call reload behavior. The factory and provider validation contract remain unchanged.

The shared agent dispatch paths resolve model placeholders only after obtaining the selected
provider and before request tracing or the model call. Direct `ai.prompt`, non-streaming agent,
and streaming agent paths use the same context construction. AI Playground raw mode uses the
same resolver before direct provider generation.

`WorkflowPlaceholderCatalog` advances its documented contract version and advertises all four
markers. Static validation accepts them. `SequentialWorkflowEngine` builds a context per step and
passes it through recursive JSON string resolution. Existing object/array traversal, structured
output selection, date/time handling, error policy, and retry behavior remain unchanged.

## 8. Task run logs

After the Runner loads a task and before visible execution output, resolve the task-effective
policy and active configured model. Write exactly one output line equal to the `ModelInfo` value:

```text
ModelName qwen3:14b - Temp 0.25 - TopP 0.9 - Context 32768
```

The JSONL `start` record remains the first physical record. `ModelInfo` is the first record exposed
by `RunLogCatalog` as a visible line. It precedes progress, warnings, tool output, final status,
and errors. Existing retention, incremental tailing, stream classification, protocol filtering,
flush, sharing, end record, and malformed-line tolerance remain unchanged.

The header uses task-level overrides because it describes the task run baseline. Individual
workflow AI steps may use step overrides, visible through `{{ModelInfo}}` inside that step and
existing sanitized generation diagnostics. Do not duplicate a new header for every step.

## 9. Failure, privacy, and compatibility

- Invalid provider/model or generation configuration follows the existing typed configuration and
  incident path; Stage 21 does not silently invent a model or numeric fallback in production.
- Model placeholder resolution itself performs no I/O and does not create incidents or warnings.
- Existing Stage 20 unknown/malformed/date/workflow-only warning counts and report behavior remain
  unchanged for all other markers.
- Workflow unknown markers remain strict and fail the current step under its established error
  policy.
- Existing private Runner Debug may contain the already resolved request under its explicit opt-in
  contract. Stage 21 adds no raw request channel.
- The task-log model header may contain only the exact `ModelInfo` fields. It contains no prompt,
  task name, tool list, path, SQL, external content, endpoint, API key, response, or exception.

## 10. Required automated tests

Add deterministic tests for at least:

1. Exact values and punctuation for all four placeholders.
2. Invariant fractional formatting and context size without grouping.
3. Case-insensitive names and surrounding whitespace.
4. Unrelated marker and malformed-brace preservation.
5. Non-recursive insertion when model name contains marker-like text.
6. Prompt-only resolver accepts model markers without unresolved warnings.
7. Agent non-streaming, streaming, and direct `ai.prompt` dispatch use the selected provider model
   and the exact explicit policy.
8. AI Playground raw mode resolves with global Settings values; agent mode remains covered by the
   shared dispatch boundary.
9. Workflow `ai.prompt` resolves step-over-task-over-Settings values.
10. Workflow skill and nested tool string arguments resolve model values; non-AI steps use the
    task-effective policy.
11. Workflow catalog metadata, validation, authoring help, and contract version include all four.
12. Ollama and OpenAI-compatible services expose reloadable model names.
13. The first visible run-log line is exact `ModelInfo`, followed by ordinary task output.
14. Concurrent tasks/steps with different policies and model snapshots remain isolated.
15. Existing Stage 20 warning, workflow output/date, generation hierarchy, playground, run-log,
    privacy, cancellation, retry, and provider tests remain green.

Use fake providers, fake agents, monitored options, and frozen policies. Tests never contact a real
model, connector, filesystem outside isolated temporary roots, or network endpoint.

## 11. Documentation

Update during Stage 21 implementation:

- `Docs/Prompts/00-Shared-Specification.md` with a concise Stage 21 addendum;
- `Docs/ARCHITECTURE-LEDGER.md` with exact formats, public API, runtime integration, task-log
  ordering, tests, and residual limitations;
- root `README.md` with one shared model-placeholder section and surface/effective-policy table;
- `Docs/User-Manual.html` and `Docs/User-Manual.en.html` with structurally equivalent FR/EN
  explanations, exact outputs, workflow/task/playground scope, and task-log behavior;
- `Docs/Prompts/README.md` with Stage 21 scope and order if that index is maintained.

Documentation must state plainly that context size is the configured declared window, not current
token usage or remaining room; that Temperature/Top P are the effective call values; and that the
task-log header is task-level while a workflow step may have its own override.

## 12. Stage gate

Run and show output:

1. `dotnet restore OD.MyLocalAgent.sln -m:1`
2. `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1`
3. `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1`
4. Validate `Docs/User-Manual.html` and `Docs/User-Manual.en.html` structurally and verify equal
   Stage 21 example coverage in both languages.
5. `git diff --check`

Build must have zero warnings and zero errors. Deterministic tests must pass. The existing local
Ollama integration probe may remain skipped. No live model, personal task, real scheduled
execution, or network call is required for the automated gate.

## 13. Acceptance addendum

- [ ] Exactly four model placeholders are added with the exact §6 formats.
- [ ] Model name and settings describe the active selected provider and effective call policy.
- [ ] Context is the declared configured window and is never described as live usage or remaining
      capacity.
- [ ] Prompt-only, Run now, scheduled, agentic, workflow, and playground surfaces resolve the
      markers before dispatch.
- [ ] Workflow AI steps use step-effective values and non-AI steps use task-effective values.
- [ ] Resolution is invariant, case-insensitive, single-pass, non-recursive, and preserves
      unrelated markers.
- [ ] Stage 20 does not warn for model placeholders; other warning classes remain unchanged.
- [ ] The first visible retained task-log line is exact task-effective `ModelInfo`.
- [ ] The log header adds no prompt, output, secret, endpoint, path, or external content.
- [ ] Provider reload, generation hierarchy, retries, cancellation, workflow errors, permissions,
      Safety, and privacy behavior remain unchanged.
- [ ] No package, provider/model call, task field, setting, migration, page, tool, skill, or general
      templating engine is added.
- [ ] Shared specification, ledger, README, both manuals, prompt index, implementation, and tests
      agree.
- [ ] Complete gate is green with `-m:1` on every dotnet command.

## 14. Before finishing implementation

1. Search every user-authored model dispatch and direct provider call; prove Stage 21 markers are
   resolved exactly once before the model sees them.
2. Compare every displayed/logged value with the exact immutable policy applied to the same call.
3. Search workflow validation, execution, helper catalogs, and authoring UI for divergent marker
   lists or stale contract versions.
4. Prove task logs expose one model header first and retain all established JSONL/tailing behavior.
5. Use canary prompts, tool arguments, paths, external text, endpoints, keys, and responses to
   verify Stage 21 adds none of them to logs or incidents.
6. Confirm replacement text is never rescanned and unrelated placeholders remain exact.
7. Validate EN/FR manual parity and exact example values.
8. Run the complete gate with `-m:1`, then report changed files, public APIs, exact formats, tests,
   documentation validation, and residual limitations.
