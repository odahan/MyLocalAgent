# Stage 6 acceptance report

Date: 2026-07-29
Target: Windows x64, self-contained Release publication
Stable root: `%LOCALAPPDATA%\Programs\OD.MyLocalAgent`

Status convention: ✅ executed and passed; ⏳ requires the stated human/environment action;
❌ executed and failed.

## Specification §11 checklist

| Checklist item | Status | Executed evidence |
|---|---:|---|
| `dotnet build OD.MyLocalAgent.sln` warning-clean; `dotnet test` all green (§7.7). | ✅ | Final Release build used `-m:1 --no-restore` and completed with 0 warnings/0 errors. Final Release test used `-m:1 --no-build --no-restore`: 66 passed, 0 failed, 1 environment-dependent Ollama integration test skipped. |
| Sample task `docs/samples/daily-mail-summary.json` copied to the tasks folder runs end-to-end: `OD.MyLocalAgent.Runner.exe run --task daily-mail-summary` → report written, index line appended, next occurrence re-registered. | ⏳ | The sample is copied, but Google credentials and interactive Gmail consent are unavailable on this account. The documented OAuth-free fallback was executed instead: `acceptance-filesystem` exited 0, increased Markdown reports 2→3 and index lines 2→3, wrote `%LOCALAPPDATA%\OD.MyLocalAgent\reports\acceptance-filesystem\20260729-054933.md`, and registered `2026-07-30 06:15 +02:00`. Human step: configure the documented desktop OAuth client, connect Gmail in Tools, then run the exact `daily-mail-summary` command. |
| UI starts in dark theme; all 9 pages navigable; stock controls render dark (Stage 6 audit checklist) — no light-default artifacts. | ✅ | The published UI started in Dark. Dashboard, Tasks, task editor, prompt editor, Logs, Tools, Tool tester, AI playground, and Settings were inspected. Populated DataGrid, check/radio controls, text/password editors, grouped and ordinary ComboBox popups, scrolling, Logs, and incident lists rendered from semantic palette roles. Light was also selected live, inspected, retained as a supported option, and changed back to saved Dark. See `Docs/theme-audit.md`. |
| Zero placeholders; every public API documented in English. | ✅ | Source scan found no `TODO`, `TBD`, `NotImplementedException`, or implementation stub. The occurrences of “placeholder” are the implemented workflow-substitution feature and its tests. `YOUR_CLIENT_ID`/`YOUR_CLIENT_SECRET` are intentional invalid credential sentinels documented in the setup guide. Warning-as-error XML-documentation build is clean. |
| All ViewModels use MVVM Toolkit source generators; no manual INPC or command classes. | ✅ | Every ViewModel is a partial `ObservableObject` and uses `[ObservableProperty]`/`[RelayCommand]`; no manual INPC implementation exists. The last custom title-bar `ICommand` class was replaced by MVVM Toolkit-generated commands that invoke WPF `SystemCommands`. |
| Settings persists the provider choice, applies without restart; Test connection reports real health + latency; API key never touches settings.json (verify the file's contents). | ✅ | OpenAI-compatible was saved and, without restart, Test connection returned the real missing protected-key failure (incident `ad8c83500dfe45eaa866d94e5a1ed3c8`). Ollama was then saved and returned healthy at 2124 ms. The resulting settings file selects `Ollama`, contains no `ApiKey`, and keeps Dark. An optional unconfigured Dropbox connector no longer breaks unrelated Settings reloads. |
| Settings → Diagnostics: Save to disk writes a complete `.log`; Reset clears view and archives/truncates rolling files. | ⏳ | Controls and code paths are present and the Diagnostics incident list was exercised. Human step: select **Save to disk**, compare the exported line count/content with the current Logs view, select **Reset**, accept its destructive confirmation, verify the view is empty, verify a new `%LOCALAPPDATA%\OD.MyLocalAgent\logs\archive\<timestamp>\` folder, and verify active rolling files were truncated. This was not automated because it would archive/truncate the user's current logs. |
| Exception drill via the "Trigger test incident" button **and** a faulted background task: both are caught, written to `incidents.jsonl` with correlation ids, shown as non-blocking notifications — the app keeps running. | ✅ | Settings **Trigger test incident** produced `Application.DispatcherUnhandledException` incident `4bc5be1fe6f643d78a5986c084ba0108`; its snackbar was displayed and the app remained responsive. A fresh task containing `unknown.alias` exited 1, appended one JSONL record, and produced Runner incident `61f24aaef4a3408ba3e2911c11ec4d50`. |
| Safety defaults verified: with flags off, `filesystem.delete`, `gmail.send` and `http.post` fail with `SafetyException` (runner `test-tool` is the quickest probe). | ✅ | Three direct `test-tool` commands exited 1 and named `Safety:AllowFileDelete`, `Safety:AllowSendEmail`, and `Safety:AllowHttpPost`. The protected acceptance file still existed afterward; no message or POST was sent. Workflow-level repetitions produced incidents `eaafa8278cfe4ac8ae90c9c2129744ca`, `034b49944c5b469990229534bbada675`, and `a9513accabf4449b8050e1254a5c87e2`. |
| `resync-schedules` repairs a stale registration (delete the task from Task Scheduler, run the command, verify it is recreated with the correct next occurrence). | ✅ | `\OD.MyLocalAgent\OD.MyLocalAgent.acceptance-filesystem` was deleted successfully; the next query failed as expected. `resync-schedules` exited 0 and recreated it. XML verification showed command `%LOCALAPPDATA%\Programs\OD.MyLocalAgent\Runner\OD.MyLocalAgent.Runner.exe`, arguments `run --task acceptance-filesystem`, Runner working directory, and `StartWhenAvailable=true`. `list` reported the next run as `2026-07-30 06:15 +02:00`. |

## Packaging and dependency evidence

```powershell
.\scripts\publish.ps1
```

The script completed a `win-x64` self-contained publish, validated the UI and Runner hosts
and runtime payloads, and promoted this stable layout:

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\
├── UI\OD.MyLocalAgent.UI.exe
└── Runner\OD.MyLocalAgent.Runner.exe
```

The exact recommended `Settings -> Scheduler:RunnerPath` value is empty; resolver verification
and the Task Scheduler XML prove that the standard split layout resolves to the Runner
directory rather than a development output.

The central-package sanity check found 7 project files, 40 package references, 30 central
version entries, 0 missing central entries, and 0 inline versions. The online outdated check
reported only `FluentAssertions 7.2.2 -> 8.10.0` as a direct update; the project intentionally
keeps 7.2.2 and changed no package version in Stage 6.

## Executed command forms

```powershell
dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1
dotnet test OD.MyLocalAgent.sln --no-build --no-restore --configuration Release -m:1

$runner = Join-Path $env:LOCALAPPDATA `
  'Programs\OD.MyLocalAgent\Runner\OD.MyLocalAgent.Runner.exe'

& $runner run --task acceptance-filesystem
& $runner test-tool --alias filesystem.delete --args '{"path":"acceptance/stage6.txt"}'
& $runner test-tool --alias gmail.send --args '{"to":"nobody@example.invalid","subject":"Stage 6","body":"blocked"}'
& $runner test-tool --alias http.post --args '{"url":"https://example.invalid","body":"{}","contentType":"application/json"}'
& $runner resync-schedules
& $runner list
```

Temporary invalid and safety-drill task files were removed immediately after their runs.
The two documented sample tasks remain in the per-user task store for the Gmail human check
and deterministic reproduction.
