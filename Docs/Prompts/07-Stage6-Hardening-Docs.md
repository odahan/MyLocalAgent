# STAGE 6 — Hardening, packaging, documentation & final acceptance

> Run only after Stage 5's gate is green.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read; **§4 (rules), §7.6 style, §8.3,
   §8.4, §9, §11** are your scope.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; it now feeds the README's documentation.
3. `docs/theme-audit.md` and the current file tree.

## 2. Mission

Close the product: complete the dark-theme audit so every stock control renders dark, run the
safety and exception drills, add the packaging story (stable publish location the scheduler
points to), write the full README, and **execute the final acceptance checklist** of spec §11.

## 3. Critical constraints (restated)

- No behavior changes this stage beyond fixes surfaced by the checklist; if a fix touches an
  earlier stage's API, record it in the ledger (spec §3.4).
- Everything you claim in the checklist must have been executed — commands, not intentions.
- Warning-clean, complete files, XML docs, English only.

## 4. Scope

### 4.1 Dark-theme completion (spec R6 + §7.6 style)

1. Walk `docs/theme-audit.md` and close it out: ensure explicit dark implicit styles exist for
   every stock control actually used by the app — at minimum where present: `DataGrid`
   (headers, rows, selection formula, alternating rows), `TextBox`, `ComboBox` + popup,
   `PasswordBox`, `ListBox`/`ListView`, `TreeView`, scrollbars, `Menu`, `ContextMenu`,
   `ToolBar`, `TabControl`, `GroupBox`, `Expander`, `CheckBox`/`RadioButton`, `Slider`,
   `DatePicker`, `ToolTip`, status bar areas, dialogs.
2. All styles derive from `Themes/DarkPalette.xaml` resources — no hard-coded colors.
3. Update `docs/theme-audit.md` to its final ✅ state, noting any control intentionally left
   to MaterialDesignThemes defaults (with a one-line justification each).

### 4.2 Packaging & scheduler stability (spec §7.3, §9-E)

1. `scripts/publish.ps1`: publishes UI + Runner (`dotnet publish -c Release`) into a stable
   folder (default `%LOCALAPPDATA%\Programs\OD.MyLocalAgent`), copies `appsettings.json`,
   prints the resolved runner path and the exact `Settings → Scheduler:RunnerPath` value (or
   empty for auto-resolution when side-by-side).
2. Verify (and document) that after publishing, `RegisterAsync` points Task Scheduler actions
   at the published runner, not at `bin\Debug`.

### 4.3 Full `README.md` (replace the skeleton — English)

Prerequisites (Windows 10/11, .NET 10 SDK, Ollama ≥ a version supporting the model, `ollama
pull gemma4:12b` + how to check the tag with `ollama list`) · quick start (publish via the
script, first launch) · Google Cloud OAuth setup (desktop client, scopes, where the id/secret
go in `settings.json`) · Dropbox app setup · **safety defaults** (spec R9: what is blocked
until opted in, and why — prompt-injection paragraph included) · configuration layering
(§8.5) and where every file lives under `%LOCALAPPDATA%\OD.MyLocalAgent` · Task Scheduler
registration: rolling next-occurrence triggers, `StartWhenAvailable`, `resync-schedules`,
`RunWhenLoggedOff`/S4U caveats (consent first, same account) · runner CLI reference (all
commands + exit codes) · workflow syntax (both forms, full Tool Map table, two complete
examples: mail summary → report file; unread mails → calendar event → Dropbox backup of the
report) · architecture diagram (ASCII or Mermaid: projects + runtime flows UI/runner/scheduler/
Ollama/cloud) · provider switching (no restart) · troubleshooting (Ollama down, model tag
missing, OAuth consent loop in headless context, safety blocks, where logs/incidents live) ·
extension points (add a tool to the Tool Map, add a skill).

### 4.4 Final acceptance (spec §11 — execute, then fill the table honestly)

1. `dotnet build OD.MyLocalAgent.sln` (warning-clean) and `dotnet test OD.MyLocalAgent.sln`.
2. **End-to-end sample**: copy `docs/samples/daily-mail-summary.json` into
   `%LOCALAPPDATA%\OD.MyLocalAgent\tasks\`, run
   `<runner> run --task daily-mail-summary` with Ollama up → report file exists,
   `index.jsonl` appended, task re-registered with the next occurrence. (Requires Gmail OAuth
   consent beforehand for the gmail step; if credentials are unavailable, run the filesystem/
   http-only variant documented in the README and flag the gmail leg for human verification.)
3. **Exception drill**: UI "Trigger test incident" button **and** a faulted background task
   (kill Ollama mid-run or run a task with an unknown alias) → both incidents land in
   `incidents.jsonl` with correlation ids, snackbar shown, app keeps running.
4. **Safety drill**: with default flags off, `test-tool --alias filesystem.delete`,
   `gmail.send`, `http.post` each fail with `SafetyException` naming the flag.
5. **Provider switch**: Ollama → OpenAI-compatible (invalid key), save, **without restarting**,
   Test connection reports a real failure; then back to Ollama, healthy. Verify
   `settings.json` contains no API key.
6. **Scheduler repair**: unregister the sample from Task Scheduler, run `resync-schedules`,
   verify recreation with correct next occurrence.
7. **UI audit**: all 9 pages navigable, dark everywhere per `docs/theme-audit.md`; Diagnostics
   Save to disk + Reset behave per spec.
8. Produce `docs/acceptance-report.md`: the §11 checklist with ✅/❌ + evidence (command
   snippets, file paths, observations) — mark ❌ honestly, never paper over.

### 4.5 Dependency finalization

- Re-run `dotnet list package --outdated`-style sanity: every reference still matches
  `Directory.Packages.props`; nothing stray; ledger versions up to date.

**Out of scope**: new features.

## 5. Working agreements

- Small fix branches of work only; after each fix, re-run build + tests.
- Where a GUI verification is impossible in your environment, mark the checklist item
  ⏳ "human verification required" in `acceptance-report.md` with precise manual steps —
  that is an acceptable terminal state, a ❌ with no evidence is not.

## 6. Stage gate (all must pass — show command output)

1. Build + tests green (final run, fresh output).
2. `docs/acceptance-report.md` complete — every §11 item has evidence or an explicit
   human-verification note.
3. `README.md` complete per §4.3; `docs/theme-audit.md` closed; `scripts/publish.ps1` runs
   (or its failure clearly diagnosed).

## 7. Before you finish

1. Append the **Stage 6** entry to the ledger: audit closure, publish layout, drill results,
   known limitations carried forward.
2. Final stage report: full file tree of the finished product, gate summary, the
   acceptance table verbatim, and a "human follow-ups" list.
