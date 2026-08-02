# STAGE 18 — Compact External-Content Guardrails

> Run only after **Stage 17** is finished. Stages 9 and 10 remain abandoned proposals and are
> neither prerequisites nor in scope. This stage optimizes the existing Guardrails pipeline; it
> does not introduce another model, provider, runtime, tool alias, page, or persisted workflow
> option.

## 1. Read first (mandatory)

1. `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§21. §21 is the
   normative contract for this stage and explicitly supersedes the Stage 7 full-source and
   sequential-Gmail rules where stated.
2. `Docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs, package versions, privacy
   boundaries, provider selection, and generation-policy decisions are binding.
3. The current `GuardrailsOptions`, `IContentTrustService`, `ContentTrustService`,
   `ContentScreenerPrompt`, `ContentSummarizerPrompt`, `ContentTrustEnvelope`,
   `ContentTrustPolicy`, `GuardrailDelivery`, Guardrails result and exception records,
   `GmailTool`, `HttpTool`, Gmail DTOs and mapper, `ToolCallLog`, `ILlmClientFactory`, internal
   generation policies, Runner debug trace, incident reporting, Settings, tests, README,
   manuals, and samples.

## 2. Mission

The current external-content circuit is safe but unnecessarily expensive for inbox analysis:
each Gmail message is screened independently, oversized bodies are recursively screened and
summarized chunk by chunk while covering every source character, and the final summary is
screened again. A modest inbox can therefore cause dozens of sequential calls to the configured
LLM even though the workflow needs only enough information to produce a brief summary and a
priority classification.

Replace that circuit with a compact, bounded pipeline:

1. deterministically extract only the Gmail or HTTP text that can be delivered downstream;
2. discard boilerplate and over-budget text **before any LLM call**;
3. screen independent compact items in bounded batches when more than one item is available;
4. summarize accepted compact items through bounded, tool-less batch calls;
5. screen the generated summaries before delivery;
6. deliver only protected summaries, compact protected raw HTTP prefixes, or application-authored
   review markers — never omitted source text.

The active provider and model remain exactly those configured for the application. The stage
must materially reduce LLM calls and timeout exposure without weakening fail-closed delivery,
tool authorization, incident hygiene, or cancellation.

## 3. Product decisions (locked by the product owner — do not re-open)

