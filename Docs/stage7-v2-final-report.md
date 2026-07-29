# Stage 7 V2 final report

Date: 2026-07-29

## Gate summary

| Gate | Result |
|---|---|
| Release build | ✅ Eight projects, 0 warnings, 0 errors |
| Deterministic tests | ✅ 95 passed, 0 failed |
| Environment-dependent test | ⏳ One existing local Ollama integration probe remains intentionally skipped |
| Runner probes | ⏳ Real HTTP transport probe blocked by this sandbox; all four output paths are covered by deterministic tool/provider tests |
| Manual UI and privacy | ⏳ Explicit human follow-up recorded below |
| Documentation | ✅ Shared addendum/ADR H, README, both offline manuals, theme audit, ledger, acceptance report |

Executed commands:

```powershell
dotnet clean OD.MyLocalAgent.sln -m:1 --configuration Debug
dotnet clean OD.MyLocalAgent.sln -m:1 --configuration Release
dotnet restore OD.MyLocalAgent.sln -m:1 --force --no-cache -p:NuGetAudit=true
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release
dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
```

Before the final validation, both cached copies of `Dropbox.Api 7.2.0`, including the
incomplete temporary extraction, were removed. The forced online restore recreated the
complete package in the user cache with NuGet audit enabled. Its SHA-256 matches the package
downloaded directly from NuGet.org, its repository signature validates, and the obsolete
temporary copy remains absent.

Both standalone manuals validate with 21 opening/closing sections, 29 unique ids, no missing
internal target, no duplicate id, no external resource, and valid embedded JavaScript.

## File-tree delta

New production project and contracts:

```text
src/OD.MyLocalAgent.Guardrails/
├── OD.MyLocalAgent.Guardrails.csproj
├── GuardrailsOptions.cs
├── ContentTrustStatus.cs
├── ContentSummaryStatus.cs
├── ContentTrustResult.cs
├── ContentSummaryResult.cs
├── IContentTrustService.cs
├── ContentTrustService.cs
├── TrustDisposition.cs
├── ContentTrustPolicy.cs
├── ContentTrustEnvelope.cs
├── GuardrailScreeningException.cs
├── GuardrailSummarizationException.cs
├── ContentScreenerPrompt.cs
├── ContentSummarizerPrompt.cs
└── ServiceCollectionExtensions.cs
```

New integration and UI files:

```text
src/OD.MyLocalAgent.Core/GuardrailDelivery.cs
src/OD.MyLocalAgent.UI/Pages/TrustFilterPage.xaml
src/OD.MyLocalAgent.UI/Pages/TrustFilterPage.xaml.cs
src/OD.MyLocalAgent.UI/ViewModels/TrustFilterViewModel.cs
```

New deterministic/manual fixtures:

```text
tests/OD.MyLocalAgent.Tests/ContentTrustServiceTests.cs
tests/OD.MyLocalAgent.Tests/TestDoubles/StubContentTrustService.cs
tests/OD.MyLocalAgent.Tests/TestDoubles/StubIncidentReporter.cs
tests/OD.MyLocalAgent.Tests/Fixtures/injection-samples.json
```

Earlier-stage files changed only for the required solution/project references, shared
composition, Gmail/HTTP protection, agent instruction, app settings, navigation, Settings
persistence/UI, test constructor updates, and documentation. Connector DTOs, Tool Map aliases,
method signatures, HTTP POST behavior, workflow syntax, and Safety gates did not change.

## Project-reference graph

```text
Ollama      -> none
Connectors  -> none
Guardrails  -> Ollama
Core        -> Ollama, Connectors, Guardrails
Workflows   -> Core
Runner      -> Workflows, Core
UI          -> Workflows, Core, Ollama, Guardrails
Tests       -> all production projects
```

The solution now contains eight projects and has no dependency cycle.

## Implemented behavior

- Prompt version 2 classifies one nonce-delimited untrusted content block using one system and
  one user message, temperature zero, and no tools, skills, or agent.
- Clean output uses the mandatory marked plaintext envelope. Caution, disabled, failed, and
  summary-truncated paths use the mandatory marked UTF-8 Base64 envelope. Low scores withhold
  content.
- Verdict parsing accepts JSON integers, clamps to 0–100, filters indicators to verbatim
  ordinal matches, de-duplicates them, and retains at most 20.
- Typed screening failure retains the original exception and a complete marked-Base64
  fallback. Typed summarization failure retains the original exception without source,
  summary, payload, nonce, or indicator data.
- Summarizer prompt version 1 covers the full source in ordered deterministic chunks, treats
  intermediate summaries as untrusted, and aggregates the minimum score. The C# cap includes
  an explicit removed-character marker.
- Instance-owned in-memory caches use 24-hour `TimeProvider` expiry and provider/model/prompt
  version/content-hash keys. They do not retain original content or envelopes.
- Gmail returns one protected canonical block per processed message, exact count/limit
  metadata, no duplicated raw component fields, and header-preserving body fallbacks.
- HTTP GET and GET Text return only protected content or exact skip markers while preserving
  safe response metadata. HTTP POST remains the documented Stage-7 residual risk.
