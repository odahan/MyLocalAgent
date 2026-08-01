# STAGE 16 — Windowless Runner and Run Monitor

> Run only after **Stage 15** is finished. Stages 9 and 10 remain abandoned proposals and are
> neither prerequisites nor in scope. The existing task-owned sequential workflow remains the
> product model.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§19. §19 is
   the normative contract for this stage.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs, privacy boundaries, protocol
   channels, and concurrency constraints are binding.
3. The current Runner (`RunnerApplication`, `RunnerCommandFactory`, `RunnerCommandHandlers`,
   `Program`), `RunnerService` (UI Run now child process), `RunnerContextProtocol`,
   `RunnerDebugProtocol`, Serilog composition in `HostApplicationBuilderExtensions`,
   `LocalAppPaths`, `LoggingOptions`, the shell navigation (`MainWindowViewModel`,
   `PageRoute`, `App.xaml`/`App.xaml.cs`), the Logs page, tests, README, and manuals.

## 2. Mission

Remove the console window that Windows Task Scheduler opens for every scheduled execution,
without losing observability — and turn that constraint into a feature: a persistent per-run
output log plus a dedicated **Runs** page in the UI that can join a run mid-execution, replay
past runs, and export their output.

Add:

- a windowless runner (`WinExe`) that still prints to a parent shell when invoked manually;
- one flushed, shareable JSONL log per `run --task <id>` execution under
  `%LOCALAPPDATA%\OD.MyLocalAgent\logs\runs\<taskId>\`;
- per-task retention (`Logging:MaxRunLogsPerTask`, default 50);
- a Runs navigation destination: run list (task, start, status, exit code), live tail with
  catch-up, display line limit, clear, save as, auto-scroll, open-folder.

## 3. Product decisions (locked by the product owner — do not re-open)

The decision table lives in §19.1 of the shared specification (S16-D1 … S16-D9). Highlights:

| # | Decision |
|---|---|
| S16-D1 | The runner is a `WinExe`; scheduled executions never show a window. |
| S16-D2 | Manual shell invocations keep printing via `AttachConsole(ATTACH_PARENT_PROCESS)`; redirected streams (UI Run now) are used as provided, no attach. |
| S16-D3/D4 | Observability is file-based only: per-run JSONL, flushed per line, readable while running; no pipes or sockets. A monitor opened mid-run replays from the beginning. |
| S16-D5 | `ODMLA_CONTEXT:` / `ODMLA_DEBUG:` lines pass through to stdout but are never persisted in run logs. |
| S16-D6 | Oldest run logs beyond `Logging:MaxRunLogsPerTask` are pruned when a new session starts. |
| S16-D7/D8 | One Runs page for scheduled and Run now executions; the Tasks Run now panel is unchanged. |
| S16-D9 | Run logs are private diagnostic files, treated like reports. |

## 4. Critical constraints (restated)

- Keep the existing `RunnerService` child-process contract: stdout/stderr redirection, the
  context and debug Base64 protocols, and exit codes are unchanged. A `WinExe` child with
  redirected streams behaves identically for the parent.
- Run-log failures (open, write, prune) must degrade gracefully: report one incident, keep
  executing the task, never change the exit code because of logging.
- No ambient mutable state across concurrent runs: the active session travels through the
  writers created for the current invocation; two simultaneous runner processes write to two
  distinct files.
- Respect R2 (English), R4/R5 (warning-clean, options pattern, `TimeProvider`), R7 (never
  crash), R10 (small diffs). No new packages beyond what the solution already pins.
- Readers must tolerate a malformed trailing line (crash mid-write) without incidents.
- The Logs page, rolling application log, incidents, reports, and index are unchanged.

## 5. Scope of work

1. **Core** — run-log contract (§19.2): entry records, a session writer (create directory,
   write `start`, mirror lines, write `end`, prune retention), a catalog/reader (enumerate
   runs, incremental offset-based tail), a line-buffering tee `TextWriter` that filters the
   private protocol prefixes, `LocalAppPaths.RunLogsDirectory`, and
   `LoggingOptions.MaxRunLogsPerTask` with validation.
2. **Runner** — `<OutputType>WinExe</OutputType>`; parent-console attach in `Program` when
   streams are not redirected; tee wiring in `RunnerApplication` (including the Serilog
   runner sink through the tee); session open/close around the `run` handler.
3. **UI** — `PageRoute.Runs`, navigation item between Logs and Tools, view model
   (list refresh, selected-run tail, line limit, clear, save as, auto-scroll, open folder),
   page XAML consistent with the Logs page look, DI and `DataTemplate` registrations.
4. **Tests** — §19.5 coverage in `OD.MyLocalAgent.Tests`.
5. **Docs** — README (runner behavior, Runs page, new setting), ledger entry, this pack's
   `README.md` table row.

## 6. Required verification (gate)

- `dotnet build OD.MyLocalAgent.sln --configuration Release -m:1` — zero warnings, zero
  errors.
- `dotnet test OD.MyLocalAgent.sln --no-build --configuration Release -m:1` — all green
  (one pre-existing local Ollama integration test remains intentionally skipped).
- `git diff --check` — clean.
- Manual checks (human): a scheduled task fires with no visible window; the Runs page lists
  the execution, follows it live when opened mid-run, and exports its text.