| # | Decision |
|---|---|
| S18-D1 | **Use the configured LLM only.** Screening and summarization resolve the existing `ILlmClientFactory`; there is no guard-specific model, model download or conversion, additional inference runtime, provider override, lifecycle control, or model-selection UI. |
| S18-D2 | **Deterministic minimization comes first.** Gmail and HTTP extractors run before Guardrails LLM calls. Text omitted by an extractor is never screened because it is never sent to any LLM, agent, tool result, report prompt, or workflow context. Omission is always represented by application-authored metadata or a marker. |
| S18-D3 | **Guard before summary.** Compact external text is screened before it is summarized. A source below `CautionThreshold`, or whose screening is disabled or failed, is never sent to the summarizer. |
| S18-D4 | **Tool-less isolation.** Every screening and summarization call is a direct `IChatClient` call with no tools, skills, agent loop, workflow context, memory, filesystem access, network tool, or side effect. Existing fixed internal temperature/top-p policies remain binding. |
| S18-D5 | **Bounded batch calls.** Independent items from one tool invocation may share one model request, but each keeps an opaque id and its own nonce-delimited payload and result. Batches never span users, tasks, tool invocations, or processing phases. There is no background queue. |
| S18-D6 | **No per-item retry cascade.** A failed, timed-out, malformed, incomplete, or id-mismatched batch fails closed for only that batch, records one sanitized incident, and processing continues with later batches. It is not retried as N singleton calls. Caller cancellation still propagates immediately. |
| S18-D7 | **Gmail becomes summary-only downstream.** A successfully processed Gmail message delivers exact bounded canonical headers plus a labeled factual summary of the compact projection. Its original body, quoted history, and omitted suffix never enter workflow context. |
| S18-D8 | **Gmail is batched, not message-sequential.** Retrieval order and output order remain stable, but compact projections are processed in bounded batches. The Stage 7 requirement to complete every LLM call for message N before starting message N+1 is superseded. |
| S18-D9 | **HTTP aliases stay distinct.** `http.getText` extracts readable page text and delivers a protected factual summary. `http.get` keeps its raw-response purpose but delivers only a protected, media-aware compact prefix. `http.post`, Web Search, Trust filter, Dropbox, filesystem, Calendar, Gmail attachments, and outbound content are unchanged. |
| S18-D10 | **No recursive full-source reduction for Gmail or HTTP GET.** The Stage 7 complete-source reduction algorithm remains available to existing direct callers but is no longer used by Gmail reads, `http.get`, or `http.getText`. Those tools use deterministic projection limits instead. |
| S18-D11 | **Per-item trust remains authoritative.** A delivered summary's effective score is the minimum of its compact-source score and its final-summary score. Batch membership never changes thresholds, encoding, withholding, suspicious-indicator union, or cache identity. |
| S18-D12 | **Compatibility without duplicate content.** Existing tool aliases, arguments, root response shapes, authorization behavior, and established trust fields remain. New projection metadata may be added, but deprecated raw fields are not reintroduced and source text is never duplicated. |
| S18-D13 | **Memory-only cache and model snapshot stay simple.** Existing 24-hour per-item in-memory caches remain; no persistent cache is added. One provider/model snapshot is used for a top-level batch operation, and a mid-operation change fails the remaining affected batch closed. |
| S18-D14 | **No speculative scope.** Use the existing HTML parser, JSON stack, provider abstractions, WPF stack, and test stack. Add only contracts and implementation required by the compact pipeline described here; no dormant feature, generic plug-in system, or unrelated extension point. |

## 4. Critical constraints (restated)

- R1, R2, R4, R5, R7, R9, R10 and addenda §12–§21 remain binding except for the explicit
  supersessions in §3 and §21. English-only product text, warning-clean complete files, English
  XML documentation on every public API, MVVM Toolkit conventions, and sanitized incident
  reporting remain mandatory.
- The configured provider may be Ollama or OpenAI-compatible. Batch prompts and parsing must be
  provider-neutral and must not assume an Ollama-only API or model family.
- Base64 spotlighting and nonce delimiters remain defense-in-depth, not security boundaries.
  No raw fallback is introduced.
- A smaller projection is safe only because omitted characters cannot reach any later LLM or
  workflow step. Do not retain an alternate raw property, diagnostic prompt field, exception
  message, or log line that defeats this boundary.
- Gmail and HTTP connector retrieval limits, host allowlists, Safety gates, task tool-group
  whitelists, and Runner timeout/cancellation behavior remain unchanged.
- Every `dotnet restore`, `dotnet build`, and `dotnet test` command explicitly uses `-m:1`.

## 5. Scope

### 5.1 Compact-content contracts (Guardrails)

Add immutable contracts representing an already minimized item and its processing result. Exact
type names may follow repository conventions, but the public shape is equivalent to:

```csharp
public sealed record CompactExternalContentItem(
    string Id,
    string Content,
    int MaximumSummaryCharacters);

public enum CompactExternalContentStatus
{
    Summarized,
    SourceWithheld,
    SourceScreeningUnavailable,
    SummarizationUnavailable,
    FinalScreeningUnavailable
}

public sealed record CompactExternalContentResult(
    string Id,
    string? Summary,
    ContentTrustResult SourceTrust,
    ContentTrustResult? SummaryTrust,
    CompactExternalContentStatus Status,
    bool RequiresHumanReview,
    long LatencyMs);

public sealed record CompactExternalContentBatchFailure(
    string Phase,
    Exception Exception,
    int ItemCount);

public sealed record CompactExternalContentBatchResult(
    IReadOnlyList<CompactExternalContentResult> Items,
    IReadOnlyList<CompactExternalContentBatchFailure> Failures);
```

