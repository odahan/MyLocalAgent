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
  docked runner pane. Completion publishes `TaskExecutionCompletedMessage`.
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
  grids, grouped and ordinary combo popups, Logs, incidents, Dark, and Light. No incident was
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
- Both offline manuals validate with matching 21-section structures, 29 unique ids, no
  missing target, duplicate id, or external resource, and valid embedded JavaScript.
- Touched earlier-stage Core Gmail/HTTP tools, shared composition, agent instruction, UI
  composition/navigation/settings, test doubles, project files, solution, and appsettings only
  where Stage-7 integration required it. Tool Map aliases, method signatures, connector DTOs,
  Safety gates, HTTP allowlisting, and POST behavior remain unchanged.

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
  145 passed, zero failed, one intentionally skipped local Ollama integration test, 146 total.
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

### Stage 14 — Hierarchical AI generation settings — 2026-07-30

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
  237 passed, zero failed, one intentionally skipped local Ollama integration test, 238 total.
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

- Agent-capable executions are limited to eight function rounds and two consecutive function
  errors, terminate unknown calls, and raise `AgentToolLoopException` before executing a third
  strictly identical function-and-arguments request.
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
- All four sample JSON files parsed. Both standalone manuals retain 22 balanced sections and
  30 unique ids with no duplicate id or missing internal anchor. `git diff --check` is clean.
- `scripts/publish.ps1 -NoRestore` produced the self-contained `win-x64` UI and Runner in the
  standard per-user installation. The published Runner `--help` smoke check exited with code 0.
- The real Gmail task supplied as data was neither created nor executed. Live AI Helper theme,
  WebView2, and real-workflow execution remain explicit human checks.

#### Post-release UI correction — 2026-07-31

- The Tasks Runner output `TextBox` now explicitly binds `DisplayedRunText` with `Mode=OneWay`.
  The property is intentionally computed and read-only; the prior default TwoWay binding caused
  a dispatcher incident when the Tasks page loaded. The correction does not execute, modify, or
  expose any task data.

#### Runner diagnosis and Gmail progress — 2026-07-31 (historical, superseded for Gmail by Stage 18)

- The observed daily Gmail workflow was not in an agent loop: its initial unbounded
  `gmail.readToday` retrieval returned 40 messages, and Guardrails began 27 sequential local
  LLM screening calls before the user cancelled. The workflow context remained empty because
  the Gmail step had not yet completed successfully.
- Tasks adds session-only **Debug (next run)** and **Large** controls. Debug routes full LLM
  request/response payloads through a private child-Runner-to-UI channel alongside the normal
  sanitized tool-argument traces; it disables automatic scrolling and never persists those
  payloads in normal logs or reports. Large raises only the diagnostic panel by 300 pixels and
  never changes the main window.
- Gmail emitted privacy-safe `message N of M` Guardrails progress at this point in the history.
  Stage 18 replaces it with bounded `batch N of M` progress without adding a hidden read limit;
  selecting `maxMessages` or a non-zero Guardrails cap remains an explicit user choice because
  it can omit messages from the report.
- Release verification after this correction: `dotnet build OD.MyLocalAgent.sln --no-restore
  --configuration Release -m:1` completed with zero warnings and errors; `dotnet test
  OD.MyLocalAgent.sln --no-build --no-restore --configuration Release -m:1` completed with
  260 passed, zero failed, and one intentionally skipped local Ollama integration test.

#### Release 1.2.4 — 2026-08-01

- Packages the post-release Tasks binding correction, interactive Runner Debug and Large
  controls, private diagnostic protocol, and Gmail Guardrails progress as version 1.2.4.
- The real Gmail task remains unchanged and was not used as a release verification fixture.

#### Workflow argument documentation — 2026-08-01

- The French and English user manuals now distinguish string-form no-argument steps from
  object-form `args` steps. They include concrete Gmail `daysBack`/`maxMessages` and Calendar
  `period` examples, and state the behavior of `gmail.readToday` and `calendar.today` when no
  arguments are sent.

#### Tasks large Runner scrolling — 2026-08-01

- The Tasks page now has a global vertical scroll viewer with a bounded task-grid height. Large
  changes Runner Output from 190 to 490 pixels while the actions and output remain reachable by
  scrolling; clearing it returns to 190 pixels without changing the application window.

#### AI Explain and Runner display corrections — 2026-08-01

