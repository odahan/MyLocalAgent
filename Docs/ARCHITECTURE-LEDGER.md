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