Extend `IContentTrustService` with one batch-oriented operation taking an ordered, non-empty
collection and returning exactly one ordered item result per input plus zero or more sanitized
batch-failure descriptors. A descriptor carries a purpose-built sanitized exception so the
existing Gmail/HTTP caller can report it once through `IIncidentReporter`; it never retains raw
provider output, an exception message containing external content, or an external-content inner
exception. Keep the signatures and semantics of `ScreenAsync` and `SummarizeAsync` unchanged for
Trust filter and existing callers. The new operation is orchestration over the current provider
abstraction; it is not a second guardrail implementation.

Input validation occurs before contacting the provider:

- ids are non-empty, unique within the call, and treated only as opaque correlation keys;
- content is non-null and no longer than `GuardrailsOptions.MaxContentChars`;
- requested summary sizes are between 1 and `MaxSummaryChars`;
- caller order is preserved in every result, including failed and withheld items;
- opaque ids, content, summaries, nonces, and hashes may not be logged; aggregate counts may.

### 5.2 Deterministic batch partitioning

Partition the ordered input greedily without reordering. A batch contains at most **12 items**
and at most **six times `MaxContentChars` source characters**. An item that fits the per-item
limit always fits a batch by itself. These constants are program-owned and are not new settings.

For each batch, the compact pipeline performs at most three provider calls:

1. one source-screening call for every item in the batch;
2. one summarization call containing only items whose source screening completed with a score
   greater than or equal to `CautionThreshold`;
3. one final-screening call for the summaries returned by call 2.

If no item is eligible for a phase, that phase makes no call. Empty or application-authored
content that can be handled deterministically must also avoid unnecessary summarization.
The summary request sets a program-owned output-token bound large enough for the strict JSON
envelope plus the sum of requested per-item summary maxima, using the repository's existing
conservative character-to-token estimate. It must not reuse the workflow/report output ceiling.
If the provider still truncates or exceeds the strict response bound, the phase is malformed and
fails closed under §5.3; partial summaries are never delivered.

Introduce dedicated, independently versioned batch screener and batch summarizer prompt
templates; do not silently repurpose the singleton prompt schemas. The batch system prompt
requires strict JSON with an array of results. Each input is wrapped in
its own fresh nonce delimiters and identifies its opaque id outside the untrusted block. The
response must contain every expected id exactly once, no unknown id, and the phase-specific
schema only. Response order is irrelevant during parsing; results are restored to caller order.
Record both batch prompt versions and their exact schemas in the architecture ledger. Existing
singleton prompt versions change only if their text actually changes.

### 5.3 Batch failure, cancellation, and isolation

- Provider timeout, transport failure, invalid JSON, wrong root shape, duplicate/unknown/missing
  id, invalid verdict, absent summary, or provider/model snapshot change invalidates the current
  phase for the entire current batch.
- A source-screening batch failure produces the existing failed-screening conservative state for
  each item and sends none of them to summarization.
- A summarization batch failure preserves successful source trust but returns no generated text;
  callers deliver their source-specific human-review marker.
- A final-summary-screening failure returns no generated summary to the caller, even in Base64;
  callers deliver their human-review marker.
- The batch result exposes one failure descriptor per failed phase. The Gmail/HTTP caller reports
  it exactly once through the existing incident path, with operation, phase, provider name,
  model name, item count, elapsed time, and correlation id only. Never include opaque ids,
  external-system ids, content, partial model output, nonces, hashes, URLs, or headers.
- After a non-cancellation failure, later batches continue. There is no automatic singleton
  replay, recursive split, provider fallback, or workflow-step failure caused solely by one
  failed batch.
- Caller cancellation and task timeout cancel the active request and stop the operation
  immediately without fallback generation or incident reporting, matching §12.4.

