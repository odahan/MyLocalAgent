# STAGE 14 — Hierarchical AI Generation Settings

> Run only after **Stage 13** is finished. Stages 9 and 10 remain abandoned proposals and are
> neither prerequisites nor in scope. The existing task-owned sequential workflow remains the
> product model.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§17.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs, privacy boundaries, provider
   behavior, and concurrency constraints are binding.
3. The current LLM providers, agent service, built-in AI skills, workflow engine, task
   serializer and validator, Task editor, Prompt editor, AI playground, AI Workflow Helper,
   Guardrails calls, Settings persistence, tests, README, manuals, and samples.

## 2. Mission

Give users controlled, explicit generation behavior without duplicating provider
configuration or weakening deterministic internal AI features.

Add:

- global default `Temperature` and `TopP` values in Settings;
- optional task-level overrides for those two values;
- optional step-level overrides for every workflow step that performs an AI call;
- a global `ContextWindowTokens` default with optional task/AI-step context overrides for every
  user-directed LLM call, while internal calls retain the global default;
- fixed program-owned generation policies for internal operations such as AI Workflow Helper,
  AI Explain, their repair calls, and Guardrails screening/reduction.

The effective user-workflow values are resolved independently for each property:

```text
AI step override > task override > global Settings default
```

An omitted property means **inherit**, not reset. For example, a step that overrides only
`temperature` still inherits `topP` from its task when present, otherwise from Settings.

## 3. Product decisions (locked by the product owner — do not re-open)

| # | Decision |
|---|---|
| D1 | **Global defaults**: Settings owns the default `Temperature` and `TopP` used by normal user-directed AI calls. Changes are reloadable and apply to the next model call without restarting the UI or runner. |
| D2 | **Task override**: a task may persist an optional `generationOptions` object containing optional `temperature`, `topP`, and/or integer `contextWindowTokens`. Missing object or properties inherit from Settings. |
| D3 | **AI-step override**: an object-form workflow step whose Tool Map definition is an AI step may persist the same optional `generationOptions`. Each property inherits independently from the task and then Settings. |
| D4 | **No non-AI override**: `generationOptions` on a deterministic tool step is a validation error. A string-form AI step has no override and inherits; users may convert it to object form to customize it. |
| D5 | **Hierarchical context window**: Settings supplies the default `ContextWindowTokens`; optional task and AI-step `generationOptions.contextWindowTokens` override it independently through step → task → Settings. Internal calls retain the global default. |
| D6 | **Context is not output length**: `ContextWindowTokens` describes the model's total request context budget. It must not be presented or implemented as `MaxOutputTokens`. Output-token control is outside this stage. |
| D7 | **Field-wise resolution**: resolving an override is a merge per property, never replacement of the complete object. Resolution returns both effective values and provenance (`Step`, `Task`, or `Settings`) for UI and tests. |
| D8 | **Normal call coverage**: task options apply to prompt-only tasks, the task agent, Prompt editor tests, direct `ai.prompt` calls, and built-in AI skill steps. AI Playground has no task and uses the global defaults. |
| D9 | **Internal fixed policies**: AI Workflow Helper, AI Explain, their correction attempts, Guardrails screening, and Guardrails reduction do not consume user `Temperature` or `TopP`. Their values are named program constants: `Temperature = 0`, `TopP = 1`. |
| D10 | **Context still global for internal calls**: fixed internal temperature/top-p policies do not create a separate model or context configuration. The global context window still applies to internal calls. |
| D11 | **Explicit immutable propagation**: effective options travel as immutable call context. Do not use a mutable singleton, static current-task value, `AsyncLocal`, or other ambient state; concurrent tasks must not leak options into one another. |
| D12 | **Provider honesty**: map supported options through `Microsoft.Extensions.AI` and the provider adapters. Never silently drop a configured value while claiming it was applied. If a provider cannot set a context window, describe the setting as the application's declared budget and let the provider remain authoritative. |
| D13 | **Backward-compatible task files**: every existing task without `generationOptions` remains valid and inherits Settings. Deserialization does not inject overrides, and canonical serialization does not add empty override objects. |
| D14 | **No secrets or sensitive logging**: settings and overrides are non-secret, but prompts, responses, task data, and workflow outputs retain all existing privacy rules. Logs may contain only sanitized effective numeric options and provenance, never model input or output. |
| D15 | **English product surface**: property names, code, XML documentation, validation messages, UI labels, helper descriptions, manuals, and samples are English. |