- AI Explain keeps the completed Text view identical to the streamed Markdown, initializes the
  restricted WebView2 document after its tab loads, and uses an application-owned `TabItem`
  template so the selected-state background and foreground cannot be replaced by a framework
  white-on-white default. The header stays centered while the response content stretches to
  the complete surface before the first streamed token.
- Tasks adds an enabled-by-default Auto-scroll control. Scrolling is deferred until after WPF
  text layout, can be paused and resumed, and returns to the latest line when re-enabled.
- Large remains repeatable by deriving the panel height from its boolean state: 190 pixels in
  normal mode and 490 pixels when selected. It never resizes or repositions the window.
- A separate local Release build visually verified AI Explain in Dark and Light, raw Markdown
  retention after completion, actual WebView2-rendered headings/lists/bold/code/separators, and
  the full-size empty response surface. The same build completed two full Large cycles
  (190 → 490 → 190 → 490 → 190) without moving or resizing the application window.
- Final Release verification completed with zero warnings and errors; 261 tests passed, zero
  failed, and one local Ollama integration test was intentionally skipped (262 total).

#### Visual Studio debug runner layout — 2026-08-01

- Debug builds of the UI now mirror the published sibling layout inside `bin`: a
  Debug-only `ProjectReference` (`ReferenceOutputAssembly=false`) builds the runner with
  the UI, and a Debug-only `CopyRunnerBesideUi` target copies its output to
  `$(TargetDir)..\Runner\`. `RunnerPathResolver` finds the runner through its existing
  `..\Runner` probe, so F5, Run now, and registration work with no code or configuration
  change. Release builds and `scripts/publish.ps1` are untouched.
- `SchedulerStartupService` is registered only in Release (`#if !DEBUG`). A debugging
  session therefore never rewrites Task Scheduler registrations that point at the deployed
  runner. Saving a task while debugging still registers it against the Debug runner, and the
  next deployed-UI start resynchronizes it to production; the Dashboard Resync schedules
  button remains available in Debug as an explicit action.

### Stage 16 — Windowless runner and run monitor — 2026-08-01

#### Product decisions and specification alignment

- Added normative §19 to `Docs/Prompts/00-Shared-Specification.md` and the stage prompt
  `Docs/Prompts/16-Stage16-WindowlessRunnerAndRunMonitor.md`. §19 supersedes the §7.5 console
  subsystem wording: the runner is now `WinExe`, so scheduled Task Scheduler executions never
  open or flash a console window.
- Observability moves from an ephemeral console to persistent per-execution JSONL run logs
  plus a dedicated UI **Runs** page, deliberately file-based (S16-D4): no named pipe, socket,
  or other IPC. A monitor opened mid-run replays the file from the beginning, so joining a
  running execution loses nothing, and past runs remain inspectable and exportable.
- The private `ODMLA_CONTEXT:` / `ODMLA_DEBUG:` channels keep flowing to stdout for the
  interactive Run now consumer but are never persisted in run logs (S16-D5), preserving the
  Stage 15 rule that debug payloads stay out of durable logs.

#### Core and runner

- New Core public surface: `RunLogStream`, `RunLogLine`, `RunLogRunInfo`, `RunLogReadResult`,
  `RunLogSession` (flushed shareable JSONL writer with per-task retention pruning),
  `RunLogSessionHolder`, `RunLogTeeWriter` (line-buffering tee that filters the private
  protocol prefixes), `IRunLogCatalog`/`RunLogCatalog` (newest-first enumeration reading the
  head record plus an 8 KB tail probe; offset-based incremental `ReadLines` tolerating a
  truncated trailing record), `LocalAppPaths.RunLogsDirectory`, and
  `LoggingOptions.MaxRunLogsPerTask` (default 50, validated positive). Session and catalog
  accept an optional root override for tests; production uses `%LOCALAPPDATA%` paths.
- The runner project is `WinExe` with `AllowUnsafeBlocks` enabled solely for the
  `LibraryImport` source generator behind `AttachConsole(ATTACH_PARENT_PROCESS)`.
  `ConsoleStreams.Acquire` keeps redirected streams as provided (UI Run now is byte-for-byte
  unchanged) and attaches to a parent shell console otherwise, so manual CLI use still prints.
- `RunnerApplication` wraps output and error in `RunLogTeeWriter`s around one
  `RunLogSessionHolder`, and the Serilog runner sink is now the in-project
  `RunnerTextWriterSink` writing console-formatted events through the tee; the
  `Serilog.Sinks.Console` package reference was removed from the runner (the pinned version
  remains in `Directory.Packages.props`). `RunnerCommandHandlers.RunAsync` opens the session
  once the task id is known and completes it with the final exit code in a `finally` block;
  session failures report an incident and degrade to an unlogged run without affecting the
  exit code (best-effort rule, §19.3).