### 5.4 Per-item cache semantics

Reuse the existing in-memory caches at item granularity:

- source verdict keys use compact item content, provider, model, and the new screener prompt
  version;
- summary keys use compact content, provider, model, the new summarizer prompt version, and the
  requested maximum;
- final-summary verdicts use the generated summary exactly like any other verdict;
- batch composition, batch position, opaque id, nonce, and neighboring items never enter a key;
- cached results may remove individual items from a batch phase, and a fully cached phase makes
  no provider call;
- the TTL, privacy exclusions, and process-local lifetime remain unchanged.

### 5.5 Gmail deterministic projection

Create one pure, unit-testable Gmail projector used by every Gmail read alias. It receives an
`EmailMessageDto` and `MaxContentChars` and produces the exact compact canonical source that may
enter Guardrails, plus projection metadata.

Raise Guardrails validation to require `MaxContentChars >= 2_048`; the shipped default remains
8,000. This is a validation change to the existing setting, not a new setting, and guarantees
enough room for canonical labels and explicit omission markers.

The canonical field order is fixed:

1. `From`
2. `To`
3. `Subject`
4. `Received`
5. `Snippet`
6. `Body`

Normalize line endings to `\n`, replace disallowed control characters with spaces, trim trailing
horizontal whitespace, and collapse more than two consecutive blank lines. Do not alter visible
Unicode text, decode embedded data, inspect attachments, or follow links.

Apply these deterministic per-field maxima before the overall limit:

- `From`: 512 characters;
- `To`: 512 characters;
- `Subject`: 1,000 characters;
- `Snippet`: 1,500 characters;
- `Received`: the existing invariant timestamp representation;
- `Body`: the remaining overall budget after labels and omission markers.

Each shortened field ends with an English application-authored marker containing only its
omitted character count. The complete canonical projection, including labels and markers, must
never exceed `MaxContentChars`.

Remove quoted reply history conservatively before applying the Body budget. A removal boundary
is recognized only at the start of a line using a maintained, compiled set covering Gmail-style
quoted blocks and the common English and French reply separators already present in real mail,
including `On … wrote:`, `Le … a écrit :`, and `-----Original Message-----`. A separator-like
phrase in the middle of a line is ordinary content. Do not remove the current message's
signature, contact details, dates, requests, or action items. The removed suffix is replaced by
one marker with its character count.

Projection metadata records original canonical character count, projected character count,
total omitted character count, whether any field was capped, and whether quoted history was
removed. It contains no omitted text.

### 5.6 Gmail batch processing and delivery

`GmailTool.ReadTodayAsync`, `ReadUnreadAsync`, and `ReadRecentAsync` keep their current arguments,
retrieval order, root object, request limit, Guardrails message limit, progress reporting, and
stable output order.

After retrieval and limit application:

1. project every message in memory without an LLM call;
2. submit the ordered projections once to the compact batch operation;
3. combine results back by the internal opaque id and original index;
4. serialize messages in the original connector order.

Use `Math.Min(800, MaxSummaryChars)` as the maximum generated Gmail summary length. The reducer
prompt remains factual and explicitly preserves names, sender intent, dates, deadlines,
decisions, requests, obligations, amounts, risks, and action items. It does not assign an
urgency category; that remains the responsibility of the consuming workflow prompt.

Successful delivery contains the exact **projected** From/To/Subject/Received fields and a
labeled AI summary, protected by `GuardrailDelivery` using the minimum source/final trust score.
The Snippet and Body are source material for the summary and are not repeated downstream.

Messages below `CautionThreshold`, with disabled or failed source screening, failed
summarization, or failed final-summary screening build a bounded projected-header plus exact
application-authored Gmail-review block and apply the existing source-trust delivery policy to
that block. A low score therefore withholds the whole block, disabled/failed screening protects
it as marked Base64, and a clean/caution source whose later phase failed uses its corresponding
normal encoding. Every case sets `requiresHumanReview=true` and never exposes projected Snippet
or Body content.