- Trust filter exposes human-only indicators and separate trust/processing statuses. Settings
  persists all Guardrails keys and explains disabled and cloud-provider semantics.

## Runner probe evidence

The Release runner host started and attempted a redacted local
`http.getText` probe. This environment rejected loopback connections and also denied the
runner's `%LOCALAPPDATA%` incident file, so the real transport probe could not reach the
guardrail call. No private, credential, suspicious, Base64, or nonce material was used or
captured.

Equivalent deterministic probes executed through `HttpTool` with mocked transport and
provider:

| Probe | Result |
|---|---|
| Benign normal-size GET/Text | ✅ Marked plaintext, `Screened`, trust metadata, raw text only inside the protected value |
| `ScreeningEnabled=false` normal size | ✅ Service makes zero provider calls and returns marked Base64 with `ScreeningDisabled` |
| Oversized but eligible source | ✅ Every ordered chunk and intermediate input classified; only protected final summary delivered |
| Source above safe maximum | ✅ Zero provider calls; exact `SkippedTooLarge` marker and safe metadata; tool call succeeds |

Human/environment follow-up: rerun these four commands against a benign public or authorized
local page where loopback/public HTTP and `%LOCALAPPDATA%` writes are permitted.

## Manual UI and privacy follow-up

The WPF project compiles its new XAML and MVVM Toolkit-generated commands warning-clean. A
human should still perform:

1. Trust filter benign sample, injection sample, Return Base64, and Cancel.
2. Disabled scoring, oversized summary, summary truncation, source-too-large, and technical
   failure states.
3. Threshold/settings hot reload without restart.
4. Dark and Light visual pass, including a long Base64 preview and indicator list.
5. Confirmation that the OpenAI-compatible warning is visible.
6. No real private email may be used for a cloud-provider check without explicit user
   authorization.

## Stage-7 V2 acceptance addendum

- ✅ Guardrails project builds; solution contains eight projects and no dependency cycle.
- ✅ Public contracts and statuses match §5.2 exactly.
- ✅ Screening requests are single-turn and provably tool-less.
- ✅ Clean external content is always enclosed in the plaintext untrusted-data wrapper.
- ✅ Caution, failed, summary-truncated, and normal-size disabled paths deliver marked
      Base64; oversized disabled paths use the documented Gmail/HTTP fallback.
- ✅ Low-score content is withheld.
- ✅ Oversized reduction covers every source character, uses no tools/skills, aggregates the
      minimum source/final score, and returns only the protected summary.
- ✅ Summary truncation includes its marker within `MaxSummaryChars` and forces Base64.
- ✅ Content above `MaxSummarizationSourceChars` produces zero LLM calls and the correct
      source-specific fallback.
- ✅ Provider/parsing/timeout failure is incident-reported once with the original inner
      exception, then safely continues with fallback content.
- ✅ Summarization failure is incident-reported once with the original inner exception;
      Gmail preserves its canonical header block and HTTP continues with its skip marker.
- ✅ Caller cancellation is not incident-reported.
- ✅ Gmail protects one canonical From/To/Subject/Snippet/Body block and never duplicates raw
      fields.
- ✅ Gmail reduction preserves exact From/To/Subject/Snippet values and substitutes only the
      body summary.
- ✅ When Gmail reduction is unavailable, the message remains present with exact canonical
      headers, the Gmail-review body marker, `requiresHumanReview=true`, and an accurate
      `trustScope`.
- ✅ Gmail default limit is unlimited; a positive user limit returns explicit counts and
      warning without exposing skipped content.
- ✅ HTTP GET and GET Text protect all returned body/text; unreducible pages return an
      explicit skip notice and do not fail the workflow.
- ✅ Agent-facing tool output contains indicator count but no verbatim indicator.
- ⏳ Trust filter shows human-only indicators and distinguishes trust status from content
      processing status. Build/bindings are verified; interactive visual evidence remains.
- ✅ Settings contain no insecure Disable Guardrails action.
- ✅ README and both manuals explain privacy, Base64 limitations, summarization, source
      limits, source-specific fallbacks, scope exclusions, and the R9 boundary.

## Residual risks and observed error classes

- A probabilistic classifier can assign high scores to novel, obfuscated, multilingual, or
  encoded attacks and low scores to benign security discussions, quoted mail, source code, or
  operational instructions.
- Base64 is spotlighting only. A model can decode it and may still be influenced.
- Factual reduction can omit nuance or retain adversarial framing; source-wide minimum scoring
  reduces but does not eliminate this risk.
- Provider/model behavior and false-positive/false-negative rates require manual evaluation
  with the supplied fixture set; deterministic unit tests validate control flow, not model
  quality.
- OpenAI-compatible selection sends protected content and all reduction chunks to the cloud
  endpoint.
- Dropbox, local files, Calendar, Gmail attachments, outbound content, and HTTP POST responses
  remain outside Stage 7. R9 is still the hard boundary.

Committed acceptance evidence contains no credential, real private external sample, Base64
payload, generated nonce, or verbatim suspicious indicator from an actual provider run.