#### UI

- New `PageRoute.Runs` between Logs and Tools, resolved to a singleton
  `RunMonitorViewModel` rendered by `RunMonitorPage`. The view model reconciles the run list
  in place (preserving selection), tails the selected run with catch-up-then-incremental
  reads on a 1 s `DispatcherTimer` started by `Activate` and stopped by `Deactivate` (list
  reconciliation every fifth tick), and honors a display line limit (500–10 000, default
  2 000) that trims oldest rendered lines without touching the file. Clear empties only the
  display; Save as exports `HH:mm:ss.fff [out|err] text` lines through
  `IDesktopInteractionService.SelectSavePath`; Open folder shells into the run-log root.
  Auto-scroll follows appended lines and can be paused, mirroring the Tasks panel semantics.

#### Verification

- Release build: all eight projects succeeded with zero warnings and zero errors (`-m:1`).
- Deterministic tests: 269 passed, zero failed, one intentionally skipped local Ollama
  integration test (270 total), including new coverage for run-log round-trip and
  incremental tailing, tee protocol filtering, retention pruning, malformed trailing-line
  tolerance, and Runs view-model reconciliation, line limit, clear, and export.
- Manual checks remaining for the operator: a scheduled task fires without any visible
  window, and the Runs page follows a live execution joined mid-run.

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
  accumulate/cancel semantics, tool-group opt-in, the non-whitelistable `ai` group, prompt
  extraction ordering, LF normalization, multiline round-trip losslessness, targeted-only
  rewriting, blocked Apply cases, the non-blocking duplication warning, and grapheme-safe
  preview truncation.

#### Residual risks

- **The assistant generation prompt was not rebuilt from the new metadata.** §20.2 requires
  `WorkflowAssistantPromptBuilder` to consume `Description` and `ArgumentDefinitions` and to
  increment its version; it still reads `Arguments`/`OptionalArguments` and
  `GenerationVersion` remains `workflow-generation-v1`. The Stage 13 assistant therefore does
  not yet benefit from the enriched descriptions. This is a known, unaddressed gap.
- **GUI verification was completed after the implementation pass.** Both dialogs were first
  validated by build and unit tests only; a later manual operator session covered layout,
  keyboard traversal, minimum size, long prompts and paths, and Light/Dark rendering. That
  session produced three corrections, all shipped and covered by tests: the Insert commands are
  hidden on the Placeholders tab, the prompt text box wraps with the horizontal scroll bar
  disabled, and Validate raises a short snackbar notification in addition to the status line.
- Canonical re-serialization intentionally discards manual formatting on every successful
  structured insertion and prompt application, which is visible to a user who hand-formatted
  the workflow array.
- Structured insertion positions a snippet relative to top-level elements only; a caret inside
  a deeply nested value still resolves to its enclosing top-level element.

#### Release 1.2.7 — 2026-08-02

- Packages the Stage 17 deterministic workflow authoring aids as version 1.2.7, set in
  `Directory.Build.props`.
- The release also carries the full Stage 17 documentation pass: shared specification §20,
  this ledger entry, `README.md`, both HTML user manuals, both FirstRun guides,
  `Docs/theme-audit.md`, `Docs/acceptance-report.md`, and the Stage 17 row of
  `Docs/Prompts/README.md`.
- Verification is build and unit tests only. GUI checks for both new dialogs are recorded as
  outstanding in `Docs/theme-audit.md` and `Docs/acceptance-report.md` and are not claimed as
  release evidence.

### Stage 20 — Agent prompt placeholders — 2026-08-03

#### Scope and pre-Stage-18/19 integration decision

- Implemented `Docs/Prompts/20-Stage20-AgentPromptPlaceholders.md` directly on the completed
  Stage 17 code line at the product owner’s request. At that point, Stage 18 compact Guardrails
  and Stage 19 SQLite were deliberately unimplemented, and no Stage 20 code, test, package,
  setting, task field, tool group, Safety flag, migration, or documentation example depended on
  them. The later Stage 18 entry below supersedes only the Stage 18 status.
