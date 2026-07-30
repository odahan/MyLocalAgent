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

## Stage 7 V2 acceptance addendum

Date: 2026-07-29

| Checklist item | Status | Executed evidence |
|---|---:|---|
| Guardrails project builds; solution contains eight projects and no dependency cycle. | ✅ | Release solution build reports all eight projects, 0 warnings, and 0 errors. Guardrails references only Ollama; Core, UI, and Tests reference Guardrails directly. |
| Public contracts and statuses match the Stage-7 V2 specification. | ✅ | Warning-clean compilation plus deterministic contract/policy tests cover the public records, enums, service, policy, envelopes, and typed exceptions. |
| Screening requests are single-turn and provably tool-less. | ✅ | Captured stub requests contain exactly one system and one user message, temperature 0, and null/empty tools. Summarization call options are checked the same way. |
| Clean external content is always enclosed in the plaintext untrusted-data wrapper. | ✅ | `CleanVerdictUsesToollessPlaintextEnvelope` and protected HTTP/Gmail tests verify the mandatory warning and nonce delimiters. |
| Caution, failed, summary-truncated, and normal-size disabled paths deliver marked Base64; oversized disabled paths use source fallbacks. | ✅ | Policy, exact Base64 round-trip, typed-failure, Gmail, HTTP, and summary-cap tests are green. |
| Low-score content is withheld. | ✅ | Boundary tests cover scores 49 and 0 against the exact `Withhold` disposition. |
| Oversized reduction covers every source character, uses no tools/skills, aggregates the minimum source/final score, and returns only the protected summary. | ✅ | Ordered chunk request capture proves `abcde` then `fgh`, intermediate reclassification, six sequential calls, and minimum score 40. Core returns only final canonical summaries. |
| Summary truncation includes its marker within `MaxSummaryChars` and forces Base64. | ✅ | The deterministic 300→100 character cap includes the removed-character marker; Core/UI force at least Base64 for `SummaryTruncated`. |
| Content above `MaxSummarizationSourceChars` produces zero LLM calls and the correct source-specific fallback. | ✅ | Service factory receives zero calls; Gmail preserves headers/body marker and HTTP returns `SkippedTooLarge` without source text. |
| Provider/parsing/timeout failure is incident-reported once with the original inner exception, then safely continues with fallback content. | ✅ | Malformed verdict and Gmail typed-failure tests verify a content-free outer exception, original inner exception, one report, and complete marked-Base64 fallback. |
| Summarization failure is incident-reported once; Gmail preserves its canonical header block and HTTP continues with its skip marker. | ✅ | HTTP technical-reduction test records one incident and returns `SkippedSummarizationFailed`; the same shared Gmail fallback path preserves exact headers and replaces only Body. |
| Caller cancellation is not incident-reported. | ✅ | A pre-canceled screening token propagates `OperationCanceledException`; Core/UI cancellation filters rethrow before incident handling. |
| Gmail protects one canonical From/To/Subject/Snippet/Body block and never duplicates raw fields. | ✅ | JSON assertions reject raw component properties and decode one exact canonical Base64 payload. |
| Gmail reduction preserves exact From/To/Subject/Snippet values and substitutes only the body summary. | ✅ | Core builds the delivered block from unchanged connector values and the raw reduction result; deterministic fallback tests exercise the same header-preserving path. |
| Gmail unavailable-reduction fallback sets the review marker, human-review flag, and accurate trust scope. | ✅ | `GmailSourceTooLargePreservesHeadersAndUsesBodyReviewMarker` verifies the marker, omission of the large body, `requiresHumanReview=true`, and `HeadersAndSnippetOnly`. |
| Gmail default limit is unlimited; a positive limit returns explicit counts/warning without skipped content. | ✅ | Default integration code processes the entire connector list sequentially; the positive-limit test verifies 2 fetched, 1 processed, 1 skipped, warning text, and no skipped body. |
| HTTP GET and GET Text protect all body/text; unreducible pages return an explicit skip notice and do not fail the workflow. | ✅ | GET/Text, source-limit, and summarization-failure tests verify protected values, safe metadata, exact markers, and successful tool JSON. POST tests remain unchanged. |
| Agent-facing tool output contains indicator count but no verbatim indicator. | ✅ | Gmail caution JSON contains count 1, no raw indicator property/text, and only an encoded canonical payload. HTTP uses the same metadata projection. |
| Trust filter shows human-only indicators and distinguishes trust status from content processing status. | ✅ | ViewModel/XAML bind separate fields, indicator list, score band, provider latency, lengths, and protected preview. Interactive visual evidence remains below. |
| Settings contain no insecure Disable Guardrails action. | ✅ | The only control is **Use LLM guardrails processing** and its adjacent text states that protection remains active with conservative fallbacks. |
| README and both manuals explain privacy, Base64 limitations, summarization, source limits, fallbacks, scope exclusions, and the R9 boundary. | ✅ | README Guardrails section and both offline manual Trust filter sections cover all listed topics; shared specification includes ADR H and the V2 addendum. |

### Stage 7 environment-dependent follow-ups