## 4. Critical constraints (restated)

- Keep the current provider/model selection and `ILlmClientFactory.Create()` behavior. Do not
  introduce per-task providers, per-task models, credentials, endpoints, or cloud fallback.
- `ToolMap` remains the single source of truth for whether a workflow alias is an AI step.
  Do not maintain a second handwritten AI-alias list in the validator or UI.
- Stage 11 immutable filesystem authorization, tool whitelists, Safety gates, Guardrails
  boundaries, and concurrent-task isolation remain unchanged.
- The Stage 13 helper remains tool-less. Generated aliases and generation overrides are inert
  JSON until a task is saved and later executed.
- Preserve current provider/model hot reload. Already-running calls retain their immutable
  resolved options; newly started calls see newly saved Settings.
- Use `Microsoft.Extensions.AI.ChatOptions.Temperature` and `TopP` as the provider-neutral
  generation contract. Keep provider-specific context-window mapping isolated in the provider
  project.
- Do not mutate caller-supplied `ChatOptions`. Clone or build a per-call instance before
  applying an effective policy.
- Do not add `TopK`, seed, frequency/presence penalties, stop sequences, or
  `MaxOutputTokens` in this stage.
- Do not add a graph engine, separate Workflows page, generation-profile library, or new
  external service.
- No new NuGet package is required or permitted.
- Warning-clean, complete files, English XML documentation on every public API, MVVM Toolkit
  source generators only, and `TimeProvider` for wall-clock behavior.
- Every `dotnet build` and `dotnet test` command explicitly uses `-m:1`.

## 5. Data contracts and validation

### 5.1 Global configuration

Extend the `AI` configuration section:

```json
{
  "AI": {
    "Provider": "Ollama",
    "GenerationDefaults": {
      "Temperature": 0.2,
      "TopP": 1.0,
      "ContextWindowTokens": 8192
    }
  }
}
```

These are safe application defaults, not claims about every model's physical capacity.

Validation:

- `Temperature` is finite and in the inclusive range `0`–`2`;
- `TopP` is finite and greater than `0` and at most `1`;
- `ContextWindowTokens` is an integer in the inclusive range `1024`–`2,000,000`;
- invalid persisted configuration produces a clear Settings validation error and must not be
  silently clamped;
- the Settings UI explains that context capacity is model-dependent and must match the active
  model;
- changing provider or model does not guess or silently rewrite the context window.

Use one reloadable options contract for these global values in both UI and Runner. The
Settings save path writes the complete `AI` section without losing `Provider` or unrelated
configuration.

### 5.2 Task override

The optional task JSON shape is:

```json
{
  "id": "precise-mail-summary",
  "name": "Precise mail summary",
  "schedule": "0 7 * * *",
  "prompt": "Summarize today's mail.",
  "generationOptions": {
    "temperature": 0.1,
    "topP": 0.9
  },
  "tools": ["gmail"],
  "fileSystemAccess": [],
  "workflow": [],
  "enabled": true,
  "errorPolicy": "StopOnError"
}
```

`generationOptions`, `temperature`, `topP`, and `contextWindowTokens` are individually optional. An explicitly empty
object is accepted on input but canonical serialization omits it. The same numeric validation
as §5.1 applies. JSON `null`, strings, `NaN`, infinities, and unknown generation-option
properties fail deterministically rather than being treated as inheritance.

### 5.3 AI-step override

The optional object-form workflow shape is:

```json
{
  "step": "ai.prompt",
  "id": "draft",
  "args": {
    "prompt": "Draft three alternative titles from {{source.output}}."
  },
  "generationOptions": {
    "temperature": 0.8
  }
}
```