- Expansion is selected only by task-owned prompt-only boundaries: Runner `run` when
  `Workflow.Count == 0`, Runner `test-prompt`, UI Run now through the same child Runner, and
  Prompt editor Test over its current unsaved prompt. Workflow precedence remains unchanged;
  AI Playground, helper/explanation, Guardrails, direct skills, Tool tester, workflow
  `ai.prompt`, and health calls retain their existing raw/taskless behavior.

#### Core placeholder contracts

- Added the pure `AgentPromptPlaceholderResolver.Resolve(prompt, taskId, taskName, localNow)`.
  It scans the original UTF-16 source once, performs no I/O or logging, never rescans inserted
  task identity, preserves all unresolved spans exactly, and returns the original string
  instance when no replacement is required.
- Added immutable public result surface `AgentPromptPlaceholderResult`,
  `AgentPromptPlaceholderOccurrence`, and `AgentPromptPlaceholderWarningKind` with the closed
  categories `Unknown`, `WorkflowOnly`, `InvalidDateTimeFormat`, and `MalformedBraces`.
  Occurrences retain source offsets and lengths only; they never retain raw marker bodies.
- Added `AgentPromptPlaceholderCatalog.Forms`/`HelpText` for Prompt editor authoring and
  `AgentPromptPlaceholderWarning.Code`/`FormatSummary` for content-free aggregate feedback.
  The durable code is `AGENT_PROMPT_PLACEHOLDERS_UNRESOLVED`.
- Moved the shared `Today`/`Now` parsing, invariant formatting, 100-character format validation,
  and ordinal filename alias normalization into Core `DateTimePlaceholderFormatter`, with typed
  `DateTimePlaceholderResult`/`DateTimePlaceholderStatus`. `WorkflowPlaceholderCatalog` and
  `WorkflowPlaceholderResolver` both consume it; the workflow layer retains strict exceptions.

#### Runner, report, and UI integration

- `RunnerCommandHandlers.ExecutePromptOnlyAsync` is the shared prompt path used beneath both
  `run` and `test-prompt`. It captures one `TimeProvider.GetLocalNow()` value, resolves before
  `IMyLocalAgentService.RunPromptAsync`, writes one content-free warning line, retains the
  existing tools, immutable `ToolExecutionContext`, generation policy, cancellation, and agent
  loop limits, and carries warnings even when the later provider/tool operation fails.
- Added `ExecutionWarning(Code, Message, OccurrenceCount)` and
  `WorkflowResult.Warnings`, defaulting to an empty collection without changing existing
  constructors. `MarkdownReportWriter` emits `## Warnings` only when needed. The final output
  and report-index shape/status remain unchanged; warnings cannot convert success to failure.
- Prompt editor now receives `TimeProvider`, resolves current unsaved text with the loaded task
  id/name before streaming, displays help from the Core catalog and an aggregate memory-only
  warning callout, and keeps warnings separate from model output/chunk count. Feedback is
  cleared on edit, load, or the next test and remains after completion or cancellation. Prompt
  Save no longer trims the source, preserving the entered template exactly.
- Prompt editor help/warnings use only semantic `MutedTextBrush`, `SurfaceBrush`, and
  `WarningBrush` resources, so Light and Dark use the active application palette without inline
  colors or a new theme.

#### Privacy and verification

- Durable warning output contains only the stable code, program-owned category labels, total,
  and per-category counts. It excludes prompts, marker bodies, task names, replacements,
  source spans, and expanded paths and never calls `IIncidentReporter`. Exact marker projection
  was intentionally omitted from the UI because aggregate feedback satisfies the authoring need
  with a smaller memory/privacy surface.
- Deterministic coverage includes defaults, offsets, invariant custom formats, aliases,
  whitespace/case behavior, one supplied snapshot, Unicode task identity, non-recursion,
  unknown/workflow-only/invalid/malformed preservation, source ordering, repeated counts,
  pathological braces, unchanged Unicode/line endings, concurrency, safe summaries, Runner
  propagation/privacy, Prompt editor unsaved text and warning lifecycle, report warnings, and
  unchanged workflow date behavior.
- Release build completed with zero warnings and zero errors. The test suite completed with
  404 passed, zero failed, and one intentionally skipped local Ollama integration test.
- Both manuals were updated in structural EN/FR parity with copyable dated/timestamp/identity/
  unknown/workflow-only examples. Automated structural validation is recorded in the Stage 20
  acceptance report. The WPF application was not launched in this implementation pass, so live
  Prompt editor warning/help rendering in Light and Dark remains a manual follow-up rather than
  claimed evidence.

#### Release 1.2.8 — 2026-08-03

