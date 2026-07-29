# Architecture Ledger

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
| `MaterialDesignThemes` | 5.3.2 |
| `Microsoft.Agents.AI` | 1.15.0 |
| `Microsoft.Extensions.AI` | 10.8.3 |
| `Microsoft.Extensions.AI.OpenAI` | 10.8.3 |
| `Microsoft.Extensions.Configuration.Json` | 10.0.10 |
| `Microsoft.Extensions.Hosting` | 10.0.10 |
| `Microsoft.Extensions.Http` | 10.0.10 |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 10.0.10 |
| `Microsoft.NET.Test.Sdk` | 18.8.1 |
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
