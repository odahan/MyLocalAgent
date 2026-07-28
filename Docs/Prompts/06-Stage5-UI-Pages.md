# STAGE 5 — UI feature pages: Tasks, editor, prompt editor, tools config, tool tester, playground

> Run only after Stage 4's gate is green.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read; **§7.6 pages 2/3/4/6/7/8, §8.1,
   §8.3, §9-C, R9** are your scope.
2. `docs/ARCHITECTURE-LEDGER.md` — full read (shell patterns, messenger conventions, runner
   path resolution from Stage 4).
3. The current file tree.

## 2. Mission

Replace the Stage-4 minimal pages with the six fully functional feature pages of spec §7.6.
At the end of this stage the application is functionally complete; Stage 6 is hardening,
docs and final acceptance.

## 3. Critical constraints (restated)

- Same MVVM rules as Stage 4 (source generators only; code-behind minimal).
- Task execution inside the UI is forbidden: **Run now = child runner process** via
  `IRunnerService` (spec §9-C).
- Tool tester must surface safety blocks helpfully (which `Safety` flag to enable) — spec R9.
- All new controls rendered with the dark palette; extend `docs/theme-audit.md` with every
  control type you introduce.
- Warning-clean, complete files, XML docs, English only.

## 4. Scope

1. **Tasks page** — `DataGrid`: name, cron, next run (`GetNextRun`), last run + status
   (latest `reports\index.jsonl` line per task), enabled toggle (updates store + scheduler);
   actions: New / Edit / Delete (confirmation; also unregisters) / **Run now**
   (spawns `run --task <id>`, streams stdout into a docked output area, cancellable; refreshes
   dashboard-relevant state via messenger on completion) / Register–Unregister
   (`IsRegistered`-driven state).
2. **Task editor page** — fields per spec §8.1: id (`[a-z0-9-]+`, validated, immutable after
   creation), name, cron with **live preview** (validity indicator + next 3 occurrences via
   Cronos + plain-language label for common patterns: minutely, hourly, daily, weekdays,
   weekly, monthly — implement the mapping directly, no new package), enabled +
   `errorPolicy` selector (`StopOnError`/`ContinueOnError`/`Retry` + retry count/backoff),
   tool whitelist checkboxes (the five groups), workflow editor (multi-line JSON/text area in
   §8.3 syntax with **validate-on-save** using Core's task validation; inline error display on
   failure, e.g. unknown alias `gmail.readTody` → did-you-mean hint if trivially computable,
   else the plain error). Save → store + scheduler sync + messenger notification.
3. **Prompt editor page** — bound to the selected task's `prompt`; **Test the prompt** button
   runs the agent streaming variant with the task's whitelist; live streamed output,
   chunk counter (+ provider token usage when the response reports it), Cancel button.
4. **Tools configuration page** — Google block: current OAuth status (credential store folder
   presence + scopes), **Connect** button launching the interactive
   `GoogleWebAuthorizationBroker` consent (UI thread, documented in ledger), Disconnect
   (clears the FileDataStore folder after confirmation). Dropbox block: token entry written to
   `ISecretStore` (DPAPI) with masked display + configured indicator. Per-tool **Test
   connection** row (Gmail / Calendar / Dropbox) with latency + typed-error display; missing
   configuration explains what to set (Google client id/secret in `settings.json`, etc.).
5. **Tool tester page** — alias picker bound from Core's `ToolMap` (grouped by tool group),
   dynamic hint of the alias's arg names, JSON args editor, Run (busy indicator) →
   pretty-printed result + duration; `SafetyException` → message naming the flag to enable in
   Settings → General.
6. **AI playground page** — free prompt → `GenerateStreamingAsync` of the **current** provider
   (not the agent — raw completion), streamed rendering, read-only badge (provider + model
   from monitored options), health indicator (`CheckHealthAsync` on load and on provider
   change), Cancel.
7. Extend `docs/theme-audit.md`: check off controls now present with their dark styles;
   list any control rendered by default styles (candidates for Stage 6 audit, e.g. `DataGrid`
   chrome, scrollbars, combo-popups).

**Out of scope**: dark-theme fine audit + README + publish script (Stage 6).

## 5. Working agreements

- Reuse Stage-4 shell mechanics (busy overlay, snackbars, messenger) — no parallel patterns.
- All grid/editor interactions route through ViewModels; no service calls from code-behind.
- Keep XAML in the pages resource-lean: shared styles belong in dictionaries, not duplicated.

## 6. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green (add UI-free ViewModel tests only if natural,
   e.g. cron preview mapping as a pure function in Core with tests).
3. Manual launch: all 9 pages functional; create a task in the editor (cron preview updates
   live), Run now executes via the child runner and appends to `index.jsonl`. Record
   observations in the ledger; if GUI launch is impossible in your environment, say so and
   flag for human verification (as in Stage 4).

## 7. Before you finish

1. Append the **Stage 5** entry to the ledger: pages wired, messenger messages added, runner
   spawn contract, theme-audit delta, deviations + GUI-verification status.
2. Stage report: files created/changed, gate summary, what Stage 6 must still verify.
