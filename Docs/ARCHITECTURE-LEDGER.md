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