This step inherits `topP` from the task or Settings. The override property is a sibling of
`args`, `onError`, and retry properties; it is not an AI prompt argument and is never exposed
to a tool or inserted into prompt text.

The contract applies to every current or future alias for which
`ToolMap.Definition.IsAiStep` is true, including `ai.prompt`, `ai.summarize`,
`ai.extractActionItems`, and `ai.classifyEmail`. A non-AI alias with
`generationOptions` fails workflow validation with the step id and a clear message.

String-form AI steps remain valid and inherit all values. Canonical workflow serialization
keeps string form only when no object-only property, including `generationOptions`, is
present.

### 5.4 Effective policy

Introduce small immutable contracts with one responsibility each:

- persisted/reusable nullable overrides (`temperature`, `topP`);
- validated global defaults (`temperature`, `topP`, `contextWindowTokens`);
- a fully resolved per-call policy with non-null values and provenance for each overridable
  property;
- named internal policies for the fixed operations in D9.

The resolver performs:

```text
temperature = step.temperature ?? task.temperature ?? settings.temperature
topP        = step.topP        ?? task.topP        ?? settings.topP
context     = step.contextWindowTokens ?? task.contextWindowTokens ?? settings.contextWindowTokens
```

It validates before any provider call. It must be a pure, deterministic service that can be
unit tested without an LLM or configuration file.

Do not pass the persisted task or mutable ViewModel into provider code. The resolved
immutable policy is the boundary.

## 6. Runtime propagation

### 6.1 Task and agent execution

For a prompt-only task, resolve the task override against the current Settings snapshot and
pass the resulting policy to `IMyLocalAgentService`.

For agent execution, apply the resolved options to every provider request made by that
execution, including agent tool-call rounds, without changing the global provider client.
Use a per-execution `IChatClient` wrapper or equivalent explicit mechanism so concurrent
agents can use different values safely. The wrapper merges into per-request `ChatOptions`
without removing tools, model id, response format, cancellation, or provider-owned options.

Existing taskless overloads used by general UI features may remain as compatibility
conveniences, but they must resolve the global defaults explicitly. Task execution paths must
not accidentally call a taskless overload and lose the task override.

### 6.2 Workflow AI steps and skills

Immediately before an AI step call:

1. resolve the step override against the task override and current Settings snapshot;
2. retain that immutable policy for every retry attempt governed by the workflow error
   policy;
3. execute the AI call with it;
4. preserve the existing output and error behavior.

`ai.prompt` passes the policy to the task agent together with the current task tool whitelist
and immutable `ToolExecutionContext`.

Built-in skills accept an explicit effective generation policy for direct workflow
execution. If a built-in skill is exposed as a function inside a task agent, it inherits the
containing task/agent policy. No skill may read mutable current-task state.

Retries reuse the values resolved when the step began. A Settings save during a running step
affects the next step or execution, not an already-started call or its retry sequence.

### 6.3 AI Playground and Prompt editor

- AI Playground uses the current global Settings defaults and displays the effective
  temperature, top-p, and declared context window beside provider/model information.
- Prompt editor **Test the prompt** uses the edited task's unsaved task override and displays
  effective values plus inheritance provenance.
- Cancellation and streaming behavior remain unchanged.

### 6.4 Fixed internal operations

Centralize named fixed policies rather than repeating magic numbers at call sites:

```text
WorkflowAuthoring:  Temperature 0, TopP 1
WorkflowExplanation: Temperature 0, TopP 1
GuardrailScreening: Temperature 0, TopP 1
GuardrailReduction: Temperature 0, TopP 1
```

Stage 13 generation/refinement repairs use `WorkflowAuthoring`; explanation repairs use
`WorkflowExplanation`. These policies ignore Settings/task/step temperature and top-p.
They still receive the global `ContextWindowTokens`.

Do not expose these constants in Settings, task JSON, helper UI, or workflow JSON.

### 6.5 Context-window handling

`ContextWindowTokens` is the effective declaration for the call, inherited through step → task → Settings:

