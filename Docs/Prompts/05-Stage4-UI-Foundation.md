# STAGE 4 — UI foundation: shell, dark theme, DI, incidents, Dashboard, Settings, Logs

> Run only after Stage 3's gate is green.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read; **§7.6 (shell, style, pages 1/5/9),
   §8.5, R5–R7, §9-C** are your scope.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; reuse Core APIs as recorded (`AddODServices`,
   `IIncidentReporter`, `IRunnerService`, `ITaskSchedulerService.ResyncAllAsync`,
   `ISecretStore`).
3. The current file tree.

## 2. Mission

Stand up the WPF application itself: Generic Host + DI, navigation shell, the global dark
theme foundation, the incident pipeline wiring with non-blocking notifications, and the first
three pages (Dashboard, Logs, Settings). The remaining pages come in Stage 5; the fine-grained
dark audit comes in Stage 6.

## 3. Critical constraints (restated)

- Strict MVVM with **CommunityToolkit.Mvvm source generators only** (spec §7.6): partial
  ViewModels, `[ObservableProperty]`, `[RelayCommand]`, `IMessenger`. Forbidden: handwritten
  INPC/`RelayCommand`, other MVVM frameworks. Code-behind limited to `InitializeComponent`,
  focus management, the Logs page auto-scroll.
- Dark-only (R6): dark `BundledTheme` merged once in `App.xaml`; **one** palette resource
  dictionary (`Themes/DarkPalette.xaml`) defining background/surface/card/primary/secondary/
  error/on-* colors + dividers; every custom style derives from it.
- Configuration rooted at `AppContext.BaseDirectory` (spec §8.5). Provider switching must work
  **without restart** (factory + `IOptionsMonitor`, per-execution agent build).
- The OpenAI API key writes go to `ISecretStore` (DPAPI) **only** — never to `settings.json`,
  never logged, never displayed back.
- Warning-clean, complete files, XML docs on public surface, English only.

## 4. Scope (in scope only)

1. **App composition** — Generic Host in `App.xaml.cs` (build host in `OnStartup`, async init,
   graceful stop in `OnExit`); `AddODServices` from Core; register shell + pages + ViewModels;
   configuration layering per spec §8.5; secrets injected at bootstrap.
2. **Incident wiring** — `AppExceptionHandlers` initialized with `IIncidentReporter`; handlers
   for `DispatcherUnhandledException` (mark handled, keep app alive),
   `TaskScheduler.UnobservedTaskException`, `AppDomain.CurrentDomain.UnhandledException`;
   reporter notification event → messenger message → shell snackbar showing the correlation id
   (non-blocking).
3. **Shell** — `MainWindow` with left navigation rail (MaterialDesign icons via IconPacks):
   Dashboard · Tasks · Logs · Tools · Tool tester · AI playground · Settings. Items whose pages
   land in Stage 5 show a placeholder-free "Under construction" disabled state? — **No**:
   create those pages as real, minimal-but-complete UserControls now (title + short
   description + disabled content area) so navigation is fully wired; Stage 5 replaces their
   content. Each page = UserControl + ViewModel resolved through DI.
4. **Theme foundation** — `Themes/DarkPalette.xaml` + dark `BundledTheme`; base implicit
   styles for the controls used by this stage's pages (Window, Navigation rail/ListBox,
   Button, Card, TextBlock, TextBox, ComboBox, ProgressBar, snackbar host, ToolTip). A running
   audit note (`docs/theme-audit.md`) lists controls touched vs. pending for Stage 6.
5. **Dashboard page** — counts (tasks/enabled), next 5 runs (store + Cronos via
   `GetNextRun`), last 10 executions from `reports\index.jsonl` with status, provider health
   badge (`CheckHealthAsync` on the current provider, async, refreshing), quick actions:
   New task (navigates), Resync schedules (`ResyncAllAsync`), Open logs folder.
6. **Logs page** — live auto-scrolling `ListView` fed by the in-memory Serilog sink
   (dispatcher-marshalled), filters (minimum level, source contains, task id contains),
   read-only.
7. **Settings page** — exactly spec §7.6 page 9, all three sections:
   - *AI provider*: radios Ollama (local, default) / OpenAI-compatible (cloud, opt-in) with a
     visible cloud warning on the latter; per-provider endpoint/model/timeout; API-key box
     (writes DPAPI only + "configured: yes/no" indicator, empty on load); **Test connection**
     → `CheckHealthAsync` + latency display; Save → `settings.json` (reload applies without
     restart — verify by switching and re-testing).
   - *General*: `FileSystem:RootPath`, HTTP timeout/user-agent + `AllowedHosts` (comma list),
     scheduler options (`RunWhenLoggedOff` with S4U/ OAuth warning label, `RunnerPath` with
     auto-resolved default shown), the three `Safety` toggles each with an explanatory label,
     Serilog minimum level; validated save + dirty-state indicator.
   - *Diagnostics*: log buffer Save to disk… (user-chosen `.log`), Reset (confirmation →
     clears buffer + archives/truncates rolling files); incident list from
     `IIncidentLogService` (expandable stack trace, Copy details, Export…, Clear all with
     confirmation); **"Trigger test incident"** button throwing through the normal command
     path (used by the Stage 6 drill).
8. **Run Now plumbing** — `IRunnerService` exposed to later pages; start `resync-schedules`
   logic once at startup (background, non-blocking).

**Out of scope**: Tasks grid/editor, Prompt editor, Tools configuration, Tool tester,
AI playground (Stage 5); full dark audit (Stage 6).

## 5. Working agreements

- XAML must compile — verify every `x:Class`, resource key, and binding path by building.
- Keep styles in dictionaries; no per-control inline colors outside the palette.
- Messenger message types live in a `Messages` folder; one record per message.
- UI unit tests are not required; view-model logic worth testing (settings validation) goes
  into the test project only if it stays UI-free.

## 6. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — still all green.
3. Manual launch (`dotnet run --project src/OD.MyLocalAgent.UI` or built exe): app starts in
   dark theme; all rail entries reachable; Settings save → provider switch takes effect on the
   next Test connection without restart; "Trigger test incident" → snackbar with correlation
   id + new line in `incidents.jsonl`. Record what you observed in the ledger; if you cannot
   launch GUI apps in your environment, state it, perform the closest possible check
   (build + XAML compile success), and flag it for human verification.

## 7. Before you finish

1. Append the **Stage 4** entry to the ledger: DI graph summary, pages/ViewModels list,
   theme dictionary structure, incident flow, deviations + GUI-verification status.
2. Stage report: files created/changed, gate summary, open questions for Stage 5.
