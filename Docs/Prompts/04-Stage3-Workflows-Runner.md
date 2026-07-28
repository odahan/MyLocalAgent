# STAGE 3 — Workflow engine + headless Runner

> Run only after Stage 2's gate is green.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read; **§7.4, §7.5, §8.1, §8.3, §8.4,
   §9-E/G** are your scope.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; use the Core APIs exactly as recorded there
   (especially `IToolRegistry`, the workflow step model, `ITaskSchedulerService.ResyncAllAsync`).
3. The current file tree.

## 2. Mission

Implement the workflow engine (`OD.MyLocalAgent.Workflows`) and the headless CLI executor
(`OD.MyLocalAgent.Runner`) so that a task defined as in spec §8.1 runs end-to-end under
Windows Task Scheduler conditions: deterministic workflow execution, report writing, rolling
schedule re-registration.

## 3. Critical constraints (restated)

- Workflow alias resolution goes through Core's `ToolMap`/`ToolRegistry` — no second alias
  table in this layer.
- Placeholder and error-policy semantics exactly per spec §8.3 (effective policy =
  step `onError` ?? task `errorPolicy` ?? `StopOnError`; exponential backoff with injected
  `TimeProvider`).
- The runner must work regardless of the working directory Task Scheduler assigns:
  configuration rooted at `AppContext.BaseDirectory` (spec §8.5), all data under
  `%LOCALAPPDATA%\OD.MyLocalAgent\…`.
- Runner = same DI composition as the UI (`AddODServices` from Core) + console Serilog sink;
  top-level `try/catch` routed through `AppExceptionHandlers` (spec R7).
- `System.CommandLine`: use the **installed version's** API (verified by compiling — its betas
  broke the surface repeatedly; no deprecated companion packages).
- Warning-clean, complete files, XML docs, English only.

## 4. Scope

### 4.1 `OD.MyLocalAgent.Workflows`

Implement spec §7.4 completely:

- Step model: `ToolStep`, `SkillStep`, `PromptStep` (parse from both §8.3 forms; reuse the
  Core step model from Stage 2 if it was built there).
- `WorkflowContext`: ordered outputs dictionary keyed by step id; `previous.output` alias.
- Placeholder resolver: plain string substitution into `args` values and prompt templates
  immediately before the step runs; unknown placeholder → `WorkflowPlaceholderException`
  (step fails → policy applies).
- `IWorkflowEngine` / `SequentialWorkflowEngine`: sequential execution; per-step error policy
  (`StopOnError` / `ContinueOnError` / `Retry` with `retryCount` default 3, backoff
  `retryBackoffSeconds * 2^(attempt-1)` via `TimeProvider.DelayAsync`); skill step with no
  `args.text` ← previous output (spec §8.3); whitelist enforcement (step alias's tool group ∈
  task `tools` else step fails); structured per-step logs (start, duration, status, output
  size); returns `WorkflowResult` (success flag, per-step results, final text =
  last step output).
- Report writer (`IReportWriter`): Markdown per spec §7.4 to
  `reports\<taskId>\<yyyyMMdd-HHmmss>.md` (header: task id/name, UTC start/end, provider,
  status) + one line appended to `reports\index.jsonl` (spec §8.4).

### 4.2 `OD.MyLocalAgent.Runner`

Implement spec §7.5 completely, with the commands:

- `run --task <id>` — full sequence: acquire named mutex `Global\OD.MyLocalAgent.<id>`
  (contention → message + exit 1) → load & validate task (§8.1/§8.3) → build agent +
  whitelisted tools → execute workflow if present else the plain prompt path → write report +
  index line → re-register next occurrence (`ITaskSchedulerService.RegisterAsync`) → exit 0.
  Failures: exit 1 (task error), 2 (configuration error incl. `LlmConfigurationException`),
  3 (task not found). Disabled task → message + exit 1.
- `list` — tasks table (id, name, schedule, enabled, next run from `GetNextRun`).
- `test-prompt --task <id>` — prompt path without scheduling; prints the final report.
- `test-tool --alias <alias> --args '<json>'` — direct Tool Map invocation; pretty-printed,
  timed result; safety blocks print which `Safety` flag enables the operation (and exit 1).
- `resync-schedules` — `ITaskSchedulerService.ResyncAllAsync`; prints per-task
  (re)registration summary.

### 4.3 Tests (spec §7.7, engine/runner portion)

- `SequentialWorkflowEngine` — step order; `{{stepId.output}}` / `{{previous.output}}` /
  `{{task.*}}`; unknown placeholder fails the step; string vs object form equivalence;
  `StopOnError` aborts, `ContinueOnError` continues, `Retry` retries exactly `retryCount`
  times with the specified backoff (fake `TimeProvider`); workflow-beats-prompt; whitelist
  rejection; skill implicit input.
- Report writer — file naming, content header, index line appended (temp directory).
- Runner arg parsing — `--task` required for `run`; unknown alias for `test-tool` exits 1
  (parser-level tests only; do not run the full DI container in unit tests).

**Out of scope**: anything WPF.

## 5. Working agreements

- Keep engine logic free of console I/O (runner prints; engine logs via `ILogger`).
- The e2e run against real Ollama is a **manual verification** prepared now and executed in
  Stage 6: add to the ledger the exact commands to copy the sample task and run it.
- If Core's step model needs a small extension for the engine, extend rather than duplicate —
  and record the change in the ledger (spec §3.4).

## 6. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green.
3. `dotnet run --project src/OD.MyLocalAgent.Runner -- list` — runs and prints the (possibly
   empty) task table against a real host.
4. `dotnet run --project src/OD.MyLocalAgent.Runner -- test-tool --alias filesystem.list --args '{ "path": "." }'` — works against a temp `FileSystem:RootPath`; safety-blocked probe
   (`filesystem.delete`) fails with the expected `SafetyException` message.

## 7. Before you finish

1. Append the **Stage 3** entry to the ledger: engine API (`IWorkflowEngine`, result types),
   runner command surface + exit codes, the manual e2e procedure for Stage 6, deviations.
2. Stage report: files created/changed, gate summary, open questions for Stage 4.