Keep existing properties wherever their meaning remains valid and add:

- `originalCanonicalCharacterCount`;
- `projectedCharacterCount`;
- `omittedCharacterCount`;
- `projectionWasTruncated`;
- `quotedHistoryWasRemoved`.

`OriginalBodyCharacterCount` remains the connector body's original length.
`ContentWasSummarized=true` only when a generated summary passed final screening.
`TrustScope` becomes `CompactCanonicalProjectionAndDeliveredSummary` on success and describes
the precise screened scope on fallback. Do not add raw header, snippet, body, or summary fields
beside the single protected `content` value.

Progress logs become batch-oriented: after retrieval, report projection completion and then
`batch N of M` with item count and phase. Do not emit one progress line per message unless a
message is mapped to a fallback, and even then log index/status only.

### 5.7 HTTP deterministic projections

Keep URL validation, allowlists, request limits, response streaming/capping, metadata, and
Safety behavior unchanged. Replace Gmail/HTTP use of recursive full-source reduction as follows.

**`http.get`.** Build a media-aware compact prefix no longer than `MaxContentChars`:

- JSON is minified when it parses successfully; otherwise it is treated as text;
- textual XML and plain text have line endings/control characters normalized without semantic
  reformatting;
- HTML uses the readable extraction rules below;
- unsupported or binary media types produce an application-authored unsupported-content marker;
- a shortened response ends with an omitted-character marker and exposes the original,
  projected, and omitted counts.

Screen this one compact prefix once and deliver it through the existing protected `body`
property. Do not summarize it: the alias retains its raw-prefix purpose. Disabled or failed
screening remains conservative; content below `CautionThreshold` is withheld normally.

**`http.getText`.** Parse HTML with the existing `HtmlAgilityPack` dependency. Remove `script`,
`style`, `noscript`, `svg`, `canvas`, `form`, `nav`, `header`, `footer`, and `aside` nodes. Prefer
visible content under the first non-empty `main`, then `article`, then `body`, falling back to the
document root. Preserve useful boundaries for title, meta description, headings, paragraphs,
list items, table cells, block quotes, and preformatted text; decode entities and compact
whitespace without flattening every block onto one line. Non-HTML textual media uses the same
normalization without DOM processing.

Cap the exact readable projection at `MaxContentChars` with an omitted-character marker, process
it as a one-item compact batch, and use `Math.Min(2_000, MaxSummaryChars)` for its factual summary.
Deliver only the protected summary in `text`. A failure at any phase delivers the existing
source-specific HTTP review/skip marker and continues the workflow.

Both aliases add `projectedCharacterCount`, `omittedCharacterCount`, and
`projectionWasTruncated`; existing `OriginalCharacterCount` and `DeliveredCharacterCount` keep
their meanings. No HTTP response text is logged or incident-reported.

### 5.8 Existing callers and explicitly unchanged behavior

- Trust filter continues to call the singleton operations and may inspect the complete text the
  user explicitly supplied. Its UI and human-only indicators are unchanged.
- Web Search keeps its Stage 8 canonical result-set truncation and one-screening-call behavior.
- `ContentTrustService.SummarizeAsync` retains the Stage 7 complete-source algorithm for direct
  compatibility, but Gmail and HTTP code paths must no longer call it.
- `http.post` response behavior and its documented residual risk are unchanged.
- Gmail attachments are neither extracted nor screened. Gmail sending is unchanged.
- No settings page, task property, workflow argument, Tool Map entry, JSON converter, migration,
  or configuration key is added by this stage.

### 5.9 Observability and privacy

Add sanitized performance events sufficient to verify the optimization:

- tool alias;
- provider and model names;
- fetched, projected, screened, summarized, delivered, withheld, and human-review counts;
- batch count and provider-call count per phase;
- cache-hit count per phase;
- elapsed milliseconds per phase and for the complete tool call;
- original/projected/omitted **aggregate character counts only**.

