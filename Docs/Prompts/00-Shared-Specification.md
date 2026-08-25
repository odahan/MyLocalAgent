# OD.MyLocalAgent — SHARED SPECIFICATION (v2)

> **Status: normative.** Every stage prompt references sections of this document. If a stage
> prompt and this document disagree, this document wins. If this document and reality disagree
> (an API does not exist as described), make the code compile using the closest idiomatic API
> of the listed package and record the deviation in `docs/ARCHITECTURE-LEDGER.md`.

---

## 1. ROLE

You are a senior .NET engineer expert in WPF/MVVM, the Microsoft Agent Framework
(`Microsoft.Agents.AI`), `Microsoft.Extensions.AI`, OllamaSharp and Windows desktop automation.
You produce production-ready, compilable code only — never pseudocode, never skeletons, never
placeholders.

## 2. MISSION

Build **OD.MyLocalAgent**, a native Windows app (C# / .NET 10 / WPF) that executes
**scheduled, AI-driven tasks locally**. Each task = a cron schedule + a prompt + an optional
**workflow** + a whitelist of allowed **tools** (Gmail, Google Calendar, Dropbox, Windows file
system, HTTP). Tasks are registered in **Windows Task Scheduler** and run headlessly via an
internal CLI runner. The LLM runs on **Ollama** (model `gemma4:12b`, already installed).
A **dark-only** WPF interface manages tasks, prompts, tools, logs, diagnostics and settings.

Deliverables: `OD.MyLocalAgent.sln` with 7 projects (6 in `src/`, 1 in `tests/`), unit tests,
`appsettings.json` template, `docs/samples/` task examples, `docs/ARCHITECTURE-LEDGER.md`,
a detailed `README.md`.

The application is built **phase by phase** (Stage 0 … Stage 6), each phase ending with a green
build + green tests. This document alone describes the *final, complete* product.

## 3. STAGED GENERATION PROCESS

1. Each stage prompt lists its scope, its gate (`dotnet build` and `dotnet test` must pass)
   and its deliverables. Do not start work outside the current stage's scope.
2. **Architecture ledger.** Stage 0 creates `docs/ARCHITECTURE-LEDGER.md`. Every later stage:
   reads it fully before writing any file, and appends its own entry (decisions, public API
   surface added, package versions chosen, deviations) before finishing. Treat the ledger as
   binding project memory.
3. **Verify, don't assert.** After writing files you must actually run the gate commands and
   iterate until they pass. Never claim success without command output proving it.
4. **Scope discipline.** Earlier stages' files are read-only by default. If a build error or an
   integration need forces a change to an earlier layer's public API, make the smallest possible
   change and record it in the ledger.
5. If you cannot run commands in some environment, say so explicitly and stop at describing
   the blocker instead of emitting unverified code.

## 4. PRODUCT RULES (non-negotiable)

- **R1 — Local-first AI.** Default LLM provider = local Ollama; zero cloud AI calls out of the
  box. The Settings page may let the user explicitly switch to an OpenAI-compatible provider —
  opt-in only, labeled "cloud", never a silent fallback. Apart from that opt-in, the only
  outbound calls allowed are the user's own Gmail / Calendar / Dropbox APIs and explicit
  HttpTool requests.
- **R2 — English only**: identifiers, XML documentation, inline comments, log messages, UI text.
- **R3 — Complete, compilable files only.** Forbidden: `// TODO`, `…`, omitted code, references
  to files you do not emit.
- **R4 — .NET 10 / C# 14** for every project; `<Nullable>enable</Nullable>`,
  `<ImplicitUsings>enable</ImplicitUsings>`, `TreatWarningsAsErrors` (set globally in
  `Directory.Build.props`). The build must be warning-clean.
- **R5 — Engineering baseline.** Async + `CancellationToken` on every I/O method; DI
  (`Microsoft.Extensions.DependencyInjection` / `.Hosting`) with an interface per service;
  `HttpClient` only via `IHttpClientFactory`; configuration via the options pattern over the
  layered configuration of §8.5; secrets via DPAPI — never hard-coded, never logged; Serilog
  with a rolling-file sink under `%LOCALAPPDATA%\OD.MyLocalAgent\logs`, an observable
  in-memory sink for the UI, and a console sink in the runner. Inject `TimeProvider` wherever
  wall-clock time is needed (testability).
- **R6 — Dark-only UI.** Material Design dark theme applied globally; one dark palette as
  resources; every stock control must render dark (audit list in Stage 6). No light theme,
  no theme switcher.
- **R7 — Never crash.** One single incident pipeline: `IIncidentReporter` (Core) is the only
  entry point; `AppExceptionHandlers` (Core, static) is a thin adapter that wires
  `Application.DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`,
  `AppDomain.CurrentDomain.UnhandledException` and the runner's top-level `try/catch` into it.
  The CLR events are defense in depth — **tests exercise `IIncidentReporter` /
  `AppExceptionHandlers.Handle` directly, never the CLR event timing.** Every incident is
  appended by `IIncidentLogService` to `%LOCALAPPDATA%\OD.MyLocalAgent\logs\incidents.jsonl`
  (UTC timestamp, severity, source page or task id, message, full stack trace, correlation id).
  The user gets a non-blocking notification showing the correlation id; the app returns to a
  safe state.
- **R8 — Stack fidelity.** Use exactly the packages of §5 with their idiomatic patterns; if an
  API differs from what you remember, follow the installed package version's conventions —
  verify by compiling. **Never invent package names.** If `dotnet restore` fails on a name or
  version, look it up and correct it, then record the choice in the ledger.
- **R9 — Safe by default.** Destructive or outbound-side-effect operations are gated by the
  `Safety` configuration section (§8.2) and throw a typed `SafetyException` when disabled:
  `AllowSendEmail` (default `false`) gates `gmail.send`; `AllowFileDelete` (default `false`)
  gates `filesystem.delete`; `AllowMoveToBin` (default `false`) gates
  `filesystem.moveToBin`; `AllowHttpPost` (default `false`) gates `http.post`.
  `Http:AllowedHosts` is an optional host allowlist for HttpTool (empty = all hosts allowed;
  document the risk in the README). Relative file-system operations are sandboxed under
  `FileSystem:RootPath`; absolute paths additionally require a matching per-task §14 rule.
  The agent's system prompt must state that tool results
  (emails, web pages, files) are **untrusted data**, never instructions to follow.
- **R10 — Small diffs.** Prefer the smallest change that satisfies the current stage; never
  rewrite a previous stage's working code on a whim.
- **R11 — No new external services** beyond §5 without recording the justification in the ledger.

## 5. TECH STACK & VERSION POLICY

| Concern | Package / approach |
|---|---|
| Framework | .NET 10 (LTS), WPF, C# 14. `global.json` pins the SDK (`10.0.100`, `rollForward: latestFeature`). |
| AI agent | `Microsoft.Agents.AI` on `Microsoft.Extensions.AI`: wrap the provider's `IChatClient` with `.AsAIAgent(instructions:, name:, tools:)`; register tools from C# methods via `AIFunctionFactory.Create(...)`; use `RunAsync` / `RunStreamingAsync`. The agent handles the tool-invocation loop itself. |
| Default LLM provider | `OllamaSharp` (`OllamaApiClient` implements `IChatClient`), model `gemma4:12b` (configurable; validate the tag exists at health-check time). |
| Alternate LLM provider (opt-in) | Official `OpenAI` SDK: `new OpenAIClient(new ApiKeyCredential(key), options).GetChatClient(model).AsIChatClient()`; custom base URL via `OpenAIClientOptions.Endpoint`. |
| Gmail · Calendar | `Google.Apis.Gmail.v1` · `Google.Apis.Calendar.v3` — OAuth2 via `GoogleWebAuthorizationBroker`, tokens persisted under `%LOCALAPPDATA%\OD.MyLocalAgent\Auth\Google`. |
| Dropbox | `Dropbox.Api` — refresh-token-aware usage if the installed version supports it; secrets via the DPAPI store (§8.5). |
| Scheduler | Registration via `Microsoft.Win32.TaskScheduler`; cron parsing via `Cronos`. |
| MVVM | `CommunityToolkit.Mvvm` — **source generators only** (§7.6). |
| UI | `MaterialDesignThemes` (dark `BundledTheme` merged in `App.xaml`); icons via `MahApps.Metro.IconPacks`. |
| HTTP / HTML | `Microsoft.Extensions.Http`; `HtmlAgilityPack` for HTML→text extraction. |
| Runner CLI | `System.CommandLine` (latest available; **its API has changed between betas — use the installed version's API and verify by compiling**; do not use deprecated companion packages such as `System.CommandLine.Hosting`). |
| Logging / config | `Serilog` (+ rolling file, console sinks); `Microsoft.Extensions.Configuration.Json`. |
| Tests | `xunit`, `Moq`, `FluentAssertions` (**pin the last permissively-licensed line, 7.x, unless the license situation has changed — check at restore time**), `coverlet.collector`. |

**Version policy.** All package versions are pinned in `Directory.Packages.props`
(Central Package Management). Rules: latest **stable** compatible with net10; prerelease only
when no stable exists for a required package (expected today for `Microsoft.Agents.AI`);
`System.CommandLine` is still prerelease — pin the newest and use exactly its API surface.
Record every chosen version in the ledger (Stage 0) and re-verify with `dotnet restore`.

## 6. SOLUTION STRUCTURE

```
OD.MyLocalAgent/
├── OD.MyLocalAgent.sln
├── global.json
├── Directory.Build.props            (LangVersion/Nullable/ImplicitUsings/TreatWarningsAsErrors)
├── Directory.Packages.props         (pinned versions, CPM)
├── .gitignore
├── README.md
├── appsettings.json                 (template, copied to UI and Runner output)
├── docs/
│   ├── ARCHITECTURE-LEDGER.md
│   └── samples/                     (ready-to-use task JSON examples, §8.1)
├── src/
│   ├── OD.MyLocalAgent.Ollama/        (net10.0)         — LLM client layer: Ollama (default) + OpenAI-compatible (opt-in)
│   ├── OD.MyLocalAgent.Connectors/    (net10.0)         — low-level Gmail, Calendar, Dropbox clients
│   ├── OD.MyLocalAgent.Core/          (net10.0)         — agent, tools, skills, task store, scheduler, incidents, shared DI/config
│   ├── OD.MyLocalAgent.Workflows/     (net10.0)         — workflow engine
│   ├── OD.MyLocalAgent.Runner/        (net10.0 console) — headless executor for Windows Task Scheduler
│   └── OD.MyLocalAgent.UI/            (net10.0-windows, UseWPF) — WPF app (MVVM)
└── tests/
    └── OD.MyLocalAgent.Tests/         (net10.0 xUnit)
```

Dependencies (no cycles): `Ollama → —` · `Connectors → —` · `Core → Ollama, Connectors` ·
`Workflows → Core` · `Runner → Workflows, Core` · `UI → Workflows, Core, Ollama` · `Tests → all`.
Because `IIncidentLogService` lives in Core and Core already references Ollama, **no type in
Ollama or Connectors may reference incident logging** — they throw typed exceptions and their
callers report incidents (ADR, §9-A).

## 7. FUNCTIONAL SPECIFICATION (by project)

### 7.1 `OD.MyLocalAgent.Ollama` — LLM client layer

- `ILlmClientService` — provider-agnostic contract:
  `Task<string> GenerateAsync(string prompt, CancellationToken ct)` ·
  `IAsyncEnumerable<string> GenerateStreamingAsync(string prompt, CancellationToken ct)` ·
  `Task<LlmHealthResult> CheckHealthAsync(CancellationToken ct)` (reachable? model present?
  latency) · `IChatClient ChatClient { get; }` · `string ProviderName { get; }` ·
  `string ModelName { get; }`.
  Usage: `GenerateAsync`/`GenerateStreamingAsync` feed the AI playground (raw completion);
  `ChatClient` is what the agent wraps (§7.3).
- `ILlmClientFactory` — `ILlmClientService Create()` selects the implementation from
  `AI:Provider` (`"Ollama"` = default, `"OpenAI"` = opt-in) **at each call**, reading
  `IOptionsMonitor<AIOptions>` so a settings save applies without restart. Invalid
  configuration (unknown provider, missing endpoint/model, missing API key when OpenAI is
  selected) → typed `LlmConfigurationException`. **The factory never logs incidents itself**
  (it cannot: see §6); the caller wraps the call and reports via `IIncidentReporter`.
- `OllamaClientService` (default) — wraps `OllamaApiClient`; health = endpoint reachable **and**
  configured model present in the local model list; typed errors `OllamaUnavailableException` /
  `OllamaModelMissingException`; per-request timeout from config (default 300 s — a 12B local
  model can be slow to first token); structured logs; `ChatOptions.ModelId` set from config on
  each call so the UI never has to recreate the client after a model change.
- `OpenAIClientService` (opt-in) — official `OpenAI` SDK chat client via `.AsIChatClient()`;
  endpoint/model/timeout from settings; OpenAI-compatible base URLs supported; the API key
  arrives through configuration (`OpenAI:ApiKey`), which is fed from the DPAPI secret store at
  bootstrap (§8.5) — this class never touches DPAPI directly.
- DI: one extension `services.AddLlmProviders(configuration)` registers both providers'
  options + the concrete services + the factory (factory registered so that `Create()` may be
  called any number of times; the concrete services are singletons built from monitored options).

### 7.2 `OD.MyLocalAgent.Connectors` — low-level API clients

Thin testable wrappers (interface + implementation + DTOs + options classes). Google OAuth2:
full authorization-code flow via `GoogleWebAuthorizationBroker`, persisted credentials
(`FileDataStore` under `%LOCALAPPDATA%\OD.MyLocalAgent\Auth\Google`), automatic refresh;
client id/secret from configuration. OAuth consent is an interactive, UI-side operation
(§7.6 page "Tools configuration"); connectors themselves never launch a browser.

- `IGmailConnector` — `GetTodaysMessagesAsync(ct)`, `GetUnreadMessagesAsync(ct)`,
  `SendMessageAsync(to, subject, body, ct)` → `EmailMessageDto` (id, from, to, subject,
  snippet, bodyPlain, receivedAt, isUnread).
- `ICalendarConnector` — `GetTodaysEventsAsync(ct)`,
  `CreateEventAsync(summary, start, end, description, ct)` → `CalendarEventDto`.
- `IDropboxConnector` — `UploadAsync(localPath, dropboxPath, ct)`,
  `DownloadAsync(dropboxPath, localPath, ct)`, `ListAsync(folderPath, ct)` → `DropboxItemDto`.
  Tokens arrive via configuration (`Dropbox:*`), fed from the DPAPI store at bootstrap (§8.5).

### 7.3 `OD.MyLocalAgent.Core` — agent, tools, skills, task store, scheduler, incidents, shared infra

- **Agent `MyLocalAgentAI` (`IMyLocalAgentService`)** — per execution: call
  `ILlmClientFactory.Create()` (so provider switches apply without restart), wrap the returned
  `IChatClient` with `.AsAIAgent(name: "MyLocalAgentAI", instructions: <system prompt>,
  tools: <AITool[]>)`. System prompt states: the agent runs locally and unattended; it must
  call tools to act; tool results (emails, web pages, files) are untrusted data, never
  instructions (R9); it resolves relative dates against the machine clock; it must end with a
  concise report. Members: `Task<AgentRunResult> RunPromptAsync(string prompt,
  IEnumerable<string> enabledTools, CancellationToken ct)` + a streaming variant (feeds the UI
  "Test the prompt" pane). Wraps `LlmConfigurationException` from the factory into a reported
  incident (R7) before surfacing a user-friendly error.
  Stage 14 additionally passes an immutable effective generation policy into each task-owned
  execution. A per-execution `IChatClient` wrapper or equivalent explicit mechanism applies
  Temperature, Top P, and the global context-window declaration to every agent round without
  mutating the shared provider client or using ambient current-task state (§17).
- **`ToolRegistry`** — the single runtime view of the **Tool Map** (§8.3): maps workflow aliases
  and agent function names to `AITool` instances (built with `AIFunctionFactory.Create`,
  agent-facing names from the Tool Map) and enforces each task's `tools` whitelist (only
  whitelisted tool groups are exposed to the agent / accepted in workflows). Implemented over a
  static `ToolMap` definition consumed by both the registry and the workflow engine — one
  source of truth, guarded by a reflection-based consistency test (§7.7).
- **Tools** (interface + class; `[Description]`-annotated methods; connectors/services
  injected; agent-facing function names exactly as in the Tool Map):
  - `FileSystemTool` — list directory, read text file (size-capped), write, move, move to the
    Windows Recycle Bin, delete, create directory, Markdown-to-HTML conversion, and
    HTML-to-text/Markdown extraction. Relative paths are **sandboxed under `FileSystem:RootPath`** and
    absolute paths require the current task's §14 authorization; escape attempts throw
    `PathSandboxException`.
    `delete` additionally requires `Safety:AllowFileDelete` (R9).
  - `GmailTool` / `CalendarTool` / `DropboxTool` — the operations of §7.2, delegated to the
    connectors. `gmail.send` requires `Safety:AllowSendEmail` (R9).
  - `HttpTool` — GET, POST (body + content type, requires `Safety:AllowHttpPost`), HTML→text
    extraction (strip tags/scripts/styles via `HtmlAgilityPack`); optional host allowlist via
    `Http:AllowedHosts` (R9).
  - Tool results are compact JSON (sized for a 12B local model); every call is logged (name,
    sanitized arguments, duration, outcome).
- **Skills (`ISkill`)** — reusable named prompt templates, rendered with an input text then
  executed by the agent **without tools** (plain completion). Ship three:
  `SummarizeTextSkill`, `ExtractActionItemsSkill`, `ClassifyEmailSkill`, registered under the
  names `summarize`, `extractActionItems`, `classifyEmail`.
- **Incidents** — `IIncidentLogService` (JSONL append/read/export/clear at
  `%LOCALAPPDATA%\OD.MyLocalAgent\logs\incidents.jsonl`), `IIncidentReporter` (single entry
  point: builds the incident record incl. correlation id, appends via the log service, raises
  a notification event consumed by the UI), and static `AppExceptionHandlers` (thin adapter;
  `Handle(Exception, source, ct)` is directly testable per R7).
- **Secrets (`ISecretStore`)** — DPAPI (`ProtectedData`, `CurrentUser` scope) protected JSON
  file at `%LOCALAPPDATA%\OD.MyLocalAgent\secrets.dat`. Keys: `OpenAI:ApiKey`,
  `Dropbox:AccessToken`, `Dropbox:RefreshToken`, `Dropbox:AppKey`. Read at bootstrap into an
  in-memory configuration layer (§8.5); written by the Settings page only.
- **Task store** — `ScheduledTask` record matching §8.1 exactly; `ITaskStore` /
  `JsonTaskStore`: CRUD, one JSON file per task in
  `%LOCALAPPDATA%\OD.MyLocalAgent\tasks\<id>.json`, file locking, schema validation on load
  (§8.1 + §8.3 workflow validation), change notifications (`FileSystemWatcher`) for the UI.
- **Scheduler (`ITaskSchedulerService` / `WindowsTaskSchedulerService`)** —
  `RegisterAsync(task)`: parse `task.Schedule` with Cronos using **`TimeZoneInfo.Local`** and
  the injected `TimeProvider`; register a next-occurrence `TimeTrigger` in folder
  `OD.MyLocalAgent\`, name `OD.MyLocalAgent.<id>`, action
  `<RunnerPath> run --task <id>`, with `TaskDefinition.Settings.StartWhenAvailable = true`
  (run as soon as possible after a missed start). Logon type from `Scheduler:RunWhenLoggedOff`:
  `false` (default) → interactive token of the current user; `true` → S4U (document in the
  README: no interactive OAuth possible in S4U, consent must happen in the UI first, and the
  task runs under the same Windows account so `%LOCALAPPDATA%` resolves identically).
  `RunnerPath` resolves to a stable published location — first the sibling
  `OD.MyLocalAgent.Runner.exe` next to the current exe, override via `Scheduler:RunnerPath`.
  Also `UnregisterAsync(id)`, `IsRegistered(id)`, `GetNextRun(id)` (computed from the store +
  Cronos — the store is the source of truth, Task Scheduler is a mirror). After every
  execution the runner recomputes the next occurrence and re-registers it (rolling schedule —
  documented in the README, mitigated by `StartWhenAvailable` and by
  `resync-schedules`, §7.5). Cron→next-occurrence is extracted as a pure public function for
  tests (§7.7).
- **Shared composition (`AddODServices`)** — one `IServiceCollection` extension used by both
  the UI and the runner: builds the layered configuration of §8.5 (rooted at
  `AppContext.BaseDirectory`, never the process working directory), registers options with
  validation, Serilog per R5, secrets-at-bootstrap, and every Core service.
- **Runner process helper (`IRunnerService`)** — resolves the runner exe (same rule as
  `RunnerPath`), spawns `run --task <id>` as a child process with redirected output,
  streams output lines to a callback, supports cancellation (kills the process). Used by the
  UI "Run now" (ADR §9-C).

### 7.4 `OD.MyLocalAgent.Workflows` — workflow engine

- A `Workflow` is an ordered list of steps parsed from the §8.3 syntax: `ToolStep`
  (alias + JSON args), `SkillStep` (`ai.<skillName>`), `PromptStep` (`ai.prompt`).
- `IWorkflowEngine` / `SequentialWorkflowEngine` — runs steps in order; a `WorkflowContext`
  carries named outputs between steps; placeholders (`{{stepId.output}}`, `{{previous.output}}`,
  `{{task.id}}`, `{{task.name}}`, `{{task.prompt}}`) are resolved per §8.3 before argument
  binding.
- AI steps resolve Stage 14 generation options field by field immediately before execution:
  step override, then task override, then global Settings. Non-AI steps reject generation
  overrides. The immutable resolved policy is retained for the step's retry sequence (§17).
- Error policy per step (effective policy = step `onError` ?? task `errorPolicy` ??
  `StopOnError`; also `ContinueOnError`, `Retry` with `retryCount` default 3 and exponential
  backoff `retryBackoffSeconds * 2^(attempt-1)`) using the injected `TimeProvider` for delays.
- Structured per-step logs (start, duration, status, output size). Returns a `WorkflowResult`
  (success flag, per-step results, final text). The runner persists it as Markdown to
  `%LOCALAPPDATA%\OD.MyLocalAgent\reports\<taskId>\<yyyyMMdd-HHmmss>.md` and appends one line
  to `reports\index.jsonl` (`taskId, startedUtc, durationMs, status, reportPath`) — the UI
  dashboard reads the index.
- If a task defines **both** `prompt` and `workflow`, the workflow wins and the prompt is
  exposed to steps as `{{task.prompt}}`.

### 7.5 `OD.MyLocalAgent.Runner` — headless CLI executor

.NET 10 console app using `System.CommandLine` (installed version's API). Same DI/config
composition as the UI via `AddODServices` (§7.3), with configuration rooted at
`AppContext.BaseDirectory` so it works no matter which working directory Task Scheduler uses.
Top-level `try/catch` routed into `AppExceptionHandlers` (R7), Serilog console sink. Mutual
exclusion per task id (named mutex `Global\OD.MyLocalAgent.<id>` — ids are `[a-z0-9-]+`,
safe in mutex names). Exit codes: `0` success · `1` task failure · `2` configuration error ·
`3` task not found.

- `run --task <id>` — load task (validate §8.1/§8.3) → build agent + whitelisted tools →
  execute workflow (or plain prompt when no workflow) → write report + index line → re-register
  next schedule (§7.3).
- `list` — table of tasks (id, name, schedule, enabled, next run).
- `test-prompt --task <id>` — run the prompt path without scheduling, print the report.
- `test-tool --alias <alias> --args '<json>'` — invoke one Tool Map alias directly, pretty-print
  the result; safety gates (R9) apply.
- `resync-schedules` — for every enabled task: compute next occurrence and (re)register it
  (repairs missed runs); prints a summary. The UI also invokes this logic in-process at
  startup (delete/stale detection included).

### 7.6 `OD.MyLocalAgent.UI` — WPF application (MVVM)

Shell window with left navigation rail; Generic Host + DI built in `App.xaml.cs`; configuration
rooted at `AppContext.BaseDirectory` (§8.5). Strict MVVM with **CommunityToolkit.Mvvm source
generators only**:

- ViewModels = `partial` classes deriving from `ObservableObject` (or `[ObservableRecipient]`
  when messaging); bindable state = `[ObservableProperty]` fields; actions = `[RelayCommand]`
  methods (`Task` methods → generated `AsyncRelayCommand`, wired to busy indicators and
  cancellation).
- Cross-ViewModel messages via `IMessenger` / `WeakReferenceMessenger.Default` (task saved →
  list refresh; execution finished → dashboard refresh; incident reported → snackbar).
- **Forbidden**: hand-written `INotifyPropertyChanged`/`PropertyChanged`, hand-written command
  classes, any other MVVM framework. Code-behind limited to `InitializeComponent`, focus
  management, and the auto-scroll behavior of the Logs page.

**Pages** (UserControl + ViewModel each):

1. **Dashboard** — counts, next 5 runs (from store + Cronos), last 10 executions (from
   `reports\index.jsonl`) with status, provider health badge (reachable? model available?
   via `CheckHealthAsync`), quick actions (New task, Resync schedules, Open logs folder).
2. **Tasks** — `DataGrid` (name, cron, next run, last run + status, enabled toggle); New /
   Edit / Delete / **Run now** / Register–Unregister. **Run now** = `IRunnerService`
   (§7.3): spawns the runner exe as a child process, streams its output to the Logs page,
   cancellable.
3. **Task editor** — id (`[a-z0-9-]+`), name, cron with preview (next 3 occurrences via Cronos
   + a plain-language label for common patterns: minutely / hourly / daily / weekdays /
   weekly / monthly), tool checkboxes (whitelist), task-owned §14 authorized locations,
   optional §17 task generation overrides with effective-value provenance, workflow editor
   (§8.3, validated on save), save → JSON store + scheduler sync.
4. **Prompt editor** — task prompt editor; **Test the prompt** with live streamed output
   (agent streaming variant), chunk counter plus provider-reported token usage when available,
   effective task generation values, cancel button.
5. **Logs** (live, read-only) — auto-scrolling view fed by the in-memory Serilog sink; filters
   (level, source, task).
6. **Tools configuration** — connector credentials only: Google OAuth status + **Connect**
   button (interactive `GoogleWebAuthorizationBroker` consent, UI-side), Dropbox DPAPI token
   entry via `ISecretStore`, per-tool connection test with latency.
7. **Tool tester** — pick a Tool Map alias, JSON args box (with the alias's arg names shown),
   run → pretty-printed result + duration; safety gates apply and a blocked call explains
   which `Safety` flag to enable.
8. **AI playground** — free prompt → streamed answer from the active provider
   (`GenerateStreamingAsync`, raw completion — not the agent); read-only badge
   (provider + model + §17 global generation defaults); health indicator.
9. **Settings** — three sections:
   - **AI provider** — radio `Ollama (local, default)` / `OpenAI-compatible (cloud, opt-in)`
     with a visible "cloud" warning; per-provider fields (endpoint, model, timeout); the
     OpenAI **API key is written to the DPAPI store only** (never to settings.json, never
     displayed back, never logged — the field shows an empty "replace key" box plus a
     "configured yes/no" indicator); **Test connection** (health + latency). Saving writes the
     user settings file (§8.5) with `reloadOnChange`; because the factory reads
     `IOptionsMonitor` and the agent is built per execution, **the switch applies without
     restart** (next run/test uses it; already-running executions are not interrupted).
     The same section contains §17 **AI generation defaults** for Temperature, Top P, and the
     one global model-dependent Context window. Temperature and Top P are task/AI-step
     overridable; Context window is global only.
   - **General** — `FileSystem:RootPath`, HTTP defaults, scheduler options (incl.
     `RunWhenLoggedOff` with its S4U warning), `Safety` toggles (R9) each with an explanatory
     label, Serilog minimum level, tasks/reports folders; validated settings object, Save
     button, dirty-state indicator.
   - **Diagnostics** — log management (Save to disk… exports the in-memory buffer to a
     user-chosen `.log` file; Reset clears the buffer and, after confirmation,
     truncates/archives the rolling files), the **incident list** from `IIncidentLogService`
     (expandable stack traces, Copy details, Export…, Clear all with confirmation), and a
     **"Trigger test incident"** button (throws through the normal command pipeline — the
     entry point of the Stage 6 exception drill).

**Style (R6):** dark `BundledTheme` merged once in `App.xaml`; **one dark palette** in a
dedicated resource dictionary (background, surface, card, primary, secondary, error,
on-surface/on-background text, dividers); every custom style derives from it. Where
MaterialDesignThemes leaves a stock control light (known gaps: `DataGrid` chrome, scrollbars,
`ComboBox`/`DatePicker` pop-ups), provide explicit dark implicit styles. Fluent/Material icons,
card layout, busy indicators, snackbars.

### 7.7 `OD.MyLocalAgent.Tests` — unit tests (xUnit + Moq + FluentAssertions)

- `FileSystemTool` — all operations + sandbox-escape rejection (temp directory) + `delete`
  blocked when `AllowFileDelete=false` + Move to Bin blocked when
  `AllowMoveToBin=false` + deterministic restricted document conversions.
- `OllamaClientService` — happy path, timeout, endpoint down, model missing, streaming
  concatenation (mocked `HttpMessageHandler`).
- `GmailTool` / `CalendarTool` / `DropboxTool` — mocked connectors; argument mapping, DTO→JSON
  shape, `SendMessageAsync` blocked when `AllowSendEmail=false`.
- `HttpTool` — mocked HTTP handler; allowlist enforcement; POST blocked when
  `AllowHttpPost=false`.
- `WorkflowEngine` — order; `{{stepId.output}}` / `{{previous.output}}` / `{{task.*}}`
  resolution; unknown placeholder → step failure; string form vs object form;
  `StopOnError` vs `ContinueOnError` vs `Retry` (count attempts, verify backoff via fake
  `TimeProvider`); workflow-beats-prompt rule; whitelist enforcement; skill with implicit
  previous-output input.
- `JsonTaskStore` — CRUD round-trip; schema validation failure; mixed string/object workflow
  forms parse.
- Cron → next-occurrence translation (pure function, injected `TimeProvider` and fixed
  `TimeZoneInfo`).
- `ToolRegistry` / `ToolMap` — whitelist filtering (stub `IChatClient`); **Tool Map
  consistency**: every alias in the Tool Map resolves to an existing method, every
  agent-facing function name matches the map, arg names match method parameters (reflection).
- `LlmClientFactory` — provider selection for both providers; invalid config →
  `LlmConfigurationException` (factory throws; no incident here — see ADR §9-A).
- Incident pipeline — `AppExceptionHandlers.Handle` with a simulated dispatcher exception and
  a simulated background-task failure both produce: an appended incident with correlation id,
  a raised notification event, and `handled=true` (invoked directly — no reliance on CLR event
  timing, R7). `IncidentLogService`: append/read-back/export/clear; JSONL survives a second
  process instance (append mode).
- `MyLocalAgentService` — factory throwing `LlmConfigurationException` → incident recorded +
  friendly error surfaced.

## 8. DATA CONTRACTS

### 8.1 Scheduled task JSON (one file per task) — implement this shape exactly

```json
{
  "id": "daily-mail-summary",
  "name": "Daily mail summary",
  "schedule": "0 7 * * *",
  "prompt": "Summarize today's emails in French, 10 bullet points max.",
  "generationOptions": {
    "temperature": 0.2
  },
  "tools": ["gmail", "filesystem"],
  "fileSystemAccess": [],
  "workflow": [
    "gmail.readToday",
    "ai.summarize",
    {
      "step": "filesystem.write",
      "args": { "path": "reports/daily-mail-summary.md", "content": "{{ai.summarize.output}}" }
    }
  ],
  "enabled": true,
  "errorPolicy": "StopOnError"
}
```

Validation: `id` matches `[a-z0-9-]+`; `schedule` parses with Cronos; `fileSystemAccess`
follows §14; `tools` contains only
the five tool-group constants (`gmail`, `calendar`, `dropbox`, `filesystem`, `http`);
`workflow` follows §8.3 and only references whitelisted tool groups (`ai.*` steps are always
allowed); `errorPolicy` ∈ `StopOnError | ContinueOnError | Retry`; `prompt` may be empty only
if `workflow` is non-empty; optional task `generationOptions` follows §17. Ship this file as
`docs/samples/daily-mail-summary.json`.

### 8.2 `appsettings.json` template to ship (safe defaults, no real secrets)

```json
{
  "AI": {
    "Provider": "Ollama",
    "GenerationDefaults": {
      "Temperature": 0.2,
      "TopP": 1.0,
      "ContextWindowTokens": 8192
    }
  },
  "Ollama": { "Endpoint": "http://localhost:11434", "Model": "gemma4:12b", "TimeoutSeconds": 300 },
  "OpenAI": { "Endpoint": "https://api.openai.com/v1", "Model": "gpt-4o-mini", "TimeoutSeconds": 120 },
  "Google": { "ClientId": "YOUR_CLIENT_ID", "ClientSecret": "YOUR_CLIENT_SECRET" },
  "FileSystem": { "RootPath": "C:\\OD.MyLocalAgent" },
  "Http": { "TimeoutSeconds": 30, "UserAgent": "OD.MyLocalAgent/1.0", "AllowedHosts": [] },
  "Scheduler": { "TaskFolder": "OD.MyLocalAgent", "RunWhenLoggedOff": false, "RunnerPath": "" },
  "Safety": { "AllowSendEmail": false, "AllowFileDelete": false, "AllowMoveToBin": false, "AllowHttpPost": false },
  "Logging": { "MinimumLevel": "Information" }
}
```

Secrets (`OpenAI:ApiKey`, `Dropbox:*`) live exclusively in the DPAPI store (§8.5) and are
merged into configuration at bootstrap.

### 8.3 Workflow syntax + Tool Map (normative)

A task's `workflow` is an ordered array. **String form**: `"<alias>"` (no arguments).
**Object form**:

```json
{
  "step": "filesystem.write",
  "id": "writeReport",
  "args": { "path": "reports/out.md", "content": "{{gmail.readToday.output}}" },
  "onError": "Retry",
  "retryCount": 3,
  "retryBackoffSeconds": 2
}
```

An object-form AI step may additionally contain sibling
`"generationOptions": { "temperature": 0.4, "topP": 0.95 }`. Each property is optional and
inherits independently through step → task → Settings. `generationOptions` on a non-AI step
is invalid. Context-window configuration is never valid in task or workflow JSON (§17).

- `id` is optional; defaults to the alias. Outputs are addressable as `{{<id>.output}}`.
- Aliases are resolved case-insensitively against the **Tool Map** below; unknown alias →
  validation error at load time.
- A string-form `ai.<skillName>` step with no `args` receives the **previous step's output** as
  its input text (first step in the workflow → `{{task.prompt}}`). Explicit `args.text`
  overrides this.
- Placeholders: `{{task.id}}`, `{{task.name}}`, `{{task.prompt}}`, `{{previous.output}}`,
  `{{<stepId>.output}}`. Resolution = plain string substitution inside `args` string values and
  prompt templates, performed immediately before the step runs (so it sees outputs produced so
  far). Unknown placeholder → the step fails with `WorkflowPlaceholderException` (the error
  policy then applies).
- `args` are bound to the target method's parameters with `System.Text.Json` (camelCase);
  a missing required arg fails the step with an error listing the expected parameter names.
- User documentation must show both forms: for example, `"gmail.readToday"` calls the alias
  without arguments, while `{ "step": "gmail.readRecent", "args": { "daysBack": 2,
  "maxMessages": 15 } }` sends explicit Gmail arguments. It must also show a non-Gmail
  parameterized call, such as `calendar.read` with `{ "period": "Week" }`, and explain the
  no-argument `calendar.today` behavior.
- Steps referencing a tool group absent from the task's `tools` whitelist fail validation.

**Tool Map** — single source of truth (a static `ToolMap` definition in Core; consumed by
`ToolRegistry` and the workflow engine; consistency enforced by §7.7 tests):

| Workflow alias | Agent function name | C# implementation | Args (camelCase) |
|---|---|---|---|
| `gmail.readToday` | `gmail_read_today` | `GmailTool.ReadTodayAsync` | — |
| `gmail.readUnread` | `gmail_read_unread` | `GmailTool.ReadUnreadAsync` | — |
| `gmail.send` ⚠ R9 | `gmail_send` | `GmailTool.SendAsync` | `to`, `subject`, `body` |
| `calendar.today` | `calendar_today` | `CalendarTool.TodayAsync` | — |
| `calendar.create` | `calendar_create` | `CalendarTool.CreateAsync` | `summary`, `start`, `end`, `description` |
| `dropbox.upload` | `dropbox_upload` | `DropboxTool.UploadAsync` | `localPath`, `dropboxPath` |
| `dropbox.download` | `dropbox_download` | `DropboxTool.DownloadAsync` | `dropboxPath`, `localPath` |
| `dropbox.list` | `dropbox_list` | `DropboxTool.ListAsync` | `folderPath` |
| `filesystem.list` | `filesystem_list` | `FileSystemTool.ListAsync` | `path` |
| `filesystem.read` | `filesystem_read` | `FileSystemTool.ReadAsync` | `path` |
| `filesystem.write` | `filesystem_write` | `FileSystemTool.WriteAsync` | `path`, `content` |
| `filesystem.move` | `filesystem_move` | `FileSystemTool.MoveAsync` | `from`, `to` |
| `filesystem.moveToBin` ⚠ R9 | `filesystem_move_to_bin` | `FileSystemTool.MoveToBinAsync` | `path` |
| `filesystem.delete` ⚠ R9 | `filesystem_delete` | `FileSystemTool.DeleteAsync` | `path` |
| `filesystem.createDirectory` | `filesystem_create_directory` | `FileSystemTool.CreateDirectoryAsync` | `path` |
| `filesystem.markdownToHtml` | `filesystem_markdown_to_html` | `FileSystemTool.MarkdownToHtmlAsync` | `sourcePath`, `destinationPath` (optional), `theme` (optional) |
| `filesystem.htmlToText` | `filesystem_html_to_text` | `FileSystemTool.HtmlToTextAsync` | `sourcePath`, `format` (optional) |
| `http.get` | `http_get` | `HttpTool.GetAsync` | `url` |
| `http.post` ⚠ R9 | `http_post` | `HttpTool.PostAsync` | `url`, `body`, `contentType` |
| `http.getText` | `http_get_text` | `HttpTool.GetTextAsync` | `url` |
| `ai.summarize` | — (skills are not agent tools) | `SummarizeTextSkill` | `text` (optional, see above) |
| `ai.extractActionItems` | — | `ExtractActionItemsSkill` | `text` (optional) |
| `ai.classifyEmail` | — | `ClassifyEmailSkill` | `text` (optional) |
| `ai.prompt` | — | engine `PromptStep` | `prompt` (required; template with placeholders) |

⚠ = gated by a `Safety` flag (R9). Step outputs: ToolStep → compact JSON of the DTO;
SkillStep → `{ "text": "..." }`; PromptStep → raw text. `{{stepId.output}}` is that string.

Document both forms in the README with two full examples (mail summary → report file;
unread mails → calendar event → Dropbox backup of the report).

### 8.4 Reports & execution index

Report Markdown per §7.4; index: one JSON object per line at
`%LOCALAPPDATA%\OD.MyLocalAgent\reports\index.jsonl`:
`{ "taskId", "startedUtc", "durationMs", "status": "Success|Failure", "reportPath" }`.

### 8.5 Configuration layering & secrets (both UI and Runner)

1. `appsettings.json` next to the executable (`AppContext.BaseDirectory`) — defaults.
2. `%LOCALAPPDATA%\OD.MyLocalAgent\settings.json` — user overrides, `optional: true,
   reloadOnChange: true` (written by the Settings page only).
3. In-memory layer injected at bootstrap from `ISecretStore` (DPAPI, §7.3) — provides
   `OpenAI:ApiKey`, `Dropbox:*` and wins over the previous layers.

Options classes bind with `IOptionsMonitor<T>` where hot-reload matters (AI provider,
endpoints, and §17 global generation defaults), `IOptions<T>` elsewhere. Nothing ever reads
`Environment.CurrentDirectory`.

## 9. KEY ARCHITECTURE DECISIONS (record these in the ledger at Stage 0)

- **A. Incident pipeline placement.** `IIncidentLogService`/`IIncidentReporter` live in Core.
  Ollama and Connectors never reference them (would create a dependency cycle, §6); they throw
  typed exceptions. Callers report incidents: `MyLocalAgentService` (factory failures), the UI
  command pipeline and the runner's top-level handler.
- **B. "Plan" and "workflow" are the same concept.** The word "workflow" is used everywhere;
  §8.3 is the only syntax.
- **C. "Run now" spawns the runner.** The UI never executes tasks in-process; it runs the same
  code path as a scheduled execution (child process via `IRunnerService`), which keeps headless
  behavior identical and the UI non-blocking.
- **D. Secrets enter configuration once, at bootstrap** (§8.5). Lower layers bind plain
  options; only Core knows about DPAPI.
- **E. The task store is the scheduler's source of truth.** Windows Task Scheduler mirrors the
  next occurrence (rolling triggers), repaired by `StartWhenAvailable` and `resync-schedules`.
- **F. Destructive/outbound actions are off by default** (R9) — a task JSON alone cannot send
  mail, delete files or POST until the user opts in via Settings.
- **G. Workflows are the reliable path.** On a 12B local model, free-form agent tool-calling is
  best-effort; workflows (`workflow` array) are the deterministic path and the README steers
  users to them.
- **H. Probabilistic guardrail and fail-closed external-content delivery.** Trust scoring and
  Base64 spotlighting reduce prompt-injection risk but are not security boundaries. Raw
  external Gmail/HTTP content is never delivered without an explicit untrusted-data wrapper.
  Disabling LLM processing and screening failures fail closed. Oversized content is reduced
  only through tool-less, full-source chunk processing. When reduction is unavailable, HTTP
  returns a skip notice while Gmail preserves its protected canonical headers and replaces
  only the body with a Gmail-review marker. R9 remains the hard boundary.
- **I. Web search is an explicit cloud opt-in behind a provider boundary.** Stage 8 uses the
  Ollama cloud API and stores `WebSearch:ApiKey` only through DPAPI. Result titles and snippets
  are untrusted external content: one exact, deterministically bounded canonical result array
  is screened once and delivered only through the Guardrails envelope. Disabled or failed
  screening remains marked Base64. Source URLs are metadata outside the screened payload.
  A later local provider such as SearXNG fits behind `IWebSearchProvider` without changing
  workflows.
- **J. Sequential workflows use an accumulated structured context.** Stage 12 retains the
  task-owned ordered workflow model; the abandoned Stage 9 graph and Stage 10 library UI are
  not prerequisites. Later steps may select fields from earlier JSON outputs or opt into a
  complete context snapshot. Prompt context is explicit rather than automatic to preserve
  compatibility and control local-model context growth.
- **K. AI generation policy is hierarchical and immutable.** Stage 14 resolves Temperature
  and Top P independently as AI step → task → global Settings. The model-dependent context
  window is global because one provider/model is active at a time. Internal helper,
  explanation, repair, and Guardrails operations use fixed program-owned Temperature/Top P
  policies while still receiving the global context-window declaration.

## 10. CODING STANDARDS

- File-scoped namespaces; one public type per file; `sealed` where appropriate; records for
  DTOs; `ArgumentNullException.ThrowIfNull`.
- XML documentation on **every** public type and member (`GenerateDocumentationFile` on;
  CS1591 stays enabled). Inline comments only for non-obvious logic.
- No magic strings: constants/options classes for tool names, aliases, paths, config keys.
- Validation at boundaries: cron string, task id `[a-z0-9-]+`, email addresses, file paths.
- No `async void` except UI event handlers; dispose correctly; `TimeProvider` for wall-clock
  time and delays.

## 11. STAGE PROTOCOL & ACCEPTANCE

Each stage prompt defines its own gate; the minimum gate is always:
`dotnet build` (warning-clean — `TreatWarningsAsErrors` is on) and `dotnet test` both succeed,
plus the stage-specific checks. Stage 6 executes the **final acceptance checklist**:

- [ ] `dotnet build OD.MyLocalAgent.sln` warning-clean; `dotnet test` all green (§7.7).
- [ ] Sample task `docs/samples/daily-mail-summary.json` copied to the tasks folder runs
  end-to-end: `OD.MyLocalAgent.Runner.exe run --task daily-mail-summary` → report written,
  index line appended, next occurrence re-registered.
- [ ] UI starts in dark theme; all 9 pages navigable; stock controls render dark (Stage 6
  audit checklist) — no light-default artifacts.
- [ ] Zero placeholders; every public API documented in English.
- [ ] All ViewModels use MVVM Toolkit source generators; no manual INPC or command classes.
- [ ] Settings persists the provider choice, applies without restart; Test connection reports
  real health + latency; API key never touches settings.json (verify the file's contents).
- [ ] Settings persists valid Temperature, Top P, and Context window values; task and AI-step
  overrides resolve field by field as step → task → Settings; fixed internal AI operations
  ignore user Temperature/Top P without losing the global context-window value.
- [ ] Settings → Diagnostics: Save to disk writes a complete `.log`; Reset clears view and
  archives/truncates rolling files.
- [ ] Exception drill via the "Trigger test incident" button **and** a faulted background
  task: both are caught, written to `incidents.jsonl` with correlation ids, shown as
  non-blocking notifications — the app keeps running.
- [ ] Safety defaults verified: with flags off, `filesystem.delete`, `filesystem.moveToBin`,
  `gmail.send` and `http.post` fail with `SafetyException` (runner `test-tool` is the quickest
  probe).
- [ ] `resync-schedules` repairs a stale registration (delete the task from Task Scheduler,
  run the command, verify it is recreated with the correct next occurrence).

## 12. GUARDRAILS ADDENDUM V2

This addendum supersedes the pre-Stage-7 solution count, Gmail read-output shape, navigation
count, and external Gmail/HTTP delivery behavior where they conflict with earlier sections.

### 12.1 Locked product decisions

- **D1:** Add `OD.MyLocalAgent.Guardrails`, referencing only
  `OD.MyLocalAgent.Ollama`. Core, UI, and Tests reference Guardrails directly. The solution
  contains eight projects.
- **D2:** Every screening operation resolves `ILlmClientFactory.Create()` so provider/model
  changes apply without restart. OpenAI-compatible screening and reduction send the protected
  Gmail/HTTP source, including every oversized chunk, to the configured cloud endpoint.
- **D3:** There is no raw pass-through or insecure off switch. `ScreeningEnabled=false`
  disables Guardrails LLM calls, not protection.
- **D4:** Screening failures throw `GuardrailScreeningException` with the original inner
  exception and a complete marked-Base64 fallback. Reduction failures throw
  `GuardrailSummarizationException` with the original inner exception and no source content.
  Gmail, HTTP, and Trust filter report each caught exception exactly once.
- **D5:** Sources above `MaxContentChars` and at or below
  `MaxSummarizationSourceChars` are reduced through sequential, deterministic, tool-less
  chunks covering every source character. Source and intermediate inputs are classified.
- **D6:** A generated summary longer than the requested maximum is capped in C#, includes an
  explicit removed-character marker, and cannot be delivered as plaintext.
- **D7:** Gmail protects one canonical From/To/Subject/Snippet/Body block. Successful
  reduction preserves exact headers and replaces only Body with a labeled AI summary.
- **D8:** Verbatim indicators are human-only Trust filter data. Agent-facing Gmail/HTTP JSON
  contains only `suspiciousIndicatorCount`.
- **D9:** Gmail messages are processed sequentially. A zero limit is unlimited; a positive
  limit processes the first N messages and returns explicit processed/skipped counts and a
  warning without skipped content.
- **D10:** Unreducible HTTP GET content is replaced by a source-specific skip marker while the
  workflow continues. Unreducible Gmail bodies are replaced by the Gmail-review marker while
  exact canonical headers remain protected.
- **D11:** Dropbox content, local filesystem content, Calendar content, Gmail attachments,
  outbound request/message content, and HTTP POST responses remain outside this stage.
- **D12:** Trust filter is the tenth functional page and eighth permanent rail entry, after AI
  playground and before Settings.

### 12.2 Configuration and contracts

The reloadable `Guardrails` section defaults to:

```json
{
  "ScreeningEnabled": true,
  "CleanThreshold": 80,
  "CautionThreshold": 50,
  "MaxContentChars": 8000,
  "SummarizeOversizedContent": true,
  "MaxSummaryChars": 4000,
  "MaxSummarizationSourceChars": 100000,
  "TimeoutSeconds": 60,
  "CacheResults": true,
  "MaxGmailMessagesPerOperation": 0
}
```

Validation requires `0 <= CautionThreshold < CleanThreshold <= 100`,
`MaxContentChars > 0`, `256 <= MaxSummaryChars <= MaxContentChars`,
`MaxSummarizationSourceChars > MaxContentChars`, `TimeoutSeconds > 0`, and a non-negative
Gmail limit.

`IContentTrustService.ScreenAsync` returns `ContentTrustResult` with
`ContentTrustStatus.Screened`, `ScreeningDisabled`, or `Failed`.
`IContentTrustService.SummarizeAsync` returns `ContentSummaryResult` with
`ContentSummaryStatus.Summarized`, `SummaryTruncated`, `LlmProcessingDisabled`,
`SummarizationDisabled`, or `SourceTooLarge`. Successful reductions require a source trust
score. The final effective score is the minimum of the source-wide score and the exact
delivered-block score.

### 12.3 Mandatory delivery and policy

Every non-withheld block starts with a non-negotiable English warning and uses a fresh,
unpredictable nonce in matching delimiters:

- plaintext: `UNTRUSTED-EXTERNAL-DATA`;
- UTF-8 Base64: `UNTRUSTED-EXTERNAL-DATA-BASE64`.

The payload round-trips exactly after UTF-8 decoding. Delimiters and warnings are never sent
to the screener and never enter cache keys. Default policy is clean (`>=80`) → marked
plaintext, caution (`>=50`) → marked Base64, lower → exact withheld marker. Disabled or
failed screening always uses marked Base64 for normal-size content. Base64 is spotlighting,
not encryption, and a model may decode it.

### 12.4 Oversized reduction, failures, and cache

Screening and reduction are direct `IChatClient` calls with temperature zero, one system and
one user message, and no tools, skills, agent, or side effects. Ordered chunks prefer a line
break, then whitespace, then a hard boundary without dropping, normalizing, reordering, or
duplicating characters. Intermediate summaries remain untrusted and are reclassified before
further reduction. Provider/model changes abort a multi-call reduction. Caller cancellation
propagates without fallback or incident reporting.

Verdict and summary caches are in-memory only with a 24-hour `TimeProvider`-measured TTL.
Keys include the content hash, provider, model, prompt versions, and requested summary maximum
where applicable. Original content, wrappers, nonces, exceptions, and incidents are never
cached.

### 12.5 Gmail and HTTP agent-facing behavior

Gmail read aliases return a root object containing `messages`, `totalFetched`, `processed`,
`skippedBecauseOfLimit`, `limitApplied`, and `warning`. Each message contains provider id,
receipt metadata, one protected canonical `content` value, trust metadata, body-processing
metadata, and trust scope. It never duplicates raw From, To, Subject, Snippet, or Body fields.
This is an intentional security deviation from the earlier bare array.

HTTP GET and GET Text preserve URL, status, and media-type metadata while placing all response
body/text only in the protected content property. Successful summaries return only the final
protected summary. Too-large, disabled, or failed reductions return exact application-authored
skip markers with `requiresHumanReview=true`; the workflow continues. HTTP POST behavior is
unchanged and remains a documented residual risk.

## 13. WEB SEARCH ADDENDUM

This addendum supersedes the five-tool-group count, secret list, and Stage-7 external-content
scope where they conflict with earlier sections.

### 13.1 Locked product decisions

- **D1:** `IWebSearchConnector` is the stable Core-facing facade. Providers implement
  `IWebSearchProvider`; Stage 8 registers only `OllamaWebSearchConnector`.
- **D2:** Guardrails protection is mandatory and wins over conflicting delivery behavior.
  Every result set, including canonical `[]`, is screened exactly once. Disabled or failed
  screening delivers marked UTF-8 Base64 with no raw fallback; a caught screening failure is
  incident-reported exactly once.
- **D3:** The exact delivered canonical payload is bounded before screening. Deterministic
  truncation and omission are allowed at `Guardrails:MaxContentChars` and are explicitly
  reported.
- **D4:** Only source `index` and `url` may appear outside `protectedResults`. Provider title
  and snippet text appear only inside the protected payload. Withholding removes sources.
- **D5:** `WebSearch:ApiKey` is a write-only DPAPI CurrentUser secret and must never be stored
  in `settings.json`.
- **D6:** The result shape is one compact protected block with aggregate Guardrails metadata
  and no agent-facing verbatim suspicious indicators.

### 13.2 Contracts and configuration

`IWebSearchConnector.SearchAsync(string query, int maxResults, CancellationToken ct)` returns
`WebSearchResult(IReadOnlyList<WebSearchResultItem> Items, string Provider)`;
`WebSearchResultItem` contains `Title`, `Url`, and `Content`.

The reloadable configuration defaults to:

```json
{
  "WebSearch": {
    "Provider": "",
    "Endpoint": "https://ollama.com/api/web_search",
    "DefaultMaxResults": 5,
    "MaxResultsCap": 10,
    "TimeoutSeconds": 30
  }
}
```

An empty provider disables the tool. `Ollama` is the only Stage-8 provider. The endpoint must
be absolute HTTP(S), limits must be positive with the default no greater than the cap, and
timeout must be positive. The Ollama provider sends
`POST https://ollama.com/api/web_search`, Bearer authentication, and compact JSON fields
`query` and `max_results`. The tolerant response parser reads `results[].title`,
`results[].url`, and `results[].content`.

### 13.3 Tool Map, whitelist, and output

The sixth tool group is `web`. The Tool Map adds:

| Workflow alias | Agent function name | C# implementation | Args (camelCase) |
|---|---|---|---|
| `web.search` | `web_search` | `WebSearchTool.SearchAsync` | `query`, `maxResults` (optional) |

The compact tool result contains `provider`, `query`, `providerCount`, delivered `count`,
`omittedBecauseOfBudget`, `truncated`, whitelisted `sources`, one `protectedResults` string,
and aggregate `guardrails` fields: `screened`, `trustStatus`, `trustScore`,
`suspiciousIndicatorCount`, `disposition`, and `contentEncoding`. `withheld` is present only
for withheld content.

The screened canonical payload is a compact ordered JSON array containing only
`index`, `title`, and `content`. It is built and bounded before the single screening call.
Complete earlier results are retained first; the first overflowing result may be shortened
with the explicit `[truncated by OD.MyLocalAgent]` marker; all remaining results are omitted.
The final canonical string never exceeds `Guardrails:MaxContentChars`. Source metadata uses
only the retained result indexes and URLs. Because the canonical empty array is `[]`, Web
Search rejects `MaxContentChars < 2` before sending a cloud query.

### 13.4 UI, privacy, and residual risk

The Tools page exposes the disabled/enabled Ollama selection, endpoint, write-only key,
configuration state, Save, and a fixed-query connection test. Web Search is visibly labeled
cloud. A custom endpoint warning states that it receives the key and queries. The task editor
adds the `web` checkbox.

Queries and snippets leave the machine for Ollama cloud. Rate limits are not a stable
documented contract and bursts may be throttled. Trust scoring remains probabilistic; Base64
is not isolation. URLs are deliberate metadata excluded from screening. Budget truncation or
omission can lose information. These risks do not weaken Safety gates, tool whitelists,
filesystem sandboxing, or HTTP allowlisting.

## 14. PER-TASK FILE-SYSTEM AUTHORIZATION ADDENDUM

This addendum supersedes the single-root-only path behavior in R9, §7.3, §7.6, §7.7, §8.1,
§8.2, and §8.3 where those sections conflict with the rules below. All other Tool Map,
workflow, Safety, incident, configuration-layering, MVVM, Guardrails, and logging requirements
remain normative.

### 14.1 Locked product decisions

- **D1:** `FileSystem:RootPath` remains a required absolute application-wide sandbox root.
  Relative tool paths continue to resolve beneath it. This preserves a simple safe workspace
  for task definitions, Tool tester, AI playground, and ready-to-copy samples.
- **D2:** A task may additionally declare absolute authorized directories through
  `fileSystemAccess`. These rules belong to the task, are persisted in its JSON file, and are
  applied to every deterministic or agent-selected local-file operation performed for that
  task.
- **D3:** An empty `fileSystemAccess` array grants no absolute-path access. It does not weaken
  the existing relative-path access beneath `FileSystem:RootPath`.
- **D4:** Authorization never grants Windows rights. NTFS ACLs, unavailable volumes, S4U
  limitations, sharing violations, and operating-system policy remain authoritative.
- **D5:** `Safety:AllowFileDelete` remains an independent global gate. Deletion requires both
  an effective read-write location and the enabled Safety flag.
- **D6:** The implementation must carry an immutable execution context. It must not mutate a
  singleton with the current task because distinct tasks may execute concurrently.
- **D7:** Dropbox upload and download local paths use the same task policy. Upload requires
  read access; download requires read-write access. Dropbox remote paths are unaffected.
- **D8:** Local application infrastructure paths for the task store, reports, report index,
  logs, incidents, credentials, and secrets remain application-owned and are not workflow
  file-system grants.
- **D9:** Local filesystem content remains outside Guardrails processing as specified by
  §12.1 D11. The authorization boundary prevents access but does not classify authorized file
  content.
- **D10:** There is no deployed task population to migrate. The repository task examples,
  tests, manuals, and acceptance material are updated directly to the new canonical contract.

### 14.2 Task contract

The canonical task object adds this property:

```json
{
  "fileSystemAccess": [
    {
      "path": "D:\\Production\\Incoming",
      "access": "ReadOnly"
    },
    {
      "path": "E:\\Archives\\Daily",
      "access": "ReadWrite"
    }
  ]
}
```

`fileSystemAccess` is always an array and defaults to an empty array. Each element has:

- `path`: a required absolute directory or volume-root path;
- `access`: `ReadOnly` or `ReadWrite`, serialized as a JSON string.

Paths are expanded and normalized when the task is saved. Empty paths, relative paths,
duplicate normalized paths, unsupported access values, and paths containing wildcard
characters are validation errors. A path may be saved while its disk is offline or its
directory does not yet exist. Nested rules are allowed; the most specific matching rule wins,
so a read-write child may be declared beneath a read-only parent.

### 14.3 Authorization semantics

- A relative path is resolved only beneath `FileSystem:RootPath` using the existing
  `Path.GetFullPath` containment rule. The global root has effective `ReadWrite` access.
- An absolute path is rejected unless it equals or is a descendant of a task
  `fileSystemAccess` rule with sufficient access.
- `filesystem.list` and `filesystem.read` require read access.
- `filesystem.write` and `filesystem.createDirectory` require read-write access.
- Both source and destination of `filesystem.move` require read-write access.
- `filesystem.moveToBin` requires read-write access plus `Safety:AllowMoveToBin`.
- `filesystem.delete` requires read-write access plus `Safety:AllowFileDelete`.
- `filesystem.markdownToHtml` requires read access to its source and read-write access to its
  explicit or `.html`-derived destination. `filesystem.htmlToText` requires read access.
- `dropbox.upload` requires read access to `localPath`.
- `dropbox.download` requires read-write access to `localPath`.
- Tool tester and AI playground executions that are not associated with a saved task receive
  no additional absolute-path rules and remain limited to relative paths beneath
  `FileSystem:RootPath`.

Containment validation must be case-insensitive on Windows, separator-aware, and resistant to
parent traversal, sibling-prefix confusion, symbolic links, directory junctions, and other
reparse-point escapes. For a new destination, all existing ancestors up to the matching
authorized root must be checked. Source and destination are validated independently.

### 14.4 Runtime propagation and Tool Map

The Runner builds the immutable file-system policy from the loaded `ScheduledTask` and passes
it through both execution paths:

- deterministic Tool Map invocation in `SequentialWorkflowEngine`;
- agent-selected tools created by `MyLocalAgentService`, including `ai.prompt` steps and
  prompt-only tasks.

The Tool Map aliases, agent-facing names, and JSON method arguments remain unchanged.
`IToolRegistry` may gain execution-context overloads, but it remains the single runtime view
of the normative Tool Map and the task tool-group whitelist remains independently enforced.

### 14.5 Task editor

When File system is enabled, the task editor displays an English **Authorized locations**
section. It provides a folder picker, a list of directory paths, a `Read only` /
`Read and write` selector, and removal. A selected volume root requires explicit
confirmation. Unavailable locations and UNC paths display warnings but may be saved; UNC
warnings state that S4U does not provide reusable Windows network credentials.

The ViewModel uses CommunityToolkit.Mvvm source generators only. Folder selection stays
behind `IDesktopInteractionService`; page code-behind remains initialization-only.

### 14.6 Required verification

Automated tests cover serialization, validation, relative-root behavior, absolute
authorization, read-only enforcement, multiple drives, nested-rule precedence, source and
destination checks, delete's double gate, Dropbox local paths, traversal, sibling prefixes,
reparse-point escape rejection, taskless Tool tester behavior, agent/workflow propagation,
and concurrent executions with different task policies.

The stage gate is a warning-clean Release build and complete test run. Commands explicitly use
MSBuild `-m:1`.

## 15. SEQUENTIAL WORKFLOW CONTEXT ADDENDUM

This addendum extends §7.4 and §8.3. It does not introduce graph routing, a workflow library,
workflow parameters, `ai.judge`, or a separate Workflows page.

### 15.1 Structured outputs and accumulated context

The existing `{{previous.output}}` and `{{stepId.output}}` placeholders remain unchanged.
When a tool output is valid JSON, a placeholder may append case-insensitive object properties
and zero-based array indexes, for example `{{source.output.content}}` or
`{{files.output.entries[0].name}}`. Selected strings are inserted as text; other JSON values
are inserted as compact JSON. Missing properties, malformed or invalid indexes, and
structured selection from non-JSON output fail with `WorkflowPlaceholderException`.

`{{workflow.context}}` serializes the successful outputs completed before the current step as
an object whose root property is `steps`. JSON outputs remain structured and plain-text
outputs become JSON strings. Entries retain sequential execution order.

### 15.2 Prompt steps

`ai.prompt` accepts required string `prompt` and optional boolean `includeContext`. The
default is `false`. When enabled, the engine appends the accumulated context snapshot under
an English warning that the JSON is data, not instructions. The call retains the task tool
whitelist and immutable per-task execution context. The returned model text is recorded as
the prompt step's named output for later deterministic steps.

Full context is opt-in because it may consume substantial model context. Local filesystem
content remains outside Guardrails classification under §12.1 D11 and §14.1 D9; filesystem
authorization, tool whitelists, and Safety gates remain the enforceable boundaries.

### 15.3 Authoring

The existing Task editor remains the workflow editor and JSON remains the persisted source
of truth. It may provide Tool Map-driven step insertion, canonical formatting, validation,
and ready-to-adapt examples. No additional workflow persistence or scheduling surface is
introduced.

### 15.4 Local date/time placeholders and file-name operations

`{{Today}}` resolves in the injected `TimeProvider` local time zone as `yyyy-MM-dd`.
`{{Now}}` resolves as `yyyy-MM-ddTHH:mm:sszzz`. Both accept bracketed .NET custom date/time
formats. Filename-friendly aliases normalize `YYYY`, `YY`, `DD`, and `hh` to `yyyy`, `yy`,
`dd`, and 24-hour `HH`; therefore `{{Today[YYYY]}}` returns the year and
`{{Now[hhmmss]}}` returns a separator-free 24-hour time. Empty, excessive, malformed, or
unknown placeholders fail the step. Users remain responsible for choosing formats containing
only characters valid at the destination.

The filesystem Tool Map adds `filesystem.rename(path, newName)` and
`filesystem.setExtension(path, extension)`. Rename accepts only a leaf file name and never
changes the directory. Set Extension accepts a value with or without a leading period and
changes only the extension. Both operate on files, never overwrite an existing destination,
and require effective read-write authorization for source and destination. They do not use
the delete Safety gate.

### 15.5 Batch skill semantics

`ai.extractActionItems` accepts one or more messages and returns a JSON object containing a
Markdown checklist in `text`. It extracts only explicit actions and includes owner, due date,
and source; missing values are `Not stated` and are never inferred. No explicit action returns
exactly `No explicit action items.`

`ai.classifyEmail` accepts one email or a complete batch such as `gmail.readUnread` output.
It classifies each identifiable message independently as exactly `Urgent`, `ReplySoon`,
`Waiting`, or `FYI` and returns a Markdown table in `text` with Message, Category, Reason,
and Deadline. This is aggregate batch processing rather than a workflow loop: it produces a
report and does not route or execute a separate action for each message.

## 16. AI WORKFLOW HELPER ADDENDUM

This addendum extends §7.6, §8.3, §14, and §15. It does not change task persistence,
sequential execution, runtime authorization, Safety gates, or the existing workflow editor as
the JSON source of truth.

### 16.1 Locked product decisions

- **D1:** The existing Task editor adds **AI Helper** for creation/modification and
  **AI Explain** for a non-empty workflow. Both use one reusable **AI Workflow Helper**
  window; there is no new page.
- **D2:** Generation returns only the workflow JSON array currently entered by hand. The
  application continues to construct and persist the complete `ScheduledTask`.
- **D3:** Existing workflow JSON is converted deterministically in C# into an editable,
  ordered, lossless textual description. Generation and refinement use that complete
  description; no visible or persisted chat transcript is maintained.
- **D4:** The currently configured LLM provider and model are used at each call. System
  prompts, Tool Map context, placeholder context, repair prompts, provider, and model remain
  internal. User-visible content is limited to authoring/refinement text, streamed output,
  validation/proposal state, and explanation.
- **D5:** Helper calls use `IChatClient` directly with no tools, skills, agent, or execution
  path. Generated aliases are inert JSON until the task is later saved and run normally.
- **D6:** The application, not the model, parses and validates JSON and derives tool groups,
  filesystem requirements, unresolved-path warnings, and Safety-gate warnings.
- **D7:** Each operation permits three model attempts total: the initial response and at most
  two deterministic-error correction responses. Exhaustion preserves the last response and
  errors for manual Retry.
- **D8:** Apply requires valid JSON and acceptance of every statically required tool-group and
  filesystem proposal. It atomically updates the Task editor workflow, tools, and authorized
  locations. One-level Undo restores the immediately preceding editor values until the next
  save/reload/navigation or helper application.
- **D9:** Invalid Explain input is rejected locally without an LLM call. A valid explanation
  is English and covers purpose, steps, workflow prompts, tools, skills, placeholders, data
  flow, permissions, Safety gates, external effects, and likely failures.
- **D10:** Assistant descriptions, refinements, responses, explanations, partial streams,
  errors, and proposal choices are memory-only and must not be logged or persisted.
- **D11:** Generation and explanation stream, are cancellable, and preserve incomplete output
  read-only after cancellation. Cancellation causes no repair, execution, editor mutation, or
  incident.
- **D12:** Explanation defaults to deterministic plain text and can switch, without another
  model call, to safely rendered Markdown through Markdig and WebView2.

### 16.2 Authoring and refinement

The deterministic description includes every step in declaration order, original
string/object form, alias, explicit and effective id, exact typed argument JSON, placeholder
text, step error policy, and retry values. It never resolves, summarizes, omits, or reorders
content.

An empty workflow begins with an empty description. Generate uses the complete editable
description. After a valid candidate, the user may edit that description or submit one
single-use refinement instruction. Refinement receives the current canonical candidate,
current deterministic description, and latest instruction only. After success, the
application rebuilds the complete description from the new JSON and clears the refinement.
There is no hidden accumulated conversation history.

Generation accepts only a response whose complete non-whitespace content is directly
deserializable as the canonical workflow array. Markdown fences, prose, prefixes, suffixes,
unknown aliases, invalid arguments, invalid placeholders, and invalid task rules are errors.
Correction calls receive the prior raw response and exact deterministic errors and must
return a complete replacement array.

Required Tool Map arguments and statically checkable placeholder grammar use shared
workflow-definition validation consumed by both the normal Task editor and the helper. The
helper must not maintain a divergent validation rule set.

### 16.3 Dynamic catalogs and model boundary

Generation context is versioned and built from `ToolMap.Definitions`, including alias, tool
group, ordered required/optional arguments, AI-step status, and Safety flag. Placeholder
syntax comes from one shared catalog used by the runtime resolver and helper; the UI does not
maintain a handwritten duplicate.

The helper calls the current factory-selected `IChatClient` with one system message and one
user message and no tools. It does not use `MyLocalAgentAI`, invoke an `ISkill`, inspect
referenced data, resolve runtime outputs, read files, call connectors, or execute candidate
steps. Model input excludes secrets, file contents, tool results, logs, and unrelated task
data. Descriptions, workflow JSON, responses, paths, and explanation content are not logged
or placed in incidents.

### 16.4 Tool, path, and Safety analysis

Non-AI tool groups are derived from Tool Map ownership. Missing groups are proposed and
require explicit acceptance. Refusal keeps the helper open and disables Apply.

Tool Map metadata additionally identifies local-path arguments, file/directory kind, and
required `ReadOnly` or `ReadWrite` access. Static analysis covers filesystem operations and
the local side of Dropbox transfers. Relative paths remain under `FileSystem:RootPath` and
need no task grant. A literal absolute file path proposes its containing directory; a literal
absolute directory proposes itself. Exact duplicate normalized proposals are combined using
the strongest access, and existing most-specific authorization rules remain authoritative.
A literal move/delete file-or-directory target proposes its containing directory with an
explicit broader-scope warning because static analysis does not inspect disk to determine its
kind.

Paths containing workflow placeholders, unexpanded environment variables, runtime outputs,
or otherwise indeterminate content produce warnings but no invented authorization. Disk
existence is not inspected. Stage-11 volume-root confirmation and UNC/S4U warnings remain
binding.

Safety flags are reported from Tool Map metadata with their current status. The helper never
changes global Safety settings. A disabled gate is a prominent likely-runtime-failure warning
but does not alter task-schema validity.

### 16.5 Apply, Undo, and explanation

Before Apply, one review lists missing tool groups, missing or insufficient filesystem grants,
unresolved dynamic paths, referenced Safety flags, and confirmation that no operation was
executed. Every required task-owned proposal must be accepted. Apply transfers canonical
indented workflow-array JSON plus accepted tools and grants to the normal Task editor as one
operation. Only the existing Save command persists and schedules the task.

AI Explain first parses and validates current draft workflow JSON. Valid input is sent with a
deterministic requirements/risk analysis. The result explains declaration order, ids,
arguments, workflow prompts, skills, placeholders, named outputs, structured selections,
context inclusion, data flow, tool groups, filesystem grants, Safety gates, external effects,
Guardrails boundaries, and statically visible failure risks. The helper's internal prompts
are never disclosed.

### 16.6 Markdown rendering and runtime fallback

Stage 13 permits exactly two new centrally pinned packages: `Markdig` 1.3.2 and
`Microsoft.Web.WebView2` 1.0.4078.44. The internal prompt contracts are
`workflow-generation-v1` and `workflow-explanation-v1`; the shared placeholder catalog is
version 1. Exact versions and justifications are recorded in the architecture ledger.

Markdig converts the single explanation response to HTML; Text preserves the exact streamed
Markdown source after completion. Raw HTML is disabled or escaped. WebView2 receives an
in-memory theme-matched document with no scripts and a Content Security Policy blocking remote
connections, resources, frames, media, fonts, and images. Navigation, pop-ups, downloads, and
new-window requests are intercepted and blocked.

If the WebView2 Runtime is unavailable or initialization fails, Text mode remains fully
functional. The UI shows a normal WPF hyperlink to
`https://developer.microsoft.com/microsoft-edge/webview2/`; only an explicit user action may
open it. The application never downloads or installs the runtime.

### 16.7 Required verification

Automated tests cover deterministic description fidelity, dynamic catalogs, tool-less current-
provider streaming, strict JSON response handling, three-attempt repair, cancellation,
tool/path/Safety analysis, proposal acceptance/refusal, atomic Apply/Undo, invalid Explain
short-circuiting, explanation coverage, Text/Markdown projection, restricted HTML, blocked
navigation, WebView2 fallback, command states, and absence of assistant persistence/logging.

The stage gate is a warning-clean Release build and complete deterministic test run. Restore,
build, and test commands explicitly use MSBuild `-m:1`.

## 17. HIERARCHICAL AI GENERATION SETTINGS ADDENDUM

This addendum extends §7.3, §7.4, §7.6, §8.1–§8.3, and §8.5. It does not introduce
per-task providers/models, a separate workflow model, or user control over deterministic
internal AI policies.

### 17.1 Locked product decisions

- **D1:** Settings owns global Temperature, Top P, and Context window values for the one
  currently selected provider/model.
- **D2:** A task may override Temperature and/or Top P. An object-form Tool Map AI step may
  independently override either value again.
- **D3:** Effective user-workflow values resolve per property as AI step → task → Settings.
  An absent property means inherit; it never resets the complete options object.
- **D4:** Context window is global only. It cannot appear in task or step JSON and is not an
  output-token limit.
- **D5:** AI Workflow Helper generation/refinement/repair, AI Explain/repair, Guardrails
  screening, and Guardrails reduction use named program-owned policies with Temperature 0
  and Top P 1. User settings and task/step overrides cannot change them.
- **D6:** Fixed internal policies still receive the same global Context window as every other
  LLM call.
- **D7:** Effective options are immutable and explicitly propagated per execution. Mutable
  singleton, static current-task, `AsyncLocal`, and other ambient approaches are forbidden
  because tasks may run concurrently.
- **D8:** Existing tasks without overrides remain valid and inherit Settings. Empty override
  objects are not emitted by canonical serialization.
- **D9:** Provider adapters apply supported settings honestly. Unsupported context-window
  configuration must not be sent as an invented API field or silently described as enforced.
- **D10:** This stage does not add Top K, seed, penalties, stop sequences, output-token
  control, per-task provider/model selection, or a generation-profile library.

### 17.2 Global configuration

The reloadable global contract is:

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

Validation is deterministic:

- Temperature is finite and in inclusive range 0–2;
- Top P is finite, greater than 0, and at most 1;
- Context window is an integer in inclusive range 1,024–2,000,000;
- invalid values are rejected, never silently clamped.

Saving Settings preserves `AI:Provider` and unrelated configuration. Values reload through
`IOptionsMonitor`; a new call sees the new snapshot, while an already-running call retains
the immutable policy with which it began. Provider/model changes do not guess or silently
rewrite the context window.

### 17.3 Persisted task and workflow contracts

A task's optional override is:

```json
"generationOptions": {
  "temperature": 0.1,
  "topP": 0.9
}
```

Both properties are optional. JSON null, non-numeric values, non-finite values, invalid
ranges, and unknown properties fail validation. An empty object is accepted on input but
omitted from canonical output.

An object-form AI workflow step uses the same sibling property:

```json
{
  "step": "ai.prompt",
  "id": "draft",
  "args": {
    "prompt": "Draft three titles from {{source.output}}."
  },
  "generationOptions": {
    "temperature": 0.8
  }
}
```

It is workflow metadata, not an `args` value. It is never inserted into a prompt or passed to
a tool. Eligibility comes only from `ToolMap.Definition.IsAiStep`, including `ai.prompt` and
built-in AI skills. A non-AI step with `generationOptions` fails validation. String-form AI
steps remain valid and inherit all values; adding an override requires object form.

`contextWindowTokens`, provider, model, endpoint, credentials, and internal policy names are
invalid in task/step generation options.

### 17.4 Resolution and runtime propagation

One pure resolver returns a fully populated immutable policy and independent provenance for
Temperature and Top P:

```text
temperature = step.temperature ?? task.temperature ?? settings.temperature
topP        = step.topP        ?? task.topP        ?? settings.topP
context     = settings.contextWindowTokens
```

Normal call coverage:

- a prompt-only Runner task uses the task-effective policy;
- every agent round in that task execution uses the same policy;
- Prompt editor Test uses the current unsaved task override;
- an `ai.prompt` or direct built-in skill step uses its step-effective policy;
- workflow retries retain the policy resolved when the step began;
- AI Playground has no task and uses the global policy.

Task execution paths must not accidentally use a taskless overload and lose overrides.
Built-in skills accept explicit policies when called as workflow steps and inherit the
containing task-agent policy when exposed inside an agent. Concurrent tasks cannot observe
or mutate each other's policy.

Apply effective Temperature and Top P through `Microsoft.Extensions.AI.ChatOptions`. A
per-execution chat-client wrapper or equivalent mechanism may merge the policy into every
agent request, but it must preserve tools, model id, response format, provider properties,
and cancellation and must not mutate caller-owned options or the shared provider client.

The implemented shared contracts are `GenerationOptions`, `EffectiveGenerationPolicy`,
`GenerationOptionSource`, and the pure `GenerationPolicyResolver`. Execution boundaries use
explicit policy-bearing overloads on `ILlmClientService`, `IMyLocalAgentService`, and
`ISkill`; `GenerationPolicyChatClient` clones caller options before adding the resolved
values. These names are normative for the Stage 14 implementation and make policy flow
reviewable without ambient execution state.

### 17.5 Fixed internal policies

Named program constants are:

```text
WorkflowAuthoring:   Temperature 0, TopP 1
WorkflowExplanation: Temperature 0, TopP 1
GuardrailScreening:  Temperature 0, TopP 1
GuardrailReduction:  Temperature 0, TopP 1
```

Workflow generation/refinement and their corrections use `WorkflowAuthoring`; explanation
and its corrections use `WorkflowExplanation`. These constants are not exposed in Settings,
task JSON, workflow JSON, or helper UI. They ignore all user Temperature/Top P values while
still receiving the current global Context window.

### 17.6 Context-window semantics

`ContextWindowTokens` is the application's global declaration for the active model's total
request budget:

```text
input messages + agent/tool context + generated output
```

It is distinct from timeout, source-character limits, and maximum output tokens. It cannot
increase a model's physical capacity.

Ollama integration maps it to the provider's supported native `num_ctx` request option.
OpenAI-compatible models own their fixed context capacity and may not expose such a request
option; no invented field is sent. In that case the configured value remains the
application-declared budget, the UI states the limitation, and the provider is authoritative.
Content is never silently truncated, summarized, or discarded to satisfy this setting.
Provider context failures use the existing typed execution and incident/reporting paths.

### 17.7 User interface and helper integration

Settings adds an **AI generation defaults** card with numeric Temperature, Top P, and Context
window controls, concise explanations, culture-safe decimal editing, inline validation, and
disabled Save while invalid. It recommends tuning Temperature or Top P rather than both and
states that context capacity is model-dependent.

Task editor adds optional **AI generation override** controls for Temperature and Top P.
Each has an independent **Use global default** state and displays its effective value and
provenance. There is no task context-window field. All changes participate in dirty state,
save, reload, and validation.

Guided workflow authoring exposes `generationOptions` only for Tool Map AI steps. Stage 13
deterministic descriptions preserve exact overrides and inherited properties; generation and
refinement know the schema but never invent provider/model/context properties. AI Explain
describes configured overrides and deterministic provenance while its own call retains the
fixed explanation policy. Apply and Undo continue to transfer the canonical workflow JSON
atomically.

AI Playground displays its global effective policy. Prompt editor displays the current
task-effective values and provenance during Test.

### 17.8 Required verification

Automated tests cover configuration load/save/hot-reload, culture-safe editing, ranges,
strict JSON validation, backward-compatible task loading, canonical omission, task/step
round-trip, non-AI rejection, every inheritance combination and provenance, Runner/agent/
prompt/skill/playground propagation, retry stability, concurrent isolation, agent multi-round
option preservation, fixed helper/explanation/Guardrails policies, global context propagation,
Ollama mapping, OpenAI-compatible non-invention, provider failure behavior, helper description
and explanation integration, UI command state, and privacy-safe logging.

The stage gate is a warning-clean Release build and complete deterministic test run. Restore,
build, and test commands explicitly use MSBuild `-m:1`.

## 18. Stage 15 addendum — Agent loop hardening and run observability

This addendum is normative for the post-Stage-14 implementation. It supersedes the earlier
`ai.prompt` agent-context wording in §§14.4 and 15.2, the Gmail/Calendar signatures and Tool
Map rows in §§5.2 and 8.3, and the dark-only part of R6 for the AI Workflow Helper window.
All credential, task-whitelist, filesystem, Guardrails, R9, and generation-policy rules remain
unchanged.

### 18.1 Tool-less prompt boundary and loop limits

`ai.prompt` resolves its placeholders and optional `includeContext` snapshot as before, then
performs one direct provider request with the step-effective generation policy. It receives no
Tool Map functions, connector functions, or skills. The workflow context is data in its prompt,
not an agent tool context.

Agent-capable calls outside `ai.prompt` retain the task whitelist and execution context, but a
single request is bounded by all of the following:

- there is no fixed function-invocation-round cap: the consecutive-identical-result guard
  below is the only protection against an agent looping forever;
- at most two consecutive tool errors;
- unknown function calls terminate the request;
- the same tool call, evidenced by an identical (function, sanitized arguments, serialized
  result) triple, may occur twice; its third consecutive occurrence, with no different call in
  between, terminates the request and raises the typed `AgentToolLoopException`. That third
  occurrence still executes so the repeated result can be observed; no fourth identical call
  is attempted.

The repeated-call signature contains the function name, the application's sanitized argument
serialization, and the serialized result. It must not add credential values to traces or
reports.

### 18.2 Extended Gmail and Calendar reads

The Tool Map adds or updates these aliases:

| Workflow alias | Agent function | Implementation | Arguments |
|---|---|---|---|
| `gmail.readToday` | `gmail_read_today` | `GmailTool.ReadTodayAsync` | optional positive integer `maxMessages` |
| `gmail.readUnread` | `gmail_read_unread` | `GmailTool.ReadUnreadAsync` | optional positive integer `maxMessages` |
| `gmail.readRecent` | `gmail_read_recent` | `GmailTool.ReadRecentAsync` | integer `daysBack` from 0 through 3650; optional positive integer `maxMessages` |
| `calendar.today` | `calendar_today` | `CalendarTool.TodayAsync` | — |
| `calendar.read` | `calendar_read` | `CalendarTool.ReadAsync` | enum string `period`: `Day`, `Week`, `Month`, or `Year` |

`daysBack` starts at the beginning of the local day minus that many days. Calendar periods use
local calendar boundaries and the correct local UTC offset at each boundary; Week starts on
Monday. The optional Gmail request limit reduces API
retrieval but does not weaken D9: `MaxGmailMessagesPerOperation` remains the independent
Guardrails processing cap and still reports fetched, processed, skipped, and warning fields.

### 18.3 Runner diagnostics and retained workflow context

Tool start logs serialize sanitized arguments as ordinary compact JSON instead of a runtime
anonymous-type descriptor. Successful completion logs add only bounded operational metadata:

- Gmail: returned message count;
- Calendar: returned event count;
- Web Search: returned result count;
- Dropbox: affected/listed item name and byte size, with list summaries bounded to ten items.

Secrets and configured tokens are never logged.

Interactive **Run now** output follows appended lines. The child Runner publishes the complete
`WorkflowContext` after each successful step through a reserved Base64 one-line protocol that
the UI consumes instead of displaying as ordinary runner output. **Show workflow context**
toggles between that JSON and runner text. The most recent context remains in UI memory after
completion and is replaced when the next task starts. It may contain task data and is therefore
diagnostic content, not a public or secret-free log.

### 18.4 AI Workflow Helper rendering and theme

The whole AI Workflow Helper window, including AI Explain Text and Rendered Markdown tabs,
uses the current application theme. Restricted Markdown emits Light or Dark local CSS from the
active theme. `NavigateToString` internal navigation is allowed exactly as an internal render;
subsequent external navigation, pop-ups, downloads, scripts, and remote resources remain
blocked. WebView2 failure continues to expose the Text fallback.

### 18.5 Required verification

Automated coverage includes direct tool-less prompt execution, repeated identical tool-call
termination, readable sanitized Gmail arguments and counts, Gmail and Calendar argument
mapping, workflow-context callbacks and protocol round-trip, and Light/Dark restricted HTML.
The final gate remains a warning-clean Release build and deterministic test run with `-m:1`,
plus standalone manual HTML validation and `git diff --check`.

### 18.6 Interactive Runner Debug and progress

The Tasks page adds three session-only Runner Output controls. **Debug (next run)** requests an
ephemeral detailed child-Runner channel for the next interactive Run now execution. It renders
sanitized Tool Map arguments already emitted by normal tool traces and the complete LLM request
and response messages for `ai.prompt`, agent calls, and Guardrails calls. It can therefore
contain task, Gmail, Calendar, or file data. It excludes credentials, tokens, and API keys and
is transported through a Base64 one-line protocol to the UI only; it must not be added to the
normal rolling logs or reports.

**Auto-scroll** is enabled by default. It follows appended Runner lines after WPF has completed
the text and layout update. Clearing it preserves the current reading position; selecting it
again immediately moves to the latest line and resumes following new output.

**Large** adds exactly 300 pixels to the Runner panel and clearing it removes exactly the same
amount. It never changes the main-window size or position. The Tasks page keeps a page-level
vertical scroll viewer and a bounded task-grid height, so the task actions and Runner remain
reachable on smaller displays. It is also session-only.

Gmail progress logs after retrieval expose `message N of M` while Guardrails processes the
bounded set. This distinguishes a finite sequential screening operation from an agent loop.
With `MaxGmailMessagesPerOperation = 0` and no `maxMessages` argument, every retrieved message
is screened serially; on a local model this can take several minutes. Choosing a non-zero
per-task `maxMessages` or Guardrails processing limit is an explicit operator trade-off and is
never silently imposed by the application.

Release verification for this increment requires a zero-warning Release build, protocol
round-trip/rejection coverage, the complete deterministic test suite, documentation review,
and `git diff --check`. The real Gmail workflow remains user data and must not be created or
executed as a verification fixture.

## 19. Stage 16 addendum — Windowless runner and run monitor

This addendum is normative for the post-Stage-15 implementation. It supersedes the console
subsystem wording of §7.5 (the runner is no longer a visible console application) and extends
run observability with a persistent, per-execution runner output log and a dedicated UI page.
All credential, task-whitelist, filesystem, Guardrails, R9, and generation-policy rules remain
unchanged.

### 19.1 Locked product decisions

| # | Decision |
|---|---|
| S16-D1 | **Windowless runner.** `OD.MyLocalAgent.Runner` builds with `<OutputType>WinExe</OutputType>`. A scheduled execution never creates or flashes a console window, regardless of the logon type. |
| S16-D2 | **CLI compatibility.** When the runner starts with non-redirected standard streams, it attempts `AttachConsole(ATTACH_PARENT_PROCESS)` and rebinds `Console.Out` / `Console.Error` to the parent console so manual invocations (`list`, `test-prompt`, …) from a shell keep printing. When standard streams are redirected (UI Run now, pipes, `>` redirection), no attach occurs and the streams are used as provided. |
| S16-D3 | **Per-run JSONL log.** Every `run --task <id>` execution appends its complete user-facing runner output to `%LOCALAPPDATA%\OD.MyLocalAgent\logs\runs\<taskId>\<yyyyMMdd-HHmmss-fff>.jsonl`, flushed line by line, readable while the run executes (`FileShare.Read`). Other commands do not create run logs. |
| S16-D4 | **File-based, no IPC.** The UI observes runs exclusively by reading these files (catch-up plus incremental tail). No named pipe, socket, or memory-mapped channel is introduced. A monitor opened mid-run replays the file from its beginning, so no output is lost. |
| S16-D5 | **Private channels stay private.** `ODMLA_CONTEXT:` and `ODMLA_DEBUG:` protocol lines pass through to standard output for the interactive Run now consumer but are **never written** to the run log file. Run logs contain the same sanitized content as the visible runner console; credentials, tokens, and API keys remain excluded. |
| S16-D6 | **Retention.** `Logging:MaxRunLogsPerTask` (positive integer, default `50`) bounds retained run-log files per task; the oldest files are pruned when a new run session starts. Retention never deletes the file of the run being started. |
| S16-D7 | **Runs page.** The UI adds a top-level **Runs** destination listing past and in-progress executions (task id, local start time, status, exit code) and streaming the selected run's lines live. It offers a display line limit, a display clear action, plain-text export (save as), an auto-scroll toggle enabled by default, and an action opening the run-logs folder. |
| S16-D8 | **Unified history.** Interactive Run now child executions produce the same run logs as scheduled executions and appear in the same Runs list. The existing Tasks Run now panel is unchanged. |
| S16-D9 | **Diagnostic content classification.** Run logs may contain task, Gmail, Calendar, or file data rendered by the runner's normal output. They are user-private diagnostic files, treated like reports, never uploaded or transmitted. |

### 19.2 Run-log contract

One UTF-8 JSONL file per execution; records in file order:

```jsonl
{"kind":"start","taskId":"daily-brief","startedUtc":"2026-08-01T06:00:00.123Z","processId":4242,"version":"1.2.5"}
{"kind":"line","timestampUtc":"2026-08-01T06:00:01.000Z","stream":"out","text":"Task 'daily-brief' started."}
{"kind":"line","timestampUtc":"2026-08-01T06:00:05.400Z","stream":"err","text":"..."}
{"kind":"end","endedUtc":"2026-08-01T06:01:10.000Z","exitCode":0}
```

- `stream` is `out` or `err`. `text` is one line without its terminator.
- A file without an `end` record represents an execution in progress (or one interrupted by
  process termination; the UI labels both "In progress / interrupted" until refreshed after
  the process exit).
- Writers hold the file with `FileShare.Read`; readers open with
  `FileShare.ReadWrite | FileShare.Delete` and perform incremental offset-based reads so a
  monitor can join mid-run and follow appended lines.
- Malformed or truncated trailing lines (a crash mid-write) are skipped by readers without
  raising incidents.

### 19.3 Runner behavior

- `RunnerApplication` wraps its `output` and `error` writers with a tee that forwards every
  character unchanged to the original writer and mirrors completed lines into the active run
  session, filtering the §19.1 S16-D5 protocol lines. The runner Serilog sink writes through
  the same tee (`WriteTo.TextWriter`) instead of a direct console sink, so scheduled runs keep
  full Serilog output in the run log.
- The run session opens in the `run` command handler once the task id is known and closes with
  the command's exit code in a `finally` block. Session open/close failures degrade to normal
  execution without run logging and report an incident; they never fail the task itself.
- The rolling application log, incident log, reports, and report index are unchanged.

### 19.4 Runs page

- Route `Runs` sits between Logs and Tools in the navigation list.
- The run list is built by enumerating `logs\runs\*\*.jsonl` (newest first) and reading each
  file's `start`/`end` records. While the page is active, a polling refresh (~1 s) updates
  the list and appends new lines of the selected run; polling stops when the page deactivates.
- The display line limit (default 2 000) bounds only the rendered collection: older rendered
  lines are dropped first; the underlying file is never modified by the limit, clear, or
  export actions.
- Export writes the currently selected run as plain text (`timestamp [stream] text`) through
  the standard save dialog. Clear empties only the display; selecting the run again reloads
  the full file within the line limit.
- Auto-scroll follows appended lines after layout, may be paused, and resumes to the latest
  line when re-enabled (same semantics as the Tasks Runner panel).

### 19.5 Required verification

Automated coverage includes run-log start/line/end round-trip, protocol-line exclusion,
mid-run incremental reads (catch-up then tail), retention pruning boundaries, malformed
trailing-line tolerance, and Runs view-model list/tail/limit/clear/export behaviors. The final
gate remains a warning-clean Release build and the deterministic test run with `-m:1`, plus
`git diff --check`. Manual checks: a scheduled execution shows no window, and the Runs page
follows a live execution joined mid-run.

## 20. Stage 17 addendum — Workflow authoring aids

This addendum extends §7.6, §8.3, §15.3, and §16. It adds two deterministic authoring aids to
the existing Task editor workflow card and enriches the Tool Map with documentation metadata.
It changes no persisted contract, no execution semantics, no authorization rule, and no Safety
behavior, and it introduces no LLM call and no package.

### 20.1 Locked product decisions

| # | Decision |
|---|---|
| S17-D1 | **Deterministic aids only.** The Tool catalog and the Prompt editor never call a model, an agent, a tool, a skill, the filesystem, or the network. Their content derives from Tool Map metadata, `WorkflowPlaceholderCatalog`, and the current editor text. |
| S17-D2 | **JSON remains the source of truth.** Both aids only produce a new `WorkflowText` value, plus an optional set of tool groups to enable for the catalog. No new persisted field, file, setting, or page. |
| S17-D3 | **One catalog, in Core.** `ToolMap` carries the English description of each alias and structured per-argument metadata; the Stage 13 assistant prompt builder consumes the same metadata. A UI-only alias or argument table is forbidden. |
| S17-D4 | **Caret plumbing is view-only.** The workflow `TextBox` exposes caret and selection through an attached behavior; snippet building, offset mapping, insertion, and prompt rewriting live in unit-testable non-UI classes. |
| S17-D5 | **Structured insertion first.** Parseable JSON is inserted through the parsed array at the caret's step position and re-serialized canonically; unparseable JSON receives verbatim snippet text at the clamped caret plus an explicit repair warning. |
| S17-D6 | **Sequential caret rule.** A caret inside an element inserts after that element; between two elements it inserts after the element on the left; before the first element it inserts at index 0; after the last element it appends. |
| S17-D7 | **Repeatable modal catalog.** Insert accumulates and keeps the window open, Insert and close applies and closes, Close returns the accumulated working copy only when at least one effective change exists, Cancel discards every insertion of the session. |
| S17-D8 | **Warn, never silently widen.** Only non-AI tool groups can be proposed for task whitelisting; a missing group produces a warning and an unchecked opt-in; a referenced disabled Safety flag is a warning only. Global Safety settings are never modified by an aid. |
| S17-D9 | **Built-in AI is not whitelistable.** The synthetic `ai` group exists for catalog grouping only. AI entries never display a missing-whitelist lock and `ai` never appears in an accepted-group result. |
| S17-D10 | **Workflow Prompt Editor scope.** Only `ai.prompt` steps are listed, in declaration order, keyed by position; only their `prompt` and `includeContext` arguments are edited. No step is added, deleted, reordered, renamed, or re-typed, and no id, policy, retry, generation option, or unrelated argument is changed. |
| S17-D11 | **Explicit context choice.** `includeContext` is an independent AI-prompt option, defaulted to `true` for a newly built `ai.prompt` step. It is never controlled by the generic Include optional arguments option. |
| S17-D12 | **No automatic context inference.** A direct prior-output placeholder may be used with `includeContext` false to send only selected data. When a direct output placeholder and `includeContext` true coexist, a duplication warning is shown but neither value is changed automatically. |
| S17-D13 | **Canonical LF prompts.** Workflow prompt working text uses LF line endings; CRLF and lone CR are normalized to LF. No other whitespace, wrapping, indentation, or trailing whitespace is changed. |
| S17-D14 | **Non-empty prompts everywhere.** An `ai.prompt` prompt that is missing, null, non-string, empty, or whitespace-only is invalid in Save, Validate, AI Helper validation, and Workflow Prompt Editor Apply. |
| S17-D15 | **Lossless multiline round trip.** Prompts are edited with real line breaks and stored with real line breaks; `System.Text.Json` writes them as `\n` escapes, so each prompt still occupies one physical JSON line. No lossy flattening. |
| S17-D16 | **Batch edit, single apply.** Several prompt/`includeContext` pairs may be edited in one session; Apply validates, rewrites them all at once and closes, Cancel discards them all, and closing with pending edits requires confirmation. This dialog has no Close command. |
| S17-D17 | **One shared one-level undo.** The Stage 13 editor snapshot becomes the single assisted-change snapshot of the AI Helper, the catalog, and the prompt editor, exposed as **Undo assisted change** and restoring workflow JSON, tool selections, and authorized locations atomically. |
| S17-D18 | **Canonical formatting is accepted.** Every successful structured insertion or prompt application re-serializes the array with `ScheduledTaskSerializer.Options`, normalizing manual formatting. |
| S17-D19 | **Active application theme.** Both dialogs follow the current Light or Dark application theme. They introduce no additional theme and no theme switcher. |
| S17-D20 | **Privacy.** Prompt text, argument values, workflow JSON, paths, selections, and dialog working copies are never logged, incident-reported, cached, or copied except through an explicit user Copy action. |
| S17-D21 | **No new packages.** Existing WPF, Material Design, and MVVM Toolkit capabilities are sufficient. |

### 20.2 Tool Map documentation metadata

`ToolDefinition` gains `Description` (one English sentence covering behavior and output) and an
ordered `ArgumentDefinitions` collection of
`ToolArgumentDefinition(Name, Kind, IsRequired, Description)` with optional `Example`,
`AllowedValues`, and `SupportsPlaceholders`. `Kind` is `String`, `Integer`, `Number`, `Boolean`,
or `Enumeration`. `Arguments` and `OptionalArguments` become projections of the new metadata and
keep their existing values, so `ToolRegistry`, the workflow engine, the validator, and the
Stage 13 analyzer are unaffected. The §7.7 Tool Map reflection test additionally enforces
descriptions, one argument definition per real parameter, kind/CLR-type compatibility, required
flags, and enumeration values equal to the parameter type's names. The assistant generation
prompt is rebuilt from this metadata and its version is incremented.

### 20.3 Snippet contract and insertion algorithm

A snippet is one object-form `WorkflowStepDefinition` with the exact Tool Map alias, an id built
by the existing uniqueness algorithm, required arguments always present and optional arguments
only on request, in Tool Map order. Placeholder values are `""`, `0`, `false`, or the first
accepted enumeration value; `ai.prompt` keeps the editor's default prompt sentence and
`includeContext` default. An optional error-policy block carries the task's current policy with
`retryCount` 1 and `retryBackoffSeconds` 2; optional `generationOptions` apply to AI steps only
and never contain context-window properties (§17).

Insertion maps the caret character offset to a byte offset over the array's top-level element
spans, computed with `Utf8JsonReader`. The caret is first clamped to the text length and moved
off a UTF-16 surrogate pair boundary. A caret inside an element inserts after that element; a
caret between two elements inserts after the element on the left; index 0 applies before the
first element and an append applies after the last. Insertion returns the canonical text and a
caret index at the start of the inserted element. The fallback path returns the verbatim
canonical snippet inserted at the clamped caret with the caret line's indentation and a warning
that separators must be fixed manually.

### 20.4 Prompt round trip

Extraction returns the ordered `ai.prompt` entries with position, explicit id, effective id,
decoded prompt text normalized to LF, `includeContext`, the effective ids declared before the
step, and display metadata including the repair-case and line-ending-normalization flags.
Application replaces only the `prompt` and `includeContext` arguments of the entries edited,
keyed by position so duplicate ids are unambiguous, promotes a string-form or `prompt`-less step
to object form, preserves every unrelated argument, and re-serializes canonically.
Extract → apply → extract without an edit is lossless. Empty or whitespace-only prompts and
placeholders rejected by the shared validation block Apply with an error naming the step
position and id. A prompt that already references prior output or accumulated context while
`includeContext` is true raises a non-blocking duplication warning; neither value is changed
automatically.

### 20.5 Required verification

Automated coverage includes Tool Map metadata consistency and projection stability, assistant
prompt-version increment, snippet building for every argument shape, structured insertion at
every array position including empty arrays and in-string carets, fallback insertion on invalid
JSON, catalog session accumulate/cancel semantics, tool-group opt-in and Safety read-only
behavior, the non-whitelistable `ai` group, prompt extraction ordering and decoding, LF
normalization, multiline round-trip losslessness, single-physical-line output, targeted-only
rewriting, blocked Apply cases including whitespace-only prompts, the non-blocking duplication
warning, single-line preview projection including grapheme-cluster safety, command enablement,
shared snapshot and atomic undo, and the absence of any provider, tool, filesystem, or network
call. The final gate remains a warning-clean Release build and the deterministic test run with
`-m:1`, plus `git diff --check`. Manual checks that remain the operator's responsibility:
insertion at the caret, invalid-JSON fallback, tool-group opt-in, a multiline prompt edited,
applied, and reopened unchanged, and both dialogs inspected in the Light and Dark themes.

## 21. Stage 18 addendum — Compact external-content Guardrails

This addendum supersedes §12 D5, D7, D9, D10 and §§12.4–12.5 only for Gmail reads,
`http.get`, and `http.getText`. The legacy complete-source reducer remains available to direct
callers and Trust filter keeps its singleton behavior. Web Search, `http.post`, Dropbox,
filesystem, Calendar, Gmail attachments, outbound content, authorization, and Safety behavior
are unchanged.

### 21.1 Locked product decisions

| # | Decision |
|---|---|
| S18-D1 | **Configured LLM only.** Guardrails continues to use the active `ILlmClientFactory` provider and model. No guard-specific model, runtime, conversion, download, provider override, lifecycle management, model UI, or new package is introduced. |
| S18-D2 | **Minimize before LLM.** Gmail and HTTP create bounded deterministic projections before any LLM call. Omitted text is not screened because it is never sent to any later LLM, agent, tool result, workflow context, prompt, or report. Every omission is explicit in metadata or a marker. |
| S18-D3 | **Guard before summary.** Compact source text is screened first. Sources below `CautionThreshold`, or with disabled/failed screening, are never summarized. |
| S18-D4 | **Tool-less isolation.** Screening and summarization are direct, fixed-policy `IChatClient` calls without tools, skills, agent loop, workflow context, memory, filesystem, or side effects. |
| S18-D5 | **Bounded batches.** Independent items from one tool invocation may share a request while retaining opaque ids and separate nonce-delimited payloads/results. Batches never cross tasks, users, invocations, or phases; no background queue exists. |
| S18-D6 | **Fail closed per batch.** Timeout, provider failure, malformed/incomplete response, id mismatch, or snapshot change fails only that batch phase, reports one sanitized incident, skips singleton replay, and continues later batches. Caller cancellation propagates immediately. |
| S18-D7 | **Gmail summary-only delivery.** Success delivers bounded canonical headers plus a labeled protected factual summary; Snippet, Body, quoted history, and omitted suffixes never enter workflow context. |
| S18-D8 | **Stable batching replaces sequential Gmail.** Retrieval/output order stays stable, but Gmail LLM work is batched and no longer completes all calls for message N before message N+1. |
| S18-D9 | **Distinct HTTP contracts.** `http.getText` delivers a protected summary of readable compact text. `http.get` delivers one protected media-aware compact raw prefix without summarization. |
| S18-D10 | **No recursive Gmail/HTTP reduction.** Gmail reads and both HTTP GET aliases no longer call the Stage 7 complete-source reducer; deterministic projection is their only size-reduction path. |
| S18-D11 | **Per-item trust.** Delivered-summary trust is the minimum compact-source/final-summary score. Thresholds, encoding, withholding, indicator union, and cache identity remain per item, never per batch. |
| S18-D12 | **Compatible shapes, no duplication.** Tool aliases, arguments, root shapes, limits, authorization, and established trust fields remain; projection metadata is additive and raw content fields are not reintroduced. |
| S18-D13 | **Existing cache and snapshot model.** The per-item 24-hour in-memory cache remains. One provider/model snapshot covers a top-level batch operation; no persistent cache is added. |
| S18-D14 | **No speculative scope.** Stage 18 adds only the compact pipeline described here. No dormant feature, generic plug-in system, unrelated extension point, setting, task field, workflow option, Tool Map alias, page, or migration belongs to it. |

### 21.2 Batch processing contract

`IContentTrustService` gains one ordered batch operation over compact items with unique opaque ids,
content no longer than `MaxContentChars`, and a per-item maximum summary size. Existing singleton
operations remain compatible. Inputs are greedily partitioned without reordering at 12 items or
six times `MaxContentChars`, whichever boundary comes first.

Each uncached batch performs at most one source-screen call, one eligible-only summary call, and
one final-summary-screen call. Empty phases make no call. Dedicated versioned batch screener and
summarizer templates use per-item nonce delimiters and strict JSON arrays; every expected id
must occur exactly once and unknown, duplicate, or missing ids invalidate the phase. Singleton
prompt schemas remain separate. Results are restored to caller order. Fixed internal
temperature/top-p, provider-neutral APIs, prompt-versioning, no-tool rules, and per-item cache
keys remain binding. Summary calls use a program-owned output-token bound sized for their JSON
envelope and requested per-item maxima, independent of workflow/report output ceilings; a
truncated or overlong response fails closed and no partial result is delivered.

A failed source phase conservatively fails screening for all current-batch items and summarizes
none. A failed summary or final-screen phase delivers no generated text. The batch result
returns one content-free failure descriptor for the Gmail/HTTP caller to report exactly once;
the sanitized incident contains phase, provider/model, item count, timing, and correlation id
only. Later batches continue; there is no singleton retry cascade. Cancellation stops
immediately without incident or fallback generation.

### 21.3 Gmail projection and delivery

Guardrails validation now requires `MaxContentChars >= 2_048`; its shipped default remains 8,000.
Every Gmail read alias projects, before LLM use, canonical `From`, `To`, `Subject`, `Received`,
`Snippet`, and `Body` fields in that order. Normalize controls and line endings; cap From and To
at 512 characters each, Subject at 1,000, Snippet at 1,500, and Body to the remaining
`MaxContentChars` budget including labels/markers. Each omission has an application-authored
character-count marker. Conservatively remove quoted-history suffixes only at recognized
line-start Gmail/English/French reply boundaries; preserve the current signature and visible
facts. Attachments and links are not inspected.

Projection metadata contains original/projected/omitted counts and truncation/history flags,
never omitted text. Gmail processes projections through the batch contract with per-item summary
maximum `Math.Min(800, MaxSummaryChars)`. Success delivers protected bounded headers plus a labeled
summary and does not repeat Snippet or Body. Low trust, disabled/failed screening, summary
failure, or final-screen failure builds bounded projected headers plus a Gmail-review marker and
applies the existing source-trust delivery policy: low trust withholds the block and
disabled/failed screening marks it as Base64.
The existing root shape, limits, order, trust fields, and single protected `content` property
remain; projection metadata is additive. Progress is batch-oriented and content-free.

### 21.4 HTTP projections and delivery

`http.get` builds a media-aware prefix no longer than `MaxContentChars`: parseable JSON is
minified, textual XML/plain text is normalized, HTML uses readable extraction, and unsupported
media receives a marker. It screens once, never summarizes, and delivers the protected prefix in
`body` with original/projected/omitted counts.

`http.getText` removes non-content HTML nodes, prefers `main`, then `article`, then `body`, and
preserves useful block boundaries while decoding entities and compacting whitespace. Its exact
readable projection is capped at `MaxContentChars`, processed as a one-item batch with summary
maximum `Math.Min(2_000, MaxSummaryChars)`, and only the protected summary is delivered in `text`.
Failures deliver bounded source-specific review markers and continue the workflow. URL
validation, host allowlists, request caps, response metadata, and Safety behavior are unchanged.

### 21.5 Observability, privacy, and verification

Sanitized metrics may include alias, provider/model, aggregate item/call/cache counts, phase and
total timings, and aggregate original/projected/omitted character counts. They never include
external ids, addresses, subjects, URLs with queries, source/projection/summary text, model
output, nonces, or hashes. Runner final protected output remains governed by the Stage 16 private
diagnostic policy.

Automated coverage includes deterministic Gmail/HTTP projection and bounds, quoted-history and
HTML rules, stable batch partitioning/order/id correlation, eligible-only phases, effective
per-item trust, cache behavior independent of batches, malformed/timeout/snapshot failures,
cancellation, sanitized incidents/logs, unchanged out-of-scope callers, and exact call counts:
1–12 fitting eligible Gmail items use three calls, 13 use six, all-low-trust batches omit summary
phases, and fully cached input uses zero. The final gate remains a warning-clean Release build
and deterministic tests with `-m:1`, plus `git diff --check`.

## 21A. Stage 18A addendum — Extensible tool integration foundation

This addendum refactors catalog integration after Stage 18 and before Stage 19. It adds no tool,
group, alias, package, setting, persisted field, migration, permission, page, or runtime behavior.
The standalone `Docs/Prompts/18A-Stage18A-ExtensibleToolIntegration.md` prompt is the complete
implementation contract.

### 21A.1 Central group metadata

Core owns one ordered immutable definition for every tool group: stable persisted name, English
display name, English description, and task-whitelist eligibility. The existing six task groups
remain unchanged and the synthetic ai group remains authoring-only. `ToolGroups.TaskWhitelist` and
the case-insensitive `ToolGroups.All` derive from that metadata. Every Tool Map entry must resolve
to a catalog group and every whitelist group must own a non-AI Tool Map entry.

### 21A.2 Metadata-driven integration

Task editor renders a collection of group-selection items rather than one Boolean and XAML checkbox
per group. Load, save, helper Apply, catalog opt-in, and assisted Undo iterate that collection in
catalog order. Authorized locations visibility is derived from whether a selected group owns a Tool
Map entry with local-path metadata; group-name special cases are forbidden.

Workflow Tool catalog and Tool tester share the catalog display order. Task validation lists legal
groups from the whitelist projection, and AI Playground continues using the derived all-groups set
behind its explicit opt-in. Unknown and ai task groups still fail closed.

### 21A.3 Compatibility and verification

Existing task JSON, runtime tools, aliases, Safety, authorization, provider calls, settings, and
logging remain unchanged. This is compiled-product extensibility, not runtime plug-in loading.
Automated coverage asserts catalog uniqueness/order, bidirectional Tool Map completeness, dynamic
Task editor projection, local-path relevance, dirty/load/save/Apply/Undo behavior, authoring/tester
order, unknown-group rejection, and all prior whitelist regressions. The gate remains a warning-
clean Release build and deterministic tests with `-m:1`, documentation parity, and
`git diff --check`.

## 21C. Stage 18C addendum — File-system Recycle Bin and document conversions

This addendum extends R9, §7.3, §8.2–§8.3, §14, §20, and §21A with three aliases in the existing
`filesystem` group. The standalone
`Docs/Prompts/18C-Stage18C-FileSystemRecycleAndDocumentConversions.md` prompt is the complete
implementation contract.

### 21C.1 Aliases, authorization, and Safety

`filesystem.moveToBin(path)` accepts an existing file or directory, requires effective ReadWrite
authorization and independently enabled `Safety:AllowMoveToBin`, and uses the Windows Recycle Bin
without UI or permanent-delete fallback. The gate ships false and is persisted in Settings.

`filesystem.markdownToHtml(sourcePath, destinationPath?, theme?)` reads an authorized Markdown
file and writes an independently authorized UTF-8 HTML destination. Omission derives `.html`;
`theme` is `Dark` by default or `Light`. `filesystem.htmlToText(sourcePath, format?)` reads an
authorized HTML file and returns `Text` by default or principal `Markdown` structure. It performs
no implicit write.

### 21C.2 Deterministic inactive conversion

Conversion uses the existing Markdig and HtmlAgilityPack packages. Raw Markdown HTML is disabled.
Standalone HTML embeds restricted CSS and a CSP blocking scripts and remote resources. HTML
extraction removes active/non-content nodes, executes nothing, performs no network request, and
preserves headings, lists, emphasis, links, quotes, and code in Markdown mode. Both sources obey
`FileSystem:MaxReadBytes`.

Local conversion content remains outside Guardrails classification. Tool results and logs follow
existing compact/privacy rules; logs never contain source or generated document content.

### 21C.3 Stage 18A integration and verification

Tool Map descriptions, ordered arguments, enum values, optionality, Safety flags, and local-path
metadata drive Tool catalog, Tool tester, helper, whitelist, validation, and authorization
proposals. No parallel group/alias UI list is added. Automated coverage includes both authorization
layers, Safety defaults, fake recycle dispatch, conversion output/restrictions, settings
persistence, reflection metadata, catalog visibility, and regressions. The gate remains a warning-
clean Release build and deterministic tests with `-m:1`, EN/FR manual parity, sample validation,
and `git diff --check`.

## 21D. Stage 18D addendum — Declarative dynamic tooling

This addendum applies after Stage 18C and before Stage 19. It supersedes Stage 18A only where
Stage 18A retained handwritten invocable Tool Map entries, concrete runtime registration, and
per-type contextual factories. The standalone
`Docs/Prompts/18D-Stage18D-DeclarativeDynamicTooling.md` prompt is the complete implementation
contract. Stage 18D adds no tool, alias, group, package, setting, task field, permission, page,
provider call, or runtime behavior.

### 21D.1 Attributed built-in discovery

Concrete tool classes compiled into `OD.MyLocalAgent.Core` declare stable group metadata, display
order, task-whitelist eligibility, singleton/contextual activation, and an optional public service
contract through a class attribute. Public invocable methods and non-cancellation parameters
declare alias, description, argument kind, example, optionality, enumeration/placeholder behavior,
Safety reference, and local-path semantics through method/parameter attributes. Tool and argument
attributes provide the effective `DescriptionAttribute` text, so catalog and agent-schema
descriptions cannot diverge.

Production discovery inspects only the fixed Core assembly. It never enumerates loaded assemblies,
probes directories, accepts an assembly path, loads a DLL, or executes a plug-in manifest. Invalid
or conflicting metadata fails during bootstrap. Group names/orders, aliases, function names,
method signatures, parameter metadata, path references, Safety identifiers, and activation
contracts are validated before any target is instantiated.

### 21D.2 One immutable catalog and generic activation

Discovery builds one ordered immutable catalog shared by Core, workflows, Runner, validation,
authoring, Tool tester, AI Playground, and WPF. Existing `ToolMap`/`ToolGroups` public members may
remain only as read-only compatibility façades over that exact snapshot; they own no parallel
runtime group or invocable-alias list. The synthetic non-whitelist `ai` workflow contribution
remains explicit because it is not an invocable attributed tool.

`ToolRegistry` depends on the catalog and one generic target activator rather than concrete tools.
Singleton targets resolve from DI; contextual targets are created with the immutable
`ToolExecutionContext`. Whitelist validation precedes activation and an unselected group is never
constructed. Tool-specific packages, options, parsers, connector/secret configuration, Safety
persistence, and specialized Settings controls remain explicit and fail closed.

### 21D.3 Dynamic UI and residual boundary

Task whitelist selections, authorized-location visibility, Tool catalog, Tool tester, helper, and
Playground projections derive from the immutable catalog. Adding an attributed built-in tool must
not require editing `ToolGroups`, `ToolMap`, the `ToolRegistry` constructor/target dictionary,
per-type contextual factories, XAML, ViewModel group Booleans, load/save branches, or group-order
arrays. New groups remain unchecked and grant no path or Safety capability until explicitly
authorized.

This is compiled extensibility, not external-DLL discovery or a plug-in system. Stage 19 and Stage
22 consume the attributed contract for SQLite, archive, document, spreadsheet, and PDF tools while
retaining their explicit implementation-specific services/settings. Automated coverage proves
metadata equivalence, fixed-assembly discovery, fail-fast validation, singleton/context isolation,
no activation before whitelist checks, catalog equality across consumers, legacy task
compatibility, and absence of concrete generic wiring. The final gate remains warning-clean
Release build/tests with `-m:1`, documentation parity, and `git diff --check`.

## 22. Stage 19 addendum — SQLite Tool

This addendum extends §7.6, §8.1–§8.3, §14, §16, §20, and §21 with one local SQLite tool group.
The standalone `Docs/Prompts/19-Stage19-SQLiteTool.md` prompt is the complete implementation
contract. Stage 19 adds no general database abstraction, ORM, remote connector, caller-supplied
connection string, saved connection, or database session.

### 22.1 Locked product decisions

| # | Decision |
|---|---|
| S19-D1 | **Four aliases in one group.** Add `sqlite.listObjects`, `sqlite.describeTable`, `sqlite.query`, and `sqlite.execute` in the whitelist group `sqlite`, registered once in the Stage 18A central group catalog. |
| S19-D2 | **Microsoft provider only.** Pin `Microsoft.Data.Sqlite` 10.0.10 in the central package file and reference it from Core. No EF Core, Dapper, System.Data.SQLite, migration framework, or second SQLite bundle. |
| S19-D3 | **Paths plus managed read target.** Every alias accepts `databasePath`, resolved by the existing sandbox, except that `sqlite.query` may instead receive one Stage 23 `storageSpaceId`. Connection strings, URI filenames, memory databases, passwords, VFS selection, attached databases, extension loading, and encryption are out of scope. |
| S19-D4 | **Independent authorization.** Read aliases require ReadOnly access. `sqlite.execute` requires the `sqlite` group, ReadWrite path access, and global `Safety:AllowSqliteWrite=true`, shipped false. |
| S19-D5 | **One statement.** Query and execute each accept exactly one prepared statement. Query requires a result-producing native-read-only statement; execute requires a non-result-producing modifying statement and runs in one application-owned transaction. |
| S19-D6 | **Layered SQL confinement.** Native preparation/read-only classification is combined with token-aware denial of ATTACH/DETACH, PRAGMA, VACUUM, extension loading, caller transaction control, virtual-table creation, TEMP creation, and executable tails. Neither a prefix check nor `sqlite3_stmt_readonly` alone is sufficient. |
| S19-D7 | **Explicit creation.** Only `sqlite.execute(createIfMissing: true)` may create a missing database. It never creates the parent directory and cleans up only a new file exclusively owned by the failed call. |
| S19-D8 | **Private bounded calls.** Every invocation owns one private non-pooled connection. Writes enable foreign keys. The tool never changes journal mode or enables WAL. |
| S19-D9 | **JSON parameter object.** Named scalar parameters are a real JSON object. Add `Object` to `WorkflowArgumentKind`; snippets emit `{}` and nested string values retain recursive placeholder resolution. |
| S19-D10 | **Bounded ordered output.** Query returns ordered column metadata and array rows, preserving duplicate names. Row/object count, complete result characters, SQL characters, and BLOB bytes obey validated SQLite settings and truncate only at complete boundaries. |
| S19-D11 | **Saved task choice.** `ScheduledTask.SqliteGuardrailsEnabled` is an explicitly serialized boolean. Missing legacy input defaults true. `SQLite:GuardrailsEnabledByDefault` is copied into new tasks and used by taskless tools only; it never live-controls a saved task. |
| S19-D12 | **Screen, do not summarize.** When task SQLite Guardrails are enabled, list/describe/query serialize their complete bounded logical payload, split it into Guardrails-sized chunks, batch-screen every chunk, and apply one minimum-trust disposition to the complete payload. SQLite results never enter Guardrails summarization. |
| S19-D13 | **Visible opt-out and fail-closed opt-in.** A task may explicitly disable SQLite Guardrails and receive labeled unscreened plaintext. When protection is requested, disabled/failed screening exposes no raw structured payload; low trust withholds the whole payload and caution protects the whole payload. |
| S19-D14 | **Native cancellation.** Synchronous SQLite work runs away from the WPF dispatcher and caller cancellation uses `sqlite3_interrupt` through the open connection handle. `SqliteCommand.Cancel` and nominal async ADO.NET calls are not treated as effective cancellation. |
| S19-D15 | **One Settings section.** Settings groups Allow SQLite writes, new-task Guardrails default, default/max rows, result characters, BLOB bytes, SQL characters, and command timeout in one SQLite section. |
| S19-D16 | **Privacy.** SQL, parameter names/values, paths, schema identifiers, database values, BLOBs, protected payloads, Guardrails chunks, model output, and raw SQLite exception messages never enter logs, incidents, or telemetry. |

### 22.2 Configuration and task contract

`SQLiteOptions` binds section `SQLite` with shipped values:

```json
{
  "DefaultMaxRows": 100,
  "MaxRows": 1000,
  "MaxResultCharacters": 64000,
  "MaxBlobBytes": 4096,
  "MaxSqlCharacters": 16000,
  "CommandTimeoutSeconds": 30,
  "GuardrailsEnabledByDefault": true
}
```

Validation requires default rows within the configured maximum; maximum rows 1–10,000; result
characters 4,096–1,000,000; BLOB bytes 0–1,000,000; SQL characters 256–1,000,000; and command
timeout 1–300 seconds. `SafetyOptions.AllowSqliteWrite` is independent and false by default.
Every value is editable in the SQLite Settings section and saved through existing atomic user
settings persistence.

`ScheduledTask` adds `sqliteGuardrailsEnabled`, always written. New-task creation snapshots the
current SQLite default. Existing tasks load only their saved choice; a missing pre-Stage-19
property becomes true. `ToolExecutionContext.FromTask` carries the value immutably. Taskless Tool
tester and AI Playground calls use the current new-task default.

### 22.3 Tool Map

| Alias | Agent function | Ordered arguments | Optional | Path / Safety |
|---|---|---|---|---|
| `sqlite.listObjects` | `sqlite_list_objects` | `databasePath`, `objectType`, `namePattern`, `maxResults` | last three | ReadOnly |
| `sqlite.describeTable` | `sqlite_describe_table` | `databasePath`, `tableName` | — | ReadOnly |
| `sqlite.query` | `sqlite_query` | `databasePath`, `sql`, `parameters`, `maxRows`, `storageSpaceId` | `databasePath`, `parameters`, `maxRows`, `storageSpaceId` | ReadOnly path or managed space |
| `sqlite.execute` | `sqlite_execute` | `databasePath`, `sql`, `parameters`, `createIfMissing` | last two | ReadWrite / `Safety:AllowSqliteWrite` |

`SQLiteObjectType` values are `All`, `Table`, `View`, `Index`, and `Trigger`. Parameters accept
only named `$`, `:`, or `@` native parameters whose supplied JSON values are null, boolean,
Int64, finite real, or string. Arrays, nested objects, positional parameters, input BLOBs,
missing parameters, and unused parameters fail before execution.

The Stage 18A central group catalog inserts sqlite after filesystem and before http; task whitelist,
editor, tester, workflow catalog, validator, and playground projections derive it without parallel
ordering tables or SQLite-specific selection properties. Object snippets emit `{}`. Tool Map
metadata continues to drive runtime registry, helper prompts, authoring, whitelist proposals,
local-path proposals, and Safety warnings.

### 22.4 Results and Guardrails

Query logical data contains ordered `columns`, array `rows`, delivered/effective counts,
`hasMoreRows`, and explicit truncation. SQLite NULL/INTEGER/REAL/TEXT map directly to JSON. A BLOB
within the configured maximum is Base64 in a typed object; a larger BLOB is wholly omitted with
its byte count. Do not infer dates, booleans, JSON, GUIDs, or encodings from SQLite affinity.

The complete outer read envelope is bounded by `MaxResultCharacters` and contains exactly one of
structured `data` or string `protectedData`. When protection is effective, row/object admission
reserves worst-case marked Base64 expansion so a caution/failure disposition cannot overflow the
limit. Explicit task opt-out returns structured data with
`screened=false`. Enabled protection chunks the complete compact logical data at Unicode scalar
boundaries no longer than Guardrails `MaxContentChars` and uses a new screen-only batch operation
that reuses Stage 18 id correlation, provider/model snapshot, cache, partition, failure, and
cancellation semantics without summary phases. All clean chunks reconstruct structured data;
any caution protects the complete data; low trust withholds it; disabled/failed screening fails
closed. A program-owned 36-chunk ceiling reduces the logical admission budget at row/object
boundaries when necessary and bounds a read to six full-size screening provider calls. Execute
returns application-authored acknowledgement metadata and is not screened.

### 22.5 Runtime and verification

`SqliteTool` is context-bound through `IContextualToolFactory<SqliteTool>`. Each call builds its
connection string only from the resolved sandbox path, uses private cache and no pooling, and
closes every command/reader/transaction/connection/native handle. Write calls enable foreign
keys and commit once. Busy timeout is not described as a CPU deadline. Caller/task cancellation
interrupts the native connection and rolls back writes.

Task editor derives the SQLite group and local-path visibility from Stage 18A metadata, adds only
the task Guardrails checkbox as SQLite-specific state, and must not add a `UseSQLite` property or
dedicated XAML group checkbox. Settings adds the complete SQLite section. Tool tester, AI Playground,
Tool catalog, helper generation/explanation, validation, manuals, samples, and group counts are
updated from shared metadata. The helper generation prompt advances from
`workflow-generation-v2` to `workflow-generation-v3`.

Automated coverage includes package/native publish resolution, Tool Map reflection, settings and
task defaults/round trips, authorization and create races, statement-token/native classification,
every forbidden construct, scalar binding, ordered/type-safe bounded output, BLOB omission,
Guardrails chunking/dispositions/call counts, native interruption, rollback, concurrency, and
privacy canaries. The gate runs restore, Release build/test, UI and Runner publish, JSON/manual
validation, native published-output smoke tests, and `git diff --check`, with `-m:1` on every
dotnet command.

## 23. Stage 20 addendum — Agent prompt placeholders

This addendum applies only to task-owned prompt execution without a workflow. It does not
depend on the Stage 18 compact Guardrails pipeline or the Stage 19 SQLite tool, and it changes
neither workflow placeholder resolution nor any taskless provider surface.

### 23.1 Scope and supported values

Scheduled/CLI `run`, UI **Run now** through that Runner path, CLI `test-prompt`, and Prompt
editor **Test the prompt** resolve exactly `Today`, `Now`, `task.id`, and `task.name` before
the agent receives the prompt. `Today` and `Now` accept the same invariant custom .NET formats
and filename aliases as workflows. One `TimeProvider.GetLocalNow()` snapshot covers every
date/time occurrence in one expansion. Save and schedule registration retain the original
template; replacement is single-pass and never scans inserted task identity text.

### 23.2 Preservation, warnings, and privacy

Unknown markers, workflow-only markers, invalid date/time formats, and malformed double braces
remain character-for-character unchanged and do not fail or skip agent execution. The resolver
returns ordered content-free source-span descriptors classified as `Unknown`, `WorkflowOnly`,
`InvalidDateTimeFormat`, or `MalformedBraces`. Runner and Markdown reports carry one aggregate
warning with the stable code `AGENT_PROMPT_PLACEHOLDERS_UNRESOLVED`; success remains success and
the report index remains `Success|Failure` only.

Durable warnings contain program-owned codes, categories, and counts only. They never contain
the original or resolved prompt, raw marker bodies, source spans, task names, replacement values,
or expanded paths, and they never create incidents. Prompt editor retains its aggregate warning
in memory until edit, reload, or another test, independently of streamed model output.

### 23.3 Shared date contract and unchanged surfaces

Core owns the single date/time parser, alias normalization, validation, and invariant formatter
used by both the workflow resolver and the prompt-only resolver. Workflow markers remain strict,
resolve only in workflow step strings with declaration-order context, and retain their established
failure policy. A task with a non-empty workflow never pre-expands its saved prompt. AI Playground,
Tool tester, AI Workflow Helper/Explain, Guardrails, skills, workflow `ai.prompt`, and health calls
remain outside prompt-only expansion. Placeholder expansion grants no tool group, filesystem
authorization, Safety permission, connector access, Windows right, or model/tool success.

## 24. Stage 21 addendum — Model information placeholders

This addendum supersedes Stage 20 only where taskless and workflow surfaces were previously
excluded from model-information expansion. Date/task placeholders, unresolved-marker warnings,
workflow output semantics, authorization, and every other Stage 20 decision remain unchanged.
The standalone `Docs/Prompts/21-Stage21-ModelInformationPlaceholders.md` prompt is the complete
implementation contract.

### 24.1 Exact values and effective policy

Every user-authored execution surface accepts `ModelName`, `ModelSettings`, `ModelContextSize`,
and `ModelInfo` inside double braces. Matching is case-insensitive and permits surrounding
whitespace. Given model `qwen3:14b`, effective Temperature `0.25`, effective Top P `0.9`, and
declared context `32768`, exact values are `qwen3:14b`, `Temp 0.25 - TopP 0.9`, `32768`, and
`ModelName qwen3:14b - Temp 0.25 - TopP 0.9 - Context 32768` respectively. Formatting is
invariant, single-pass, and non-recursive.

Temperature and Top P are the immutable values applied to that call: workflow step over task
over Settings, task over Settings, or Settings for taskless/playground calls. Context is the
configured declared window, not token usage, remaining capacity, output budget, or provider
telemetry. Model name is the exact configured identifier for the selected provider; resolution
performs no health, network, catalog, tool, or extra model call.

### 24.2 Runtime surfaces and architecture

Prompt-only scheduled/CLI execution, UI Run now, agentic tasks, Prompt editor Test, workflow
string arguments and AI prompts/skills, and AI Playground raw/agent modes resolve the four
markers before dispatch. The shared agent boundary resolves after provider selection as a safety
net; direct raw-provider playground generation resolves explicitly. Internal system prompts,
Guardrails schemas, responses, tool outputs, and connector payloads are not rescanned.

Core owns one immutable model-placeholder context and one pure resolver that preserves unrelated
markers. `ILlmClientService.ModelName` exposes the current monitored provider model.
`WorkflowPlaceholderCatalog` advertises the four forms and validates them everywhere; each AI
step uses its step-effective policy and each non-AI step uses the task-effective policy. Model
markers are valid in Stage 20 prompt resolution and never produce unresolved warnings.

### 24.3 Task-log header, privacy, and verification

The first visible line of every retained task run log is the task-effective `ModelInfo` value;
the structured JSONL start record remains physically first. The header appears once per task run,
before progress/output, and contains only model name, Temperature, Top P, and declared context.
Workflow steps may expose different step overrides through their own placeholder value; no
per-step log header is added.

Stage 21 adds no prompt, task name, tool argument/result, path, SQL, external content, endpoint,
key, model response, or exception payload to logs/incidents. It adds no setting, task field,
migration, provider, package, model call, tool, skill, page, or authorization. Automated coverage
includes exact formatting, invariant/case-insensitive/non-recursive replacement, agent and direct
provider dispatch, workflow effective-policy resolution, authoring metadata, first-visible-log
ordering, reload/concurrency isolation, and all prior placeholder/generation/run-log regressions.
The final gate remains a warning-clean Release build and deterministic tests with `-m:1`, manual
parity validation, and `git diff --check`.

## 25. Stages 23–25 addendum — Research, publications, and human workflow editing

The standalone Stage 23, 24, and 25 documents are the complete implementation contracts for
their respective surfaces. They extend, rather than replace, the existing local-first, Guardrails,
immutable-execution-context, declarative-tool-catalog, task-authorization, privacy, and
warning-clean gate rules.

### 25.1 Storage spaces and Stage 23 research

Stage 23 adds application-owned named storage spaces, a built-in `default` space, and optional
`ScheduledTask.storageSpaceId`. An omitted value resolves to `default`; a supplied id must exist
when the task is validated and is carried in an immutable execution snapshot. Storage spaces are
not task-supplied database paths. Their SQLite files are app-managed, schema-owned, and reserved
from ordinary filesystem grants. `Microsoft.Data.Sqlite` remains centrally pinned at `10.0.10`.

Research profiles compile persisted themes to deterministic query templates. The discovery window
is 2, then 4, then 7 days only when the verified, unique, in-window document count remains below
the profile threshold. Unknown-date documents never satisfy that threshold. The system stores
compact document metadata and a bounded short summary, never the full external article/post body.
Research records have bounded age retention; user publication records do not.

### 25.2 Stage 19 bridge and Stage 24 publications

Stage 19 `sqlite.query` accepts exactly one of `databasePath` or `storageSpaceId` when Stage 23
is installed. Managed-storage target mode is always read-only, resolves no caller-visible path,
and retains every Stage 19 statement-confinement, result-bound, Guardrails and privacy rule.
`sqlite.execute`, schema inspection, and arbitrary managed writes stay path-only or unavailable
as specified by Stage 19. Managed table names and columns are documented stable contracts in this
product version; do not create SQL views merely to conceal an otherwise fixed schema.

Stage 24 stores user-owned `PublicationRecord` metadata in the selected storage space and exports
`PublicationReport` Markdown. Publication data is never automatically purged; `periodKey` is a
user-selected grouping value, not a primary key and not necessarily a calendar year. No record
stores the full body of a social post or article.

### 25.3 Stage 25 workflow authoring surface

Stage 25 retains JSON as the persisted, imported, exported and runtime workflow format, but makes
deterministic human-readable Workflow text the default Task editor surface. Its parser/renderer
must satisfy semantic JSON-to-text-to-JSON round trips. AI Helper may propose a validated workflow
from free user text; it is neither the parser nor the reverse renderer. AI Explain remains an
explanation feature. Raw **JSON edit (advanced)** remains available and no valid JSON feature is
removed for users who need it. Tool argument editor metadata drives multi-line Prompt, SQL and
Markdown editing without per-tool UI maps.
# Addendum Stage 22 — Archive, Office and PDF tools

Stage 22 adds four independently whitelisted contextual groups: `archive`, `document`,
`spreadsheet`, and `pdf`. Local paths remain separately sandboxed and authorized. Archive writes
and extraction additionally require `Safety:AllowArchiveWrite`; ZIP overwrite is explicit. DOCX,
XLS/XLSX/XLSB, and PDF operations are read-only and bounded by the validated `FileContent`
configuration. Extracted content is untrusted and must never be logged. No passwords, OCR,
Office automation, macro execution, formula recalculation, external fetch, or document writing is
provided.

# Addendum Stage 28 — Workflow step editor

The Task editor may structurally locate, insert, edit, and delete one top-level workflow step.
The operation is in-memory and canonical JSON remains the only persisted workflow format. Tool
metadata comes from the immutable Core catalog. An existing alias is immutable: a different tool
requires delete followed by insert. Delete warns for later valid output placeholders but never
repairs them or changes task permissions. Missing groups and capabilities are explicit opt-in
proposals; Save task remains the only persistence boundary.

# Addendum Stage 30 — Limited step-level `forEach`

Stage 30 adds optional top-level `forEach` only to object-form `ai.prompt` and
`filesystem.write` steps. It is deliberately not a workflow container: no nested workflow,
parallel map, item branch, item-specific route, rollback, or persisted child-run model is added.
The immutable declarative Tool Map advertises support through `SupportsForEach`; every other alias
rejects the property.

The source is exactly one `{{id.output...}}` placeholder selecting an array from an earlier step
that dominates the repeated step on every control-flow path. Surrounding text,
`previous.output`, task/date/model markers, and `foreach.*` markers are invalid as sources.
Within the repeated step arguments, `{{foreach.item}}`, structured item selectors, and the
one-based `{{foreach.position}}` are available; outside such a step they are invalid.

Execution is sequential and stable, with a hard maximum of 100 source items. Empty input succeeds
with `[]`; non-array and oversized inputs fail before item execution. Successful item results are
published atomically to workflow context as one ordered JSON array. `Retry` retries only the
current item. The first final item failure stops the batch and publishes no partial output;
`ContinueOnError` may continue the outer workflow but does not process remaining items. External
effects completed by prior iterations are not rolled back. A repeated `filesystem.write` with no
`foreach.*` marker in its path remains legal but the editor must warn that it will replace the
same file repeatedly.