- Packages the Stage 20 agent prompt placeholder implementation as version 1.2.8, set in
  `Directory.Build.props`.
- Includes the Tasks page `Open Folder` action for opening the task storage directory.
- At release 1.2.8, Stage 18 compact Guardrails and Stage 19 SQLite were intentionally deferred;
  the later Stage 18 entry below supersedes the Stage 18 status.

### Stage 21 — Model information placeholders — 2026-08-03

#### Specification and locked format

- Added `Docs/Prompts/21-Stage21-ModelInformationPlaceholders.md` using the shared staged
  specification protocol and added normative shared-specification §24. Stage 21 supersedes only
  the Stage 20 exclusions for model-information values; task/date identity expansion and its
  privacy-preserving warning behavior remain unchanged.
- Added exactly four case-insensitive placeholders with invariant, single-pass output:
  `ModelName` returns the configured model id; `ModelSettings` returns
  `Temp <temperature> - TopP <topP>`; `ModelContextSize` returns the ungrouped declared token
  count; and `ModelInfo` returns
  `ModelName <name> - Temp <temperature> - TopP <topP> - Context <tokens>`.
- Context is explicitly the global declared `ContextWindowTokens`, not consumed tokens,
  remaining capacity, output budget, or provider telemetry. Temperature and Top P are the
  immutable effective values already applied to the call: step over task over Settings, task
  over Settings, or Settings for taskless/playground calls.

#### Public API and shared resolution

- Added immutable public `ModelPromptPlaceholderContext`, which snapshots model name,
  Temperature, Top P, and context tokens and exposes exact `ModelSettings`,
  `ModelContextSize`, and `ModelInfo` projections.
- Added pure public `ModelPromptPlaceholderResolver` with `Resolve`, `IsSupported`, and
  `TryResolve`. It performs one regex pass, resolves only the four program-owned names,
  preserves unrelated complete markers exactly, uses invariant formatting, and never rescans
  inserted model text.
- Extended `ILlmClientService` with reloadable `ModelName`; both Ollama and OpenAI-compatible
  services project their monitored configured model. `MyLocalAgentService` resolves model
  placeholders after provider selection and before tracing/model dispatch in direct
  `ai.prompt`, non-streaming agent, and streaming agent paths.
- `AgentPromptPlaceholderResolver.Resolve` accepts an optional immutable model context and
  recognizes the four names even when final provider-bound resolution is deferred. They never
  produce Stage 20 unresolved warnings. `AgentPromptPlaceholderCatalog` now advertises all four.

#### Workflow, playground, and authoring integration

- Advanced `WorkflowPlaceholderCatalog.Version` from `1` to `2`, added the four definitions,
  and made validation accept them at every workflow string position. Existing task/output/date
  markers, structured selectors, strict unknown failure, and recursive JSON traversal remain.
- `SequentialWorkflowEngine` builds one model context per step. AI steps use their exact
  step-effective policy; non-AI tool steps use the task-effective policy. The context is passed
  through nested object/array string resolution before tool, skill, or prompt execution.
- AI Playground resolves model markers before both its direct raw-provider call and agent path;
  the latter also retains the shared dispatch safety net. Workflow assistant/catalog metadata
  receives the version-2 definitions without a second handwritten catalog.

#### Task run logs and privacy

- Added `RunLogSession.WriteModelInfo`. After a Runner loads the task, it resolves the selected
  configured model plus task-effective policy and writes exact `ModelInfo` as the first visible
  output line. The structured JSONL `start` record remains physically first; retention,
  incremental tailing, protocol filtering, stream mapping, flushing, and `end` behavior are
  unchanged. Workflow step overrides are not duplicated as per-step headers.
- The header contains only model name, Temperature, Top P, and declared context. Stage 21 adds no
  prompt, task name, tool argument/result, path, SQL, external content, endpoint, secret, model
  output, or exception payload to logs/incidents. Expansion grants no authorization or Safety
  capability and performs no extra provider, health, model, tool, or network call.

#### Verification and documentation

- Deterministic tests cover exact punctuation and numeric formatting, case/whitespace handling,
  unrelated-marker preservation, non-recursive model names, prompt-only warning compatibility,
  shared agent dispatch, workflow step-effective values, AI Playground raw mode, catalog
  metadata, and first-visible run-log ordering. The provider reload, generation hierarchy,
  workflow, Stage 20, privacy, and run-log regression suite remains included.
- Updated README, prompt index, shared specification, and both standalone manuals in structural
  FR/EN parity. The manuals state the effective-policy hierarchy, declared-context limitation,
  all supported surfaces, exact values, and task-log behavior.