Never log individual Gmail ids, sender/recipient addresses, subjects, snippets, bodies, URLs
with query strings, HTTP text, summaries, model output, nonces, content hashes, or exception
payloads. Existing Runner output may display the final protected tool result under the Stage 16
private diagnostic policy; no new raw diagnostic channel is added.

## 6. Required automated tests

Add or update deterministic tests for at least:

### 6.1 Projection tests

- Gmail canonical field order, invariant timestamp, line-ending/control normalization, blank-line
  compaction, field caps, exact omission counts, overall `MaxContentChars` bound, empty fields,
  Unicode, and a prefix whose labels nearly exhaust the budget;
- conservative English/French/original-message reply boundary removal, non-boundary phrases,
  signature preservation, quoted suffix count, and no attachment/link inspection;
- HTTP JSON minification/fallback, plain text and XML normalization, unsupported media marker,
  HTML node removal, main/article/body/root fallback, block-boundary preservation, entity
  decoding, caps, and omission metadata.

### 6.2 Batch service tests

- greedy stable partitioning at 12 items and six-times-character budget boundaries;
- one-call source screening, eligible-only summarization, final-summary screening, and phase
  omission when no work remains;
- strict id correlation with provider response order changes;
- duplicate, missing, unknown, or malformed ids and invalid JSON fail only the current batch;
- source failure prevents summarization; summary or final-screen failure prevents generated-text
  delivery; later batches continue;
- low-trust items are never passed to summarization;
- effective score is the source/final minimum and indicators are unioned per item only;
- provider/model snapshot changes fail closed;
- caller cancellation stops immediately without later batches or incident reporting;
- exactly one sanitized incident per failed batch phase;
- no tools or agent path are present on any provider call;
- output lengths and schemas are bounded and malformed overlong output is rejected.

### 6.3 Cache and privacy tests

- cache keys and hits remain per item regardless of batch membership/order/id/nonce;
- partially cached batches send only misses and restore full caller order;
- fully cached phases make no provider call;
- prompt-version changes invalidate relevant entries;
- logs/incidents contain aggregate counts and sanitized metadata but none of the supplied canary
  ids, addresses, subjects, URLs, source text, summaries, hashes, nonces, or partial responses.

### 6.4 Gmail and HTTP integration tests

- every Gmail read alias projects all in-limit messages before batch processing, preserves
  connector order, request/Guardrails limits and counts, and maps results to the correct message;
- successful Gmail output contains bounded headers plus summary only, never Snippet/Body source;
- Gmail low-trust/disabled/failed phases return review markers and continue other batches;
- no Gmail path calls the legacy recursive `SummarizeAsync`;
- `http.get` performs one screen of its compact prefix and no summarization;
- `http.getText` performs compact screen/summary/final screen and returns summary only;
- HTTP failures return the correct marker and do not fail the workflow;
- Web Search, Trust filter, `http.post`, Gmail send, attachments, authorization, and Safety
  behavior remain unchanged.

### 6.5 Call-count acceptance tests

With caches empty and all items eligible, assert for one Gmail operation:

- 1–12 compact messages whose combined content fits the character budget use exactly three LLM
  calls, not three calls per message;
- 13 such messages partition into two batches and use exactly six calls;
- all-low-trust messages use source-screen calls only;
- fully cached messages use zero calls;
- one malformed first batch does not prevent the second batch from completing.

Use a fake provider and virtual time; tests never contact a real model, Gmail, or HTTP endpoint.

## 7. Documentation

Update:

- `Docs/ARCHITECTURE-LEDGER.md` with the compact projection boundary, batch schemas and prompt
  versions, fixed batch limits, superseded Stage 7 rules, delivery/failure semantics, and the
  exact public APIs added;
