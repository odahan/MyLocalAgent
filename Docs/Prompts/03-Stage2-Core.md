# STAGE 2 — Core: incidents, config, task store, scheduler, Tool Map, tools, skills, agent

> Run only after Stage 1's gate is green.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read; **§7.3, §8 (all), §9** are your
   scope.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; public APIs from Stage 1 are binding.
3. The current file tree.

## 2. Mission

Implement `OD.MyLocalAgent.Core` end-to-end: the heart of the product — incident pipeline,
layered configuration with DPAPI secrets, task store, scheduler, the normative **Tool Map**
with all five tools, the three skills, and the agent service — plus the unit tests of spec
§7.7 that belong to Core.

## 3. Critical constraints (restated)

- Dependencies: Core → Ollama + Connectors only. Nothing in Core references Workflows/UI.
- Incident entry point is `IIncidentReporter` **only**; `AppExceptionHandlers.Handle` is the
  thin, directly-testable adapter (spec R7). Tests invoke it directly — never wait on CLR
  events.
- The **Tool Map (spec §8.3) is a single static definition** consumed by `ToolRegistry` (agent
  names) and, in Stage 3, by the workflow engine (aliases). Do not scatter alias strings.
- Safety gates (spec R9): `filesystem.delete`, `gmail.send`, `http.post` must throw
  `SafetyException` when their `Safety` flag is off; `Http:AllowedHosts` enforced when
  non-empty; file paths sandboxed under `FileSystem:RootPath`.
- Inject `TimeProvider` everywhere wall-clock time or delays are needed.
- Warning-clean, XML docs everywhere, complete files only, English only.

## 4. Scope (in scope = everything below; out of scope = Workflows/Runner/UI projects)

### 4.1 Foundations

- `ScheduledTask` record + validation exactly per spec §8.1/§8.3 (including workflow parsing
  into an internal step model reusable by Stage 3 — string form and object form with
  `id`, `args`, `onError`, `retryCount`, `retryBackoffSeconds`).
- Typed exceptions: `SafetyException`, `PathSandboxException`, `TaskValidationException`,
  `WorkflowValidationException`, plus the Stage-1 types already available.
- `SafetyOptions`, `FileSystemOptions`, `HttpOptions`, `SchedulerOptions`, `LoggingOptions`
  bound per spec §8.2/§8.5.

### 4.2 Incident pipeline

- `IIncidentLogService` / `JsonlIncidentLogService` at
  `%LOCALAPPDATA%\OD.MyLocalAgent\logs\incidents.jsonl` (append/read/export/clear; file-safe
  append across processes).
- `IIncidentReporter` / `IncidentReporter`: builds the incident record (UTC timestamp,
  severity, source, message, stack, correlation id), appends, raises a notification event.
- Static `AppExceptionHandlers`: `Initialize(IIncidentReporter)` + `Handle(Exception, string
  source)` returning the correlation id; wiring helpers for the three CLR events and a
  runner top-level hook (used in Stages 3–4).

### 4.3 Configuration & secrets

- `ISecretStore` / `DpapiSecretStore` (spec §7.3): `ProtectedData` `CurrentUser` JSON store at
  `%LOCALAPPDATA%\OD.MyLocalAgent\secrets.dat`; `Get`, `Set`, `Remove`, `Exists` (never expose
  values in logs).
- `AddODServices(IHostApplicationBuilder)` (or equivalent) implementing spec §8.5 layering:
  `appsettings.json` at `AppContext.BaseDirectory` → user `settings.json` (`reloadOnChange`)
  → in-memory secret layer; options with validation; Serilog per spec R5 (rolling file +
  in-memory observable sink consumed later by the UI; console sink parameter for the runner);
  registration of every Core service.

### 4.4 Task store & scheduler

- `ITaskStore` / `JsonTaskStore` per spec §7.3 (one file per task, lock, validate on load,
  `FileSystemWatcher` change notifications).