- No package, setting, persisted task field, migration, provider, tool, skill, page, dialog,
  model call, external service, or authorization was added.
- Final gate: NuGet restore succeeded from the normal user cache/source; the Release solution
  build completed with zero warnings and zero errors; deterministic tests completed with 411
  passed, zero failed, and one intentionally skipped local Ollama integration probe (412 total).
  Both standalone manuals passed HTML parsing, unique-id/internal-link checks, external-resource
  checks, and Stage 21 marker/example parity. `git diff --check` passed.

#### Release 1.2.9 — 2026-08-03

- Packages the Stage 21 model information placeholders and task-log model header as version
  1.2.9, set centrally in `Directory.Build.props`.
- The annotated Git tag for this release is `V129` and identifies the Stage 21 implementation.

### Stage 18 — Compact external-content Guardrails — 2026-08-03

#### Scope and superseded Stage 7 behavior

- Implemented `Docs/Prompts/18-Stage18-CompactExternalContentGuardrails.md` on the completed
  Stage 21 line. This entry supersedes the Stage 7 complete-source and message-sequential rules
  only for Gmail reads, `http.get`, and `http.getText`. The singleton `ScreenAsync` and legacy
  complete-source `SummarizeAsync` contracts remain available to the Trust filter and direct
  compatibility callers. Web Search, HTTP POST, Gmail send/attachments, Calendar, Dropbox,
  filesystem, authorization, Safety, providers, models, packages, settings, task fields, aliases,
  pages, and migrations are unchanged.
- Gmail and HTTP now establish a deterministic minimization boundary before any LLM request.
  Omitted source characters are discarded and represented only by application-authored markers
  and aggregate counts. Compaction does not classify omitted text as safe.

#### Public APIs and deterministic projectors

- Guardrails adds `CompactExternalContentItem`, `CompactExternalContentStatus`,
  `CompactExternalContentResult`, `CompactExternalContentBatchFailure`,
  `CompactExternalContentBatchResult`, `CompactExternalContentPhaseException`, and
  `IContentTrustService.ProcessCompactAsync(IReadOnlyList<CompactExternalContentItem>,
  CancellationToken)`.
- Core adds pure `GmailContentProjector.Project(EmailMessageDto, int)` with
  `GmailContentProjection`, and `HttpContentProjector.Project(string, string?, int)` with
  `HttpContentProjection`. Gmail canonical order is From, To, Subject, Received, Snippet, Body;
  field limits are 512, 512, 1,000, invariant timestamp, 1,500, and the remaining overall
  budget. English/French/original-message quoted suffixes are removed only at compiled line-start
  boundaries. HTTP handles minified JSON, normalized text/XML, readable HTML, and an unsupported
  media marker. Guardrails option validation now requires `MaxContentChars >= 2048`.

#### Batch schemas, versions, limits, and cache

- `CompactContentBatchScreenerPrompt.Version` is `compact-screen-v1`. Its exact response schema
  is `{"results":[{"id":"opaque-id","score":85,"indicators":["verbatim suspicious span"]}]}`.
- `CompactContentBatchSummarizerPrompt.Version` is `compact-summary-v1`. Its exact response schema
  is `{"results":[{"id":"opaque-id","summary":"factual summary"}]}`.
- Ordered inputs are greedily partitioned at 12 items or six times `MaxContentChars`. Every
  uncached eligible batch makes at most one source-screen request, one eligible-only summary
  request, and one final-summary-screen request. Each item has a fresh nonce block; ids are
  correlated strictly and response order is irrelevant. Unknown, duplicate, missing, malformed,
  overlong, or out-of-range data invalidates the complete phase.
- All calls use the selected provider/model snapshot, fixed Guardrail policies, direct
  `IChatClient`, `Tools=null`, and a program-owned `MaxOutputTokens` bound. Phase failures produce
  one content-free exception without inner exception or provider response, do not trigger
  singleton replay, and do not stop later batches. Caller cancellation stops immediately.
- Verdict and compact-summary caches remain process-local with the existing 24-hour TTL. Keys
  contain content hash, provider, model, dedicated prompt version, and requested summary maximum
  where applicable; opaque id, nonce, batch composition, position, and neighbors are excluded.

#### Delivery, privacy, and verification

