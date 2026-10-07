# Architecture Ledger

## Stage 28 — Reflection-driven workflow step editor

### Version 1.6.0 — Complete typed workflow-step authoring — 2026-08-24

- Added `WorkflowStepEditingService` as the pure Core owner of Unicode-safe top-level step
  location plus canonical insertion, immutable-alias replacement, and single-step deletion.
- `ToolArgumentDefinition` retains reflected CLR type, safe default, numeric bounds, and editor
  hint, including nullable-value information. Catalog construction rejects incompatible metadata;
  validation consumes this projection rather than reflecting methods by name.
- The Task editor applies insert, edit, and delete results as one undoable in-memory draft change.
  Existing aliases are immutable; changing tools requires deletion followed by insertion.
- Delete analysis is deliberately limited to valid later `stepId.output` placeholder references.
  It warns and requires an explicit second confirmation, but never changes references, ids,
  permissions, ordering, or any other step.
- `ai.prompt` exact tools remain catalog ordered; model and generation inheritance are independent;
  Knowledge may only inherit or narrow the task scope. Authorization proposals are separate,
  unchecked opt-ins and successful dialog results are validated against a complete provisional
  task before one assisted draft mutation.
- Public additions are `WorkflowStepEditingService`, `WorkflowStepDependencyAnalyzer`,
  `WorkflowArgumentEditorHint`, `IWorkflowStepEditorDialogService`, and the memory-only editor
  request/result contracts. No package, persistence format, runtime tool, or network dependency
  was added.
- The final Release gate completed with 0 warnings, 0 errors, 765 passing non-integration tests,
  and one expected local Ollama integration probe skipped. Dark modal and Light/Dark resource
  rendering were inspected live, and self-contained single-file `win-x64` UI and Runner artifacts
  were published atomically to the standard local installation.

This ledger is the binding project memory for OD.MyLocalAgent. Every stage must read it completely before changing the repository and append an entry that records decisions, public API additions, package choices, and deviations from the normative shared specification.

## Key architecture decisions

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
- **H. Direct Chat owns an application-only database, not a workflow tool database.**
  `<FileSystem:RootPath>\.od-my-local-agent\data\direct-chat.db` is accessed only through
  `IDirectChatStore`; `SqliteTool`, task path authorization, and workflow capabilities do not
  participate. Context compression never deletes original messages.

## Package versions

| Package | Version |
|---|---:|
| `CommunityToolkit.Mvvm` | 8.4.2 |
| `coverlet.collector` | 10.0.1 |
| `Cronos` | 0.13.0 |
| `Dropbox.Api` | 7.2.0 |
| `FluentAssertions` | 7.2.2 |
| `Google.Apis.Auth` | 1.75.0 |
| `Google.Apis.Calendar.v3` | 1.75.0.4206 |
| `Google.Apis.Gmail.v1` | 1.74.0.4162 |
| `HtmlAgilityPack` | 1.12.4 |
| `MahApps.Metro.IconPacks` | 6.2.1 |
| `Markdig` | 1.3.2 |
| `MaterialDesignThemes` | 5.3.2 |
| `Microsoft.Agents.AI` | 1.15.0 |
| `Microsoft.Extensions.AI` | 10.8.3 |
| `Microsoft.Extensions.AI.OpenAI` | 10.8.3 |
| `Microsoft.Extensions.Configuration.Json` | 10.0.10 |
| `Microsoft.Extensions.Hosting` | 10.0.10 |
| `Microsoft.Extensions.Http` | 10.0.10 |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 10.0.10 |
| `Microsoft.NET.Test.Sdk` | 18.8.1 |
| `Microsoft.Web.WebView2` | 1.0.4078.44 |
| `Moq` | 4.20.72 |
| `OllamaSharp` | 5.4.30 |
| `OpenAI` | 2.12.0 |
| `Serilog` | 4.4.0 |
| `Serilog.Extensions.Hosting` | 10.0.0 |
| `Serilog.Sinks.Console` | 6.1.1 |
| `Serilog.Sinks.File` | 7.0.0 |
| `System.CommandLine` | 2.0.10 |
| `TaskScheduler` | 2.12.2 |
| `xunit` | 2.9.3 |
| `xunit.runner.visualstudio` | 3.1.5 |

## Solution-wide conventions

- Namespaces and project names use the `OD.MyLocalAgent` root. Code, documentation comments, logs, and user-interface text are written in English.
- Projects target .NET 10 and C# 14 with nullable reference types, implicit usings, XML documentation, and warnings-as-errors enabled globally.
- Public types and members receive XML documentation. Types use file-scoped namespaces, records represent data transfer objects, and implementation types are sealed where inheritance is not intended.
- Services that access I/O are asynchronous, accept a `CancellationToken`, and are registered behind interfaces through dependency injection.
- Wall-clock access and time-dependent delays use an injected `TimeProvider`.
- Configuration is rooted at `AppContext.BaseDirectory` and layered in this order: executable-adjacent `appsettings.json`, user overrides in `%LOCALAPPDATA%\OD.MyLocalAgent\settings.json`, then an in-memory layer populated from the DPAPI secret store.
- Secrets are never hard-coded, logged, or persisted to settings files. Destructive and outbound side effects remain disabled by default.

## Stage entries

### Stage 0 — 2026-07-29

#### Files created

- Solution and toolchain files: `OD.MyLocalAgent.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.gitignore`.
- Seven project files under `src/` and `tests/`, their minimal entry points, project metadata, WPF application resources, and the infrastructure smoke test.
- Product scaffolding: `appsettings.json`, `README.md`, and `Docs/samples/daily-mail-summary.json`.
- This architecture ledger.

#### Pinned package versions