- ⏳ Exercise the Trust filter benign, injection, Return Base64, Cancel, disabled, oversized,
  truncated, source-too-large, and technical-failure states in both Dark and Light.
- ⏳ Confirm the OpenAI-compatible cloud warning visually. Do not use real private mail unless
  the user explicitly authorizes that cloud test.
- ⏳ Repeat the four real Runner HTTP probes in an environment that permits a loopback/public
  HTTP listener and writable `%LOCALAPPDATA%`. This sandbox rejected the loopback connection
  and denied the per-user incident file. Deterministic tool/provider tests cover all four
  output paths without network or private data.

## Stage 8 acceptance addendum

Date: 2026-07-30

| Checklist item | Status | Executed evidence |
|---|---:|---|
| Contracts §5.1 exact; connector request-shape test proves auth + body fields. | ✅ | Public options/results/facade contracts compile warning-clean. `OllamaProviderMapsDocumentedRequestAndResponse` captures POST URL, Bearer authentication, `query`, `max_results`, and normalized response values. |
| Guardrails screening provably in the search path: the 3 dispositions verified (stubbed) and `screened:true` visible in a real probe; disabled/failed screening is marked Base64; no raw fallback exists. | ⏳ | Deterministic tests pass for PassThrough, EncodeBase64, Withhold, disabled, failed, exact one-call payload, and cancellation. One failure produces one incident. A real probe requires a user-supplied Ollama Web Search key and remains human-verifiable. |
| Budget regression proves every delivered title/snippet character was in the one screened canonical payload; overflow is explicitly truncated/omitted and counted. | ✅ | `SearchBudgetsCanonicalPayloadBeforeScreening` verifies the exact screened/delivered string, maximum length, truncation marker, retained count, and omission count. The source URL is absent from canonical content. |
| API key in DPAPI only (checked by reading `settings.json`, not by trust). | ✅ | `SaveOperationsMergeSectionsAndKeepWebSearchKeySecret` reads the resulting JSON, finds no `ApiKey`, verifies the `WebSearch:ApiKey` secret-store call, and proves Web Search/general settings preserve each other and unrelated sections. |
| Fresh-install default: tool disabled with an actionable error; enabling + key + test in the UI under 5 minutes. | ⏳ | `Provider` defaults to empty and `FacadeRejectsDisabledWebSearch` verifies the actionable Tools-page message. The complete UI flow is implemented; timing and live-key success remain a human check. |
| README "Web search (Ollama)" section + sample task committed; Tool Map table updated. | ⏳ | README documents setup, cloud/privacy, limits, DPAPI, budget behavior, provider extensibility, and `web.search`. `Docs/samples/web-daily-brief.json` is the third workflow example. Both are complete in the working tree; no Git commit was requested or created. |

### Stage 8 environment-dependent follow-ups

- ⏳ Supply an Ollama Web Search API key in **Tools**, enable the provider, and confirm the
  fixed connection test reports latency and result count without displaying result content.
- ⏳ Run `test-tool --alias web.search --args '{"query":"ollama web search"}'` and verify a
  real `screened:true` compact result with `protectedResults`, Guardrails, and budget fields.
- ⏳ Copy `Docs/samples/web-daily-brief.json` into the task store, run it through the Runner,
  and verify the workflow report and guarded search output.
- ⏳ Inspect the Tools card and sixth task whitelist checkbox in Dark and Light.

## Stage 11 per-task file-system authorization

The task contract now carries read-only or read-write absolute authorized locations while
relative paths remain beneath `FileSystem:RootPath`. Deterministic tests cover canonical task
serialization, invalid rules, relative and absolute containment, nested-rule precedence,
read-only enforcement, move and delete authorization, Dropbox local paths, sibling-prefix and
reparse-point rejection, immutable workflow/agent context propagation, taskless registry
behavior, concurrent task isolation, and task-editor add/remove/save behavior.

Deployment-environment follow-up: exercise one secondary local volume, one unavailable or
removable volume, one NTFS-denied directory, and one UNC path in both InteractiveToken and S4U
modes. Application authorization never replaces Windows permissions or supplies S4U network
credentials.

## Stage 13 AI Workflow Helper

Deterministic tests exercise lossless workflow descriptions, strict pure-array parsing,
shared argument/placeholder validation, dynamic Tool Map and placeholder prompt context,
tool-less current-provider streaming, exactly three attempts, correction content, retained
errors, cancellation without repair or incident, tool/path/Safety analysis, proposal
refusal/acceptance, atomic Apply/Undo, invalid Explain short-circuiting, one-response
Text/Markdown projection, raw-HTML disabling, and restrictive local HTML.

The Release stage gate passed with 191 tests, zero failures, and the one explicit local Ollama
probe skipped. All sample JSON parsed and `git diff --check` passed. The WPF application was
not launched during this implementation pass, so Author/Explain streaming with a live model,
refusal, cancellation, Undo, rendered Markdown, and missing-WebView2 fallback remain honest
manual GUI follow-ups. Valid JSON does not guarantee a semantically correct workflow, and
literal authorization analysis cannot predict dynamic runtime paths or values.