- Gmail reads project every in-limit message before batch processing and retain connector order
  and root count/limit fields. Success delivers only projected From/To/Subject/Received plus a
  labeled factual summary. Fallback delivers only projected headers plus a bounded Gmail-review
  marker under source trust. Snippet, Body, quoted history, omitted suffixes, and generated text
  that failed final screening never reach workflow context. Projection metadata is additive.
- `http.get` screens once and delivers one protected media-aware compact prefix without summary.
  `http.getText` processes one compact item and delivers only its protected factual summary or a
  review marker. Both expose original/projected/omitted counts and a truncation flag.
- Tool arguments no longer log HTTP URLs or Gmail recipient addresses. Tool failure logging emits
  only the exception type, and compact incidents carry phase/provider/model/item count/timing only.
- Dedicated deterministic coverage proves projections, quoted-history and HTML rules, strict id
  correlation, eligible-only phases, per-item effective trust/cache identity, cancellation, later-
  batch continuation, summary-only delivery, and fake-provider call counts: 1–12 eligible items
  use three calls, 13 use six, low-trust batches use source screening only, and fully cached items
  add zero calls.

No package or package version changed.

#### Release 1.3.0 — 2026-08-03

- Packages the Stage 18 compact external-content Guardrails implementation as version 1.3.0,
  set centrally in `Directory.Build.props`.

### Stage 18A — Extensible Tool Integration Foundation — 2026-08-04

#### Scope and compatibility

- Implemented `Docs/Prompts/18A-Stage18A-ExtensibleToolIntegration.md` after Stage 18 and before
  the still-unimplemented Stage 19 SQLite tool. This refactor adds no tool, group, alias, package,
  setting, Safety flag, persisted task field, migration, provider call, page, or external service.
- The six existing task groups and synthetic authoring-only ai group retain their exact stable
  names and behavior. Existing task JSON requires no migration; new-task selections remain false.

#### Central group catalog

- Added immutable public `ToolGroupDefinition` with stable name, English display name,
  description, and task-whitelist eligibility.
- `ToolGroups.Catalog` is the single stable display order. `TaskWhitelist` and the
  case-insensitive `All` set derive from it; `Find` and `GetDisplayOrder` provide shared lookup and
  ordering. Existing group constants remain source-compatible.
- Deterministic consistency tests require unique nonblank metadata, every Tool Map group to resolve,
  every whitelist group to own a non-AI runtime definition, and ai to remain non-whitelistable.

#### Metadata-driven UI integration

- Added `ToolGroupSelectionItem`. `TaskEditorViewModel` exposes a read-only observable collection
  built from `ToolGroups.TaskWhitelist`; it no longer owns six per-group Boolean properties or six
  load/save/apply branches.
- Task editor XAML now renders one templated wrapping `ItemsControl`. Generic load, save, AI Helper
  Apply, Tool catalog opt-in, translation example, and assisted Undo operate on stable group names
  in catalog order.
- `RequiresLocalPathAuthorization` is computed from each group's Tool Map local-path arguments.
  Authorized locations visibility no longer special-cases filesystem and Dropbox names.
- Workflow Tool catalog and Tool tester now sort through `ToolGroups.GetDisplayOrder`; the former's
  private order array and the tester's alphabetical group ordering were removed. Scheduled-task
  validation lists expected groups from the ordered whitelist projection.

#### Verification, documentation, and residual boundary

- Added catalog/group/UI projection tests and updated Task editor tests to use generic selection.
  Release build completed with zero warnings and zero errors; 449 deterministic tests passed, zero
  failed, and one local Ollama integration probe was intentionally skipped.
- Updated shared specification, prompt index, README, First Run guides, both standalone manuals,
  theme audit, and acceptance report. The manuals preserve FR/EN semantic parity.
- This is compiled-product extensibility, not runtime plug-in discovery. A future group still needs
  catalog metadata, Tool Map entries, concrete registration, authorization/Safety design, tests,
  and documentation. Live Light/Dark inspection of generated checkbox wrapping/tooltips remains a
  manual follow-up.

### Stage 18C — File-system Recycle Bin and document conversions — 2026-08-04

#### Scope and locked behavior

- Added `Docs/Prompts/18C-Stage18C-FileSystemRecycleAndDocumentConversions.md` and normative
  shared-specification §21C after Stage 18A. Exactly three aliases were added to the existing
  `filesystem` group; no group, task field, migration, page, provider/model call, connector,
  external service, or runtime plug-in mechanism was added.