- root `README.md`, `Docs/User-Manual.html`, `Docs/User-Manual.en.html`, and the relevant
  `Docs/FirstRun.md` / `Docs/FirstRun-EN.md` sections with the summary-only Gmail and
  `http.getText` behavior, compact-prefix `http.get`, visible omission metadata, aggregate
  progress, privacy boundary, and deterministic verification commands;
- relevant samples or fixture documentation only when an existing documented output shape
  changes; never add real Gmail or HTTP content.
- User Guide EN and FR when it matters

Documentation must state plainly that compacting content is a minimization boundary, not a claim
that omitted text was safe, and that generated summaries can omit details. Users requiring the
original must review it in Gmail or at the source URL.

## 8. Stage gate (all mandatory)

- [ ] No new NuGet package, project, provider, model, runtime, page, setting, task field, workflow
      argument, or tool alias.
- [ ] Gmail and HTTP extractors are deterministic, pure where practical, bounded, and fully
      tested.
- [ ] Gmail reads and `http.getText` deliver protected summaries only; `http.get` delivers one
      protected compact prefix; omitted content is unreachable downstream.
- [ ] Batch calls are provider-neutral, tool-less, strict-schema, stable-order, cache-aware, and
      fail closed per batch without singleton retry cascades.
- [ ] Existing authorization, Safety, cancellation, incident, envelope, threshold, encoding, and
      effective-score behavior remains green.
- [ ] Call-count acceptance tests demonstrate grouping and absence of recursive Gmail/HTTP
      reduction.
- [ ] Privacy tests prove raw/projection/summary content does not enter logs or incidents.
- [ ] `Docs/ARCHITECTURE-LEDGER.md`, README, manuals, tests, and samples are consistent.
- [ ] `dotnet restore OD.MyLocalAgent.sln -m:1` succeeds.
- [ ] `dotnet build OD.MyLocalAgent.sln --no-restore --configuration Release -m:1` succeeds with
      zero warnings and zero errors.
- [ ] `dotnet test tests/OD.MyLocalAgent.Tests/OD.MyLocalAgent.Tests.csproj --no-build --no-restore --configuration Release -m:1` passes.
- [ ] `git diff --check` passes and no `bin/`, `obj/`, secrets, tokens, real external content,
      placeholder code, or generated junk is included.

## 9. Acceptance addendum

The stage is not accepted merely because unit tests pass. Before completion, run a synthetic
instrumented scenario containing at least 13 mixed-length Gmail fixtures: ordinary French and
English messages, a newsletter, quoted reply history, an empty body, long fields, a benign
discussion of prompt injection, an explicit injection attempt, a low-trust result, and a forced
batch failure. Demonstrate from sanitized counters that:

- projections are within bounds and omissions are explicit;
- calls are grouped by phase and never made per message on the happy path;
- the injection source is not summarized or delivered;
- the benign security discussion is not automatically rejected by deterministic extraction;
- one failed batch does not abort later batches or the tool;
- final Gmail output contains only bounded headers, protected summaries, or review markers;
- no fixture content appears in application or incident logs.

Also exercise synthetic HTML and JSON HTTP responses to prove `http.getText` summary-only and
`http.get` compact-prefix behavior. This acceptance run uses fake connectors and a fake model; it
does not require network access.

## 10. Before you finish

1. Re-read the complete Stage 18 diff against §21 and every explicit Stage 7 supersession.
2. Search production Gmail/HTTP paths for calls to the legacy recursive `SummarizeAsync`; none
   may remain.
3. Search logs and incident payloads for external DTOs, prompt text, model output, URLs, and
   summaries; remove every leak.
4. Confirm batch prompt parsing rejects partial success rather than guessing or aligning by
   array position.
5. Confirm every failure path returns a bounded application-authored marker and continues later
   batches unless caller cancellation was requested.
6. Run the complete gate with `-m:1`, then report changed files, prompt-version changes, measured
   fake-provider call counts, tests, build status, and any residual limitations.
