# MASTER PROMPT — Generate the "OD.MyLocalAgent" Application (WPF / .NET 10 / Microsoft Agent Framework / OllamaSharp)

> **Historical reference:** This original all-in-one specification is preserved for context.
> The staged specifications in the parent `Prompts` directory are authoritative for the current implementation.

> **How to use:** copy everything below the horizontal line and paste it as a single prompt into your code-generation agent (Codex, Copilot Workspace, etc.).

---

## 1. ROLE

You are a senior .NET engineer expert in WPF/MVVM, the Microsoft Agent Framework (`Microsoft.Agents.AI`), `Microsoft.Extensions.AI`, OllamaSharp and Windows desktop automation. You produce production-ready, compilable code only — never pseudocode, never skeletons, never placeholders.

## 2. MISSION

Generate the **complete source code** of **OD.MyLocalAgent**, a native Windows app (C# / .NET 10 / WPF) that executes **scheduled, AI-driven tasks locally**. Each task = a cron schedule + a prompt + a list of allowed **tools** (Gmail, Google Calendar, Dropbox, Windows file system, HTTP). Tasks are registered in **Windows Task Scheduler** and run headlessly via an internal CLI runner. The LLM runs on **Ollama** (model `gemma4:12b`, already installed). A modern **dark** WPF interface manages tasks, prompts, workflows, tools, logs and settings.

Deliverables: `OD.MyLocalAgent.sln`, 7 projects, unit tests, `appsettings.json` template, a detailed `README.md`.

## 3. PRODUCT RULES (non-negotiable)

1. **Local-first AI.** Default LLM provider = local Ollama, zero cloud AI calls out of the box. The Settings page (§6.6) may let the user explicitly switch to an **OpenAI-compatible** provider — opt-in only, labeled "cloud", never a silent fallback. Apart from that opt-in, the only outbound calls allowed are the user's own Gmail / Calendar / Dropbox APIs and explicit `HttpTool` requests.
2. **English only**: identifiers, XML documentation, inline comments, log messages, UI text.
3. **Complete, compilable files only.** Forbidden: `// TODO`, `…`, omitted code, references to files you do not emit.
4. **.NET 10 / C# 14** for every project, with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`; write warning-clean code.
5. **Engineering baseline.** Async + `CancellationToken` on every I/O method; DI (`Microsoft.Extensions.DependencyInjection` / `.Hosting`) with an interface per service; `HttpClient` only via `IHttpClientFactory`; configuration via the options pattern over `appsettings.json` (§7.2); secrets via DPAPI or user-secrets — never hard-coded, never logged; Serilog with a rolling-file sink under `%LOCALAPPDATA%\OD.MyLocalAgent\logs`, an observable in-memory sink for the UI, and a console sink in the runner.
6. **Dark-only UI.** Material Design dark theme applied globally to every control of every page (§6.6). No light theme, no theme switcher.
7. **Never crash.** Global handlers in the UI (`Application.DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, `AppDomain.CurrentDomain.UnhandledException`) plus a top-level `try/catch` in the runner's `Main`. Every incident is appended by `IIncidentLogService` (in `OD.MyLocalAgent.Core`) to `%LOCALAPPDATA%\OD.MyLocalAgent\logs\incidents.jsonl` — UTC timestamp, severity, source page or task id, message, full stack trace, correlation id. The user gets a non-blocking notification showing the correlation id, and the app returns to a safe state.
8. **Stack fidelity.** Use exactly the packages of §4 with their idiomatic patterns; if an API differs from what you remember, follow the listed package's conventions; **never invent package names**.

## 4. TECH STACK (exact packages)

| Concern | Package / approach |
|---|---|
| Framework | .NET 10, WPF, C# 14 |
| AI agent | `Microsoft.Agents.AI` on `Microsoft.Extensions.AI`: wrap the provider's `IChatClient` with `.AsAIAgent(name:, instructions:, tools:)`; register tools from C# methods via `AIFunctionFactory.Create(...)`; use `RunAsync` / `RunStreamingAsync` |
| Default LLM provider | `OllamaSharp` (`OllamaApiClient` implements `IChatClient`), model `gemma4:12b` (configurable) |
| Alternate LLM provider (opt-in) | Official `OpenAI` SDK, bridged with `.AsIChatClient()` |
| Gmail · Calendar | `Google.Apis.Gmail.v1` · `Google.Apis.Calendar.v3` — OAuth2 via `GoogleWebAuthorizationBroker`, tokens stored under `%LOCALAPPDATA%\OD.MyLocalAgent\Auth` |
| Dropbox | `Dropbox.Api` |
| Scheduler | Registration via `Microsoft.Win32.TaskScheduler`; cron parsing via `Cronos` |
| MVVM | `CommunityToolkit.Mvvm` — **source generators only** (rules in §6.6) |
| UI | `MaterialDesignThemes` (dark `BundledTheme` merged in `App.xaml`); icons via `MahApps.Metro.IconPacks` |
| HTTP / HTML | `Microsoft.Extensions.Http`; `HtmlAgilityPack` for HTML→text extraction |
| Runner CLI | `System.CommandLine` |
| Logging / config | `Serilog` (+ file, console sinks); `Microsoft.Extensions.Configuration.Json` |
| Tests | `xunit`, `Moq`, `FluentAssertions`, `coverlet.collector` |

## 5. SOLUTION STRUCTURE

```
OD.MyLocalAgent/
├── OD.MyLocalAgent.sln
├── README.md
├── appsettings.json                       (template, copied to UI and Runner output)
├── src/
│   ├── OD.MyLocalAgent.Ollama/        (net10.0)         — LLM client layer: Ollama (default) + OpenAI-compatible (opt-in)
│   ├── OD.MyLocalAgent.Connectors/    (net10.0)         — low-level Gmail, Calendar, Dropbox clients
│   ├── OD.MyLocalAgent.Core/          (net10.0)         — agent, tools, skills, plans, task store, scheduler, incidents
│   ├── OD.MyLocalAgent.Workflows/     (net10.0)         — workflow engine
│   ├── OD.MyLocalAgent.Runner/        (net10.0 console) — headless executor for Windows Task Scheduler
│   └── OD.MyLocalAgent.UI/            (net10.0-windows) — WPF app (MVVM)
└── tests/
    └── OD.MyLocalAgent.Tests/         (net10.0 xUnit)
```

Dependencies (no cycles): `Ollama → —` · `Connectors → —` · `Core → Ollama, Connectors` · `Workflows → Core` · `Runner → Workflows, Core` · `UI → Workflows, Core, Ollama` · `Tests → all`.

## 6. FUNCTIONAL SPECIFICATION (by project)

### 6.1 `OD.MyLocalAgent.Ollama` — LLM client layer

- `ILlmClientService` — provider-agnostic contract consumed by the agent:
  `Task<string> GenerateAsync(string prompt, CancellationToken ct)` · `IAsyncEnumerable<string> GenerateStreamingAsync(string prompt, CancellationToken ct)` (token-by-token streaming) · `Task<bool> IsAvailableAsync(CancellationToken ct)` · `IChatClient ChatClient { get; }` · `string ProviderName { get; }`.
- `ILlmClientFactory` — selects the implementation at runtime from `AI:Provider` (`"Ollama"` = default, `"OpenAI"` = opt-in); invalid configuration → typed `LlmConfigurationException`, recorded as an incident (rule 7).
- `OllamaClientService` (default) — wraps `OllamaApiClient`; health check = endpoint reachable **and** configured model pulled; typed errors `OllamaUnavailableException` / `OllamaModelMissingException`; timeout from config (default 120 s); structured logs.
- `OpenAIClientService` (opt-in) — official `OpenAI` SDK chat client via `.AsIChatClient()`; endpoint/model/timeout from Settings, OpenAI-compatible base URLs supported; API key read from the DPAPI-protected store (§6.6 Settings).
- DI: one extension `services.AddLlmProviders(configuration)` registers both providers + factory.

### 6.2 `OD.MyLocalAgent.Connectors` — low-level API clients

Thin testable wrappers (interface + implementation + DTOs + options classes). Google OAuth2: full authorization-code flow via `GoogleWebAuthorizationBroker`, persisted credentials, automatic refresh; client id/secret from configuration.

- `IGmailConnector` — `GetTodaysMessagesAsync`, `GetUnreadMessagesAsync`, `SendMessageAsync(to, subject, body)` → `EmailMessageDto` (id, from, to, subject, snippet, bodyPlain, receivedAt, isUnread).
- `ICalendarConnector` — `GetTodaysEventsAsync`, `CreateEventAsync(summary, start, end, description)` → `CalendarEventDto`.
- `IDropboxConnector` — `UploadAsync(localPath, dropboxPath)`, `DownloadAsync(dropboxPath, localPath)`, `ListAsync(folderPath)` → `DropboxItemDto`.

### 6.3 `OD.MyLocalAgent.Core` — agent, tools, skills, plans, tasks, scheduler, incidents

- **Agent `MyLocalAgentAI` (`IMyLocalAgentService`)** — takes the `IChatClient` of the active `ILlmClientService`, calls `.AsAIAgent(name: "MyLocalAgentAI", instructions: <system prompt>, tools: <AITool[]>)`. System prompt: the agent runs locally and unattended, must call tools to act, must end with a concise report, resolves dates against the machine clock. Members: `Task<AgentRunResult> RunPromptAsync(string prompt, IEnumerable<string> enabledTools, CancellationToken ct)` + a streaming variant (feeds the UI "Test the prompt" pane). A `ToolRegistry` maps tool names → `AITool` (built with `AIFunctionFactory.Create`) and enforces each task's `tools` whitelist.
- **Tools** (interface + class; `[Description]`-annotated methods; connector/services injected):
  - `FileSystemTool` — list directory, read text file (size-capped), move, delete, create directory. Paths validated and **sandboxed under `FileSystem:RootPath`**; escape attempts throw.
  - `GmailTool` / `CalendarTool` / `DropboxTool` — the operations of §6.2, delegated to the connectors.
  - `HttpTool` — GET, POST (body + content type), HTML→text extraction (strip tags/scripts/styles via `HtmlAgilityPack`).
  - Tool name constants: `gmail`, `calendar`, `dropbox`, `filesystem`, `http`. Tool results are compact JSON (sized for a 12B local model); every call is logged (name, sanitized arguments, duration, outcome).
- **Skills (`ISkill`)** — reusable named prompt templates rendered then executed by the agent. Ship three: `SummarizeTextSkill`, `ExtractActionItemsSkill`, `ClassifyEmailSkill`.
- **Plans** — a `Plan` is a named ordered list of step references per the §7.3 syntax; plans are what the workflow engine (§6.4) executes.
- **Incidents (`IIncidentLogService`)** — JSONL append/read/export/clear per rule 7.
- **Task store** — `ScheduledTask` record matching §7.1 exactly; `ITaskStore` / `JsonTaskStore`: CRUD, one JSON file per task in `%LOCALAPPDATA%\OD.MyLocalAgent\tasks\<id>.json`, file locking, schema validation on load, change notifications (`FileSystemWatcher`) for the UI.
- **Scheduler (`ITaskSchedulerService` / `WindowsTaskSchedulerService`)** — `RegisterAsync(task)`: parse `task.Schedule` with Cronos → register a **next-occurrence `TimeTrigger`** in folder `OD.MyLocalAgent\`, name `OD.MyLocalAgent.<id>`, action `OD.MyLocalAgent.Runner.exe run --task <id>` ("run whether user is logged on" per config). Also `UnregisterAsync(id)`, `IsRegistered(id)`, `GetNextRun(id)`. After every execution, the runner recomputes the next occurrence and re-registers it (rolling schedule — document this design in the README).

### 6.4 `OD.MyLocalAgent.Workflows` — workflow engine

- A `Workflow` is an ordered list of steps parsed from §7.3 syntax: `ToolStep` (tool + method + JSON args), `SkillStep`, `PromptStep` (free prompt to the agent).
- `IWorkflowEngine` / `SequentialWorkflowEngine` — runs steps in order; a `WorkflowContext` dictionary carries named outputs between steps (`{{stepId.output}}` placeholders resolved in later args/prompts).
- Error policy per step (default `StopOnError`; also `ContinueOnError`, `Retry(n, backoff)`); structured per-step logs (start, duration, status, output size). Returns a `WorkflowResult` (success flag, per-step results, final text). The runner persists it to `%LOCALAPPDATA%\OD.MyLocalAgent\reports\<id>\<timestamp>.md`; the UI displays it.
- If a task defines **both** `prompt` and `workflow`, the workflow wins and the prompt is exposed to steps as `{{task.prompt}}`.

### 6.5 `OD.MyLocalAgent.Runner` — headless CLI executor

.NET 10 console app (`System.CommandLine`), same DI setup as the UI via a shared `ServiceCollection` extension in `OD.MyLocalAgent.Core`, single-instance per task (named mutex). Exit codes: `0` success · `1` task failure · `2` configuration error · `3` task not found.

- `run --task <id>` — load task → build agent + whitelisted tools → execute workflow (or prompt) → write report → re-register next schedule (§6.3).
- `list` · `test-prompt --task <id>` · `test-tool --name <tool> --method <m> --args '<json>'`.

### 6.6 `OD.MyLocalAgent.UI` — WPF application (MVVM)

Shell window with left navigation rail; DI built in `App.xaml.cs`. Strict MVVM with **CommunityToolkit.Mvvm source generators only**:

- ViewModels = `partial` classes deriving from `ObservableObject` (or `[ObservableRecipient]` when messaging); bindable state = fields annotated `[ObservableProperty]`; actions = methods annotated `[RelayCommand]` (`Task` methods → generated `AsyncRelayCommand`, wired to busy indicators and cancellation).
- Cross-ViewModel messages (task saved → list refresh; execution finished → dashboard refresh) via `IMessenger` / `WeakReferenceMessenger.Default`.
- **Forbidden**: hand-written `INotifyPropertyChanged`/`PropertyChanged`, hand-written `RelayCommand`/`DelegateCommand`, any other MVVM framework. Code-behind limited to `InitializeComponent` and focus management.

**Pages** (UserControl + ViewModel each):

1. **Dashboard** — counts, next 5 runs, last 10 executions with status, provider health badge (active provider reachable? model available?), quick actions.
2. **Tasks** — `DataGrid` (name, cron, next run, last run + status, enabled toggle); New / Edit / Delete / **Run now** / Register–Unregister.
3. **Task editor** — id, name, cron with human-readable preview + next 3 occurrences, tool checkboxes, workflow editor; validation; save → JSON store + scheduler sync.
4. **Prompt editor** — task prompt editor; **Test the prompt** button with live streamed output, token counter, cancel.
5. **Logs** (live monitoring, read-only) — auto-scrolling view fed by the in-memory sink; filters (level, source, task).
6. **Tools configuration** — connector credentials only: Google OAuth status + **Connect** button, Dropbox token, per-tool connection test.
7. **Tool tester** — pick tool + method, JSON args box, run → pretty-printed result + duration.
8. **AI playground** — free prompt → streamed answer from the active provider; read-only badge (provider + model); health indicator.
9. **Settings** — three sections:
   - **AI provider** — radio `Ollama (local, default)` / `OpenAI-compatible (cloud, opt-in)` with a visible "cloud" warning on the latter; per-provider fields (endpoint, model, timeout); OpenAI **API key persisted via DPAPI** (`ProtectedData`) — never plain text, never logged; **Test connection** (health + latency); the switch applies to the agent **without restarting** and persists to the user settings file.
   - **General** — app-wide preferences: `FileSystem:RootPath`, HTTP defaults, scheduler options, Serilog minimum level, tasks/reports folders; validated settings object, Save button, dirty-state indicator.
   - **Diagnostics** — application log management (the page-5 view plus: **Save to disk…** exports the buffer to a user-chosen `.log` file; **Reset** clears the buffer and, after confirmation, truncates/archives the rolling log files) and the **incident list** from `IIncidentLogService` (expandable stack traces, Copy details, Export…, Clear all with confirmation).

**Style (see rule 6):** dark `BundledTheme` merged once in `App.xaml`; **one dark palette** defined as resources in a dedicated dictionary (background, surface, card, primary, secondary, error, on-surface/on-background text, dividers); every custom `Style`/`ControlTemplate` derives from it. **Every stock control renders dark** — `DataGrid` (headers, rows, selection, alternating colors), `TextBox`, `ComboBox` + popup, `PasswordBox`, `ListBox`/`ListView`, `TreeView`, scrollbars, `Menu`, `ContextMenu`, `ToolBar`, `TabControl`, `GroupBox`, `Expander`, checkboxes/radios, `Slider`, `DatePicker`, `ToolTip`, status bars, dialogs. Fluent/Material icons, card layout, busy indicators, snackbars.

### 6.7 `OD.MyLocalAgent.Tests` — unit tests (xUnit + Moq + FluentAssertions)

- `FileSystemTool` — all operations + sandbox-escape rejection (temp directory).
- `OllamaClientService` — happy path, timeout, endpoint down, streaming concatenation (mocked HTTP layer).
- `GmailTool` / `CalendarTool` / `DropboxTool` — mocked connectors; argument mapping and DTO JSON shape.
- `WorkflowEngine` — order, `{{…}}` resolution, `StopOnError` vs `ContinueOnError` vs `Retry`, workflow-beats-prompt rule.
- `JsonTaskStore` — CRUD round-trip, schema validation failure.
- Cron → next-occurrence translation (pure function extracted from the scheduler).
- `ToolRegistry` — whitelist filtering (stub `IChatClient`).
- `LlmClientFactory` — provider selection; invalid config → `LlmConfigurationException` + incident.
- `IncidentLogService` — append / read-back / export / clear; JSONL survives restarts.
- Global exception handlers — a simulated dispatcher exception and a faulted background task are captured, persisted, and marked handled.

## 7. DATA CONTRACTS

### 7.1 Scheduled task JSON (one file per task) — implement this shape exactly

```json
{
  "id": "daily-mail-summary",
  "name": "Daily mail summary",
  "schedule": "0 7 * * *",
  "prompt": "Summarize today's emails in French, 10 bullet points max.",
  "tools": ["gmail", "filesystem"],
  "workflow": ["gmail.readToday", "ai.summarize", "filesystem.writeReport"],
  "enabled": true,
  "errorPolicy": "StopOnError"
}
```

### 7.2 `appsettings.json` template to ship (safe defaults, no real secrets)

```json
{
  "AI": { "Provider": "Ollama" },
  "Ollama": { "Endpoint": "http://localhost:11434", "Model": "gemma4:12b", "TimeoutSeconds": 120 },
  "OpenAI": { "Endpoint": "https://api.openai.com/v1", "Model": "gpt-4o-mini", "TimeoutSeconds": 120 },
  "Google": { "ClientId": "YOUR_CLIENT_ID", "ClientSecret": "YOUR_CLIENT_SECRET" },
  "Dropbox": { "AccessToken": "", "RefreshToken": "", "AppKey": "" },
  "FileSystem": { "RootPath": "C:\\OD.MyLocalAgent" },
  "Http": { "TimeoutSeconds": 30, "UserAgent": "OD.MyLocalAgent/1.0" },
  "Scheduler": { "TaskFolder": "OD.MyLocalAgent", "RunWhenLoggedOff": false },
  "Logging": { "MinimumLevel": "Information" }
}
```

### 7.3 Workflow step syntax

Dotted form `tool.method` / `ai.<skillName>`; object form `{ "step": "filesystem.writeReport", "args": { "path": "...", "content": "{{gmail.readToday.output}}" } }`. Document both in the README with two full examples (mail summary → report file; unread mails → calendar event → Dropbox backup).

## 8. CODING STANDARDS

- File-scoped namespaces; one public type per file; `sealed` where appropriate; records for DTOs; `ArgumentNullException.ThrowIfNull`.
- XML documentation on **every** public type and member; inline comments only for non-obvious logic.
- No magic strings: constants/options classes for tool names, paths, config keys.
- Validation at boundaries: cron string, task id = `[a-z0-9-]+`, email addresses, file paths.
- No `async void` except UI event handlers; dispose correctly.

## 9. OUTPUT FORMAT (how YOU must answer)

1. Start with the **complete file tree**.
2. Then emit **every file's full content**, one file per fenced code block, each preceded by a header `### <relative/path>`.
3. Order: `OD.MyLocalAgent.sln` → all `.csproj` → `OD.MyLocalAgent.Ollama` → `OD.MyLocalAgent.Connectors` → `OD.MyLocalAgent.Core` → `OD.MyLocalAgent.Workflows` → `OD.MyLocalAgent.Runner` → `OD.MyLocalAgent.UI` → `OD.MyLocalAgent.Tests` → `appsettings.json` → `README.md`.
4. On hitting the output-length limit: **finish the current file completely**, stop at that file boundary, end your message with the single token `CONTINUE`. On my next "continue", resume at the next file. Never truncate a file, never summarize or skip the remaining files.
5. README contents: prerequisites (Windows 10/11, .NET 10 SDK, Ollama + `ollama pull gemma4:12b`), Google Cloud OAuth setup, Dropbox app setup, build/run, Task Scheduler registration, runner CLI reference, workflow syntax, architecture diagram (ASCII or Mermaid), troubleshooting, extension points (add a tool / skill / plan).

## 10. ACCEPTANCE CRITERIA (self-check before finishing)

- [ ] `dotnet build OD.MyLocalAgent.sln` succeeds with the packages of §4.
- [ ] `dotnet test` passes (all §6.7 tests).
- [ ] `OD.MyLocalAgent.Runner.exe run --task daily-mail-summary` runs end-to-end with the §7.1 task file.
- [ ] UI starts in **dark theme**; all 9 pages navigable; every stock control renders dark — zero light-default artifacts.
- [ ] Zero placeholders; every public API documented in English.
- [ ] All ViewModels use MVVM Toolkit source generators; no manual INPC or command classes.
- [ ] Settings persists the provider choice (Ollama ↔ OpenAI-compatible), applies it without restart; Test connection reports real health.
- [ ] Settings → Diagnostics: Save to disk writes a complete `.log` file; Reset clears the view and archives/truncates log files.
- [ ] Exception drill: one exception from a UI command **and** one from a background task are both caught, written to the incident log with correlation ids, shown as non-blocking notifications — the app keeps running.

**Begin now with step 9.1: the complete file tree.**