- every normal and internal LLM request receives the same current value;
- Ollama provider integration maps it to the supported native context-window request option;
- an OpenAI-compatible model owns its physical context capacity and may not expose a request
  option for changing it; do not send an invented API field;
- when a provider cannot configure the window, retain the value as the application's declared
  budget, show that limitation honestly in Settings, and allow the provider to reject an
  oversized request;
- never truncate, summarize, or drop user/task/tool content silently to fit the configured
  window;
- provider context-length failures remain normal typed execution failures and use the existing
  incident/reporting path.

The UI and documentation must distinguish:

```text
context window = input messages + tool/agent context + generated output
```

from timeout, character limits, and output-token limits.

## 7. User interface

### 7.1 Settings

Add an **AI generation defaults** card within the existing AI provider section:

- numeric `Temperature`;
- numeric `Top P`;
- integer `Context window (tokens)`;
- concise help explaining precision/creativity, nucleus sampling, inheritance, and the
  model-dependent context capacity;
- a note recommending that most users tune either Temperature or Top P, not both;
- visible validation errors and disabled Save while invalid;
- no claim that context-window configuration increases a model's physical capacity.

Use culture-safe editing and invariant persistence. Decimal input must work predictably under
both comma-decimal and dot-decimal Windows cultures.

### 7.2 Task editor

Add a compact **AI generation override** section:

- separate **Use global default** controls for Temperature and Top P;
- numeric fields enabled only when their override is active;
- read-only effective values and provenance;
- a **Context window (tokens)** field with independent global inheritance, blank-as-inherit behavior,
  integer validation, effective-value display, and provenance;
- changes participate in dirty state, Save validation, reload, and existing task editor
  cancellation/navigation behavior.

An override applies to every AI call owned by the task unless a workflow AI step overrides
that property.

### 7.3 Workflow authoring

Extend the existing guided JSON support and Stage 13 AI Workflow Helper:

- the object-form step schema exposes `generationOptions` only for Tool Map AI steps;
- generated descriptions preserve exact overrides and state which properties inherit;
- generation/refinement prompts know integer `generationOptions.contextWindowTokens` and must
  not add provider, model, or credential properties;
- deterministic validation rejects overrides on non-AI steps;
- AI Explain describes configured overrides and deterministic effective provenance, but its
  own explanation call continues to use the fixed internal policy;
- Apply and one-level Undo naturally include step overrides as part of canonical workflow
  JSON;
- no second generation-settings editor or separate page.

## 8. Required tests

Add deterministic tests, without a live Ollama or cloud call, for at least:

1. global Settings defaults load, validate, save, hot-reload, and preserve provider fields;
2. culture-safe Temperature and Top P editing;
3. invalid ranges, non-finite values, invalid JSON types, and unknown option properties;
4. existing task JSON without overrides deserializes and executes unchanged;
5. task override round-trip, empty-object canonical omission, and partial override;
6. AI-step override round-trip for `ai.prompt` and every built-in skill alias;
7. string-form AI steps inherit all values;
8. `generationOptions` on every non-AI Tool Map alias fails validation;
9. context-window overrides round-trip inside `generationOptions`; null, fractional, out-of-range,
   and root/args context-window properties fail validation;
10. field-wise precedence for every combination of step, task, and Settings values;
11. provenance reports `Step`, `Task`, or `Settings` independently per property;
12. prompt-only Runner execution receives task-effective values;
13. Task editor Prompt test receives unsaved task-effective values;
14. `ai.prompt` receives step-effective values while preserving whitelist and execution
    context;
15. direct built-in skill steps receive step-effective values;
16. retries retain the policy resolved when the step began;
17. concurrent tasks with different values cannot observe each other's options;
18. agent multi-round calls retain one immutable policy without losing tools or model id;
19. AI Playground uses global defaults;
20. AI Workflow Helper generation/refinement/repair always uses its fixed policy;
21. AI Explain and explanation repair always use its fixed policy;
22. Guardrails screening/reduction always use their fixed policies;
23. changing user defaults does not affect any fixed internal Temperature or Top P;
24. normal calls receive their step/task/Settings-effective context window; internal calls retain
    the global default;
