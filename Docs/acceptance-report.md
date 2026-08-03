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
| Settings → Diagnostics: Save to disk writes a complete `.log`; Reset clears view and archives/truncates rolling files. | ✅ | Controls and code paths are present and the Diagnostics incident list was exercised. A manual operator session then selected **Save to disk**, compared the exported content with the Logs view, selected **Reset**, accepted its destructive confirmation, and confirmed the emptied view, the new `%LOCALAPPDATA%\OD.MyLocalAgent\logs\archive\<timestamp>\` folder, and the truncated active rolling files. This remains deliberately non-automated because it archives/truncates the user's current logs. |
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

- ✅ The Trust filter benign, injection, Return Base64, Cancel, disabled, oversized,
  truncated, source-too-large, and technical-failure states were inspected in both Dark and
  Light during a manual operator session.
- ✅ The OpenAI-compatible cloud warning was confirmed visually. No real private mail was used.
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
- ✅ The Tools card and the sixth task whitelist checkbox were inspected in Dark and Light.

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

## Stage 14 hierarchical AI generation settings

Date: 2026-07-30

| Checklist item | Status | Executed evidence |
|---|---:|---|
| Safe global defaults and strict ranges are configured and reloadable. | ✅ | `GenerationPolicyTests` covers defaults, finite/range rejection, and a new execution observing a changed `IOptionsMonitor` snapshot while an in-flight execution retains its resolved policy. |
| Resolution is field-wise step → task → Settings with independent provenance. | ✅ | Deterministic resolver cases cover no override, partial task override, partial step override, and mixed task/step inheritance for both properties. |
| Existing task JSON remains valid; strict override JSON round-trips canonically. | ✅ | Tests cover legacy tasks, partial task and every AI-alias step round-trip, empty-object omission, unknown/null/non-numeric/out-of-range fields, forbidden context/provider fields, and every non-AI alias rejection. |
| Runner, agent, skills, workflow retries, Prompt editor, helper, Playground, and concurrent tasks retain the intended immutable policy. | ✅ | Service, workflow-engine, task-editor, helper, and policy-wrapper tests cover explicit propagation, multi-round option preservation, retry stability after a settings change, and concurrent isolation. |
| Internal authoring, explanation, screening, and reduction policies remain fixed at Temperature 0 / Top P 1. | ✅ | Named-policy assertions plus captured helper and Guardrails requests verify all four constants and the current global context window. |
| Provider mapping is honest. | ✅ | Captured Ollama requests contain native `num_ctx`; captured OpenAI-compatible requests contain Temperature and Top P but no invented context-window property. Existing provider error paths remain unchanged. |
| Settings and Task editor validation/command behavior is deterministic and culture-safe. | ✅ | ViewModel tests cover comma/dot parsing in French and English cultures, independent partial overrides, effective provenance, dirty state, save/reload, and disabled save on invalid input. |
| Documentation and sample coverage are complete. | ✅ | README, shared specification §17, both standalone manuals, theme audit, architecture ledger, acceptance report, and the scheduled mail-summary sample document the delivered hierarchy and limits. |

### Stage 14 environment-dependent follow-ups

- ✅ Settings, Task editor, Prompt editor, and AI playground were inspected in Dark and Light,
  including invalid, inherited/disabled, long-value, keyboard-focus, and narrow-window states.
- ⏳ Run one real Ollama request and inspect its provider behavior with a model whose supported
  context capacity is known; run one configured OpenAI-compatible endpoint and confirm its own
  context-limit error behavior.
- ✅ The WPF application was not launched during the implementation pass itself, but a later
  manual operator session covered the GUI states listed above. Automated binding, ViewModel,
  serialization, runtime propagation, provider-option, build, and test evidence remains
  complete; live-provider results are still not claimed.

## Stage 15 agent loop hardening and run observability

Date: 2026-07-31

| Checklist item | Status | Executed evidence |
|---|---:|---|
| `ai.prompt` cannot enter an agent/tool loop. | ✅ | The workflow engine uses one direct tool-less model call. A deterministic service test verifies one request and an empty tool list. |
| Agent-capable calls cannot repeat forever. | ✅ | Requests stop after eight rounds, two consecutive errors, unknown calls, or before the third identical tool-and-arguments execution. A test verifies the typed loop exception and that the tool ran only twice. |
| Gmail and Calendar expose bounded/period reads. | ✅ | Tool Map and connector tests cover optional Gmail maximums, `gmail.readRecent(daysBack)`, and `calendar.read` period mapping. |
| Runner traces and context aid diagnosis without exposing configured credentials. | ✅ | Tests cover readable sanitized JSON arguments, returned Gmail count, per-step context callbacks, and Base64 context protocol rejection/round-trip. Run output is wired to auto-scroll and retains the last context until the next run. |
| AI Explain rendering follows the current theme and no longer blocks its own document navigation. | ✅ | Light/Dark CSS tests pass; internal `NavigateToString` is distinguished from blocked external navigation. A local Release build rendered the same completed response through installed WebView2 in both palettes, with readable selected tabs and a full-size response host from the first frame. WebView2 Text fallback remains unchanged. |
| Documentation and deployment are complete. | ✅ | README, shared specification §18, FR/EN manuals, First Run guides, theme audit, Ledger, and this report are updated. Both manuals validate structurally, all sample JSON parses, Release publish succeeds, and the published Runner help command exits successfully. |

### Stage 15 final evidence

- `dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release`: zero warnings,
  zero errors.
- `dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release`:
  256 passed, zero failed, one intentionally skipped local Ollama integration test.
- Self-contained `win-x64` UI and Runner published successfully to the standard per-user
  installation; published Runner `--help` returned exit code 0.
- Each standalone manual has 22 balanced sections, 30 unique ids, no duplicate id, and no
  missing internal anchor. Four sample JSON files parsed successfully. `git diff --check` is
  clean.

### Stage 15 human follow-up

- ✅ A local Release UI exercised AI Explain in both Light and Dark, including completed raw
  Markdown and Rendered Markdown with the installed WebView2 runtime. The selected tabs stayed
  readable and the response host occupied its full surface before streaming began.
- ✅ AI Helper Author/Apply/Undo, keyboard traversal, the minimum-size layout, and the
  missing-WebView2 fallback were exercised manually.
- ⏳ Run the real daily Gmail workflow manually with a small explicit `maxMessages`, inspect the
  retained workflow context, and confirm the report content. This implementation pass did not
  execute or create that user task.

## Post-release Tasks diagnostics correction

Release: 1.2.4 (2026-08-01)

| Checklist item | Status | Executed evidence |
|---|---:|---|
| Tasks page loads without a read-only binding incident. | ✅ | `DisplayedRunText` explicitly uses a OneWay binding. |
| An operator can distinguish the pre-Stage-18 sequential Gmail screening from a loop. | ✅ | The historical run retrieved 40 messages, then issued 27 sequential Guardrails LLM calls before cancellation. At that time, Gmail emitted `message N of M` progress. Stage 18 supersedes this path with bounded batch progress. |
| Interactive detailed diagnostics are available without normal-log persistence. | ✅ | Debug protocol tests cover round-trip and malformed-line rejection. The opt-in child Runner trace includes LLM request/response messages and existing sanitized tool arguments; the independent Auto-scroll control can pause or resume following output. |
| Long diagnostic output is readable. | ✅ | Large repeatedly changes only the Runner panel from 190 to 490 pixels; the application window is untouched and the page remains scrollable. A local Release build completed two full check/clear cycles with those exact heights and no window movement. |
| Release verification is clean. | ✅ | Release build completed with zero warnings/errors; 261 tests passed, none failed, and one local Ollama integration test was intentionally skipped. |

### Remaining operational choice

Historical note, superseded by Stage 18: the real task intentionally had no Gmail cap and
`MaxGmailMessagesPerOperation` was configured as zero. That preserved complete daily coverage,
while the then-sequential local Guardrails processing scaled linearly with retrieved messages.
Stage 18 keeps the task setting unchanged but replaces that per-message path with bounded batches.

## Stage 17 workflow authoring aids

Release: 1.2.7 (2026-08-02)

| Checklist item | Status | Executed evidence |
|---|---:|---|
| The Tool catalog exposes every Tool Map alias with typed argument metadata. | ✅ | Catalog projection tests assert the alias set, argument kinds, allowed values, and placeholder metadata resolved from the Tool Map. |
| Snippet insertion is deterministic and calls no model, tool, file, or network. | ✅ | `WorkflowSnippetBuilder` and `WorkflowCaretInsertionService` are pure and unit-tested; no provider or tool dependency is injected. |
| Sequential inserts land at the advancing caret position. | ✅ | Caret insertion tests cover empty documents, mid-array insertion, trailing positions, and repeated sequential inserts. |
| Unparsable workflow JSON still inserts the snippet verbatim with a warning. | ✅ | Tests cover the malformed-document path and assert verbatim insertion plus the manual-repair warning. |
| A tool group not authorized by the task is proposed unchecked. | ✅ | Projection tests assert the missing-group entries start unchecked; the built-in `ai` group is never proposed for whitelisting. |
| The prompt editor round-trips `ai.prompt` steps without collateral edits. | ✅ | `WorkflowPromptEditingService` tests assert LF-canonical text, independent `includeContext`, ordered projection, and that untouched steps are re-serialized unchanged. |
| An empty or whitespace-only prompt is rejected. | ✅ | Validator tests cover empty, whitespace-only, and valid prompt text. |
| Undo assisted change reverts the last assisted application. | ✅ | The shared undo snapshot is exercised for catalog inserts and prompt applies alike. |
| Release verification is clean. | ✅ | `dotnet build -c Release` completed with zero warnings and zero errors; 367 tests passed, 1 skipped (local Ollama integration), 0 failed. |

### Stage 17 human follow-up

✅ A manual operator session exercised the Release UI for this stage. The following acceptance
items are **verified in the GUI**:

- ✅ Open the Tool catalog from the Workflow JSON card, search, select an alias, and confirm the
  detail pane and snippet preview render correctly in Light and Dark.
- ✅ Verify Insert, Insert and close, Close, and Cancel behave as specified, including that
  Cancel discards an accumulated multi-insert session.
- ✅ Verify the caret actually advances between consecutive inserts in the live editor.
- ✅ Open the Workflow prompt editor, edit a multiline prompt, toggle `includeContext`, and
  confirm Apply, Revert, and Cancel behave as specified.
- ✅ Confirm the duplicate-step warning banner and the read-only Safety summary are displayed as
  described.
- ✅ Confirm Undo assisted change restores the previous editor value and that only Save task
  persists the result.
- ✅ Check keyboard traversal, focus visuals, minimum-size and maximized layouts for both
  windows.

### Stage 17 post-implementation corrections

Three defects found during the manual session were corrected and re-verified:

| Correction | Status | Executed evidence |
|---|---:|---|
| Insert and Insert and close are meaningless on the Placeholders tab, where a placeholder is obtained with Copy. | ✅ | Both commands are now bound to `IsStepsTabSelected` and hidden on that tab, leaving Cancel and Close; the status line states that insertion applies to steps only. Covered by `InsertionIsOfferedOnTheStepsTabOnly`. |
| The prompt text box did not wrap, keeping a horizontal scroll bar. | ✅ | `TextWrapping` is `Wrap` and `HorizontalScrollBarVisibility` is `Disabled`, so the content width is constrained to the control. Wrapping is visual only and does not alter the LF-canonical text. |
| The Validate outcome could be missed when the status line is scrolled out of view. | ✅ | Validation now also raises a short snackbar notification, `Validation OK.` or `Invalid workflow.`, through the same mechanism as Save, while the detailed status line is unchanged. Covered by `ValidateWorkflowRaisesAShortNotification`. |

### Residual implementation gap

The assistant prompt builder was not rebuilt from the new argument metadata. The AI Helper
therefore still describes tools from its previous prompt source while the Tool catalog uses
the richer typed metadata. This divergence is intentional for this pass and is recorded in the
ledger rather than silently reconciled.

## Stage 20 agent prompt placeholders

Date: 2026-08-03

| Checklist item | Status | Executed evidence |
|---|---:|---|
| Exactly `Today`, `Now`, `task.id`, and `task.name` resolve in prompt-only task execution. | ✅ | Pure resolver tests cover defaults, custom formats, aliases, case/whitespace, Unicode identity, and non-recursion. |
| One Core date contract serves agent prompts and workflows. | ✅ | `DateTimePlaceholderFormatter` is called by both Core catalog validation and `WorkflowPlaceholderResolver`; all legacy workflow tests remain green. |
| One local snapshot covers one expansion. | ✅ | Resolver accepts one explicit `DateTimeOffset`; Runner and Prompt editor each call injected `TimeProvider.GetLocalNow()` once immediately before resolution. |
| Unknown, workflow-only, invalid-format, and malformed occurrences are exact, non-failing warnings. | ✅ | Source-span/category tests cover preservation, adjacency, nesting, unmatched braces, excessive formats, ordering, and counts. |
| Workflow precedence and strict workflow placeholders are unchanged. | ✅ | Runner branches to the workflow engine before prompt resolution; workflow placeholder tests remain green. |
| Runner and Prompt editor send the resolved prompt with existing tools, context, policy, and cancellation. | ✅ | Runner and ViewModel tests capture agent inputs, task identity, immutable context, generation policy, unsaved text, output, and chunk count. |
| Success with warnings remains Success and report index stays binary. | ✅ | Markdown writer test asserts a separate Warnings section, ordinary final output, and `Success` index status. |
| Durable feedback contains no prompt, raw marker, task name, replacement, path, or source span and creates no warning incident. | ✅ | Canary tests assert the aggregate Runner line and warning formatter expose only program-owned counts/categories; the warning path has no incident dependency or call. |
| Prompt editor warning/help states use active-theme resources and clear deterministically. | ✅ automated / ⏳ visual | XAML uses `MutedTextBrush`, `SurfaceBrush`, and `WarningBrush`; tests cover unsaved resolution and edit clearing. Live Light/Dark inspection was not performed. |
| Taskless surfaces remain unchanged. | ✅ | Final call-site audit finds resolver calls only in Runner prompt-only execution and Prompt editor Test; AI Playground and all excluded callers continue to call their existing services directly. |
| EN/FR manuals contain all required copyable examples and remain standalone. | ✅ | Structural validation reports balanced tags, unique ids, valid anchors, no external resources, valid embedded JavaScript, reciprocal language links, and matching Stage 20 example inventory. |
| Release gate is clean. | ✅ | Restore, Release build, 404 passed / 1 expected skip / 0 failed, JSON sample parse, manual HTML validation, and `git diff --check` completed with `-m:1` on every dotnet command. |

### Stage 20 residuals

- Stage 19 remains intentionally unimplemented. Stage 20 has no dependency on it.
- The application was not launched for this pass. Prompt editor callout rendering in Light/Dark
  and a real scheduled crossing-midnight execution remain operator checks; deterministic tests
  cover the non-environmental behavior.

## Stage 18 compact external-content Guardrails

Date: 2026-08-03

Release: 1.3.0

| Checklist item | Status | Executed evidence |
|---|---:|---|
| Gmail and HTTP minimize before LLM use. | ✅ | Pure projector tests cover canonical Gmail fields, normalization, quoted-history removal, JSON/text/HTML/media handling, exact bounds, and omission metadata. |
| Gmail success is summary-only; fallbacks expose no Snippet or Body. | ✅ | Gmail tool tests assert projected headers plus protected summary or review marker and absence of body canaries. |
| `http.get` is a screened compact prefix; `http.getText` is summary-only. | ✅ | HTTP tool tests assert no compact summary call for GET and one compact batch with review behavior for GET Text. |
| Batches are strict, stable, tool-less, and fail closed by phase. | ✅ | Fake-provider tests cover response reordering, duplicate/missing ids, strict schema, output bounds, no tools, fixed policy, malformed first batch, and later-batch continuation. |
| Provider calls are materially grouped. | ✅ | Empty-cache acceptance tests measure exactly 3 calls for 1–12 eligible items, 6 for 13, 1 for an all-low-trust batch, and 0 additional calls for fully cached items. |
| Cache identity is per item. | ✅ | Reordered input with changed opaque ids reuses source, summary, and final verdicts without another provider call. |
| Incidents and logs are content-free. | ✅ | Batch exceptions have no inner exception/provider output; HTTP URLs and Gmail recipients are no longer logged; aggregate projection/batch counters contain no external text. |
| Legacy direct reducer and out-of-scope behavior remain. | ✅ | Existing singleton Guardrails, Web Search, Trust filter, HTTP POST, Gmail send, authorization, Safety, and complete regression tests remain in the gate. |
| Release gate is clean. | ✅ | Restore and Release build completed with zero warnings/errors; 441 tests passed, 1 expected local Ollama integration test was skipped, JSON samples parsed, both manuals passed structural validation, and `git diff --check` succeeded. Every dotnet command used `-m:1`. |

Compaction is a minimization boundary rather than a safety verdict over omitted text. Generated
summaries may omit details; Gmail or the source URL remains the authority when completeness is
required.

## Stage 18A extensible tool integration foundation

Date: 2026-08-04

| Checklist item | Status | Executed evidence |
|---|---:|---|
| One ordered Core catalog owns group names, labels, descriptions, and whitelist eligibility. | ✅ | `ToolGroupDefinition` and `ToolGroups.Catalog` expose the metadata; uniqueness, nonblank fields, order, lookup, `TaskWhitelist`, and `All` are covered by `ToolGroupCatalogTests`. |
| Tool Map and group metadata cannot drift silently. | ✅ | Bidirectional tests require every alias group to resolve and every whitelist group to own a non-AI Tool Map entry; ai remains authoring-only. |
| Task editor whitelist has no concrete per-group state or XAML list. | ✅ | One read-only observable selection collection drives one templated `ItemsControl`; load, save, helper Apply, catalog opt-in, example loading, and Undo iterate that collection. |
| Authorized locations visibility follows Tool Map path metadata. | ✅ | Projection tests prove filesystem and Dropbox require local paths while Gmail does not; selection changes update visibility and dirty state. |
| Tool catalog and Tool tester share catalog order. | ✅ | Both call `ToolGroups.GetDisplayOrder`; catalog ordering has a deterministic equality test. |
| Existing tasks and capabilities are unchanged. | ✅ | No alias, group, package, setting, Safety flag, persisted field, migration, provider call, or runtime tool was added; existing regression tests remain green. |
| Documentation records compiled extensibility without claiming runtime plug-ins. | ✅ | Shared spec, standalone prompt, ledger, README, prompt index, First Run guides, manuals, theme audit, and this report use the same boundary. |
| Release verification is clean. | ✅ | Release build completed with zero warnings/errors; 449 tests passed, 1 expected local Ollama probe was skipped, and `git diff --check` passed. Every dotnet build/test command used `-m:1`. |

### Stage 18A human follow-up

The application was not launched during this pass. Live verification of generated checkbox
wrapping, tooltips, keyboard traversal, and Light/Dark focus/contrast remains an operator check.