- `ITaskSchedulerService` / `WindowsTaskSchedulerService` per spec §7.3: Cronos with
  `TimeZoneInfo.Local` + injected `TimeProvider`; `StartWhenAvailable = true`; logon type per
  `Scheduler:RunWhenLoggedOff`; runner path resolution per spec; `RegisterAsync`,
  `UnregisterAsync`, `IsRegistered`, `GetNextRun` (store + Cronos), plus
  `ResyncAllAsync` (the logic the runner command and UI startup will share).
- **Public pure function** `CronSchedule.NextOccurrence(string cron, DateTimeOffset from,
  TimeZoneInfo tz)` for tests.

### 4.5 Tool Map, tools, skills, agent

- Static `ToolMap` (spec §8.3 table, verbatim): alias ↔ agent function name ↔ declaring
  method ↔ arg names ↔ safety gate (if any).
- `FileSystemTool`, `GmailTool`, `CalendarTool`, `DropboxTool`, `HttpTool` per spec §7.3 with
  exactly the Tool Map method names/args; compact JSON results; per-call structured logging
  (name, sanitized args, duration, outcome).
- `IToolRegistry` / `ToolRegistry`: expose whitelisted tools as `AITool[]` via
  `AIFunctionFactory.Create` with the agent names from the Tool Map; resolve alias →
  invocation for the workflow engine (Stage 3 consumes this API).
- `ISkill` + `SummarizeTextSkill`, `ExtractActionItemsSkill`, `ClassifyEmailSkill`
  (names `summarize`, `extractActionItems`, `classifyEmail`): render template + input text →
  run through the agent's `IChatClient` as a plain completion (no tools).
- `IMyLocalAgentService` / `MyLocalAgentService` per spec §7.3: per-execution factory call
  (hot provider switch), `.AsAIAgent(name: "MyLocalAgentAI", instructions: …, tools: …)` with
  the spec system prompt (untrusted-content warning, local-unattended behavior, machine-clock
  dates, concise final report), streaming variant; factory failures → incident + friendly
  error.

### 4.6 Runner process helper

- `IRunnerService` / `RunnerService` per spec §7.3 (locate runner exe per
  `Scheduler:RunnerPath` rules; spawn with redirection; stream output lines; cancellable).

### 4.7 Tests (spec §7.7, Core portion)

`FileSystemTool` (incl. sandbox escape + delete gate) · `GmailTool`/`CalendarTool`/
`DropboxTool` (mocked connectors; JSON shape; send gate) · `HttpTool` (mocked handler;
allowlist; POST gate) · `JsonTaskStore` (round-trip, validation failure, mixed workflow forms
parse) · cron pure function (fixed `TimeProvider` + fixed `TimeZoneInfo`) · `ToolRegistry`/
`ToolMap` (whitelist filtering; **consistency**: every alias resolves to a real method, every
agent name matches, every arg name matches a parameter — reflection) · `AppExceptionHandlers`/
`IncidentLogService` (direct invocation; correlation ids; JSONL survives re-open) ·
`MyLocalAgentService` (factory failure → incident + friendly error; happy path with stub
`IChatClient`).

## 5. Working agreements

- When the spec and a Stage-1 signature disagree, prefer the Stage-1 compiled API and note the
  adjustment in the ledger (spec §3.2 governs overall).
- Do not reference `Microsoft.Win32.TaskScheduler` APIs you have not compiled; the logon-type
  mapping must be verified against the installed package.
- `Path.Combine`-based sandbox checks are forbidden; implement the `Path.GetFullPath` +
  rooted-prefix comparison described in spec §7.3 (case-insensitive on Windows).

## 6. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green.
3. `ToolMap` consistency test output visible in the test run.
4. No new package beyond `Directory.Packages.props` without a pinned addition + ledger note.

## 7. Before you finish

1. Append the **Stage 2** entry to the ledger: public API surface (services + key members),
   Tool Map as implemented, DI registrations list, deviations.
2. Stage report: files created/changed, gate summary, anything Stage 3 must know (especially
   the exact `IToolRegistry` invocation API the workflow engine will consume).