The versions in the [Package versions](#package-versions) table were verified against NuGet and pinned through Central Package Management. FluentAssertions 7.2.2 was selected as the latest 7.x release, and its NuGet metadata declares the permissive Apache-2.0 license.

#### Decisions and deviations

- `Microsoft.Agents.AI` 1.15.0 is stable. The specification expected that a prerelease might be necessary, but the stable release is used in accordance with the normative stable-first version policy.
- `System.CommandLine` 2.0.10 is stable. This supersedes the prompt's expectation of a prerelease; its API differs from the older beta APIs, and later runner work must use the installed 2.0.10 surface exactly.
- `Microsoft.Extensions.AI.OpenAI` is pinned because it supplies the documented adapter between the official OpenAI SDK and `Microsoft.Extensions.AI`.
- Supporting packages for hosting, options binding, OAuth, Serilog integration, and test discovery are pinned centrally so later stages use one verified dependency graph.
- `OD.MyLocalAgent.Tests` targets `net10.0-windows` instead of plain `net10.0`. A `net10.0` project cannot reference the required `net10.0-windows` WPF project; the Windows target is the smallest idiomatic change that preserves the mandated test-to-all-projects dependency graph.
- The pre-existing `Docs/Prompts` directory casing is retained. Windows resolves it equivalently to the specification's `docs/prompts` path, and retaining it avoids rewriting the supplied normative prompts.

#### Verification

- `dotnet --version`: 10.0.302, selected through `global.json` and compatible with the requested 10.0.100 feature-band roll-forward policy.
- `dotnet restore OD.MyLocalAgent.sln -m:1`: succeeded for all seven projects.
- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore`: succeeded with zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore`: one test passed, with no failures or skipped tests.
- `dotnet run --project src/OD.MyLocalAgent.Runner/OD.MyLocalAgent.Runner.csproj --no-build --no-restore`: printed `OD.MyLocalAgent Runner 0.1.0`.

### Stage 1 — 2026-07-29

#### Files created or changed

- Replaced the metadata-only source in `OD.MyLocalAgent.Ollama` with the provider options,
  contracts, typed exceptions, Ollama and OpenAI-compatible services, the hot-switching
  factory, managed HTTP transports, validation, and `AddLlmProviders`.
- Replaced the metadata-only source in `OD.MyLocalAgent.Connectors` with connector options,
  DTOs, contracts, typed exceptions, Google credential acquisition, Gmail, Calendar and
  Dropbox implementations, managed HTTP adapters, mapping helpers, and `AddConnectors`.
- Added direct `Microsoft.Extensions.Http` references to both Stage 1 leaf projects. The
  package was already pinned at version 10.0.10, so no central version change was required.
- Deleted the Stage 0 infrastructure smoke test. Added Stage 1 unit tests, reusable HTTP and
  options test doubles, a representative Gmail JSON fixture, and a skipped-by-default local
  Ollama integration probe.

#### Public API surface added

`OD.MyLocalAgent.Ollama`:

- `OD.MyLocalAgent.Ollama.AIOptions`
- `OD.MyLocalAgent.Ollama.OllamaOptions`
- `OD.MyLocalAgent.Ollama.OpenAIOptions`
- `OD.MyLocalAgent.Ollama.LlmHealthResult`
- `OD.MyLocalAgent.Ollama.ILlmClientService`
- `OD.MyLocalAgent.Ollama.ILlmClientFactory`
- `OD.MyLocalAgent.Ollama.OllamaClientService`
- `OD.MyLocalAgent.Ollama.OpenAIClientService`
- `OD.MyLocalAgent.Ollama.LlmClientFactory`
- `OD.MyLocalAgent.Ollama.LlmConfigurationException`
- `OD.MyLocalAgent.Ollama.OllamaUnavailableException`
- `OD.MyLocalAgent.Ollama.OllamaModelMissingException`
- `OD.MyLocalAgent.Ollama.ServiceCollectionExtensions`

`OD.MyLocalAgent.Connectors`:

- `OD.MyLocalAgent.Connectors.GoogleOptions`
- `OD.MyLocalAgent.Connectors.DropboxOptions`
- `OD.MyLocalAgent.Connectors.EmailMessageDto`
- `OD.MyLocalAgent.Connectors.CalendarEventDto`
- `OD.MyLocalAgent.Connectors.DropboxItemDto`
- `OD.MyLocalAgent.Connectors.IGoogleCredentialProvider`
- `OD.MyLocalAgent.Connectors.IGmailConnector`
- `OD.MyLocalAgent.Connectors.ICalendarConnector`
- `OD.MyLocalAgent.Connectors.IDropboxConnector`
- `OD.MyLocalAgent.Connectors.GoogleCredentialProvider`
- `OD.MyLocalAgent.Connectors.GmailConnector`
- `OD.MyLocalAgent.Connectors.CalendarConnector`
- `OD.MyLocalAgent.Connectors.DropboxConnector`
- `OD.MyLocalAgent.Connectors.ConnectorConfigurationException`
- `OD.MyLocalAgent.Connectors.GmailApiException`
- `OD.MyLocalAgent.Connectors.CalendarApiException`
- `OD.MyLocalAgent.Connectors.DropboxApiException`
- `OD.MyLocalAgent.Connectors.ServiceCollectionExtensions`

#### Exception taxonomy

- `LlmConfigurationException` identifies actionable provider-selection, endpoint, model,
  timeout, and OpenAI API-key errors.
- `OllamaUnavailableException` identifies transport and provider-timeout failures.
- `OllamaModelMissingException` identifies a configured Ollama model absent from the local
  model list and carries the missing model identifier.
- `ConnectorConfigurationException` identifies missing Google OAuth client credentials,
  missing cached Google consent, and incomplete Dropbox token configuration.
- `GmailApiException`, `CalendarApiException`, and `DropboxApiException` carry the failed
  operation and an HTTP status code when the underlying SDK exposes one.
- Neither leaf project references Core or the incident pipeline. Stage 2 callers remain
  responsible for reporting these typed failures.

#### Decisions and SDK deviations

- The latest stable versions already pinned by Stage 0 were re-verified before implementation:
  OllamaSharp 5.4.30, Microsoft.Extensions.AI and its OpenAI adapter 10.8.3, OpenAI 2.12.0,
  Google.Apis.Auth 1.75.0, Google.Apis.Gmail.v1 1.74.0.4162,
  Google.Apis.Calendar.v3 1.75.0.4206, Dropbox.Api 7.2.0, and the .NET extensions 10.0.10.
  No prerelease package or version downgrade was introduced.
- OllamaSharp 5.4.30 exposes `ListLocalModelsAsync`; health and pre-generation model checks use
  that high-level API. Its `OllamaApiClient(HttpClient, model)` constructor is used so every
  transport comes from `IHttpClientFactory`. A dynamic `IChatClient` wrapper applies the
  monitored endpoint, model, and timeout on every call.
- OpenAI 2.12.0 is adapted with `AsIChatClient()`. Its `OpenAIClientOptions.Transport` is set
  to `HttpClientPipelineTransport` over a factory-created client, and custom base URLs use
  `OpenAIClientOptions.Endpoint`.
- Google APIs use their own `Google.Apis.Http.IHttpClientFactory` abstraction. A forwarding
  adapter bridges that abstraction to the application `IHttpClientFactory` for OAuth refresh,
  Gmail, and Calendar requests.
- Google.Apis.Auth 1.75.0 marks `TokenResponse.IsExpired(IClock)` obsolete; the silent
  credential path uses the current stable `TokenResponse.IsStale` API before refresh.
- `IGoogleCredentialProvider.AuthorizeInteractiveAsync` owns the browser-capable
  `GoogleWebAuthorizationBroker` path. Gmail and Calendar use only
  `GetCredentialSilentAsync`, so runner-side connectors never initiate consent.
- Dropbox.Api 7.2.0 supports both an access-token constructor and the
  refresh-token-plus-app-key constructor. Both paths receive factory-managed normal and
  long-poll HTTP clients.
- The normative specification enumerates every `EmailMessageDto` field but does not enumerate
  the Calendar and Dropbox record fields. The minimal normalized records are
  `CalendarEventDto(Id, Summary, Description, Start, End, Location)` and
  `DropboxItemDto(Name, Path, IsFolder, Size, ModifiedAt)`.

#### Tests added

- Ollama: reachable endpoint and installed model, endpoint failure, missing model, configured
  timeout, model ID in the request, and ordered streaming concatenation against a mocked
  `HttpMessageHandler`.
- Factory: default Ollama selection, hot provider switch to OpenAI, unknown provider,
  missing OpenAI API key, and invalid Ollama endpoint.
- Connectors: recursive plain-text Gmail mapping from a static representative JSON fixture,
  missing Google OAuth client configuration, and missing Dropbox token configuration.
- Integration: `OllamaIntegrationTests.LocalOllamaReportsReachableConfiguredModel` is present
  and skipped by default because it requires the local service and model.

#### Verification

- `dotnet restore OD.MyLocalAgent.sln -m:1`: succeeded for all seven projects. The command
  used the healthy user NuGet cache explicitly because the isolated temporary cache contained
  an unrelated incomplete package directory.
- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore`: succeeded with zero warnings and zero
  errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore`: 13 tests passed, zero
  failed, and the one expected local Ollama integration test was skipped.
- Manual non-gating probe:
  `curl --max-time 5 http://localhost:11434/api/tags` returned HTTP success and listed
  `gemma4:12b` with completion, tools, thinking, and vision capabilities.

#### Stage 2 handoff

- Core can depend only on `ILlmClientFactory`, connector interfaces, DTOs, and the typed
  exceptions above. It must route surfaced failures into `IIncidentReporter`.
- The interactive Google Connect command must call
  `IGoogleCredentialProvider.AuthorizeInteractiveAsync`; unattended execution must continue
  to use the silent path through the connectors.
- The in-memory secrets layer must populate `OpenAI:ApiKey` and `Dropbox:*` before resolving
  Stage 1 options. No Stage 1 type reads or writes DPAPI data.

### Stage 2 — 2026-07-29

#### Files created or changed

- Replaced the metadata-only Core project with scheduled-task and workflow contracts,
  validation and serialization, typed safety/validation exceptions, options, application data
  paths, and the absolute-path sandbox.
- Added the JSONL incident pipeline, directly testable static exception adapter, DPAPI
  CurrentUser secret store, layered host composition, rolling-file Serilog configuration, and
  the bounded observable in-memory log sink.
- Added the watched one-file-per-task JSON store, pure cron calculator, Windows Task Scheduler
  mirror and resynchronization service, shared runner-path resolution, and cancellable
  child-process runner service.
- Added the normative static Tool Map, all five tool implementations, the whitelisting and
  direct-invocation registry, all three plain-completion skills, and `MyLocalAgentAI`.
- Added Stage 2 Core unit tests and reusable stub chat clients. Updated `README.md` with safety,
  scheduler, S4U, configuration, and workflow-form documentation and examples.
- No package reference or central package version changed.

#### Public API surface added

Foundations and configuration:

- `ScheduledTask`, `WorkflowStepDefinition`, `WorkflowErrorPolicy`,
  `ScheduledTaskSerializer`, and `ScheduledTaskValidator.Validate`.
- `SafetyOptions`, `FileSystemOptions`, `HttpOptions`, `SchedulerOptions`, and
  `LoggingOptions`.
- `SafetyException`, `PathSandboxException`, `TaskValidationException`, and
  `WorkflowValidationException`.
- `LocalAppPaths`, `ToolGroups`, `IPathSandbox` / `PathSandbox`.
- `HostApplicationBuilderExtensions.AddODServices(IHostApplicationBuilder,
  Action<LoggerConfiguration>?)`.

Incidents, secrets, and logging:

- `IncidentRecord`, `IncidentSeverity`, `IncidentReportedEventArgs`.
- `IIncidentLogService` / `JsonlIncidentLogService`: `AppendAsync`, `ReadAsync`,
  `ExportAsync`, and `ClearAsync`.
- `IIncidentReporter` / `IncidentReporter`: error-default and explicit-severity
  `ReportAsync` overloads plus `IncidentReported`.
- `AppExceptionHandlers.Initialize`, `Handle`, `HandleAsync`,
  `HandleDispatcherException`, and `WireProcessEvents`.
- `ISecretStore` / `DpapiSecretStore`: `GetAsync`, `SetAsync`, `RemoveAsync`, and
  `ExistsAsync`; `SecretKeys`.
- `IInMemoryLogSink` / `InMemoryLogSink`.

Task persistence, scheduling, and runner process:

- `ITaskStore` / `JsonTaskStore`: `GetAsync`, `GetAllAsync`, `SaveAsync`, `DeleteAsync`,
  and `TasksChanged`; `TaskStoreChangedEventArgs` and `TaskStoreChangeKind`.
- `CronSchedule.NextOccurrence(string, DateTimeOffset, TimeZoneInfo)`.
- `ITaskSchedulerService` / `WindowsTaskSchedulerService`: `RegisterAsync`,
  `UnregisterAsync`, `IsRegistered`, `GetNextRunAsync`, and `ResyncAllAsync`;
  `SchedulerResyncResult`.
- `RunnerPathResolver.Resolve`, `IRunnerService` / `RunnerService.RunTaskAsync`.

Tools, skills, and agent:

- `IFileSystemTool` / `FileSystemTool`, `IGmailTool` / `GmailTool`,
  `ICalendarTool` / `CalendarTool`, `IDropboxTool` / `DropboxTool`, and
  `IHttpTool` / `HttpTool`.
- `ToolDefinition`, static `ToolMap.Definitions` / `ToolMap.Find`.
- `IToolRegistry` / `ToolRegistry`: `Definitions`,
  `GetAgentTools(IEnumerable<string>)`, `TryGetDefinition`, and
  `InvokeAsync(string, JsonElement, IEnumerable<string>, CancellationToken)`.
- `ISkill`, `SkillBase`, `SummarizeTextSkill`, `ExtractActionItemsSkill`, and
  `ClassifyEmailSkill`.
- `IMyLocalAgentService` / `MyLocalAgentService`: `RunPromptAsync` and
  `RunPromptStreamingAsync`; `AgentRunResult`, `AgentExecutionException`.

#### Tool Map implemented

| Alias | Agent function | Method | Arguments | Safety gate |
|---|---|---|---|---|
| `gmail.readToday` | `gmail_read_today` | `GmailTool.ReadTodayAsync` | — | — |
| `gmail.readUnread` | `gmail_read_unread` | `GmailTool.ReadUnreadAsync` | — | — |
| `gmail.send` | `gmail_send` | `GmailTool.SendAsync` | `to`, `subject`, `body` | `Safety:AllowSendEmail` |
| `calendar.today` | `calendar_today` | `CalendarTool.TodayAsync` | — | — |
| `calendar.create` | `calendar_create` | `CalendarTool.CreateAsync` | `summary`, `start`, `end`, `description` | — |
| `dropbox.upload` | `dropbox_upload` | `DropboxTool.UploadAsync` | `localPath`, `dropboxPath` | — |
| `dropbox.download` | `dropbox_download` | `DropboxTool.DownloadAsync` | `dropboxPath`, `localPath` | — |
| `dropbox.list` | `dropbox_list` | `DropboxTool.ListAsync` | `folderPath` | — |
| `filesystem.list` | `filesystem_list` | `FileSystemTool.ListAsync` | `path` | — |
| `filesystem.read` | `filesystem_read` | `FileSystemTool.ReadAsync` | `path` | — |
| `filesystem.write` | `filesystem_write` | `FileSystemTool.WriteAsync` | `path`, `content` | — |
| `filesystem.move` | `filesystem_move` | `FileSystemTool.MoveAsync` | `from`, `to` | — |
| `filesystem.delete` | `filesystem_delete` | `FileSystemTool.DeleteAsync` | `path` | `Safety:AllowFileDelete` |
| `filesystem.createDirectory` | `filesystem_create_directory` | `FileSystemTool.CreateDirectoryAsync` | `path` | — |
| `http.get` | `http_get` | `HttpTool.GetAsync` | `url` | — |
| `http.post` | `http_post` | `HttpTool.PostAsync` | `url`, `body`, `contentType` | `Safety:AllowHttpPost` |
| `http.getText` | `http_get_text` | `HttpTool.GetTextAsync` | `url` | — |
| `ai.summarize` | — | `SummarizeTextSkill` | optional `text` | — |
| `ai.extractActionItems` | — | `ExtractActionItemsSkill` | optional `text` | — |
| `ai.classifyEmail` | — | `ClassifyEmailSkill` | optional `text` | — |
| `ai.prompt` | — | workflow prompt step | `prompt` | — |

#### Dependency injection registrations

`AddODServices` registers the five Core options, Stage 1 `AddLlmProviders` and
`AddConnectors`, the named HTTP client, `TimeProvider.System`, the DPAPI secret store, path
sandbox, all five concrete tools and interfaces, `IToolRegistry`, the three concrete skills
and `IEnumerable<ISkill>`, incident log and reporter, task store, scheduler, agent service,
runner service, and both the concrete and interface views of the in-memory Serilog sink.

The configuration source order is executable-adjacent `appsettings.json`, reloadable
per-user `settings.json`, then the in-memory secret dictionary. The optional additional
Serilog configurator is the runner's console-sink hook.

#### Decisions and SDK deviations

- Microsoft.Agents.AI 1.15.0 returns `AgentResponse`, not the specification's named
  `AgentRunResult`. Core converts it to its own stable `AgentRunResult(Text, ProviderName)`
  contract while retaining `.AsAIAgent`, `RunAsync`, and `RunStreamingAsync`.
- Core targets `net10.0` and therefore does not reference WPF dispatcher types.
  `HandleDispatcherException` is the direct, testable adapter; Stage 4 must wire the WPF
  `DispatcherUnhandledException` event to it. AppDomain and unobserved-task hooks are wired
  directly by `WireProcessEvents`.
- The .NET 10 reference framework does not include the `ProtectedData` type and no
  `System.Security.Cryptography.ProtectedData` package was pinned in Stage 0. To avoid an
  unplanned dependency, `DpapiSecretStore` calls Windows `CryptProtectData` /
  `CryptUnprotectData` directly with the default CurrentUser scope and UI disabled. The
  encrypted store remains interoperable with DPAPI semantics required by the specification.
- TaskScheduler 2.12.2 was compiled against the implemented APIs:
  `TaskService.NewTask`, `TimeTrigger(DateTime)`, `ExecAction`,
  `TaskFolder.RegisterTaskDefinition` with `InteractiveToken` or `S4U`, and
  `TaskSettings.StartWhenAvailable`.
- Dropbox upload and download local paths also pass through `IPathSandbox`; this applies the
  Stage 2 restatement that file paths remain under `FileSystem:RootPath`.
- Connector-backed tools serialize returned DTOs or DTO lists directly as compact camel-case
  JSON. `ToolRegistry.InvokeAsync` unwraps the `AIFunctionFactory` JSON-string envelope so
  Stage 3 receives that exact tool result.

#### Tests added

- File-system operations, absolute and parent-traversal rejection, and delete gate.
- Gmail, Calendar, and Dropbox connector mapping, compact DTO JSON, and email-send gate.
- HTTP allowlist, HTML-to-text extraction, request mapping, and POST gate.
- Task-store CRUD, validation-on-load, and mixed workflow forms.
- Pure cron calculation with a fixed time zone.
- Tool Registry filtering and reflection consistency for all 21 aliases and 17 invocable
  functions.
- Direct incident handlers, notification, JSONL reopen/export/clear, and correlation ids.
- DPAPI persistence, reopen, encrypted-at-rest assertion, and removal.
- Agent factory failure to incident/friendly error and happy path through a stub
  `IChatClient`.

#### Verification

- `dotnet restore OD.MyLocalAgent.sln -m:1 --ignore-failed-sources
  -p:NuGetAudit=false` with `NUGET_PACKAGES` set to the healthy user cache: succeeded for all
  seven projects. The sandbox blocks the NuGet service and its temporary cache contains an
  unrelated incomplete package, so the same cache-isolation approach recorded in Stage 1 was
  used. No dependency changed.
- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore`: succeeded with zero warnings and zero
  errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --logger
  "console;verbosity=detailed"`: 34 tests passed, zero failed, and the one expected manual
  Ollama integration probe was skipped.
- The detailed test output printed:
  `Tool Map consistency: 21 aliases, 17 invocable tools, all methods/names/arguments matched.`

#### Stage 3 handoff

- Parse tasks only through `ScheduledTaskSerializer` or call
  `ScheduledTaskValidator.Validate`; `WorkflowStepDefinition` already normalizes string and
  object forms and exposes `EffectiveId`, `Args`, step policy, and retry settings.
- For a tool step, call
  `IToolRegistry.InvokeAsync(alias, resolvedArgsJsonElement, task.Tools, cancellationToken)`.
  It performs alias resolution, whitelist enforcement, required-argument validation,
  System.Text.Json binding through `AIFunctionFactory`, cancellation propagation, and compact
  result unwrapping.
- For validation and dispatch decisions, use `IToolRegistry.TryGetDefinition` or the static
  `ToolMap.Find`; do not duplicate aliases, group names, agent names, or argument names.
- Resolve skills by `ISkill.Name` from `IEnumerable<ISkill>`. The registered names are
  `summarize`, `extractActionItems`, and `classifyEmail`; skill execution is a no-tools plain
  completion.
- The runner command and UI startup must share
  `ITaskSchedulerService.ResyncAllAsync`. After each execution, call `RegisterAsync` for the
  task to roll its next occurrence.

### Stage 3 — 2026-07-29

#### Files created or changed

- Replaced the metadata-only Workflows project with executable `ToolStep`, `SkillStep`, and
  `PromptStep` models, the ordered workflow context, placeholder resolver, typed placeholder
  failure, sequential engine, result contracts, Markdown report writer, and workflow DI
  registration.
- Replaced the runner version stub with the complete System.CommandLine 2.0.10 command
  surface, lazy parser/test composition, shared Core host startup, console Serilog sink,
  top-level incident handling, per-task global mutex, command handlers, exit-code
  classification, JSON formatting, and schedule rolling.
- Added engine, report-writer, and parser-level runner tests. Updated the README with the
  deterministic-workflow recommendation, runner commands, exit codes, reports, and completed
  Stage 3 status.
- Extended Core's internal `ToolJson.Options` with an explicit
  `DefaultJsonTypeInfoResolver`. The real cold-start runner probe exposed that
  `AIFunctionFactory` makes these options read-only before the first serialization; without an
  explicit resolver, .NET 10 rejected this initialization order. No Core public API changed.
- No package reference or central package version changed.

#### Public workflow API added

- `WorkflowStep` with `ToolStep`, `SkillStep`, and `PromptStep`;
  `WorkflowStepFactory.Create`.
- `WorkflowContext`, `WorkflowPlaceholderException`, and `WorkflowStepStatus`.
- `WorkflowStepResult` and `WorkflowResult`.
- `IWorkflowEngine` / `SequentialWorkflowEngine.ExecuteAsync`.
- `IReportWriter` / `MarkdownReportWriter.WriteAsync` and `ReportWriteResult`.
- `ServiceCollectionExtensions.AddWorkflowServices`.

The normalized persistence model remains Core's `WorkflowStepDefinition`; the workflow layer
does not define a second JSON contract or alias table. `WorkflowStepFactory` classifies steps
through `IToolRegistry`, and tool execution always returns through
`IToolRegistry.InvokeAsync`.

#### Engine semantics

- Steps execute sequentially and record successful outputs in a case-insensitive,
  insertion-ordered `WorkflowContext`. `previous.output` addresses the most recently
  successful output.
- Placeholders are substituted recursively in JSON string values immediately before each
  attempt. Task, named-output, previous-output, tool argument, and prompt-template forms are
  covered by tests. Unknown placeholders become failed steps through
  `WorkflowPlaceholderException`.
- Effective error policy is the step policy followed by the task policy. Retry means the
  initial attempt plus exactly `retryCount` retries; the default is three retries. Backoff is
  `retryBackoffSeconds * 2^(retry-1)` through `Task.Delay(TimeSpan, TimeProvider, ct)`.
  Because the normative specification does not state a missing `retryBackoffSeconds` default,
  the engine uses one second.
- Skill steps without `args.text` use the previous output, or the task prompt when first.
  Skill output is compact `{"text":"..."}` JSON; prompt-step output remains raw text.
- `WorkflowResult.Success` is false when any step fails, including a continued failure. Its
  final text is the last executed step's output, or empty when that step failed.

#### Reports

`MarkdownReportWriter` writes
`%LOCALAPPDATA%\OD.MyLocalAgent\reports\<taskId>\<yyyyMMdd-HHmmss>.md` with the task id and
name, UTC start and end, provider, status, step table, error, and final output. It appends
`taskId`, `startedUtc`, `durationMs`, `status`, and the absolute `reportPath` to
`reports\index.jsonl`. Exclusive file access with injected-time retry protects index appends
from concurrent runner processes.

#### Runner command surface and exit codes

- `run --task <id>`: acquires `Global\OD.MyLocalAgent.<id>`, loads the validated task, rejects
  disabled tasks, executes workflow-over-prompt, writes the report/index, and calls
  `ITaskSchedulerService.RegisterAsync` for the next occurrence.
- `list`: prints id, name, schedule, enabled state, and the store-derived next run.
- `test-prompt --task <id>`: executes only the prompt path, writes a report, prints its full
  Markdown, and does not schedule.
- `test-tool --alias <alias> --args <json>`: validates the Core Tool Map alias and JSON object
  at parser level, invokes through `IToolRegistry`, pretty-prints and times the result, and
  names the required safety flag when blocked.
- `resync-schedules`: calls `ITaskSchedulerService.ResyncAllAsync` and prints per-task and
  aggregate results.
- Exit codes are `0` success, `1` task/tool/safety failure, `2` configuration failure
  (including nested `LlmConfigurationException`), and `3` task not found. Parser errors also
  return `1`.

System.CommandLine's installed stable 2.0.10 API is used directly: `Option.Required`,
option validators, `Command.SetAction`, `ParseResult.GetRequiredValue`, and
`InvocationConfiguration.EnableDefaultExceptionHandler = false`. Disabling the library's
default exception handler ensures the runner top-level catch routes unhandled command
exceptions through `AppExceptionHandlers`.

#### Tests added

- Workflow order; task, named-output, previous-output, and prompt-template placeholders;
  unknown placeholder failure; string/object equivalence; stop, continue, and exact retry
  behavior; fake-time backoff; workflow precedence; whitelist failure; implicit skill input;
  and prompt-step output.
- Markdown report path/header/content and append-only JSON Lines indexing in an isolated
  directory.
- Parser-only required `run --task` and unknown `test-tool` alias exit behavior without
  constructing the application host.

#### Stage 6 manual end-to-end procedure

Run these commands from the repository root after completing Google consent in the UI,
confirming Ollama has `gemma4:12b`, and setting `FileSystem:RootPath` to an existing absolute
directory:

```powershell
$taskDirectory = Join-Path $env:LOCALAPPDATA 'OD.MyLocalAgent\tasks'
New-Item -ItemType Directory -Force -Path $taskDirectory
Copy-Item -LiteralPath '.\Docs\samples\daily-mail-summary.json' -Destination (Join-Path $taskDirectory 'daily-mail-summary.json') -Force
dotnet run --project .\src\OD.MyLocalAgent.Runner\OD.MyLocalAgent.Runner.csproj -- run --task daily-mail-summary
dotnet run --project .\src\OD.MyLocalAgent.Runner\OD.MyLocalAgent.Runner.csproj -- list
Get-Content -Tail 1 -LiteralPath (Join-Path $env:LOCALAPPDATA 'OD.MyLocalAgent\reports\index.jsonl')
```

Verify that the command exits `0`, the displayed Markdown report exists, the final index line
has status `Success`, and `list` shows the next cron occurrence. The execution should also
leave `OD.MyLocalAgent.daily-mail-summary` registered in the configured Task Scheduler
folder.

#### Verification

- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded
  with zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  48 tests passed, zero failed, and the one expected manual Ollama probe was skipped.
- `dotnet run --project src/OD.MyLocalAgent.Runner/OD.MyLocalAgent.Runner.csproj --no-build
  --no-restore --configuration Release -- list`: started the real host and printed the empty
  task table.
- With a temporary existing `FileSystem:RootPath`,
  `test-tool --alias filesystem.list --args '{ "path": "." }'` returned formatted JSON and
  exit `0`.
- With safe defaults, `test-tool --alias filesystem.delete --args
  '{ "path": "blocked.txt" }'` returned exit `1` and named
  `Safety:AllowFileDelete`.

#### Stage 4 handoff

- The UI composition root must call `AddODServices` followed by `AddWorkflowServices`.
- Dashboard report history can read `reports\index.jsonl`; each `reportPath` is absolute.
- UI startup and its schedule repair action must continue to call the existing
  `ITaskSchedulerService.ResyncAllAsync`. There are no open Stage 4 blockers.

### Stage 4 — 2026-07-29

#### Files created or changed

- Replaced the Stage 0 window stub with the Generic Host composition root, DI-resolved shell,
  navigation rail, Material icons, snackbar host, and graceful application lifecycle.
- Added `Themes/DarkPalette.xaml` as the single color-role dictionary and
  `Themes/ControlStyles.xaml` for palette-derived implicit and named styles. Added the running
  control inventory in `Docs/theme-audit.md`.
- Added one messenger record per file for navigation, general snackbar notifications, and
  durable incident notifications.
- Added the Dashboard, Logs, and Settings pages and ViewModels, plus real disabled-content
  landing pages for Tasks, Tools, Tool tester, and AI playground.
- Added UI services for desktop interaction, atomic per-user settings persistence, log
  export/reset, incident-to-messenger bridging, and one-shot startup schedule repair.
- Added a bindable `PasswordBox` behavior so the replacement API key remains compatible with
  strict MVVM and page code-behind stays limited to initialization and Logs auto-scroll.

#### DI graph and lifecycle

The UI builds `Host.CreateApplicationBuilder` with `AppContext.BaseDirectory` as content root,
then calls `AddODServices` and `AddWorkflowServices`. UI registrations add the singleton
messenger and snackbar queue; desktop, settings, and log-management services; the incident
bridge; `SchedulerStartupService`; the shell; every page; and every ViewModel.

`App.OnStartup` builds the host, initializes `AppExceptionHandlers`, wires the process hooks
and WPF dispatcher hook, starts the incident bridge, resolves the DI-backed shell, starts the
host asynchronously, and displays the window. `SchedulerStartupService` calls
`ITaskSchedulerService.ResyncAllAsync` once in the background. `App.OnExit` performs a bounded
graceful host stop.

Core continues to expose `IRunnerService` through the shared container for the Stage 5 Run now
page; no duplicate execution path was introduced in UI.

#### Pages and ViewModels

- `MainWindow` / `MainWindowViewModel`
- `DashboardPage` / `DashboardViewModel`
- `TasksPage` / `TasksViewModel`
- `LogsPage` / `LogsViewModel`
- `ToolsPage` / `ToolsViewModel`
- `ToolTesterPage` / `ToolTesterViewModel`
- `AIPlaygroundPage` / `AIPlaygroundViewModel`
- `SettingsPage` / `SettingsViewModel`

Dashboard reads tasks through `ITaskStore`, computes the closest five runs through
`ITaskSchedulerService.GetNextRunAsync`, reads the last ten valid report-index lines, checks
the factory-selected provider asynchronously, and exposes New task, schedule resync, and logs
folder actions.

Logs projects the bounded Serilog sink into a dispatcher-marshalled, auto-scrolling,
read-only `ListView`. Minimum level, source substring, and task-id substring are live
collection-view filters.

Settings covers all three required sections. The settings service validates and atomically
writes only non-secret sections to the reloadable per-user JSON file. A replacement OpenAI key
is written only through `ISecretStore`, copied into the winning in-memory configuration
provider, and followed by `IConfigurationRoot.Reload`, so the next factory call observes both
provider and key changes without restart. Diagnostics export/reset logs, display expandable
incidents, copy/export/clear them, and deliberately throw the test incident through the
generated command path.

#### Incident flow

`IIncidentReporter.IncidentReported` is bridged to `IncidentNotificationMessage`.
`MainWindowViewModel` receives it and enqueues a non-blocking snackbar containing the
correlation id; `SettingsViewModel` refreshes the durable incident list. Dispatcher exceptions
are marked handled before reporting so the application remains usable. Bootstrap exceptions
are also reported when the host has already been constructed, with a message-box fallback
when durable incident storage is unavailable.

#### Theme structure and package deviation

`DarkPalette.xaml` defines background, surface, card, primary, secondary, error, warning,
success, on-background, on-surface, muted text, and divider roles. All custom styles consume
those roles; pages do not contain inline colors.

The post-stage window-chrome refinement replaces the Windows-colored non-client frame with a
theme-owned WPF title bar. It retains native move, resize, system-command, and DWM-shadow
behavior while adding a one-pixel palette outline that becomes subtly brighter for the active
window.

The post-stage appearance refinement adds `LightPalette.xaml` with the same semantic keys as
the dark palette. `ThemeService` synchronizes the Material base theme and replaces the
application palette at runtime. Settings exposes an immediate Dark/Light selector; the
validated value is stored as `Appearance:Theme` in the existing per-user settings file and is
applied before the shell is created at the next startup. Missing or invalid values fall back
to Dark.

MaterialDesignThemes 5.3.2 no longer contains the normative
`Themes/MaterialDesignTheme.Defaults.xaml` resource. Its stable v5 equivalent is
`Themes/MaterialDesign2.Defaults.xaml`, which is merged once after the dark `BundledTheme`.
Version 5.3.2 also has no named `MaterialDesignCard` or `MaterialDesignSnackbar` style;
those custom controls retain their theme styles while the application applies palette
setters without a nonexistent named `BasedOn` resource.

#### Manual GUI verification

- The Release executable opened a responsive `OD.MyLocalAgent` window and closed cleanly.
  The shell, rail, cards, inputs, logs, scrollbars, and diagnostics surfaces rendered dark.
- The theme-owned title bar and one-pixel outline rendered consistently in normal and
  maximized states; maximize, restore, and close commands were exercised successfully.
- Dark-to-Light and Light-to-Dark changes were exercised without restarting. A saved Light
  selection survived a full application restart; the test preference was then restored to
  Dark.
- Dashboard reported local Ollama healthy with its measured latency.
- Every rail entry was reached and displayed its registered page.
- Settings saved a temporary switch to OpenAI-compatible. The next Test connection, without
  restart, used OpenAI and rejected the absent DPAPI key with the expected configuration
  message and correlation-id snackbar. Ollama was then restored and saved.
- The resulting `%LOCALAPPDATA%\OD.MyLocalAgent\settings.json` selected `Ollama` and contained
  no `ApiKey`, access token, refresh token, or app-key text.
- Trigger test incident produced a `Critical` durable record sourced from
  `Application.DispatcherUnhandledException`, and the application remained responsive.

#### Verification

- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded
  with zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  56 tests passed, zero failed, and the one expected manual Ollama integration probe was
  skipped.

#### Stage 5 handoff

- Replace the four disabled content areas with the task/editor, connector, tool-tester, and
  AI-playground functionality while retaining their routes and DI registrations.
- Resolve `IRunnerService` in the Tasks ViewModel for Run now and stream its callback output
  into the existing in-memory/logs experience.
- Extend `Docs/theme-audit.md` as Stage 5 introduces `DataGrid`, editor, pop-up, and streaming
  controls; Stage 6 remains responsible for the exhaustive dark-state audit.

### Stage 5 — 2026-07-29

#### Files created or changed

- Replaced the Tasks, Tools, Tool tester, and AI playground landing pages and ViewModels with
  their complete Stage 5 implementations.
- Added the task editor and prompt editor pages, ViewModels, navigation messages, DataTemplates,
  and dependency-injection registrations.
- Added task-list and Tool Map picker projection records plus task-saved,
  execution-completed, edit-task, edit-prompt, and provider-configuration messages.
- Extended shared control styles for `DataGrid`, headers, rows, cells, grouped
  `ComboBoxItem` states, text selection, and separators. Extended `Docs/theme-audit.md` with
  the Stage 5 control inventory and remaining default-template audit candidates.
- Removed the Stage 4 `StageFivePageViewModel` placeholder base. No package or central package
  version changed.

#### Pages and behavior

- Tasks reads next occurrences and the latest per-task `reports/index.jsonl` entry, displays
  scheduler registration state, persists enabled toggles, confirms delete and unregisters
  first, synchronizes explicit Register/Unregister, and executes Run now exclusively through
  the cancellable `IRunnerService` child-process path. Stdout and stderr stream into the
  docked runner pane. Completion publishes `TaskExecutionCompletedMessage`. Task-store load
  failures produce a prominent non-destructive error panel with the validation detail, folder
  and retry actions; startup also publishes a visible notification instead of silently showing
  zero tasks.
- Task editor enforces immutable ids after creation, produces live three-occurrence cron
  previews and direct common-pattern labels, exposes all five tool-group checkboxes, validates
  workflow JSON through `ScheduledTaskValidator`, supplies a bounded Levenshtein
  did-you-mean hint for unknown aliases, and synchronizes persistence and scheduling on save.
- Prompt editor persists the selected task prompt and streams
  `IMyLocalAgentService.RunPromptStreamingAsync` with exactly that task's tool whitelist. It
  reports chunk count and exposes cancellation.
- Tools configuration reports Google client configuration, credential-folder presence, and
  requested scopes; starts interactive Google authorization from the UI command; clears both
  the provider cache and FileDataStore directory after confirmation; writes Dropbox
  access/refresh/app-key replacements only through DPAPI; updates the winning in-memory
  configuration layer; and tests Gmail, Calendar, and Dropbox with latency and typed errors.
- Tool tester projects invocable aliases directly from `IToolRegistry.Definitions`, groups
  them by Tool Map group, derives argument hints/templates, pretty-prints results, measures
  duration, and resolves nested `SafetyException` instances into an exact Settings → General
  flag instruction.
- AI playground uses the current factory-selected raw provider, not the agent, and streams
  `GenerateStreamingAsync`. The provider/model badge comes from monitored options; health is
  checked on activation and after `ProviderConfigurationChangedMessage`.

#### Cross-layer integration changes

- `CronSchedule.NextOccurrences` and `CronSchedule.DescribeCommonPattern` provide the pure
  occurrence sequence and direct minutely/hourly/daily/weekdays/weekly/monthly labels required
  by the editor. Unit tests cover all six mappings and the three-occurrence sequence.
- `IGoogleCredentialProvider.DisconnectAsync` clears the in-memory credential and persisted
  FileDataStore state. `GoogleCredentialProvider.CredentialStorePath` and
  `RequiredScopes` expose the provider-owned status facts to the UI.
- `DropboxConnector` now consumes `IOptionsMonitor<DropboxOptions>` so a DPAPI credential
  replacement and configuration reload is visible to the next connection test without
  restarting the application.

#### Decisions and deviations

- The normative §8.1 task contract has no task-level retry-count or backoff properties.
  Consequently, the UI persists only task-level `errorPolicy` and explains that
  `retryCount`/`retryBackoffSeconds` belong on workflow-step objects. No non-normative JSON
  properties were introduced.
- The current `IMyLocalAgentService.RunPromptStreamingAsync` contract yields text chunks only.
  The prompt editor therefore displays that token usage is not reported by the current
  streaming contract while maintaining an exact chunk count. Provider token usage can be
  added only after a future Core streaming contract exposes it.
- Task and prompt editors are contextual destinations under the selected Tasks rail item,
  reached through dedicated messages instead of adding two permanent rail entries.
- The Google interactive command is initiated and awaited on the WPF dispatcher context;
  the connector retains ownership of `GoogleWebAuthorizationBroker`.

#### Manual GUI verification

- The Release UI executable opened and closed cleanly without a XAML resource or startup
  incident.
- Dashboard selected Ollama and reported `gemma4:12b` healthy.
- Tasks rendered the explicit dark `DataGrid`, empty state, action commands, and docked runner
  output. New opened the task editor; its default daily cron rendered the next three local
  occurrences and the `Daily at 07:00` label.
- Tools rendered Google scope/status, three write-only Dropbox fields, and connector rows.
  The Gmail test with placeholder Google configuration returned the visible typed
  `ConnectorConfigurationException` and measured latency.
- Tool tester rendered all 17 invocable aliases in a dark grouped pop-up and updated the
  selected alias argument template.
- AI playground reported Ollama healthy, sent the local prompt `Reply exactly OK.`, streamed
  `OK`, counted one chunk, restored its Generate state, and exposed Cancel while active.
- A persistent task was deliberately not created during this automated pass, so the contextual
  prompt editor and Run now/index append were not exercised through the GUI. The Debug/Release
  build output also does not publish the runner beside the UI; Stage 6 must verify these two
  paths with a published sibling runner or an explicit `Scheduler:RunnerPath`, then remove its
  acceptance task.

#### Verification

- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded
  with zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
  --logger "console;verbosity=normal"`: 63 tests passed, zero failed, and the one expected
  manual Ollama integration probe was skipped.

#### Stage 6 handoff

- Publish or configure the runner, create a disposable task through the UI, verify prompt
  editing, Run now cancellation/completion, report-index append, dashboard refresh, and
  scheduler rolling registration, then remove the acceptance task.
- Complete the remaining populated-grid, keyboard, disabled, high-DPI, long-text, internal
  `DataGrid`/scrollbar/pop-up, and native-dialog theme checks listed in
  `Docs/theme-audit.md`.

### Stage 6 — 2026-07-29

#### Product closeout

- Completed the application-owned control inventory and replaced the pending theme audit
  with final Dark and Light evidence. Dark remains the default. Light remains an intentional
  supported appearance and was explicitly retained during closeout.
- Added palette-derived validation, selection, disabled, row, header, item, and scrollbar
  states to the shared control dictionary. The interactive pass exposed two nonexistent
  Material resource keys; `ComboBoxItem` now uses the application palette directly and
  `ListViewItem` derives from the available compatible Material list-box item style.
- Replaced the custom `ICommand` title-bar implementation with MVVM Toolkit-generated commands
  that invoke WPF `SystemCommands`. Every ViewModel continues to use source generators, and no
  manual property-change or command class remains.
- Replaced the README skeleton with the complete packaging, configuration, OAuth, Dropbox,
  safety, prompt-injection, scheduler, CLI, workflow, Tool Map, architecture, provider,
  diagnostics, troubleshooting, and extension documentation.
- Added the deterministic OAuth-free `Docs/samples/acceptance-filesystem.json` workflow and
  the executed `Docs/acceptance-report.md` plus final stage report.

#### Self-contained publish and runner resolution

Both application projects declare `RuntimeIdentifier=win-x64` and `SelfContained=true`.
`scripts/publish.ps1` performs Release self-contained publishes into a temporary staging
directory, validates the expected hosts and runtime payload, and promotes the staged tree to
the stable destination with rollback protection.

The stable default layout is deliberately split:

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\
├── UI\OD.MyLocalAgent.UI.exe
└── Runner\OD.MyLocalAgent.Runner.exe
```

A merged self-contained directory is not safe because UI and Runner publish framework files
with different payloads; merging in either order caused either WPF `WindowsBase` or Runner
`hostpolicy` failure. `RunnerPathResolver` preserves the existing adjacent-executable
contract, then checks the standard sibling `UI`/`Runner` layout when the setting is empty.
Task Scheduler verification confirmed the stable Runner path, `run --task <id>` arguments,
Runner working directory, and `StartWhenAvailable=true`.

#### Acceptance-surfaced fixes and deviations

- `TaskExecutionMutex` keeps its established type and global per-task gate contract but uses
  a named `Semaphore` instead of a named `Mutex`. A CLR mutex is thread-affine and cannot be
  safely released after awaited workflow execution resumes on another thread. The semaphore
  preserves cross-process exclusion without that invalid release constraint; a regression
  test releases from a different task and proves reacquisition.
- `UserSettingsService` now preserves the `Google` client id and secret while rewriting the
  per-user document. This prevents an unrelated Settings save from removing OAuth client
  configuration.
- Dormant Dropbox configuration is now valid at the options-binding layer. Strict credential
  validation remains in `DropboxConnector` immediately before use, so an unconfigured
  optional connector no longer breaks an unrelated configuration reload while connector
  calls still produce the required typed error.
- The title-bar uses MVVM Toolkit-generated commands that invoke WPF `SystemCommands`, closing
  the final MVVM acceptance item without changing window behavior.
- No direct package version changed. The final NuGet check reported only
  `FluentAssertions 7.2.2 -> 8.10.0` as a top-level update. Version 7.2.2 remains deliberately
  pinned as the last project-approved permissively licensed line; reported transitive
  updates are controlled by their direct packages.

#### Executed acceptance evidence

- The self-contained filesystem task completed with exit code 0, created its sandboxed file,
  wrote `reports\acceptance-filesystem\20260729-054933.md`, appended one
  `reports\index.jsonl` line, and registered the next occurrence.
- The three direct `test-tool` probes were rejected before side effects and named
  `Safety:AllowFileDelete`, `Safety:AllowSendEmail`, and `Safety:AllowHttpPost`.
- The deliberate Settings incident was recorded with correlation id
  `4bc5be1fe6f643d78a5986c084ba0108`; the application remained responsive. A fresh invalid
  workflow task produced Runner incident `61f24aaef4a3408ba3e2911c11ec4d50`.
- OpenAI-compatible was saved without a key and returned the real protected-key failure
  without restart. Ollama was then saved, returned healthy with measured latency, and the
  resulting `settings.json` contained no API key.
- The acceptance scheduler registration was deleted, its absence observed, and
  `resync-schedules` recreated it from the task store with the published split-layout Runner.
- The final control pass covered all nine routes plus task and prompt editors, populated
  grids, grouped and ordinary combo pops, Logs, incidents, Dark, and Light. No incident was
  added after the two invalid style references were corrected and republished.

#### Verification and known human follow-ups

- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded
  with zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  66 tests passed, zero failed, and the one environment-dependent Ollama integration test was
  skipped.
- Seven project files contain 40 centrally managed package references, with zero missing
  `Directory.Packages.props` entries and zero inline versions.
- The Gmail leg of `daily-mail-summary` still requires interactive Google consent on the
  target Windows account.
- Diagnostics Save to disk and Reset remain an explicit human acceptance action because
  Reset archives and truncates the user's current rolling logs. Native-dialog high-contrast
  and non-100% DPI checks are also carried as deployment-environment follow-ups.

### Post-Stage 6 usability enhancements — 2026-07-29

#### User-interface changes

- The AI playground now has explicit `Allow Tools` and `Allow Skills` opt-ins. With both
  cleared it preserves the raw provider-completion path. Enabling tools switches to
  `MyLocalAgentService` with all five tool groups; existing safety gates, the filesystem
  sandbox, connector configuration, and the HTTP allowlist remain authoritative.
- Built-in skills are exposed as agent-callable functions only for the new interactive
  opt-in overloads on `IMyLocalAgentService`. Their actual execution remains a plain
  completion without tools. Scheduled tasks, prompt testing, and workflows retain their
  previous behavior, and the normative workflow aliases remain unchanged.
- The task editor now offers common plain-language cron presets, an expandable five-field
  syntax guide, validation, a common-pattern description, and the next three occurrences.
- The Ollama model setting is now an editable combo box populated from the configured
  endpoint. Loading uses a bounded ten-second catalog request and can be refreshed after an
  endpoint change or model installation.

#### Public API additions

- `IOllamaModelCatalog.ListInstalledModelsAsync` exposes the distinct installed Ollama model
  tags; `OllamaClientService` implements the contract and remains the single transport owner.
- `IMyLocalAgentService` has opt-in overloads that accept `allowSkills`.
- `ISkill` now exposes `AgentFunctionName` and `Description` metadata used only when skills
  are enabled interactively.
- `CronSchedulePreset` carries a user-facing scheduler label and cron expression.

#### Verification

- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded
  with zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
  --logger "console;verbosity=normal"`: 69 tests passed, zero failed, and the one expected
  local Ollama integration probe was skipped.

### Standalone user manual — 2026-07-29

- Added `Docs/User-Manual.html`, a French HTML5 user manual that works offline as one file.
  Its dark responsive layout, grouped sticky navigation, instant section search, reading
  progress, active-section highlighting, mobile menu, expandable troubleshooting topics,
  printable layout, and copy controls use only embedded CSS and JavaScript.
- The manual provides step-by-step scenarios for Ollama configuration, a first local
  filesystem task, prompt testing and real execution, Google authorization, Dropbox
  credentials, the AI playground tool/skill opt-ins, isolated tool testing, and the explicit
  OpenAI-compatible cloud switch.
- The reference portion documents every application page, task and workflow rules,
  placeholders, error policies, Tool Map aliases and arguments, cron scheduling, Windows
  registration behavior, safety boundaries, reports, logs, diagnostics, troubleshooting,
  data locations, and runner commands.
- `README.md` now links to the standalone guide. The manual contains no external resource,
  style, script, image, font, or network dependency.
- Validation confirmed 20 HTML sections, 21 valid internal navigation targets, no duplicate
  id, no missing anchor, balanced structural tags, zero external resources, valid inline
  JavaScript syntax, and a clean `git diff --check`.

### English standalone user manual — 2026-07-29

- Added `Docs/User-Manual.en.html` as a complete English counterpart to the French guide.
  It remains a single offline HTML5 file with an embedded dark responsive theme, grouped
  navigation, instant search, reading progress, active-section highlighting, mobile menu,
  expandable troubleshooting topics, printable layout, and code-copy controls.
- The English edition mirrors the eight guided scenarios and the complete reference for
  pages, workflows, Tool Map aliases and arguments, cron scheduling, safety, reporting,
  diagnostics, troubleshooting, storage locations, and runner commands.
- Both manuals now include reciprocal EN/FR language links, and `README.md` links directly
  to both editions.
- Validation confirmed matching 20-section structures, 21 valid internal navigation targets,
  9 expandable reference/troubleshooting topics, 10 copyable code examples, no duplicate id,
  no missing anchor, balanced structural tags, zero external resources, valid inline
  JavaScript syntax, and a clean `git diff --check` for both editions.

### Stage 7 V2 — prompt-injection content trust filter — 2026-07-29

#### Project and dependency graph

- Added `OD.MyLocalAgent.Guardrails` as the eighth solution project. It references only
  `OD.MyLocalAgent.Ollama` and uses the already-pinned Microsoft Extensions packages.
- Core, UI, and Tests add direct Guardrails project references. No central package version or
  inline package version changed.
- The resulting graph is `Guardrails -> Ollama`,
  `Core -> Guardrails, Ollama, Connectors`,
  `UI -> Guardrails, Core, Workflows, Ollama`, and `Tests -> all projects`; no cycle was
  introduced.
- Core composition calls `AddGuardrails(configuration)` immediately after
  `AddLlmProviders(configuration)`. `IContentTrustService` is an explicit singleton with
  instance-owned in-memory caches and monitored options.

#### Public Guardrails API

- `GuardrailsOptions`.
- `ContentTrustStatus`, `ContentSummaryStatus`, and `TrustDisposition`.
- `ContentTrustResult` and `ContentSummaryResult`.
- `IContentTrustService.ScreenAsync` and `SummarizeAsync`.
- `ContentTrustPolicy.Decide`.
- `ContentTrustEnvelope.WrapPlaintext` and `WrapBase64`.
- `GuardrailScreeningException` and `GuardrailSummarizationException`.
- `ContentScreenerPrompt` version `2` and `ContentSummarizerPrompt` version `1`.
- `ServiceCollectionExtensions.AddGuardrails`.

The complete reloadable keys are `Guardrails:ScreeningEnabled`, `CleanThreshold`,
`CautionThreshold`, `MaxContentChars`, `SummarizeOversizedContent`, `MaxSummaryChars`,
`MaxSummarizationSourceChars`, `TimeoutSeconds`, `CacheResults`, and
`MaxGmailMessagesPerOperation`.

#### Screening, reduction, and caching decisions

- Screening uses one system and one nonce-delimited user message, temperature zero, no tools,
  no skills, and no agent wrapper. Verdict parsing clamps JSON integer scores, validates
  verbatim indicators with ordinal matching, de-duplicates them, and retains at most 20.
- The envelope type is the sole owner of warning text, UTF-8 Base64 conversion, delivery
  nonces, and delimiters. Clean delivery is still explicitly marked. Caution, disabled,
  failed, and summary-truncated paths fail closed to marked Base64; low scores are withheld.
- Oversized reduction splits the complete source without dropping or normalizing characters,
  processes chunks sequentially, classifies every source/intermediate input, and aggregates
  the minimum score. The exact final source-specific delivery block is screened again by
  Core. Provider/model changes abort multi-call reduction.
- Verdict and summary caches are instance-owned, memory-only, keyed by content hashes plus
  provider/model/prompt versions and requested maximum, and expire after 24 hours through
  `TimeProvider`. Original content, wrappers, nonces, exceptions, and incidents are not
  cached.
- Caller cancellation is propagated. Technical screening/reduction failures retain the
  original inner exception in a content-free typed exception. Core and Trust filter report
  each caught typed exception once.

#### Core output integration and intentional deviation

- `gmail.readToday` and `gmail.readUnread` now return a root object with message/count/limit
  metadata instead of the Stage-6 bare DTO array. This intentional V2 security deviation
  prevents duplicating raw From, To, Subject, Snippet, and Body values beside the single
  protected canonical message block.
- Gmail body reduction preserves exact connector headers and snippet, substitutes only a
  labeled complete-body summary, and screens the final canonical block. Unavailable reduction
  substitutes the exact Gmail-review marker and sets human-review/trust-scope metadata.
- HTTP GET and GET Text protect the entire capped body or extracted text. Successful
  reduction returns only the protected summary; unavailable reduction preserves safe request
  metadata and returns the exact source-specific skip marker without failing the workflow.
  HTTP POST remains unchanged and is a recorded residual risk.
- Agent-facing Gmail/HTTP JSON contains only suspicious-indicator counts. Verbatim spans are
  reserved for the human Trust filter page and deterministic tests.
- The `MyLocalAgentAI` instruction explicitly applies the untrusted-data rule to plaintext and
  decoded Base64 envelopes.

#### UI, settings, documentation, and earlier-stage files

- Added Trust filter as the eighth permanent rail destination after AI playground and before
  Settings. Its MVVM Toolkit ViewModel supports normal and oversized paths, cancellation,
  score bands, distinct trust/processing statuses, human-only indicators, provider latency,
  and protected preview.
- Settings persists and reloads the complete Guardrails section. It states that disabling LLM
  processing does not disable protection and warns that OpenAI-compatible screening and
  full-source chunk reduction send Gmail/HTTP content to the cloud endpoint.
- Updated the shared specification with ADR H and the V2 addendum, README, both offline
  manuals, theme audit, acceptance report, and Stage-7 final report.

#### Deterministic tests and manual evidence

- Added service tests for wrappers, exact Base64 round-trip, policy boundaries, disabled
  processing, safe typed failure, verdict caching, tool-less requests, complete-source
  chunking, minimum-score aggregation, summary capping, source limits, and cancellation.
- Added Gmail tests for the canonical block, caution Base64, human-only indicator handling,
  user limit, body-marker fallback, and exactly-once failure reporting.
- Added HTTP tests for protected GET/Text behavior, too-large skip, summarization-failure skip,
  exactly-once reporting, and unchanged allowlist/POST gates.
- Added manual-only injection fixtures with five classic attacks, three benign examples, a
  post-first-boundary attack descriptor, and an above-limit source descriptor.
- Release build/test evidence and explicit runner/UI follow-ups are recorded in
  `Docs/stage7-v2-final-report.md`. The sandbox rejected the local loopback runner probe and
  denied the per-user incident path; deterministic transport/provider stubs therefore remain
  the executed automated runner-path evidence in this environment.
- Final dependency hygiene removed both cached copies of `Dropbox.Api 7.2.0`, including the
  incomplete temporary extraction, before a forced online restore with NuGet audit enabled.
  The restored package matches the NuGet.org SHA-256, has a valid repository signature, and
  the obsolete temporary copy remains absent.

#### Residual risks

- Trust scores can produce false positives and false negatives. Encoded, obfuscated, novel,
  or model-specific attacks may evade classification.
- Base64 is not encryption or isolation, and a model may decode it.
- A selected OpenAI-compatible provider receives screened external content and every
  full-source reduction chunk.
- Dropbox, local filesystem, Calendar, Gmail attachments, outbound content, and HTTP POST
  responses are not protected by Stage 7.
- R9 Safety gates, tool whitelists, filesystem sandboxing, and HTTP allowlisting remain the
  hard boundaries.

### Stage 8 — Ollama Web Search — 2026-07-30

#### Contracts, provider boundary, and configuration

- Added reloadable `WebSearchOptions` with disabled-by-default provider selection, Ollama
  endpoint, DPAPI-populated API key, default/cap result limits, and timeout.
- Added the stable `IWebSearchConnector` facade, normalized `WebSearchResult` and
  `WebSearchResultItem` records, and the internal provider-selection boundary
  `IWebSearchProvider`. Stage 8 registers only `OllamaWebSearchConnector`; another provider
  can be added without changing Core or workflow contracts.
- The Ollama provider uses the documented
  `POST https://ollama.com/api/web_search` contract with Bearer authentication and JSON
  `query`/`max_results`. It tolerates missing/unknown response fields, caps parsed results,
  distinguishes caller cancellation from timeout, and throws typed, actionable
  configuration/API failures.
- `WebSearch:Provider` defaults to empty. Valid enabled configuration requires `Ollama`, an
  absolute HTTP(S) endpoint, positive timeout and limits, and a default not greater than the
  cap. The named managed HTTP client reads its timeout from monitored options.

#### Tool Map, Guardrails envelope, and secrets

- Added the sixth `web` tool group and `web.search` / `web_search` Tool Map entry mapped to
  `WebSearchTool.SearchAsync(query, maxResults?)`. Tool definitions now explicitly identify
  optional arguments; registry validation and Tool tester hints/templates use that metadata.
- `WebSearchTool` clamps result requests to 1–10, logs only query length and result count, and
  protects one compact result set. It builds an ordered canonical JSON array containing only
  index/title/content, deterministically retains complete earlier results, truncates the first
  overflowing result with an explicit marker when possible, and omits the remainder.
- The exact bounded canonical string—including `[]`—is sent to
  `IContentTrustService.ScreenAsync` exactly once. Pass-through, Base64, and withholding use
  the shared Guardrails policy/envelope. Disabled and failed screening use marked Base64 with
  no raw fallback; a caught typed failure is reported once; caller cancellation is propagated.
- Agent-facing output exposes only aggregate trust metadata. Title/snippet text exists solely
  inside `protectedResults`; source index/URL is the deliberate metadata whitelist and is
  removed on withholding. Verbatim suspicious indicators never enter tool output.
- Added `WebSearch:ApiKey` to the DPAPI bootstrap whitelist. The Tools page field is
  write-only, and merged settings persistence never writes the key to JSON.

#### UI and persistence

- Added the Web Search cloud card immediately after Dropbox with explicit enablement,
  endpoint, custom-endpoint privacy warning, masked configuration status, Save, and a fixed
  read-only connection test that displays only latency/count or a typed error.
- Added `Web search (web) (cloud)` to the task whitelist editor. Tool tester renders
  `maxResults` as optional and generates required-only JSON.
- General Settings and Tools Web Search saves now share a locked atomic JSON-object merge.
  Known Settings sections are updated without deleting Web Search or unrelated future
  sections; Web Search saves patch only Provider/Endpoint and preserve its other limits.
  Configuration reload remains immediate.

#### Earlier-stage files and documentation

- Touched existing connector composition/options validation, Core composition/secrets/tool
  map/registry, Runner configuration classification, Tools/Task editor/Tool tester UI, user
  settings service, appsettings, and related tests only where Stage-8 integration required it.
- Updated the Stage-8 prompt with product-owner decisions, shared specification with ADR I
  and §13, README, both offline manuals, theme audit, acceptance report, and the new
  `web-daily-brief` sample.
- The actual Ollama request and response shape matched the assumed Stage-8 contract; no
  payload deviation was required.

#### Deterministic verification and residual risks

- Provider tests capture authentication/body/URL, partial and malformed JSON, missing key,
  HTTP authentication error, timeout, cancellation, disabled provider, and unknown provider.
- Tool tests cover exact single-call canonical delivery, all three trust dispositions,
  disabled/failed Base64 fallback, exactly-once incident reporting, cancellation, empty
  result, clamping, URL exclusion, and pre-screen budget truncation/omission.
- Persistence tests read JSON from disk to prove secret exclusion and bidirectional section
  preservation. Tool Map and task validation tests cover the new whitelist and optional
  argument.
- Residual risks are explicit: provider rate limits are not a stable documented contract;
  trust screening remains probabilistic and Base64 is not isolation; whitelisted source URLs
  are not screened; deterministic truncation/omission can lose relevant information; search
  queries and snippets leave the machine for the configured cloud endpoint.
- Safety gates, per-task tool whitelists, filesystem sandboxing, and HTTP allowlisting remain
  the enforceable boundaries.

### Stage 11 — Per-task file-system authorization — 2026-07-30

#### Normative decision

- Added shared specification §14 and the Stage 11 prompt before implementation. The global
  required `FileSystem:RootPath` remains the sandbox for relative paths.
- The canonical task contract gains `fileSystemAccess`, an array of absolute directory rules
  with `ReadOnly` or `ReadWrite` access. Empty means no additional absolute-path grant.
- Absolute paths are authorized only by the current task. The most-specific nested rule wins.
  Windows ACLs, unavailable volumes, S4U limitations, and Safety gates remain independent
  authorities.
- Immutable execution context must flow through deterministic workflows and agent-selected
  tools. Singleton services must not contain mutable current-task state.
- File-system tools and Dropbox local paths share this policy. Tool Map aliases and method
  arguments remain unchanged.
- The repository has no deployed task population, so examples and tests move directly to the
  new canonical task shape without a runtime migration mechanism.

#### Delivered public API and implementation surface

- `FileSystemAccessMode`, `FileSystemAccessRule`, and `ToolExecutionContext`.
- Context-aware `IPathSandbox`, `IToolRegistry`, and `IMyLocalAgentService` operations.
- `IContextualToolFactory<TTool>` creates immutable task-bound file-system and Dropbox tool
  instances without singleton current-task state. Existing taskless registry and agent-service
  overloads remain available for Tool Tester and AI Playground and are limited to relative paths
  under `FileSystem:RootPath`.
- The task editor exposes an Authorized locations card when File System or Dropbox is enabled.
  Users select folders, choose read-only or read/write access, remove rules, see availability
  information, and must confirm a volume-root grant.
- Task validation and serialization reject relative, wildcard, duplicate, malformed, and
  existing-file rules and persist normalized absolute directory paths.
- `PathSandbox` applies separator-aware containment, most-specific-rule precedence, independent
  source/destination checks for move operations, and reparse-point rejection. File deletion
  additionally retains the global `Safety:AllowFileDelete` gate.

#### Verification

- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded with
  zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  145 passed, zero failed, and one intentionally skipped local Ollama integration test, 146 total.
- Tests cover task-rule validation and round trips, relative and absolute sandbox behavior,
  read-only/read-write enforcement, most-specific nested rules, sibling-prefix rejection,
  source/destination move authorization, delete Safety composition, reparse points, concurrent
  context isolation, workflow and agent context propagation, taskless Tool Map behavior, Dropbox
  upload/download access modes, and task-editor add/change/remove flows.
- All JSON samples parse successfully. Both English and French HTML manuals have balanced
  sections, unique IDs, and valid internal navigation targets.
- `git diff --check` reports no whitespace errors.
- No package, persistence migration, installer, executable topology, scheduling, or Windows Task
  Scheduler registration changes were required.

### Stage 12 — Sequential workflow context enrichment — 2026-07-30

#### Scope and decisions

- Stage 9 graph workflows and Stage 10 workflow-library UI are explicitly not being
  implemented. The existing sequential task-owned workflow remains the product model.
- The existing output context is extended without changing persisted step shape or execution
  order. Legacy `{{stepId.output}}`, `{{previous.output}}`, and task placeholders retain
  identical behavior.
- JSON tool outputs now support deterministic property and zero-based array selection, for
  example `{{source.output.content}}` and `{{files.output.entries[0].name}}`.
- `{{workflow.context}}` produces a JSON snapshot of all successful named outputs accumulated
  before the current step. Valid JSON outputs remain structured; plain text remains a JSON
  string.
- `ai.prompt` gains optional boolean `includeContext`, defaulting to `false`. When enabled,
  the accumulated JSON snapshot is appended to the prompt with an explicit instruction that
  tool output is data, not instructions. The model response remains the step output and can
  be consumed by later deterministic tools.
- The existing task editor remains the only workflow editor. It now offers Tool Map-driven
  step insertion, formatting, validation, and a read/translate/write example while preserving
  direct JSON editing.
- The follow-up placeholder contract adds local `Today`/`Now` values with optional custom
  formats. Resolution uses the engine's injected `TimeProvider`; filename-oriented aliases
  make `Today[YYYY]` and `Now[hhmmss]` produce the product-owner-requested values.
- The filesystem Tool Map adds `filesystem.rename(path, newName)` and
  `filesystem.setExtension(path, extension)`. Both are file-only, retain the current
  directory, refuse overwrite through `File.Move`, and independently resolve source and
  destination through the existing read-write sandbox policy.
- `ai.extractActionItems` now specifies a non-invented Markdown checklist contract for one or
  more messages. `ai.classifyEmail` specifies batch classification with one Markdown-table
  row per identifiable message and the closed category set `Urgent`, `ReplySoon`, `Waiting`,
  or `FYI`. Skill output remains the existing JSON `{ "text": "..." }` envelope.

#### Compatibility and safety

- No package, task schema, scheduler, runner command, tool alias, filesystem authorization,
  Safety gate, or external-service change was introduced.
- Unknown properties, malformed paths, invalid indexes, and non-JSON structured lookups fail
  through the existing `WorkflowPlaceholderException` path.
- Rename rejects directory-bearing names; Set Extension rejects directory-bearing or empty
  extensions. Both require read-write authorization and do not weaken the independent delete
  Safety gate.
- Full context is opt-in to limit local-model context growth. Local filesystem content remains
  outside Guardrails classification per §12.1 D11 and §14.1 D9; per-task filesystem
  authorization remains the hard access boundary.

#### Verification

- `dotnet restore OD.MyLocalAgent.sln -m:1`: succeeded using the healthy user NuGet cache.
- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded
  with zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  156 passed, zero failed, one intentionally skipped local Ollama integration test, 157 total.
- Every JSON file under `Docs/samples` parsed successfully and `git diff --check` reported no
  whitespace errors.
- Both standalone manuals validate with 22 balanced sections, 30 unique ids, no duplicate id,
  and no missing internal anchor. Their workflow examples use `.output.text` for the existing
  JSON skill-result envelope.

### Stage 13 — AI Workflow Helper — 2026-07-30

#### Scope and contracts

- Added one reusable modal **AI Workflow Helper** window with explicit Author and Explain modes.
  The Task editor remains the only persistence surface and workflow-array JSON remains the
  source of truth.
- Authoring converts loaded JSON locally into a deterministic, editable description preserving
  string/object form, order, alias, explicit/effective id, compact typed arguments,
  placeholders, policies, retry values, and output name. Generation and single-use refinement
  keep no transcript or hidden history.
- `WorkflowAssistantService` resolves `ILlmClientFactory.Create()` for every attempt and calls
  the selected provider's `IChatClient` directly with exactly one system message, one user
  message, streaming, temperature zero, and `Tools=null`. It does not use the agent, skills,
  connectors, workflow engine, or any side-effect callback.
- Prompt contracts are `workflow-generation-v1` and `workflow-explanation-v1`. Generation
  context is built from `ToolMap.Definitions` and `WorkflowPlaceholderCatalog` version 1 rather
  than a UI alias/placeholder duplicate.
- Strict parsing accepts only a complete workflow JSON array. Shared validation now covers
  required and unknown arguments, supported JSON kinds, placeholder grammar and declaration
  order, ids, retries, aliases, `includeContext`, tool whitelisting, and normal task rules.
- One initial attempt plus at most two deterministic correction attempts is enforced for
  generation, refinement, and explanation. Cancellation preserves partial text, performs no
  repair or incident report, and cannot enable Apply.

#### Requirements, Apply, and privacy boundaries

- `ToolDefinition.LocalPathArguments` centralizes local-path argument, target kind,
  read/read-write access, and rename/set-extension derivation metadata for filesystem and
  Dropbox aliases.
- `WorkflowRequirementAnalyzer` derives non-AI tool groups, missing groups, literal absolute
  directory grants, strongest duplicate access, dynamic-path warnings, volume-root/UNC review
  facts, and current Safety-gate status without inspecting disk contents or target existence.
  Relative paths remain under `FileSystem:RootPath`; placeholders, runtime values, unexpanded
  variables, and malformed values never create grants.
- Every missing task-owned proposal starts unchecked. Safety flags are warnings and are never
  changed. Apply returns canonical JSON plus accepted tool/grant unions and updates all three
  Task editor values as one command. One-level Undo is cleared by save, load, navigation away,
  or a later helper application.
- Assistant descriptions, refinements, prompts, raw/partial responses, explanations, errors,
  paths, and proposal choices are memory-only and are not logged or persisted. Provider
  incidents contain only sanitized operation metadata and a correlation id.
- Explain input is validated locally before opening a model-backed operation. Markdown is
  stored once and projected to Text and restricted in-memory HTML without regeneration.

#### Markdown and WebView2

- Added centrally pinned `Markdig` 1.3.2, the current stable .NET-10-compatible release at
  implementation time, for raw-HTML-disabled Markdown parsing, deterministic plain text, and
  HTML conversion.
- Added centrally pinned `Microsoft.Web.WebView2` 1.0.4078.44, the current stable release at
  implementation time, for WPF rendering. The in-memory document has no script and uses a CSP
  that blocks default resources, scripts, connections, frames, images, media, fonts, objects,
  forms, and base URIs. The control disables developer/context-menu/host-object features and
  blocks navigation, pop-ups, new windows, and downloads.
- Runtime detection or initialization failure switches back to Text. The only recovery action
  is an explicit shell-open of Microsoft's official WebView2 Runtime page through
  `IDesktopInteractionService`; no download or installation is started.

#### Exact file-tree delta

Added:

- Core:
  `LocalPathArgumentDefinition.cs`, `LocalPathDerivationKind.cs`,
  `LocalPathTargetKind.cs`, `WorkflowDefinitionSerializer.cs`,
  `WorkflowDescriptionFormatter.cs`, `WorkflowPathRequirement.cs`,
  `WorkflowPlaceholderCatalog.cs`, `WorkflowRequirementAnalysis.cs`,
  `WorkflowRequirementAnalyzer.cs`, and `WorkflowSafetyRequirement.cs`.
- UI:
  `AIWorkflowHelperWindow.xaml`, `AIWorkflowHelperWindow.xaml.cs`,
  `Controls/SafeMarkdownView.cs`, `Services/AIWorkflowHelperDialogService.cs`,
  `Services/AIWorkflowHelperRequest.cs`, `Services/AIWorkflowHelperResult.cs`,
  `Services/IAIWorkflowHelperDialogService.cs`, `Services/IMarkdownRenderingService.cs`,
  `Services/IWebView2AvailabilityService.cs`, `Services/IWorkflowAssistantService.cs`,
  `Services/MarkdownRenderingService.cs`,
  `Services/WorkflowAssistantPromptBuilder.cs`, `Services/WorkflowAssistantService.cs`,
  `Services/WebView2AvailabilityService.cs`,
  `ViewModels/AIWorkflowHelperMode.cs`, `ViewModels/AIWorkflowHelperState.cs`,
  `ViewModels/AIWorkflowHelperViewModel.cs`, `ViewModels/IDeactivatablePage.cs`, and
  `ViewModels/WorkflowProposalItem.cs`.
- Tests: `WorkflowAssistantCoreTests.cs` and `WorkflowAssistantServiceTests.cs`.

Modified:

- `Directory.Packages.props`, `README.md`, `Docs/Prompts/00-Shared-Specification.md`,
  `Docs/ARCHITECTURE-LEDGER.md`, both standalone manuals, `Docs/theme-audit.md`, and
  `Docs/acceptance-report.md`.
- Core: `ScheduledTaskValidator.cs`, `ToolDefinition.cs`, and `ToolMap.cs`.
- Workflows: `WorkflowPlaceholderResolver.cs`.
- UI: `App.xaml.cs`, `OD.MyLocalAgent.UI.csproj`, `Pages/TaskEditorPage.xaml`,
  `Services/DesktopInteractionService.cs`, `Services/IDesktopInteractionService.cs`,
  `Themes/ControlStyles.xaml`, `ViewModels/MainWindowViewModel.cs`, and
  `ViewModels/TaskEditorViewModel.cs`.
- Tests: `TaskEditorViewModelTests.cs`.

#### Verification and residual risks

- `dotnet restore OD.MyLocalAgent.sln -m:1`: succeeded for all eight projects.
- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded with
  zero warnings and zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  191 passed, zero failed, one intentionally skipped local Ollama integration test, 192 total.
- All four JSON files under `Docs/samples` parsed successfully. `git diff --check` reported no
  whitespace errors.
- The WPF application was not launched in this implementation pass. Author/Explain with a
  live provider, refusal, cancellation, Undo, rendered Markdown, and missing-runtime fallback
  were therefore not claimed as manual GUI verification; deterministic tests cover their
  non-environmental state and policy behavior.
- Generated workflows can be semantically wrong despite valid JSON. Literal analysis cannot
  predict every runtime path or value. Disabled Safety gates can still make an applied
  workflow fail. Context and latency grow with workflow size. Selecting the cloud provider
  sends entered description and workflow context to that configured provider. Markdown
  rendering is best-effort and Text remains the dependable fallback.

### Stage 14 — Hierarchical AI generation settings — 2026-08-03

#### Scope, contracts, and defaults

- Added reloadable `AI:GenerationDefaults` with Temperature `0.2`, Top P `1.0`, and Context
  window `8192`. Validation rejects non-finite or out-of-range sampling values and context
  outside `1024`–`2000000`; values are never clamped.
- Added immutable public contracts `GenerationOptions`, `EffectiveGenerationPolicy`, and
  `GenerationOptionSource`, the pure `GenerationPolicyResolver`, and centralized
  `GenerationPolicyValidation`. Temperature and Top P resolve independently as
  AI step → task → Settings; Context window is always the Settings snapshot.
- Added named program-owned policies `WorkflowAuthoring`, `WorkflowExplanation`,
  `GuardrailScreening`, and `GuardrailReduction`. All fix Temperature 0 / Top P 1 while
  receiving the current global Context window. They are not user-editable or serializable.
- Task and object-form AI-step JSON accept only optional lowercase `temperature` and `topP`.
  Strict parsing rejects null, non-number, non-finite, duplicate, unknown, or out-of-range
  values. Context/provider/model/endpoint/credential/internal-policy fields are therefore
  invalid. Empty objects are accepted on input and omitted from canonical output.

#### Runtime propagation and provider behavior

- `GenerationPolicyChatClient` clones each caller-owned `ChatOptions`, preserves tools,
  model id, response format, provider properties, and cancellation, then merges the immutable
  policy without disposing or mutating the shared provider client.
- Explicit policy-bearing overloads on `ILlmClientService`, `IMyLocalAgentService`, and
  `ISkill` carry the resolved policy across task execution. Prompt-only Runner calls, every
  agent round, agent-exposed skills, `ai.prompt`, direct built-in skills, workflow retries,
  Prompt editor Test, and AI Playground use the intended snapshot. Taskless compatibility
  overloads resolve the current Settings defaults; the provider-neutral default
  `ILlmClientService` policy overload also applies the wrapper instead of discarding a policy.
- A workflow AI step resolves once before its retry loop. A prompt-only task resolves before
  entering the agent. Concurrent executions carry separate immutable values and do not use
  static, singleton-current-task, `AsyncLocal`, or another ambient task context.
- Ollama maps Context window to its native `num_ctx` provider option. OpenAI-compatible calls
  receive Temperature and Top P but no invented context-window request property; the
  configured Context value remains an application-declared total budget and the provider
  remains authoritative. No output-token control or silent content reduction was added.
- Workflow authoring/refinement/correction, explanation/correction, Guardrails screening, and
  Guardrails reduction now call through their named fixed policies. Context is captured once
  per operation so an in-flight call remains stable across configuration reload.

#### UI, persistence, and helper integration

- Settings adds **AI generation defaults** numeric fields, English help text, culture-safe
  comma/dot decimal parsing, inline validation, and disabled Save while invalid.
  `UserSettingsService` merges `AI:GenerationDefaults` without losing provider or unknown
  sibling properties.
- Task editor adds independent **Use global default** states for Temperature and Top P,
  override inputs, effective values, provenance, validation, dirty tracking, save/reload, and
  no task context-window control. Prompt editor Test displays and uses current unsaved
  task-effective values passed as a non-persisted snapshot. AI Playground displays and uses
  Settings values.
- Deterministic workflow descriptions preserve configured overrides and can report effective
  values/provenance when task/global context is available. Authoring prompts expose
  `generationOptions` only for Tool Map AI steps and forbid context/provider/model fields.
  Explain covers configured overrides and provenance while its own request remains fixed.
- The scheduled mail-summary sample uses a Temperature `0.1` task override to demonstrate a
  real repeatability-oriented exception without distributing redundant overrides.

#### Exact file-tree delta

Added:

- Ollama:
  `AIGenerationDefaults.cs`, `EffectiveGenerationPolicy.cs`, `GenerationOptionSource.cs`,
  `GenerationOptions.cs`, `GenerationPolicyChatClient.cs`, `GenerationPolicyResolver.cs`,
  `GenerationPolicyValidation.cs`, `InternalGenerationPolicies.cs`, and
  `InternalGenerationPolicy.cs`.
- Core: `GenerationOptionsJsonConverter.cs`.
- Tests: `GenerationPolicyTests.cs`, `GenerationSurfaceViewModelTests.cs`, and
  `RunnerGenerationPolicyTests.cs`.

Modified:

- Root/docs: `appsettings.json`, `README.md`,
  `Docs/Prompts/00-Shared-Specification.md`, `Docs/ARCHITECTURE-LEDGER.md`,
  `Docs/User-Manual.html`, `Docs/User-Manual.en.html`, `Docs/theme-audit.md`,
  `Docs/acceptance-report.md`, and `Docs/samples/daily-mail-summary.json`.
- Ollama: `AIOptions.cs`, `DynamicOllamaChatClient.cs`, `DynamicOpenAIChatClient.cs`,
  `ILlmClientService.cs`, `LlmOptionsValidation.cs`, `OllamaClientService.cs`,
  `OpenAIClientService.cs`, and `ServiceCollectionExtensions.cs`.
- Core: `ClassifyEmailSkill.cs`, `ExtractActionItemsSkill.cs`,
  `IMyLocalAgentService.cs`, `ISkill.cs`, `MyLocalAgentService.cs`, `ScheduledTask.cs`,
  `ScheduledTaskSerializer.cs`, `ScheduledTaskValidator.cs`, `SkillBase.cs`,
  `SummarizeTextSkill.cs`, `WorkflowDescriptionFormatter.cs`,
  `WorkflowStepDefinition.cs`, and `WorkflowStepDefinitionJsonConverter.cs`.
- Guardrails/Workflows/Runner: `ContentTrustService.cs`,
  `SequentialWorkflowEngine.cs`, `WorkflowStep.cs`, and `RunnerCommandHandlers.cs`.
- UI pages/services:
  `Pages/AIPlaygroundPage.xaml`, `Pages/PromptEditorPage.xaml`,
  `Pages/SettingsPage.xaml`, `Pages/TaskEditorPage.xaml`,
  `Services/AIWorkflowHelperRequest.cs`, `Services/UserSettingsData.cs`,
  `Services/UserSettingsService.cs`, `Services/WorkflowAssistantPromptBuilder.cs`, and
  `Services/WorkflowAssistantService.cs`.
- UI ViewModels:
  `AIPlaygroundViewModel.cs`, `AIWorkflowHelperViewModel.cs`,
  `MainWindowViewModel.cs`, `PromptEditorViewModel.cs`, `SettingsViewModel.cs`, and
  `TaskEditorViewModel.cs`.
- UI messages: `EditPromptMessage.cs`.
- Tests:
  `ContentTrustServiceTests.cs`, `MyLocalAgentServiceTests.cs`,
  `TaskEditorViewModelTests.cs`, `UserSettingsServiceTests.cs`,
  `WorkflowAssistantServiceTests.cs`, and `WorkflowEngineTests.cs`.

#### Verification and residual risks

- Release solution test before final stage gates:
  237 passed, zero failed, one intentionally skipped local Ollama integration test,
  238 total. Final restore/build/test, sample parsing, standalone-manual validation, and
  whitespace gates are recorded after their execution below.
- Deterministic coverage includes defaults/ranges, strict and backward-compatible JSON,
  every AI/non-AI alias, every precedence/provenance combination, monitored reload,
  agent/skill/workflow propagation, retry stability, concurrent isolation, fixed internal
  policies, option preservation, Ollama `num_ctx`, OpenAI-compatible non-invention, culture
  parsing, editor command state, save/reload, and privacy-safe policy logging.
- The WPF application was not launched in this implementation pass. Dark/Light appearance,
  keyboard traversal, narrow layout, long values, and live provider/model behavior remain
  explicit human checks rather than claimed GUI evidence.
- Context capacity remains model-dependent. The declared value cannot enlarge a model, is not
  a maximum-output-tokens control, and never causes silent truncation. OpenAI-compatible
  endpoints may reject an oversized request according to their own fixed capacity. Provider
  request failures continue through existing typed execution, incident, and report paths.
- Temperature/Top P semantics and accepted ranges may vary slightly by provider/model.
  Changing both simultaneously is harder to reason about, and higher randomness can reduce
  structured-output and tool-selection reliability. Provider tokenization is authoritative;
  the application declaration is not a universal exact token counter.

#### Final Stage 14 gate output

- `dotnet restore OD.MyLocalAgent.sln -m:1`: succeeded; three projects restored and five of
  eight were already current. The first sandboxed attempt could not reach NuGet, and a second
  attempt exposed an incomplete isolated temporary-cache package; the successful required
  gate used the normal user NuGet cache without changing source files.
- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: succeeded
  for all eight projects with `0 Warning(s)` and `0 Error(s)`.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  237 passed, zero failed, and one intentionally skipped local Ollama integration test, 238 total.
- All four JSON files under `Docs/samples` parsed successfully.
- Both standalone manuals passed the structural HTML gate: each has 22 balanced sections,
  30 unique ids, no duplicate id, and no missing internal anchor.
- `git diff --check`: clean.

### Stage 15 — Agent loop hardening and run observability — 2026-07-31

#### Product decisions and specification alignment

- Added normative §18 to `Docs/Prompts/00-Shared-Specification.md`. It explicitly supersedes
  the former `ai.prompt` agent-tool behavior: deterministic workflow prompt steps are now one
  direct, tool-less provider request after placeholder/context resolution.
- The AI Workflow Helper follows the currently selected Light or Dark theme. This is an
  explicit product-owner evolution from the original dark-only R6 while retaining the same
  semantic resources, restricted rendering, and Text fallback.
- Existing credential, R9, task whitelist, filesystem authorization, Guardrails, and
  generation-policy boundaries remain unchanged.

#### Runtime safety and connector surface

- Agent-capable executions allow two consecutive function errors, terminate unknown calls, and
  raise `AgentToolLoopException` when the same (function, sanitized arguments, serialized
  result) triple occurs three times consecutively with no different call in between. The
  earlier eight-function-round cap and the arguments-only repeated-call signature were
  superseded by the v1.3.3 loop-guard hardening (2026-08-06): the guard keys on the repeated
  result, the stuck third occurrence executes so its result can be observed, and no fourth
  identical call is made.
- Added `IMyLocalAgentService.RunPromptWithoutToolsAsync`; `SequentialWorkflowEngine` uses it
  exclusively for `ai.prompt`, including the step-effective generation policy.
- Gmail read aliases accept optional positive `maxMessages`; new `gmail.readRecent` accepts
  `daysBack` (0–3650) and optional `maxMessages`. The connector stops pagination at the
  requested limit while D9's independent Guardrails processing limit and explicit counts stay
  intact.
- Added `calendar.read(period)` with local `Day`, Monday-based `Week`, `Month`, and `Year`
  boundaries while preserving `calendar.today`.

#### Logs, Run now, and helper UI

- `ToolCallLog` serializes sanitized arguments as readable compact JSON. Gmail, Calendar, and
  Web Search completion messages include returned counts; Dropbox messages include bounded
  item names and byte sizes. No configured key or token is added.
- Runner/UI communication adds a reserved Base64 context line. Run now exposes **Show workflow
  context**, refreshes after every successful step, retains the last context after completion,
  replaces it at the next task start, and automatically scrolls runner output to new lines.
- `SafeMarkdownView` now distinguishes its own `NavigateToString` navigation from blocked
  external navigation, fixing the empty Rendered Markdown tab. Window resources and generated
  restricted HTML follow the active theme.

#### Verification

- Release build: all eight projects succeeded with zero warnings and zero errors.
- Deterministic tests: 256 passed, zero failed, one intentionally skipped local Ollama
  integration test, 257 total.
- All four sample JSON files parsed. Both standalone manuals retain 22 balanced sections and 30 unique ids with no duplicate id or missing internal anchor. `git diff --check` passed.
- `scripts/publish.ps1 -NoRestore` produced the self-contained `win-x64` UI and Runner in the
  standard per-user installation. The published Runner `--help` smoke check exited with code 0.
- The real Gmail task supplied as data was neither created nor executed. Live AI Helper theme,
  WebView2, and real-workflow execution remain explicit human checks.

### Stage 17 — Workflow authoring aids — 2026-08-02

#### Product decisions and specification alignment

- Implemented from `Docs/Prompts/17-Stage17-WorkflowAuthoringAids.V2.md`; the original
  `Docs/Prompts/OldSpecs/17-Stage17-WorkflowAuthoringAids.md` is retained as historical
  specification. §20 of `Docs/Prompts/00-Shared-Specification.md` was rewritten to match the V2,
  extending the locked decisions to S17-D21.
- Two deterministic, memory-only authoring aids were added to the existing Task editor workflow
  card: a **Tool catalog** dialog and a **Workflow prompt editor** dialog. Neither calls a
  model, an agent, a tool, a skill, the filesystem, or the network. No persisted task field,
  file, setting, page, workflow model, or migration was introduced, and no package was added.
- Five V2 corrections over the V1 specification are now normative: the sequential caret rule
  (inside an element inserts after it), the synthetic `ai` group is not whitelistable,
  `includeContext` is an independent option never driven by the generic optional-arguments
  toggle, prompt working text is LF-canonical, and a missing, null, non-string, empty, or
  whitespace-only `ai.prompt` prompt is invalid in every validation path.
- No automatic context inference: a direct prior-output placeholder combined with
  `includeContext` true raises a non-blocking duplication warning and changes neither value.

#### Core

- New public surface: `WorkflowArgumentKind` (`String`, `Integer`, `Number`, `Boolean`,
  `Enumeration`), `ToolArgumentDefinition(Name, Kind, IsRequired, Description)` with optional
  `Example`, `AllowedValues`, and `SupportsPlaceholders`, plus `ToolDefinition.Description` and
  `ToolDefinition.ArgumentDefinitions`. `Arguments` and `OptionalArguments` remain as
  projections of that metadata and keep their existing values, so `ToolRegistry`, the workflow
  engine, the validator, and the Stage 13 analyzer are unaffected. `ToolGroups.Ai` and
  `SafetyOptions.IsEnabled` were centralized to remove duplicated literals.
- `WorkflowSnippetBuilder` and `WorkflowSnippetOptions` build one object-form
  `WorkflowStepDefinition` with a unique id, required arguments always present, optional
  arguments only on request, typed placeholder defaults, an explicit `includeContext` for
  `ai.prompt`, and error-policy/generation-option blocks that apply to AI steps only.
- `WorkflowCaretInsertionService` and `WorkflowCaretInsertionResult` clamp the caret to the text
  length, move it off a UTF-16 surrogate pair boundary, map the character offset to a byte
  offset over top-level element spans computed with `Utf8JsonReader`, and insert structurally.
  Unparseable JSON falls back to a verbatim snippet at the clamped caret with an explicit repair
  warning.
- `WorkflowPromptEditingService`, `WorkflowPromptEntry`, and `WorkflowPromptEdit` extract the
  ordered `ai.prompt` entries keyed by declaration position, normalize prompt text to LF, flag
  repair cases and normalized line endings, and rewrite only the `prompt` and `includeContext`
  arguments while preserving every unrelated argument. `ReferencesWorkflowOutput` detects
  `workflow.context`, `previous.output`, and prior-step output placeholders for the duplication
  warning. Position keying makes duplicate step ids unambiguous.
- `WorkflowPromptPreviewProjection` renders a display-only single-line preview: line feeds and
  tabs become visible markers, remaining C0 controls and DEL use their Unicode control picture,
  and truncation at 120 text elements never splits a grapheme cluster. The projection is never
  written back to prompt text.
- `ScheduledTaskValidator` was tightened: an `ai.prompt` `prompt` must be a non-empty,
  non-whitespace string, carriage returns are rejected, duplicate argument names are rejected
  case-insensitively, and argument optionality now derives solely from Tool Map metadata.

#### UI

- `TextBoxSelectionBinding` is a reusable attached behavior exposing `CaretIndex`,
  `SelectionStart`, and `SelectionLength` with a reentrancy guard and length clamping. All id
  generation, snippet construction, offset mapping, insertion, extraction, rewriting, and
  preview projection stay in non-WPF classes.
- Tool catalog: `WorkflowToolCatalogViewModel`, `WorkflowToolCatalogWindow`,
  `IWorkflowToolCatalogDialogService`/`WorkflowToolCatalogDialogService`, and the
  `WorkflowToolCatalogRequest`/`WorkflowToolCatalogResult`/`WorkflowToolCatalogInsertion`
  contracts. The session accumulates into a working copy: Insert keeps the dialog open, Insert
  and close returns the accumulated result, Close returns it only when an effective change
  exists, Cancel discards the session, and closing with pending insertions requires an explicit
  confirmation. Missing non-AI groups require an unchecked opt-in; the `ai` group never appears
  in an accepted-group result.
- Workflow prompt editor: `WorkflowPromptEditorViewModel`, `WorkflowPromptEditorItem`,
  `WorkflowPromptEditorWindow`,
  `IWorkflowPromptEditorDialogService`/`WorkflowPromptEditorDialogService`, and the
  `WorkflowPromptEditorRequest`/`WorkflowPromptEditorResult`/`WorkflowPromptEditorChange`
  contracts. Apply is enabled only when an effective change exists and no prompt is blank; it
  validates, writes every pending edit at once and closes. Revert step restores one entry,
  Cancel discards the session, and there is deliberately no Close command. The result carries
  positions and effective ids but never prompt content.
- `TaskEditorViewModel` gained `WorkflowCaretIndex`, `OpenToolCatalogCommand`, and
  `OpenWorkflowPromptEditorCommand`, and delegates `DeserializeWorkflow` to
  `WorkflowDefinitionSerializer` instead of a divergent private parser. The Stage 13 snapshot
  became the single assisted-change snapshot shared by the AI Helper, the catalog, and the
  prompt editor; its members were renamed to `HasAssistedUndoSnapshot`,
  `UndoAssistedChangeCommand`, `UndoAssistedChange`, `CanUndoAssistedChange`,
  `_assistedUndoSnapshot`, and `ClearAssistedUndo`, and the button is now
  **Undo assisted change**.

#### Privacy

- Workflow JSON, prompt text, argument values, paths, selections, and dialog working copies are
  never logged, incident-reported, placed in telemetry, or persisted. The only outbound copy is
  the explicit Copy placeholder action in the catalog. `WorkflowPromptEditorResult` reports
  counts, positions, and effective ids, never prompt content.

#### Verification

- Release build: zero warnings and zero errors (`-m:1`).
- Deterministic tests: 367 passed, zero failed, one intentionally skipped local Ollama
  integration test (368 total). New coverage spans Tool Map metadata consistency and projection
  stability, snippet building for every argument shape, structured insertion at every array
  position including empty arrays and in-string carets, invalid-JSON fallback, catalog session
  accumulate/cancel semantics, tool-group opt-in, translation example, and assisted Undo.
- Manual checks remaining for the operator: a scheduled task fires without any visible
  window, and the Runs page follows a live execution joined mid-run.

### Stage 18D — Declarative dynamic tooling

- Added declarative tool attributes: `[ToolGroup]`, `[Tool]`, `[ToolArgument]`, and `[ToolLocalPath]`, plus `ToolActivationKind` (`Singleton`, `Contextual`).
- Built `IToolCatalog` / `ToolCatalog` as an immutable fail-fast dynamic catalog discovered from compiled Core types and methods via reflection.
- Decorated all built-in tools (`GmailTool`, `CalendarTool`, `DropboxTool`, `FileSystemTool`, `HttpTool`, `WebSearchTool`) with declarative attributes.
- Refactored `ToolGroups` and `ToolMap` into lightweight compatibility façades over `ToolCatalog.Default`.
- Added `IToolTargetActivator` / `ToolTargetActivator` for generic instance resolution.
- Refactored `ToolRegistry` and `HostApplicationBuilderExtensions` to perform dynamic catalog registration and activation without per-type factory wiring. Removed `IContextualToolFactory` and `ContextualToolFactory`.

### Stage 19 — SQLite native runtime security correction — 2026-08-05

- The original `Microsoft.Data.Sqlite` 10.0.10 meta-package transitively selected
  `SQLitePCLRaw.bundle_e_sqlite3` / `SQLitePCLRaw.lib.e_sqlite3` 2.1.11. NuGet reports that
  embedded native bundle as affected by GHSA-2m69-gcr7-jv3q, with no patched package version
  available at the time of implementation.
- Core now uses `Microsoft.Data.Sqlite.Core` 10.0.10 with the deliberate direct runtime
  selection `SQLitePCLRaw.bundle_winsqlite3` 2.1.11. This preserves the Microsoft ADO.NET API
  while using the SQLite runtime serviced by Windows and removes the vulnerable embedded bundle
  from the transitive graph. No NuGet-audit suppression is retained.

### Stage 19 — Bounded local SQLite tool — 2026-08-05

#### Public surface and safety boundary

- Added the contextual `SqliteTool` / `ISqliteTool` with exactly four declarative aliases in the
  `sqlite` group: `sqlite.listObjects`, `sqlite.describeTable`, `sqlite.query`, and
  `sqlite.execute`. The central catalog places the group after `filesystem` and before `http`;
  the Task editor, Tool catalog, Tool tester, AI Playground, workflow validation, snippets, and
  helper projections derive it from that metadata.
- Every call receives an authorized database file path, never a caller-controlled connection
  string. Read aliases require existing ReadOnly path access; `sqlite.execute` requires ReadWrite
  access, the task whitelist, and the disabled-by-default `Safety:AllowSqliteWrite` switch.
  Missing files are created only for an explicit write with `createIfMissing=true`.
- Query and execute allow one prepared statement only. Parameters are named scalar JSON values
  bound by the provider; transaction controls, attachments, extensions, memory modes, unsafe
  SQLite administration statements, and internal schema objects are rejected. Writes run in one
  application-owned transaction with foreign keys enabled and roll back on error or cancellation.

#### Limits, privacy, and Guardrails

- `SQLiteOptions` owns validated limits for default and maximum rows, result characters, BLOB
  bytes, SQL characters, and busy timeout. Read results retain ordered column metadata and
  array-shaped rows; oversized data is omitted or truncated at deterministic object/row
  boundaries.
- A task persists `SqliteGuardrailsEnabled`. The Settings default is copied only for a newly
  created task. Enabled reads use screen-only Guardrails on the complete bounded payload and fail
  closed; explicit opt-out returns the bounded structured result without claiming screening.
- SQL, paths, parameters, identifiers, database values, BLOBs, protected data, and raw provider
  exceptions remain excluded from logs and incidents. The tool uses private non-pooled
  connections and the Windows-serviced `winsqlite3` runtime; no `e_sqlite3.dll` or alternate
  native bundle is shipped.

#### Verification and documentation

- Targeted SQLite automated coverage passed 51 tests. The complete non-interactive Release suite
  passed 528 tests with zero failures and one explicitly skipped local Ollama probe; the
  sequential Release build completed with zero warnings and zero errors.
- Manual WPF verification confirmed the SQLite Settings controls and validation, task-editor
  selection/Guardrails persistence, and dynamic Tool Tester, AI Playground, and Tool catalog
  projections. The checks used no personal database, personal task, user configuration, or real
  LLM request.
- README, both standalone user guides, the SQLite task sample, acceptance report, and this ledger
  document the file-path-only syntax, authorization, named scalar parameters, limits, Guardrails,
  and synthetic examples.

### Process execution tools — 2026-08-09

- Added `filesystem.shellExecute` and `filesystem.powerShell` to the existing contextual File
  system group. Declarative catalog discovery automatically exposes both aliases to the Task
  editor, Tool catalog, Tool tester, AI Playground, workflow validation, snippets, and agent
  functions; no handwritten UI list or registry entry is needed.
- Both aliases require the disabled-by-default `Safety:AllowProcessExecution` switch, persisted
  through Settings. `waitForExit` controls whether a workflow waits for process completion.
  PowerShell may return standard output and error with `captureOutput`; Windows shell execution
  cannot capture output and rejects that option.

### Project-wide AI instructions — 2026-08-09

- Added the reloadable non-secret `AI:GlobalSystemPrompt` setting for stable project conventions.
  It is appended to the existing agent system instructions and cannot replace built-in safety
  rules, tool availability, or untrusted-data handling.
- Added the persisted `ScheduledTask.UseGlobalSystemPrompt` opt-out, defaulting to `true` for
  existing and new tasks. The preference flows through prompt-only agent execution and direct
  workflow `ai.prompt` calls.
- Settings and the Task editor document the scope. The task editor exposes the opt-out beside a
  **View system prompt** modal action, without expanding the main editor layout.

### Scoped task and workflow model selection — 2026-08-09

- Added optional persisted `ScheduledTask.Model` and object-form `WorkflowStepDefinition.Model`.
  A task model scopes every model call made by that task; a `model` declared on `ai.prompt`
  scopes only that step and takes precedence. The workflow validator rejects empty model names
  and step-level use outside `ai.prompt`.
- `ModelExecutionScope` uses an asynchronous execution scope with deterministic restoration in
  `Dispose`, so no provider option, Settings value, concurrent task, retry, cancellation, or
  failure can retain a temporary model selection.
- `ModelOverrideChatClient` clones request options and forces only the scoped `ModelId`.
  Dynamic Ollama and OpenAI-compatible clients then instantiate their provider request with that
  requested id rather than rewriting the configured default. Model placeholders use the same
  effective name.
- Runner wraps task execution and test-prompt execution in the task scope. A workflow
  `ai.prompt` establishes its nested scope and, for Ollama, performs a real one-token warm-up
  immediately before the selected step. The existing task-level warm-up likewise loads the
  task-selected model. Missing local models fail safely without configuration mutation.
- Added round-trip and request-option tests. Release build and full Release test suite passed
  with `-m:1` (538 passed, one intentionally skipped local Ollama integration test).
# Stage 22 — local content parsing

Core centrally references SharpCompress 0.50.1 (MIT), DocumentFormat.OpenXml 3.5.1 (MIT),
ExcelDataReader 3.9.0 (MIT), and PdfPig 0.1.15 (Apache-2.0). The public surface is contextual
`IArchiveTool`, `IDocumentTool`, `ISpreadsheetTool`, and `IPdfTool`; its ten aliases are discovered
from attributes. `FileContent` owns all parser limits and `Safety:AllowArchiveWrite` defaults to
false. Parser output is compact JSON marked `untrustedContent`; parser errors use stable,
content-free categories.

Verification on 2026-08-09 confirmed the package nuspec licenses and that the published UI and
Runner each contain SharpCompress, OpenXML, ExcelDataReader, and PdfPig runtime assemblies.
The resolved Open XML transitive dependency is `DocumentFormat.OpenXml.Framework 3.5.1`, which
resolves `System.IO.Packaging 10.0.2`; the other three parser packages add no separate NuGet
dependency in Core's `net10.0` target. The NuGet.org direct/transitive vulnerability audit reported
no known vulnerability in any solution project.

Archive reads require an allowlisted extension and detected container agreement. ZIP-based Office,
NuGet, JAR, and EPUB marker structures fail closed when renamed to `.zip`. Compressed TAR streams
are expanded into bounded application-owned temporary TAR files so the seekable archive API can
preflight the complete graph; raw BZIP2/LZIP/XZ/Zstandard streams use a bounded synthetic single
entry and delete their owned temporary container on disposal. ZIP writes remain the only public
archive mutation, are Safety-gated, stage a complete sibling, validate it, revalidate authorization,
and only then replace the destination.

The final Release gate restored cleanly, built with zero warnings/errors, and ran 567 tests
(566 passed and the expected local Ollama probe skipped). Both self-contained `win-x64` publishes
contain all parser/runtime assemblies. Reflection smoke tests loaded the published Core and read
synthetic ZIP, DOCX, XLSX, and PDF content. Six JSON samples parse, both manuals have seven
copyable Stage 22 scenarios with structural/anchor/parity tests and valid embedded JavaScript, and
`git diff --check` passes. Live WPF inspection was attempted through the Windows-control bridge,
but that bridge was unavailable in the execution environment; XAML, binding, persistence, and
dynamic-catalog behavior are covered by compilation and deterministic tests.

### Stage 23 — web discovery tool

Added the `research.discover` workflow tool for bounded web-document discovery with search-theme
compilation, freshness-window expansion, document classification, and late protected delivery.

#### Files created

- `src/OD.MyLocalAgent.Core/ResearchDiscoverTool.cs` — main deterministic collection and protected-delivery implementation
- `src/OD.MyLocalAgent.Core/IResearchDiscoverTool.cs` — tool interface
- `src/OD.MyLocalAgent.Core/IResearchTranslationService.cs` and `ResearchTranslationService.cs`
  — dedicated bounded, tool-less translation boundary
- `src/OD.MyLocalAgent.Core/QueryCompiler.cs` — internal query compilation (themes → queries)
- `src/OD.MyLocalAgent.Core/UrlCanonicalizer.cs` — internal URL normalization
- `src/OD.MyLocalAgent.Core/SearchHit.cs` — data contract
- `src/OD.MyLocalAgent.Core/WebDocument.cs` — data contract
- `src/OD.MyLocalAgent.Core/WebDocumentKind.cs` — enum (Article, Documentation, ReleaseNote, Repository, Post, Video, Other)
- `src/OD.MyLocalAgent.Core/PublicationDateStatus.cs` — enum (Verified, Unknown)
- `src/OD.MyLocalAgent.Core/FreshnessWindow.cs` — enum (Sliding, None)
- `src/OD.MyLocalAgent.Core/Theme.cs` — data contract
- `tests/OD.MyLocalAgent.Tests/ResearchDiscoverContractTests.cs` — 20 contract tests (13 QueryCompiler/UrlCanonicalizer + 6 catalog + 1 ToolCatalog)
- `tests/OD.MyLocalAgent.Tests/WebSearchIntegrationTests.cs` — 5 network integration tests

#### Modified files

- `src/OD.MyLocalAgent.Core/ToolGroups.cs` — added `Research = "research"` constant (display order 1200)
- `src/OD.MyLocalAgent.Core/OD.MyLocalAgent.Core.csproj` — added `InternalsVisibleTo` for test project
- `tests/OD.MyLocalAgent.Tests/ToolCatalogTests.cs` — added `"research"` to expected task whitelist
- `tests/OD.MyLocalAgent.Tests/SqliteCatalogContractTests.cs` — added `"research"` to expected task whitelist
- `tests/OD.MyLocalAgent.Tests/ToolMapMetadataTests.cs` — added `research.discover` to expected argument contracts
- `tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj` — documented test filter conventions
- `AGENTS.md` — documented default test filter for agents

#### Decisions and deviations

- `QueryCompiler` and `UrlCanonicalizer` are `internal static` classes, exposed to tests via `InternalsVisibleTo`.
- `CompiledQuery` record and `QueryIntent` enum are `internal` types, accessible from test project.
- All parameters are inline (S23-D1); no profiles, no database, no Settings UI (S23-D1/D2).
- `freshnessWindow = "sliding"`: one seven-day search collection is evaluated locally against the
  2→4→7 local-calendar windows until verified in-window docs ≥ `minimumDocuments`; old, future,
  and unknown-date documents are excluded. `windowHistory` exposes eligibility and stop reasons.
  `"none"` keeps the unfiltered and undated behavior.
- A discovery run accepts at most eight themes and performs at most eight paid search requests:
  one primary per theme, then conditional single fallbacks while budget remains. Preferred domains
  are a local ranking signal and never generate extra searches. Page-fetch limits remain advanced
  compatibility safeguards for runtime, memory, politeness, and pathological results.
- Search-provider failures are never represented as genuine empty discovery. The result exposes
  `searchStatus`, success/failure counts, and the last HTTP code. HTTP 429 stops remaining phases
  and yields a rate-limit diagnostic instead of an unsupported zero-source conclusion.
- Tool auto-discovered via `[ToolGroup]` / `[Tool]` attributes; `ToolCatalog.FromAssembly` reflection.
- Reuses existing `IWebSearchConnector`, `IContentTrustService`, `IIncidentReporter`, and
  `IHttpClientFactory` infrastructure. Search, retrieval, date extraction, filtering,
  deduplication, and ranking execute before Guardrails.
- The final bounded title/summary JSON is the unit screened and delivered through
  `protectedResults`; raw external text never appears in candidate metadata. Disabled or failed
  screening keeps mandatory marked-Base64 delivery.
- Translation uses `IResearchTranslationService` only after a clean source verdict. Its exact
  translated JSON receives a second final screening before delivery; other dispositions skip it.
- The bundled daily LinkedIn workflow emits no post for zero candidates. The Sunday long-form
  workflow emits only a factual diagnostic for zero candidates, a focused single-source article
  for one, a limited cross-analysis for two, and a full multi-source article from three.
- Document classification is URL-based heuristic; no AI classification step.
- Integration tests read API key from DPAPI secret store (`%LOCALAPPDATA%\OD.MyLocalAgent\secrets.dat`).
- Default test filter excludes integration tests: `dotnet test --filter "Category!=Integration"`.

#### Verification

- Build: 0 warnings, 0 errors
- Tests: 594 passed (589 unit + 5 integration), 1 skipped (local Ollama)
- Git tag `AVANTSPEC23` created and pushed

### Per-task capability authorization and workflow long-text editing — 2026-08-11

- Replaced global Safety configuration gates with stable `TaskCapability` values persisted on
  `ScheduledTask`; missing collections default to empty and no migration infers grants.
- `ToolRegistry` centrally filters agent tools and rejects every direct invocation whose immutable
  `ToolExecutionContext` lacks the Tool Map-declared capability. Tool groups and path sandbox rules
  remain independent mandatory checks.
- Initial capabilities cover Gmail send, permanent deletion, Recycle Bin, HTTP POST, SQLite write,
  PowerShell, Shell Execute, and archive write/extraction. PowerShell and Shell Execute are distinct.
- Task editor, Tool catalog, AI Workflow Helper, and Tool Tester now operate on task permissions;
  taskless AI Playground and Tool Tester contexts expose no sensitive aliases.
- Tool argument metadata now includes `IsLongText`; the atomic **Edit workflow text** dialog edits
  `ai.prompt.prompt` with `includeContext` and `filesystem.powerShell.script` with a local-execution warning.

### Stage 24 — Direct Chat persistence and context memory — 2026-08-12

- Added **Direct Chat** immediately after **Tasks** in the permanent WPF navigation rail.
  `DirectChatPage` and `DirectChatViewModel` provide multiple conversations, streamed replies,
  cancellation, safe Markdown rendering with text fallback, deletion confirmation, progressive
  message loading, and automatic titles based on the first user message. Direct Chat deliberately
  exposes no tools or skills.
- Added `DirectChatConversation`, `DirectChatMessage`, role/state enums, and the
  `IDirectChatStore` contract in Core. `SqliteDirectChatStore` owns
  `<FileSystem:RootPath>\.od-my-local-agent\data\direct-chat.db`, schema version 2, parameterized SQL,
  foreign-key cascades, transactional updates, deterministic ordering, and recovery of abandoned
  `Pending`/`Streaming` messages as `Failed`. The Windows-serviced SQLite provider initialization
  is centralized in `SqliteNativeBootstrap` and shared with `SqliteTool`.
- The database uses `journal_mode=DELETE`, `synchronous=FULL`, no connection pooling, and one
  application synchronization gate. Assistant content is checkpointed no more than once per
  second. The file is local but not encrypted; only secrets in `secrets.dat` receive DPAPI
  protection.
- Direct Chat attachments are persisted with their user message. Text, PDF, DOCX, and Excel files
  are locally converted to bounded text; PNG, JPEG, WebP, GIF, and BMP remain provider-neutral
  `DataContent` for vision-capable models. Image support is decided by the active provider/model.
- `DirectChatContextBuilder` projects a fixed chat instruction, the optional global system prompt,
  cumulative memory, and messages after the summary boundary into provider-neutral
  `Microsoft.Extensions.AI.ChatMessage` instances. `ConservativeTokenEstimator` supplies the
  provider-independent size estimate.
- `DirectChatHistoryCompressor` runs a deterministic internal model call when estimated input
  reaches the configured ratio. It summarizes only a complete old-message prefix, preserves at
  least the configured recent user turns, uses the target context ratio to choose the prefix,
  and atomically advances `SummaryThroughSequence`. Original messages remain stored and visible.
- `DirectChatOptions` defaults to a 70% trigger, 45% target, 25% response reserve, four recent
  turns, 1,024 summary tokens, and four estimated characters per token. The declared total window
  remains `AI:GenerationDefaults:ContextWindowTokens`.
- Added SQLite persistence/recovery/cascade tests, context and compression tests, a ViewModel
  streaming-to-persistence test, and a navigation-order assertion. The final Release build passed
  with zero warnings and errors; the non-integration suite passed 599 tests with the expected
  local Ollama probe skipped.

### Provider interoperability, streamed workflow prompts, and model overrides — 2026-08-12

- Moved the persistent **Active provider** selector from the Settings header to the
  **Test connection** row. The target of a health test is now visible at the control which starts
  it, while both Ollama and OpenAI-compatible settings remain independently persisted and can be
  switched without reconfiguration.
- Added `IOpenAIModelCatalog`, implemented by `OpenAIClientService`, and registered it from
  `AddLlmProviders`. The Settings OpenAI-compatible model field is now an editable catalog with a
  refresh command, progress/status reporting, and safe manual fallback. The catalog parses the
  standard `GET /models` `data[].id` response, removes case-insensitive duplicates, and sorts ids.
- Corrected OpenAI-compatible health validation to use the same authenticated `GET /models`
  catalog and verify the configured id. This replaces `GET /models/{model}`, which causes a 404 at
  OpenRouter although the endpoint and model are valid. API keys remain DPAPI-only.
- OpenAI-compatible workflow `ai.prompt` now consumes the streaming response path and aggregates
  ordered text fragments. This avoids the Microsoft OpenAI adapter's non-streaming choice-shape
  failure observed with OpenRouter. `DynamicOpenAIChatClient` also sets
  `OpenAIClientOptions.NetworkTimeout` from `OpenAIOptions.TimeoutSeconds`, removing the hidden
  shorter SDK network timeout.
- Added sanitized streamed-prompt diagnostics: operation, provider, model, input characters,
  first-content latency, fragment count, output characters, completion state, and total duration.
  Prompts, output text, endpoint, and secrets are deliberately excluded. This makes long remote
  free/reasoning-model generation distinguishable from local workflow overhead.
- Exposed the existing scoped model-resolution contract in the task and workflow editors:
  **Settings model → task model override → object-form `ai.prompt` model override**. Empty UI
  fields inherit, an override cannot select a different provider, and non-`ai.prompt` steps cannot
  carry a model. The task/editor persistence path preserves unrelated workflow arguments.
- Added or updated deterministic coverage for the OpenAI catalog and health route, streamed
  OpenAI-compatible prompts, model override round trips and editor serialization. The final
  non-integration verification completed with 606 passed tests and one intentionally skipped local
  Ollama integration probe. A real OpenRouter workflow validated the corrected streaming path.
- Added `Docs/Provider-Interoperability.md`, expanded the README, and synchronized the French and
  English user manuals with the active-provider flow, OpenRouter configuration, catalog refresh,
  model hierarchy, Direct Chat send behavior, and diagnostics interpretation.

### Direct Chat interactive agent and task handoff — 2026-08-12

- Added a persisted, per-conversation agent profile to `direct-chat.db` schema 3. New and existing
  conversations default to chat-only mode with no tool group and no sensitive capability selected.
  The profile is independent of global Settings and survives reopening a conversation.
- Direct Chat now exposes **Tools & permissions** below the conversation list. The expandable panel
  permits the same shared task-whitelist groups and sensitive capability switches as a task, then
  saves the explicit profile for the selected conversation. Existing path sandbox, Guardrails,
  connector configuration, and HTTP allowlist checks remain mandatory.
- When agent mode is enabled, Direct Chat calls `IMyLocalAgentService` with the saved group whitelist
  and a conversation-scoped `ToolExecutionContext`; otherwise it retains direct streaming chat.
- Added **Create task**. It copies only the latest user prompt and selected tool/capability grants
  into a new disabled scheduled-task draft, then opens the task editor. Conversation history,
  attachments, endpoints, and secrets are never copied. The user reviews the task and selects its
  eventual schedule before enabling it.
- Updated both user manuals and the README. Build completed with zero warnings/errors and the
  non-integration suite completed with 606 passed tests and one expected skipped local probe.

### Direct Chat multimodal image and audio responses — 2026-08-12

- Added explicit provider capabilities for text chat, image input, image output, audio output, and
  reserved video output. OpenAI compatibility no longer implies any media capability; existing
  Ollama and text-only providers expose no unsupported Direct Chat actions.
- Replaced the assistant-text-only persistence assumption with ordered `DirectChatContent` parts.
  SQLite schema 4 adds `message_contents` with text/image/audio and reserved video/file kinds,
  MIME type, name, size, BLOB/text data, duration, and cloud/provider metadata. Existing text rows
  migrate automatically, and cascading conversation deletion removes their media.
- Added 25 MiB per-item and 250 MiB per-conversation defaults. Media bytes are bounded before and
  during provider reads and before persistence. API keys remain exclusively in the DPAPI secret
  store; logs retain only sanitized model, timing, result-size, and state metadata.
- Added the official OpenAI media adapter with dedicated `POST /images/generations` and
  `POST /audio/speech` calls. Image output is decoded from base64; speech requests WAV so Direct
  Chat can play it locally and derive duration when the WAV header supplies it. Video is not
  implemented.
- Added conditional image/audio composer actions, operation states, cloud-origin disclosure,
  image preview/full-size opening/saving, WAV play/stop/saving, keyboard names, and theme-resource
  colors. Task conversion continues to copy only the latest user text and explicit permissions.
- Added deterministic persistence, migration, ViewModel projection, endpoint routing, saving,
  cancellation, and failure coverage, while retaining the existing text streaming tests.

### Version 1.4.0 — Direct Chat reliability, permissions, and file metadata — 2026-08-12

- Restored a stable selected conversation during initial loading, streaming, and recent-activity
  updates. The composer and conversation list now remain usable after a reply completes or fails.
- Hardened OpenAI-compatible streaming: a response is complete only with an explicit `stop` or
  `tool_calls` finish reason. Missing, length-limited, or content-filtered terminal reasons are
  reported as failed incidents rather than silently persisting a partial answer.
- Added conversation-scoped authorized filesystem locations, persisted through Direct Chat schema
  5. Agent mode is off by default; each saved location grants only the selected read or read/write
  access and continues to be constrained by the shared filesystem safety checks.
- Added created and last-modified UTC metadata to `filesystem.list` entries, alongside the existing
  name, type, and size metadata.
- Improved Direct Chat ergonomics with a draggable conversation splitter, a themed slim scrollbar
  in the independently scrolling permission list, and keyboard-accessible copy-to-clipboard actions
  on messages.
- Added deterministic coverage for filesystem metadata, schema-5 authorization persistence,
  selection retention, empty/failed responses, and OpenAI streaming terminal-reason validation.

### Version 1.4.1 — Workspace reports and operational UI refinements — 2026-08-12

- `MarkdownReportWriter` now derives its default report directory from the current
  `FileSystemOptions.RootPath`: every execution report and the shared `reports\index.jsonl`
  reside below the configured filesystem root. `DashboardViewModel` and `SettingsViewModel`
  resolve the same location. The executable directory and `%LOCALAPPDATA%` user-data directory
  are not report fallbacks.
- The report renderer omits the **Steps** section when no workflow step ran and appends final
  agent text as normal Markdown rather than as an indented code block. Dedicated tests cover
  both behaviors and the configured-root default.
- `RunMonitorPage` uses compact log item containers so a persisted runner line no longer gains
  an empty visual line of padding. Wrapping remains available for genuinely long output.
- The Settings incident list is a fixed-height, internally scrolling, recycling `ListBox`.
  This prevents an unbounded incident history from expanding the Settings page or forcing
  repeated outer-scroll repositioning while items load.
- Added two PowerShell helpers for the intelligent Downloads-sorting workflow:
  `Build-DownloadsInventory.ps1` creates a read-only inventory with hashes and duplicate hints;
  `Invoke-DownloadsSortPlan.ps1` validates an untrusted JSON sorting plan immediately before
  conflict-safe moves and records an execution log. They are intended as deterministic workflow
  tools around an AI planning step, not as unrestricted file-management scripts.
- Release verification: `dotnet test OD.MyLocalAgent.sln --configuration Release --no-restore
  --filter "Category!=Integration" -m:1` completed with 625 passed, 0 failed, and 1 skipped
  local integration probe. The Release build completed with 0 warnings and 0 errors.

### Version 1.4.2 — Specialized system prompts — 2026-08-14

- Replaced the single project-wide user prompt with specialized Agent tasks, Direct Chat, and
  workflow `ai.prompt` instructions, while retaining the built-in safety prompt for every mode.
- Kept the legacy `AI:GlobalSystemPrompt` as a read-time migration fallback and remove it from the
  persisted document on the first subsequent Settings save.
- Added Settings and task-editor projections plus deterministic coverage for prompt isolation,
  fallback migration, and persistence.

### Provider-neutral reasoning and bounded Trust Filter — 2026-08-14

- Added `GenerationOptions.ReasoningEnabled` and the resolved
  `EffectiveGenerationPolicy.ReasoningEnabled` preference. Agentic task calls inherit `true` when
  the persisted property is absent; the Task Editor exposes **Enable reasoning for agentic task
  calls** and serializes only an explicit opt-out. This preserves existing task behavior.
- `GenerationPolicyChatClient` owns the provider-neutral mapping. Each request receives
  `ChatOptions.Reasoning` (`Medium`/`Full` when enabled, `None` when disabled); Ollama additionally
  receives native `think`. OpenAI-compatible adapters map the standard effort to their supported
  request field. Inputs are materialized before a possible retry so a one-pass message enumerable
  is never consumed twice.
- A provider/model rejection of reasoning before the first response update gets one retry with the
  reasoning fields removed. The retry is limited to reasoning-specific HTTP 400/422 failures and
  reasoning-option diagnostics; it never retries cancellation, timeout, transport failure, or a
  partially streamed answer. This keeps a preference incompatibility from becoming an application
  failure without duplicating visible output or tool activity.
- Reasoning is deliberately disabled for workflow `ai.prompt`, Guardrails, skills, Direct Chat
  history compression, workflow assistants, AI Playground generation, and warm-up. Only Direct
  Chat and agentic task calls request it. This separates the quality/cost trade-off of agentic work
  from bounded utility and safety operations.
- Direct Chat has a per-send **Reasoning** control and consumes `TextReasoningContent` separately
  from normal answer text in both chat and agent modes. Readable reasoning is shown in the transient
  **Model reasoning** panel; it is neither appended to the assistant answer nor persisted to the
  Direct Chat SQLite database. **Create task** transfers the selected reasoning preference into its
  new disabled task draft.
- Trust Filter classifier prompt version 4 requires compact JSON with all materially distinct
  suspicious verbatim spans and no explanatory prose. It disables reasoning and requests JSON
  response formatting. Its direct-screening output budget scales from 1,024 to 8,192 tokens by
  source size, avoiding an evidence-loss ceiling for long web pages or PDFs while retaining a
  small bounded request for short input. The UI explicitly identifies a screening timeout before
  falling back to marked Base64.
- Runner warm-up remains conditioned on `AI:Provider == Ollama`; no warm-up request is made to
  OpenAI-compatible/cloud providers. Runtime logs and user-facing failures now name the configured
  model rather than incorrectly calling every model "Ollama". The OpenAI-compatible service uses
  the same model-centred log wording, including when its endpoint is OpenRouter.
- Deterministic tests cover standard and Ollama reasoning mapping, non-streaming and first-update
  fallback, Trust Filter JSON/budget behavior, and Direct Chat separation of visible reasoning from
  persisted assistant content. The non-integration test gate completed with 630 passed tests and
  one expected skipped local integration probe.

### Version 1.4.7 — Reliable sliding discovery and workflow diagnostics — 2026-08-16

- Enforced the local 2→4→7 calendar-day freshness window after retrieval, with cumulative
  retrieval budgets reserved for each expansion phase and auditable `windowHistory` diagnostics.
- Moved discovery prompt-injection screening to the final bounded delivery boundary, after
  deterministic search, retrieval, date extraction, filtering, deduplication, and ranking.
- Distinguished genuine empty discovery from web-search provider failures through `searchStatus`,
  query success/failure counters, and the last HTTP status. Provider-wide HTTP 429 responses stop
  later phases and are reported explicitly to the next workflow step.
- Updated the LinkedIn and Sunday editorial workflows to fail closed on unavailable search and to
  write a focused article when exactly one eligible source is available.
- Added task-load diagnostics, preserved scheduled task files during workflow updates, and updated
  the technical documentation, README, and English/French user guides.

### Version 1.4.8 — Search-efficient Web Discovery — 2026-08-17

- Replaced repeated 2/4/7-day provider searches with one seven-day collection and local in-memory
  window evaluation.
- Limited discovery to eight themes and eight paid search requests per run. Each theme gets one
  primary query; a single fallback is conditional on insufficient usable results and remaining
  budget. Preferred domains remain local ranking signals.
- Increased the normal provider result count to ten. Retained the page-fetch ceiling only as an
  advanced compatibility safeguard for runtime, memory, politeness, and pathological results,
  with a default of forty; bundled workflows no longer configure either advanced limit.
- Retrieval consumes primary results rank-by-rank across themes so a small safeguard is shared
  fairly. Diagnostics expose `searchRequestLimit` and locally evaluated `windowHistory` entries.
- Release verification completed with zero build warnings or errors, 652 non-integration tests
  passing, and one expected environment-dependent local Ollama probe skipped.

### Version 1.4.9 — Workspace-centered durable storage — 2026-08-20

- Added the immutable `WorkspacePaths` composition service. The UI and runner now resolve task
  JSON, Direct Chat storage, application and run logs, incidents, examples, and generated reports
  below `<FileSystem:RootPath>\.od-my-local-agent`. Only settings, DPAPI secrets, and Google OAuth
  credentials remain under `%LOCALAPPDATA%\OD.MyLocalAgent`.
- Report output moved from the workspace-level `reports` directory to
  `.od-my-local-agent\outputs\reports`; UI folder commands and execution history use the same
  injected paths as their stores.
- The application-owned directory is marked hidden on Windows and is an unconditional deny zone
  for `IPathSandbox`: direct reads and writes are rejected even with an explicit absolute grant,
  listings omit it, recursive archive sources cannot contain it, and write operations cannot
  target an ancestor that would modify it. Task and Direct Chat editors reject explicit grants.
  PowerShell and Shell Execute remain intentionally unrestricted process capabilities outside the
  managed path boundary and expose that distinction in their permission descriptions.
- Kept legacy data migration out of both executables. The one-time
  `scripts\Migrate-DataToWorkspace.ps1` utility previews with `-WhatIf`, requires stopped runtime
  processes and disabled scheduled tasks for a real run, creates a verified backup, copies through
  temporary files, compares SHA-256 hashes, merges and rebases historical report indexes, and
  removes only verified legacy sources. Historical task backups are retained below
  `.od-my-local-agent\tasks\backups`.
- Release verification completed with zero build warnings or errors, 663 non-integration tests
  passing, one expected environment-dependent Ollama probe skipped, all task examples parsing as
  JSON, and a successful end-to-end migration against an isolated temporary fixture.

# Stage 26 — Embedded Knowledge and declarative prompt tools (2026-08-20)

- The historical no-tools invariant for `ai.prompt` is retired. Object-form prompt steps persist exact `toolAliases`; the registry resolves them exclusively through the declarative catalog and enforces the task group whitelist and immutable execution context.
- Knowledge authorization is deny-by-default and normalized as task/conversation scope → optional prompt-step narrowing → optional call narrowing. Explicit unauthorized tags fail before embedding or database access.
- The library is stored below `.od-my-local-agent/data/knowledge` in `knowledge.db`, `documents`, and `staging`. SQLite uses schema 3, foreign keys, WAL, `synchronous=NORMAL`, and a 5-second busy timeout; schemas 1 and 2 migrate forward without destructive recovery. Documents point to immutable active revisions carrying hashes and embedding/index metadata; imported originals use application-generated canonical names. Replacement and full reindex activation are transactional. Schema 3 persists pending canonical-file cleanup; startup retries it and reconciles abandoned staging or unreferenced canonical files without deleting active content.
- Embeddings use `IEmbeddingGenerator<string, Embedding<float>>` through `IKnowledgeEmbeddingGeneratorFactory`, with independent, reloadable Ollama or OpenAI-compatible provider/model settings persisted by the Settings service. Ollama defaults to pinned `embeddinggemma:300m`. One persisted provider/model/dimension/unit-normalization/chunking/index/cosine contract prevents mixed vector spaces.
- The vector backend is the portable exact-cosine baseline over tag-prefiltered SQLite rows. No `sqlite-vec` package was retained: the optional native publication/equivalence spike was not needed for the correctness baseline.
- `knowledge.search` is an ordinary contextual catalog tool in group `knowledge` (display order 1300). It returns bounded, citable, untrusted excerpts and never exposes vectors or internal paths.
- Direct Chat schema advances from 5 to 6 with nullable `knowledge_access_json`. Legacy conversations receive an empty scope.
- Public services added: `IKnowledgeLibraryService`, `IKnowledgeSearchService`, `IKnowledgeEmbeddingService`, `IKnowledgeDocumentExtractor`, and `IKnowledgeEmbeddingGeneratorFactory`; persisted contracts add `KnowledgeAccessDefinition`, prompt `ToolAliases`, and task/conversation Knowledge scopes. DOCX/PDF parsing primitives are shared with `DocumentTool` and `PdfTool`, and PDF chunks retain one-based page metadata.
- Stage verification completed with a warning-free Release build, 685 passing non-integration tests (one expected local Ollama probe skipped), clean self-contained single-file `win-x64` UI and Runner publications, a successful Runner host check, and a three-second UI startup smoke check. Six explicitly requested Integration tests also pass, including a real local `embeddinggemma:300m` request proving stable, finite, unit-normalized vectors for French and English inputs. Network-backed tests remain outside the normal stage gate.

### Version 1.5.0 — Embedded Knowledge and RAG — 2026-08-20

### Version 1.5.1 — Knowledge administration refinements — 2026-08-21

- Knowledge administration is organized into dedicated Documents, Preview, Import document, Search, and Maintenance tabs. Document metadata editing uses a separately scrollable dialog, while import input remains independent from the selected document details.
- Existing authorization tags are normalized, case-insensitively deduplicated, and sorted for guided reuse during import, search, and document-list filtering. Diagnostic search provides four tag selectors with OR semantics and prevents duplicate effective selections.
- Search state is cleared when a document is deleted, and operation feedback is scoped to its owning tab so stale search or reindex messages do not appear elsewhere. Valid Knowledge embedding settings use the success color; invalid settings remain errors.

# Stage 27 — Hybrid Knowledge retrieval and context selection — 2026-08-22

- Knowledge schema 4 owns an embedded `chunk_fts` FTS5 table using `unicode61 remove_diacritics 2`. It contains only active chunk headings and text; startup verifies the active-chunk projection and transactionally rebuilds the table if an interrupted migration left it inconsistent.
- `knowledge.search` keeps its public tag-scoped contract while combining safe literal exact-term candidates and tag-prefiltered cosine candidates through rank-only reciprocal-rank fusion. MMR chooses complementary anchors, and bounded same-heading/page contiguous chunks may supply surrounding context.
- Candidate-pool, fusion, diversity, lexical-query, and context-neighbor limits are global reloadable Knowledge settings. They are query-time policy and do not require re-embedding or grant document access.


### Version 1.5.3 — Per-prompt execution controls — 2026-08-22

- Added optional `ai.prompt.args.timeout`, an integer from 1 to 999 expressed in minutes. When omitted, the active provider timeout remains unchanged; when supplied, `TimeoutExecutionScope` applies the override only to the prompt step and restores the preceding value on completion, cancellation, or failure.
- Added optional `ai.prompt.args.thinking`. When omitted, the resolved task/Settings reasoning policy is preserved; when supplied, it overrides reasoning only for the prompt step. This permits structured long-context report steps to disable provider reasoning without changing the task-wide default.
- `DynamicOllamaChatClient` and `DynamicOpenAIChatClient` both consume the scoped timeout, including OpenAI-compatible transport `NetworkTimeout`, so provider choice does not change the contract. Prompt-step validation rejects non-boolean `thinking` values and timeout values outside the declared range.
- The task catalog, technical manual, and English/French user manuals document the controls. Deterministic coverage verifies validation, generated workflow snippets, scoped timeout restoration, and prompt-level reasoning-policy override.

### Version 1.5.4 — Workflow text editor scope — 2026-08-23

- Restored **Edit workflow text** to its focused role: it edits only eligible string arguments already present in the workflow JSON and preserves every other step setting, including models, exact tool aliases, Knowledge scope, and `includeContext`.
- All ordinary string arguments are eligible, including paths, URLs, content, queries, and SQL. Short or technically constrained values are explicitly excluded in Tool Map metadata; absent optional arguments are not offered and are never created by the editor.
- The dialog no longer exposes prompt-step model, tool, Knowledge, or context configuration. Those values belong to the workflow step editor; a future dedicated step editor may provide typed editing and before/after insertion.

### Version 1.5.5 — Global LLM execution limit — 2026-08-24

- Added reloadable `LlmExecution:MaxConcurrentRequests`, persisted from **Settings → AI provider** and defaulting to `1` with a validated range of `1` through `8`.
- `GlobalLlmExecutionGate` uses a named Windows semaphore shared by the UI and every Runner process. It serializes or limits chat, streaming, and OpenAI-compatible media generation without holding the workflow runner itself.
- Queue waiting is cancellation-aware and deliberately begins before the provider timeout. The timeout therefore covers actual provider/model work rather than time spent waiting for an execution slot.
- The named semaphore’s capacity is established by the first active caller. A changed limit takes full effect once active model calls have released their slots; the default remains appropriate for local Ollama/GPU use.
- No package versions changed. Release verification completed with a warning-free build and 714 non-integration tests passing, with one expected local Ollama probe skipped.

# Stage 29 — Conditional workflow routing and structured prompt responses — 2026-08-24

### Version 2.0.0 — Conditional workflows and structured AI responses — 2026-08-24

- The persisted workflow remains one task-owned JSON array. Object steps gain optional `next`;
  `@end` is the collision-free terminal target. Legacy arrays without routing retain declaration-order behavior.
- `flow.decision` is a workflow-only catalog entry with deterministic text, numeric, JSON-path,
  empty/null/existence, regex, case, and negation semantics. True selects `target`, false selects
  `next` or the following declaration, and an indeterminate evaluation selects optional `fallback`.
- Validation builds an in-memory control-flow graph, rejects unknown targets, unreachable nodes,
  and cycles. Runtime also enforces an acyclic execution-count bound.
- Decisions appear in ordered step results but do not enter `WorkflowContext` or replace
  `previous.output`. A handled fallback produces warning code `WorkflowDecisionFallback`.
- Tool-free `ai.prompt` steps gain optional top-level `responseSchema`. The root must be an object
  and uses the portable application-owned subset `type`, `properties`, `required`,
  `additionalProperties`, `enum`, `const`, numeric/string bounds, and array `items`.
- The schema is transmitted with `ChatResponseFormat.ForJsonSchema` and enforced again locally.
  Invalid JSON or schema violations fail the prompt step. Stage 29 rejects combining
  `responseSchema` with `toolAliases`; structured final responses after an agentic tool loop remain deferred.
- `IWorkflowEngine` and task storage locations remain unchanged. No package dependency or workflow
  library was introduced.
- Stage verification completed with a warning-free solution build and 783 passing
  non-integration tests; one expected environment-dependent local Ollama probe was skipped.

# Stage 30 — Limited step-level `forEach` — 2026-08-25

### Version 2.1.0 — Limited sequential `forEach` execution — 2026-08-25

- The persisted object-step contract gains optional string `forEach`. It remains a property of
  one step and does not introduce a nested workflow, child execution graph, parallel map, or
  rollback boundary.
- Declarative Tool Map metadata owns support. Only `ai.prompt` and `filesystem.write` set
  `SupportsForEach`; catalog, validation, editor, and helper surfaces consume the same flag.
- Static validation requires one exact prior named-output selector whose producer dominates the
  consumer. Iteration placeholders are scoped to repeated-step arguments and support the current
  JSON value, structured paths, and one-based position.
- `SequentialWorkflowEngine` resolves and bounds the source before execution, processes at most
  100 items sequentially, and commits one ordered JSON-array output only after full success.
- Retry state is item-local. A final item failure stops the batch; `ContinueOnError` may route past
  the failed outer step but does not execute remaining items. Previously completed external side
  effects remain observable and are not rolled back.
- `WorkflowForEachContext` is the immutable runtime carrier. The existing placeholder resolver,
  workflow context, run results, report pipeline, scheduler, and task storage locations remain the
  integration boundaries; no package or workflow library was added.
- The Task editor exposes repeat controls only for supported aliases and warns when a repeated
  `filesystem.write` path lacks an iteration placeholder and would overwrite the same file.
- Verification completed with 797 non-integration tests passing, no failures, one expected local
  Ollama probe skipped, and a clean `git diff --check`.

### Knowledge HTML and batch ingestion refinements — 2026-08-25

- Knowledge ingestion now accepts HTML and HTM as native source formats. Extraction reuses the
  existing safe HTML converter in structured-Markdown mode, preserving useful headings and lists
  while removing scripts, styles, forms, frames, and other non-content nodes. Original source files
  remain the canonical private copies, so replacement and reindex use the same extraction path.
- The Knowledge picker supports multiple files. Batch semantics are explicit and deterministic:
  authorization tags are shared, each document name comes from its file name without the extension,
  and descriptions are empty. Files run sequentially; duplicates and individual failures are counted
  and skipped without stopping later files.
- Import progress reports the current file plus preparation, copy, duplicate checking, extraction,
  chunking, embedding, and save phases. Canceling stops the current and remaining work while preserving
  documents whose atomic commits completed earlier in the batch.
- Release verification completed with a warning-free build and 801 passing non-integration tests;
  one expected local Ollama probe was skipped.

### Version 2.1.1 — Explicit filesystem write modes — 2026-08-25

- `filesystem.write` adds the optional `mode` enumeration with exact values `overwrite`,
  `createNew`, and `append`; omission preserves the historical overwrite behavior.
- `createNew` uses the operating system's atomic create-new operation. `append` creates a
  missing file, writes UTF-8 text exactly as supplied, and inserts no implicit newline.
- The existing three-argument `IFileSystemTool.WriteAsync` overload remains available for
  source compatibility and delegates to `overwrite`. Tool results now include the effective mode.
- No package, authorization, capability, task schema, workflow routing, or storage-location
  change was introduced. Deterministic coverage exercises all modes and invalid-mode rejection.
- Release verification completed with 803 passing non-integration tests, no failures, and one
  expected skipped local Ollama probe.

# Stage 23 addendum — Web Search proposal memory (2026-08-30)

- `research.discover` now accepts an optional authorized proposal-memory SQLite path, a 1–60
  month lookback, the opt-in historical `used` rule, and a 0.80–0.99 exact cosine threshold.
  The contextual tool resolves ReadOnly access before opening a private non-pooled connection.
- `ProposalMemoryService` owns the compatible `editorial_proposals` schema, supported legacy
  migration, deterministic title/URL normalization, compact `title + summary` embeddings,
  vector encoding, compatibility checks, parameterized applicable-row selection, and local
  cosine comparison. It never invokes a chat model, agent, generic SQLite alias, or external
  vector service.
- The contextual SQLite group adds `sqlite.createProposalDatabase` and
  `sqlite.storeProposal`. Both require ReadWrite path access and `SqliteWrite`; workflows never
  supply schema SQL or vectors. Proposal keys make retries idempotent and writes commit once.
- Discovery output adds aggregate proposal-memory diagnostics only. Stored proposal content,
  database paths, keys, and raw vectors remain absent from discovery output and normal logs.
- No package or application-owned database changed. The bundled daily editorial workflow owns
  its database path, initializes it before discovery, and exposes only `sqlite.storeProposal`
  to its bounded agentic prompt step.

# Version 2.5.6 — Repair editorial event identity filtering (2026-09-13)

- Editorial event fingerprints now hash the normalized entities and claimed facts, rather than
  the enumerable object representations. Distinct undated events can no longer be treated as
  the same historical event.
- Discovery anti-repetition now compares a candidate URL against every canonical source retained
  for a historical proposal, including supplemental sources.
- Final editorial-reservation diagnostics identify whether a rejection was caused by an exact
  title, event identity, or source match.
- Regression coverage verifies distinct undated events and supplemental-source filtering.

# Version 2.5.5 — Provider-specific reasoning levels (2026-09-09)

- `GenerationOptions` persists an optional open-ended `reasoningEffort`; task, step, and
  `ai.prompt.args.thinking` overrides resolve atomically so a nearer boolean does not accidentally
  retain an inherited level.
- The Task and workflow-step editors suggest `low`, `medium`, and `high` while retaining editable
  text for provider-specific keywords such as `xhigh` and `max`.
- Ollama receives the exact string through native `think`; OpenAI-compatible chat-completions
  receive the exact `reasoning_effort` value through raw request options. Existing boolean behavior
  and the pre-response reasoning fallback remain compatible.
- Task save probes every explicit model/level pair once per provider endpoint. Stable accept/reject
  results are cached, transient failures remain retryable, rejected values are removed from the
  draft, and unavailable probes save with an explicit warning.
- Deterministic coverage verifies JSON persistence, precedence, workflow argument typing, exact
  provider payloads, caching, retryable unavailability, and rejected-value removal. The Release
  gate completed with 0 warnings, 0 errors, 871 passing non-integration tests, one expected local
  Ollama integration probe skipped, and a clean `git diff --check`.

# Version 2.5.4 — Repair editorial selection enum handling (2026-09-09)

- Editorial selection deserialization now accepts the named novelty decisions required by the
  workflow response schema and rejects undocumented numeric enum values.
- Safe editorial contract failures retain their actionable message and underlying local
  diagnostic instead of being misreported as generic SQLite failures.
- The daily editorial response schema requires non-empty proposal sources, claimed facts, and
  fact sources before deterministic reservation begins.
- Regression coverage uses production-shaped named enum JSON, rejects numeric decisions, checks
  safe error propagation, and verifies the strengthened workflow schema.
- Verification: 858 non-integration tests passed, one expected local Ollama integration probe
  was skipped, and `git diff --check` was clean before release publication.

# Version 2.5.3 — Preserve Gmail analysis after individual AI failures (2026-09-08)

- An AI prompt repeated over Gmail messages now converts an individual provider timeout,
  cancellation, invalid structured response, or other analysis exception into a deterministic
  `A vérifier` record and continues with the remaining messages.
- Explicit cancellation of the complete task still stops execution promptly.
- The daily Gmail classification timeout is raised from 5 to 15 minutes for reasoning mode, and
  the report formatter includes the deterministic review category.
- Regression coverage verifies that completed classifications survive, the failing item contains
  no protected message body, later messages are processed, and the downstream write step runs.

# Version 2.5.2 — Honor explicit AI prompt reasoning (2026-09-07)

- Removed the service-level override that silently disabled reasoning for every `ai.prompt` call.
- An explicit `args.thinking` value now reaches the provider and takes priority over the inherited generation policy. Omission preserves that policy, and the override does not leak into subsequent steps.
- Regression coverage exercises enabled and disabled reasoning for plain and structured prompts with Ollama and OpenAI-compatible providers, plus workflow-to-provider precedence and inheritance.
- The non-integration suite passes: 850 tests passed and one environment-dependent probe skipped.

# Version 2.5.1 — Discover anti-repetition and deterministic editorial delivery (2026-09-05)

- `minimumDocuments` remains a bounded collection target for `research.discover`: it controls
  fallback queries and 2→4→7-day window selection, but never suppresses eligible candidates.
  A shortfall is observable through `insufficient=true` and `insufficientReason`.
- Discovery filtering remains early and ReadOnly (canonical URL, normalized title, compatible
  local cosine); a second, transactionally serialized filter is applied after structured
  selection. This separates finding candidates from committing editorial novelty.
- The SQLite tool group adds `sqlite.reserveEditorialSelection`,
  `sqlite.prepareEditorialReport`, `sqlite.confirmEditorialExport`,
  `sqlite.importEditorialHistory`, and `sqlite.rebuildEditorialEmbeddings`. All writes require
  the existing `SqliteWrite` capability and authorized local paths; no caller supplies SQL,
  embeddings, fingerprints, or transaction control.
- Reservation records an idempotent run keyed by `{{workflow.runId}}`, accepts only source IDs
  delivered by Discover, rejects exact/semantic duplicates deterministically, and makes only
  approved proposals available to the drafting step. Report preparation requires exactly one
  aligned draft per approved proposal; export confirmation verifies the prepared SHA-256.
- The DB daily workflow routes `candidateCount=0` to an empty reservation. If candidates exist,
  its local response schema requires 1–3 selections (`minItems`/`maxItems`) and retries invalid
  model output twice. An empty report can therefore result only from no delivered candidates or
  from final deterministic rejection of every selection.
- `WorkflowResponseSchemaValidator` adds portable array-cardinality checks. Ollama generation
  receives a reduced schema without size keywords because its grammar compiler rejects some
  valid JSON Schema bounds; the original schema is enforced locally after generation.
- Historical report import and embedding rebuild default to dry-run so operators can inspect
  effects before mutating task-owned editorial history.
- Verification: 838 non-integration tests passed, one expected local Ollama integration probe
  was skipped, and `git diff --check` was clean before release publication.

## Task and AI-step context-window overrides — 2026-10-05

- Supersedes the Stage 14 global-only context restriction for user-directed calls.
  `GenerationOptions.ContextWindowTokens` is an optional integer from 1024 through 2000000.
  Each call resolves context independently through step → task → Settings, with provenance
  in `EffectiveGenerationPolicy.ContextWindowSource` and workflow execution logs.
- Task editor exposes **Context window (tokens)** with global inheritance. AI-step editor
  exposes **Override context window**. Both display the effective value and its origin;
  blank editor values inherit. JSON inheritance uses omission, not null.
- Context-only overrides survive canonical task/workflow serialization without changing
  reasoning or sampling. Existing tasks inherit their previous defaults.
- Ollama receives the resolved value through `num_ctx`, including tool-enabled prompt
  rounds. OpenAI retains its model-defined capacity; no context request field is invented.
  Internal helper, screening, and reduction calls retain the global context declaration.
- Updated README, bilingual user manuals, technical manual, shared and related authoring
  specifications. Older installed versions reject the new JSON option, so deployment must
  precede editing production task files.
- Verification: 903 non-integration tests passed, zero failed, and one expected probe skipped.
  Deployment is handled separately by the user; production editorial workflows still inherit
  the global context until explicitly changed.

## Stage 31 — Workflow-only local Gmail decisions — 2026-10-06

Application version: **2.6.6** (incremented from 2.6.5).

- Introduces the opt-in `GmailDecision` settings contract and contextual `gmailDecision`
  group. `readClassify` collects once and returns versioned coverage; `renderReport`
  validates that coverage and renders Markdown deterministically. `WorkflowOnly` and
  explicit invocation surfaces exclude agent tools, taskless calls and prompt tool aliases.
- Ollama owns the dedicated `ILocalDecisionClient` boundary: loopback-only endpoints,
  pinned localhost, no redirects/proxy, bounded `/v1/systemone` calls and fixed structured
  `/api/chat` second opinions/fallback. This is the scoped Stage 18 exception: these calls
  use the shared global LLM gate but do not enter agent tools, RAG, Guardrails or summarization.
  Existing Gmail tools and their protection contracts remain unchanged.
- Ordered named questions support choice, noul and score answers. Deterministic acceptance
  validates native distributions and applies settings → workflow → question → choice rules,
  typed conflicts and floor-rounded second-opinion budgets. Independent conventional calls
  receive the projected source and question contract, never the native answer or scores.
  Provisional and unresolved rows remain visible; downgrade disagreements preserve attention.
- The effective native context budget is 2,050 tokens, rather than advertised large-context
  metadata. A conservative UTF-8-byte upper bound reserves question/protocol space and
  routes current-content truncation to second opinion. This can increase arbitration rates;
  synthetic technical probes do not establish real-mail quality or latency acceptance.
- Cross-process OS file handles own endpoint request boundaries, operation intent and
  embedding priority. Rendezvous files contain no persistent ownership. Endpoint aliases
  are coalesced conservatively; another reader prevents end-of-batch unload. Embeddings
  hand off at request boundaries and unload only a verified dedicated decision identity.
  Cancellation and process death release ownership; cleanup has an independent short deadline.
- Public compatibility additions include `ToolInvocationSurface`, optional execution
  correlation, `WorkflowOnly`, `GlobalLlmExecutionGate`, local decision contracts and
  additive run-warning fields. No package dependency or production task migration is added.
- Settings provide explicit synthetic tests and model refresh; Save never probes models.
  Workflow authoring preserves ordered question descriptions, exposes policy JSON and a
  triage starter. Runs surface completed-with-warnings coverage. Documentation is bilingual;
  `Docs/samples/gmail-decision-watch.json` is deliberately disabled and uses StopOnError.
- Automated validation covers runtime authorization, provider recording fakes, typed answers,
  thresholds/conflicts/budgets, fallback/circuit/retry limits, deadlines, output bounds,
  report escaping, settings persistence, actual workflow execution and residency/process death.
  Release build: zero warnings/errors. Release non-integration suite: 980 passed, zero
  failures, one expected Ollama probe skipped; every command used `-m:1`.
  `git diff --check` passed. Live Gmail quality/latency evaluation, integration tests,
  deployment and production schedule changes remain separately authorized operations.

## Stage 32 phase A — Common local decision evaluation — 2026-10-07

- Added `DecisionOptions` and reloadable `AddDecisionConfiguration`. An explicit `Decision`
  section is authoritative, including an empty section's defaults; without it, native settings
  come from legacy `GmailDecision`. Generic enablement remains false even when Gmail is enabled.
  Shared native validation is extracted from the mail contract; generic operation/input budgets
  do not inherit mail counts or batch deadlines. Saved settings persist the canonical section
  and remove shared native properties from the saved mail section.
- Both option monitors resolve the current configuration on reload. Gmail projection deliberately
  does not read a potentially stale native monitor during reload callbacks. Tests caught and
  removed this callback-order dependence. Gmail retains collection, report, arbitration and
  lightweight fallback policy; `LlmOnly` still does not require a decision-model installation.
- The base `appsettings.json` intentionally retains legacy native defaults. Adding a canonical
  section there would mask existing user-file legacy overrides before migration. A standalone
  canonical configuration example documents the new section without changing effective user
  settings at bootstrap. Application version and package versions are unchanged in this phase.
- Added `IDecisionEvaluationService` / `DecisionEvaluationService`, simple-contract validation,
  normalized acceptance and versioned result/timing contracts. Common `ScoreAsync` preserves
  named Boolean/rubric/multiple native questions and is reused by Gmail in application DI.
  Generic evaluation has no mail classifier, report mapping or Gmail credential dependency.
- Declarative `decision.evaluate` accepts explicit source, one literal question and 2–24 named
  described options. Policy fields cannot be authored from previous outputs; only source permits
  placeholders. The independent task group is opt-in, workflow-only, unavailable to agents,
  Direct Chat, prompt tool aliases and forEach. The existing authorized workflow-test surface
  remains supported; a specialized authoring/test action is deferred to phase B.
- Valid unique winners meeting every effective threshold are `accepted`; valid ties or failed
  thresholds are `uncertain`, retaining candidates/scores. Technical `unavailable` results have
  null choice and scores with sanitized reason codes. Overrides only tighten defaults; legacy
  percentage defaults convert once to [0,1]. Authoring/enablement/authorization errors remain
  errors and caller cancellation propagates. No conventional LLM fallback is introduced.
- Full-prompt budgeting reuses the conservative one-token-per-UTF-8-byte assumption, counting
  source and serialized questions/options with a 400-token reserve and at most 2050 tokens.
  Oversized input is unavailable before any catalog/inference call, without truncation.
  Generic deadlines are 1–3600 seconds (default 180), input budgets 512–2050 (default 2050).
- Native transport, global execution gate and residency coordinator are reused. Generic model
  identity checks protect configured chat/embedding models; Gmail retains its additional mail
  model checks. Embedding handoff uses canonical settings for either enabled caller. Cleanup
  uses the existing ownership rules and an independent five-second deadline after success,
  failure or cancellation. Logs retain hashes, model, status, sanitized codes and timings, not
  source content. Cold timings include provider-reported loading/inference, without invented
  separate loading durations.
- Settings separates **Decision models** from **Gmail decision reader**, using MVVM Toolkit.
  Catalog/editor generic metadata and AI Helper expose the contract. The disabled
  `decision-readiness.json` sample checks status before accepted choice and exercises all four
  handling branches through the actual registry/engine in tests. Bilingual guides, user manuals,
  technical manual, README and shared-spec addendum describe phase A and deferred work.
- Verification: `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore` completed with zero warnings
  and zero errors. `dotnet test OD.MyLocalAgent.sln -m:1 --no-restore --filter "Category!=Integration"`
  completed with **1029 passed, zero failed, one expected Ollama probe skipped**. The suite uses
  fake provider responses and covers migration/reload/persistence, authorization, score validity,
  threshold boundaries, cancellation/deadlines, cleanup ownership, embedding handoff and routing.
  Two initial isolated test runs could not establish the local testhost connection; the successful
  suite ran outside that isolation. Every build/test invocation included `-m:1`.
- Provider contract was rechecked against the official Tev1 documentation; the isolated CLI
  version check reported Ollama 0.40.0 but could not reach its server. No new live inference,
  integration test, model download, deployment, Git publication, production migration or model
  quality claim is part of this phase. Specialized UX (B), controlled FR/EN evaluation (C),
  optional integrated routing (D) and agentic exposure (E) remain deferred.