- `filesystem.moveToBin(path)` accepts files or directories, requires effective ReadWrite path
  access plus independently enabled `Safety:AllowMoveToBin`, and never falls back to permanent
  deletion. The shipped gate is false and is loaded, saved, and displayed with the other WPF
  Safety settings.
- `filesystem.markdownToHtml(sourcePath, destinationPath?, theme?)` reads an authorized Markdown
  source and independently authorizes the explicit or `.html`-derived destination. `Dark` is the
  default and `Light` is explicit. Repeated conversion replaces the destination, while identical
  source/destination paths are rejected.
- `filesystem.htmlToText(sourcePath, format?)` defaults to normalized `Text` and optionally
  returns principal `Markdown` structure in compact JSON. It performs no implicit file write.

#### Public API and implementation surface

- Added `MarkdownHtmlTheme`, `HtmlTextFormat`, and `IRecycleBinService`.
- Added `IFileSystemTool.MoveToBinAsync`, `MarkdownToHtmlAsync`, and `HtmlToTextAsync` with matching
  `FileSystemTool` implementations and agent functions `filesystem_move_to_bin`,
  `filesystem_markdown_to_html`, and `filesystem_html_to_text`.
- Added `SafetyOptions.AllowMoveToBin` and its fail-closed `IsEnabled` mapping.
- Added `LocalPathDerivationKind.OptionalDestinationOrSameDirectoryExtension` plus
  `LocalPathArgumentDefinition.DefaultValue`, allowing the Stage 13 analyzer to represent an
  optional explicit destination or deterministic `.html` fallback without inventing grants.
- Added the internal `WindowsRecycleBinService`, which uses the Windows Shell delete-with-undo
  operation with confirmation, progress, and error UI suppressed. Tests inject
  `IRecycleBinService` and never touch the real Recycle Bin.
- Added the internal deterministic `HtmlDocumentConverter`. It removes active/non-content nodes,
  prefers main/article/body, decodes entities, and produces normalized Text or Markdown headings,
  lists, emphasis, links, quotes, inline/fenced code, and line breaks.
- Core now directly references the already centrally pinned Markdig 1.3.2 package. No version or
  new package pin was added. Markdown raw HTML remains disabled; standalone output embeds only CSS
  and a restrictive CSP blocking scripts and remote resources. Both conversion sources retain the
  existing `FileSystem:MaxReadBytes` cap and remain outside Guardrails classification.

#### Stage 18A, WPF, privacy, and documentation

- Tool Map owns descriptions, ordered required/optional arguments, enum values, Safety metadata,
  and read/write path semantics. The WPF Tool catalog, Tool tester, AI Workflow Helper, workflow
  validation, task whitelist, and authorization proposals derive the new aliases without a new
  checkbox, group-order array, or alias list.
- Settings gained one English `Allow moving files to the Recycle Bin` checkbox through the
  existing MVVM Toolkit load/dirty/save pipeline. User settings persistence and Task editor helper
  Safety snapshots include the new value.
- Tool logs never contain Markdown, HTML, extracted content, or generated HTML. Local document
  data receives no new durable logging, incident payload, provider call, network request, or
  Guardrails delivery path.
- Updated README, prompt index, appsettings defaults, and the standalone French and English user
  manuals. Both manuals document aliases, arguments, defaults, authorization, Safety, restricted
  output, and explicit chaining with `filesystem.write` in semantic parity.

#### Verification and residual checks

- Initial restore using the isolated temporary CLI cache reproduced the ledgered invalid Cronos
  cache (`NU5037`). `dotnet restore OD.MyLocalAgent.sln -m:1` with the normal user cache then
  succeeded with every project current.
- `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1` succeeded for all
  eight projects with zero warnings and zero errors.
- `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore
  --configuration Release -m:1` completed with 459 passed, zero failed, and one intentionally
  skipped local Ollama integration probe (460 total).
- Tests cover dedicated gate/fake recycle dispatch, read/write authorization, source size cap,
  default/explicit destinations, Dark/Light restricted HTML, inactive HTML extraction, enum/default
  registry binding, Tool Map reflection, analyzer derivation/dynamic warning, catalog/tester
  visibility, and settings persistence.
- All five sample JSON files parsed. Both manuals retain 22 balanced sections and 30 unique ids,
  no duplicate id, missing internal anchor, or external resource, with identical Stage 18C alias
  occurrence counts.
- The WPF application was not launched and the production Windows Recycle Bin adapter was not
  invoked in this implementation pass. Live Settings/catalog rendering and a disposable manual
  Recycle Bin recovery check remain explicit operator follow-ups.