25. Ollama receives the native context-window option and both generation options;
26. OpenAI-compatible calls receive Temperature and Top P but no invented context API field;
27. provider rejection is surfaced without silent truncation or fallback;
28. Task editor controls, dirty state, validation, effective-value display, and provenance;
29. Stage 13 description/generation/explanation preserve and understand AI-step overrides;
30. no prompts, responses, workflow outputs, or sensitive task data enter logs or incidents.

Use captured stub `IChatClient` requests to assert the final `ChatOptions` for every relevant
call path.

## 9. Documentation

Update during implementation:

- shared specification §17 and the earlier normative Settings/task/workflow sections;
- `appsettings.json` with safe defaults;
- `README.md` with precedence, JSON examples, fixed internal behavior, context semantics, and
  provider limitations;
- both standalone user manuals with the Settings, Task editor, guided workflow, Prompt editor,
  and AI Playground behavior;
- all shipped task samples only where an override demonstrates a real need; do not add
  redundant override objects everywhere;
- `Docs/theme-audit.md` for the new Settings and Task editor controls and validation states;
- `Docs/ARCHITECTURE-LEDGER.md` with contracts, immutable propagation, provider mappings,
  fixed policies, compatibility, exact verification output, GUI status, and residual risks;
- acceptance material with inheritance, override, fixed-policy, concurrency, and
  provider-limitation scenarios.

Document these residual risks honestly:

- Temperature and Top P semantics can vary slightly between providers/models;
- tuning both simultaneously can make behavior harder to predict;
- a context-window setting cannot increase a model's physical capacity;
- OpenAI-compatible endpoints may reject unsupported ranges or oversized requests;
- provider tokenization is authoritative, so a declared budget is not a universal exact token
  counter;
- higher randomness can reduce structured-output and tool-selection reliability.

## 10. Stage gate (all must pass — show command output)

1. `dotnet restore OD.MyLocalAgent.sln -m:1`.
2. `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release` — zero
   warnings and zero errors.
3. `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release` —
   all deterministic tests green; the explicit local Ollama integration probe may remain
   skipped.
4. Parse every JSON file under `Docs/samples`.
5. Validate both standalone manuals as HTML.
6. `git diff --check`.

**Stage-14 acceptance addendum:**

- [ ] Settings persists valid Temperature, Top P, and Context window values and applies them
      to the next call without restart.
- [ ] Existing tasks without overrides inherit global defaults.
- [ ] A task can override either or both generation values independently.
- [ ] An object-form AI step can override either or both values independently.
- [ ] Effective values resolve per property as Step > Task > Settings and show their source.
- [ ] Non-AI steps reject generation overrides; context-window overrides are accepted only
      inside task/AI-step `generationOptions`.
- [ ] Prompt-only tasks, workflow AI steps, built-in skills, Prompt editor tests, and AI
      Playground use the intended policy.
- [ ] AI Workflow Helper, AI Explain, repair calls, and Guardrails retain fixed program-owned
      Temperature and Top P.
- [ ] Each user-directed call receives its effective context window, while internal calls
      inherit Settings; no silent content truncation is introduced.
- [ ] Concurrent tasks cannot leak generation settings.
- [ ] Existing tool, authorization, Safety, Guardrails, privacy, streaming, cancellation, and
      scheduling behavior remains intact.
- [ ] No per-task provider/model, Top K, seed, output-token control, graph, or separate
      Workflows page was introduced.

## 11. Before you finish

1. Append the Stage 14 entry to `Docs/ARCHITECTURE-LEDGER.md`.
2. Record the exact file-tree delta, public contract changes, defaults, fixed policy values,
   provider mappings, and gate results.
3. State whether the WPF application was actually launched and which Settings, Task editor,
   Prompt editor, helper, playground, and invalid-state flows were exercised.
4. Do not claim context-window enforcement or exact token counting beyond what each provider
   actually supports.
5. Do not claim GUI verification unless those flows were actually exercised.
