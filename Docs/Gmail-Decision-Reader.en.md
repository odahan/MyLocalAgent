# Gmail decision reader

The decision reader is a separate, opt-in workflow tool. It collects Gmail once, classifies each
selected message sequentially with an installed local decision model, and returns structured
coverage. A second workflow step creates Markdown in code. Existing Gmail readers and saved
tasks retain their protected summary behavior.

## Classification modes

The `classificationMode` argument of `gmailDecision.readClassify` accepts `Hybrid` (default)
or `LlmOnly`. Hybrid mode uses Tev1 and acceptance checks before any second opinion.
`LlmOnly` makes one local structured call per mail, without querying, loading or unloading
Tev1. The reader must remain enabled in Settings; Tev1 does not need to be installed for this mode.

To use the configured Ollama model directly, add this property to the step arguments:

```json
"classificationMode": "LlmOnly"
```

The model is `FallbackModel`, or by inheritance `ArbitrationModel`, then the Ollama chat model;
the timeout is `FallbackTimeoutSeconds`. The conventional context is 8192 tokens, with output
bounded to 512 tokens. Questions and source are budgeted independently of the 2050-token native
context. Native thresholds and arbitration budgets are not applied; choices in
`alwaysArbitrateChoices` still require human review. Output includes `classificationMode=LlmOnly`,
`decisionSource=directLlm`, `counts.directLlmResolved` and `timing.classificationMs`, without
fabricated native scores.

The `projection.decisionContentTruncated` and `projection.llmContentTruncated` flags distinguish
omissions by model. The former is null in direct mode; older results may omit both. Reports
label truncated LLM sources. In hybrid mode, `VisibilityProtectionOverride` retains a relevant
native category despite a noise second opinion, with `Provisional` status and explicit human review.

## Enable and configure

1. Open **Settings → Local decision models** and enable the reader.
2. Select an already installed decision model (default `tev1:latest`). The application never pulls
   models automatically. The supported provider is `OllamaSystemOne`.
3. Leave the decision endpoint blank to inherit Ollama. Only loopback HTTP(S) is supported;
   remote host names, credentials, URL paths and redirects are rejected.
4. Leave arbitration and fallback models blank to inherit the local Ollama chat model. They
   always stay local, even when the application's active provider is OpenAI. The decision model
   must have a different installed digest from chat, arbitration, fallback and embedding models
   at the same endpoint. Aliases do not evade this check.
5. Save. Saving performs configuration validation and local persistence; it does not load a model
   or wait for inference. Use **Refresh models** to inspect the installed catalog and **Test
   decision model** for two synthetic greeting requests. The test reads no mailbox and releases
   only the dedicated decision model afterwards.

Keep-alive defaults to 10 seconds, with mandatory cleanup after a batch. Zero unloads after each
request and can make loading dominate runtime. Cold inference has a 120-second ceiling; warm
inference, arbitration and fallback each default to 15 seconds. The complete operation has a
180-second deadline including collection and lock/gate waits. Cleanup uses an independent
five-second budget after cancellation. Idle lifetime does not guarantee server eviction priority.

## Author a workflow

Use [the disabled starter task](samples/gmail-decision-watch.json) as a separate task. It grants
`gmailDecision` and `filesystem`, reads Recent messages, renders a report and writes a relative
`reports/gmail-watch.md` path under the configured file-system root. Absolute paths still need
the existing per-task write grant. Importing this example does not enable or replace a production
task or schedule.

In the step editor, select `gmailDecision.readClassify` and choose **Load triage example**. The
named question fields edit names, types, instructions and answer descriptions. Move rubric
answers up/down to define their zero-based urgency order. **Apply question fields** commits
them to the step draft; the main Apply also commits pending edits. If you edit questions JSON
directly, use **Reload fields from JSON** before continuing in named fields. Mappings,
per-question/per-choice thresholds and cross-question conflicts remain available as structured
JSON arguments. Changing question names/choices requires updating references in those objects.
No global question profile is created.

Supported questions are `choice` (2–24 named choices), `noul` (a native true score, ultimately
displayed as Boolean), and `score` (2–10 ordered rubric levels). Questions and answer keys must
be unique literal identifiers. Instructions/descriptions can be English or French. Policies,
enum values, keys and mappings must be literal. Authored question text may use ordinary task/date
placeholders; previous outputs and accumulated mail/workflow context cannot author questions.
Unknown properties, casing collisions, incompatible thresholds and invalid references fail before
collection. The report includes only canonical answer types and bounded display metadata, never
mail bodies, snippets, classifier prompts or reasoning.

`gmailDecision.renderReport` consumes `{{mailTriage.output}}`, a title and `includeNoise`. It
does not fetch Gmail, invoke a model or write a file. Add `filesystem.write` separately. Do not
add screening, summarization, Knowledge/RAG or an LLM formatting step to this pipeline.

These aliases cannot be agent tools, Direct Chat tools, `ai.prompt.toolAliases`, or `forEach`
steps. The Tool tester requires a selected authorized task and the explicit **Authorized workflow
test** checkbox. Merely possessing a task id or the old `gmail` group is insufficient. The reader
grants no send, delete, attachment retrieval or mark-read capability.

## Understand results

Top answer score, minimum lead (percentage points), and concentration score refer to native model
scores. They are not calibrated correctness probabilities or accuracy guarantees. Acceptance uses
inclusive, unrounded comparisons. A native top score of 0.81 and runner-up of 0.16 have a lead of
65 percentage points. A noul true score of 0.05 has 95% certainty on the false side; the report's
true/false boundary independently defaults to 50%. Rubric acceptance evaluates the level
distribution, not its weighted numeric score. Ties require a second opinion.

Second-opinion budget is `min(M, floor(M × percent / 100), maxCalls when nonzero)`, where M is
the collected count. Attempted failed calls also consume the budget. Valid native answers remain
**Provisional** if arbitration is disabled, exhausted or fails. Missing valid answers produce
**Unresolved** rows. A structurally valid lightweight fallback resolves a row without requiring
review merely because the decision provider was unavailable. Explicit uncertain choices remain
provisional. If one model kept a mail visible and another selected rank-zero noise, the higher
attention category is retained, disagreement is shown, and the second model's reply/urgency
answers remain intact.

Technical unavailability defaults to **LightweightLlmFallback**. This is one fixed, non-streaming,
tool-free, temperature-zero, thinking-disabled local chat request per affected mail, using the
same authored questions and strict validator. It is also the independent arbitration service;
arbitration is not given decision scores or answers. The reader never falls back to the legacy
multi-pass pipeline or a cloud provider. **Fail** instead rejects technical unavailability.
Transient decision errors receive at most one retry. Two consecutive malformed decisions open
an operation circuit; missing/incompatible models and unsupported routes open it immediately.
Remaining mails use fallback without refetching or probing SystemOne again.

Every collected mail remains in source order in version-1 JSON, with `Classified`, `Provisional`
or `Unresolved` status. Counts satisfy `classified + provisional + unresolved = collected`.
Deadline-skipped rows explicitly carry `NotProcessedBeforeDeadline`; cancellation publishes no
successful result. Collection bounds expose partial coverage without inventing a provider total.
Large results omit probability details first (`scoreDetailsOmitted`), retain all rows and selected
scores, and fail explicitly if required metadata still exceeds 512 KiB.

Reports group by descending attention rank while retaining source order inside each group. They
show coverage, local models, fallback/second-opinion usage and phase timing. Provisional/unresolved
mail has a human-review badge. `includeNoise=false` explicitly states the intentionally hidden
count. Empty collections say **No messages found**. All sender/subject/title values are escaped;
Gmail links are constructed only from validated opaque hex identifiers.

## Budgeting, memory and diagnostics

Tev1's observed effective scoring context is approximately 2,050 tokens. Without a verified local
tokenizer, this implementation uses a documented conservative byte-fallback BPE upper bound of
one token per UTF-8 byte, with 400 tokens reserved for templates/scoring/output. Questions are
never trimmed; contracts leaving fewer than 128 conservative source tokens are rejected. Mail is
shortened deterministically on Unicode boundaries with an omission marker. Quoted history and
current-content truncation are recorded separately. Significant source truncation forces a second
opinion, even when native scores are strong. The conventional call has its own bounded 8,192-token
context and 512-token output reserve. This conservative estimator can increase arbitration rates;
the technical probe's repeated synthetic fixtures do not establish real-mail quality or latency.

Cross-process OS file handles own reader intent and request-boundary leases. Empty rendezvous
files are not ownership; process death releases handles. Pending embeddings take priority between
mail requests, unload the verified dedicated decision model, then run under the same global LLM
gate. Readers retain progress and detect reloads through residency checks before choosing cold
versus warm timeout. Lock order is embedding intent, endpoint request boundary, then global LLM
gate; no owner waits for residency while holding the gate. End-of-batch cleanup waits for the
request boundary and skips unloading while another reader operation still owns intent, including
model aliases. External Ollama clients are outside this coordination boundary.

Runs display **Completed with warnings** and workflow reports expose degradation/quality warnings.
Ordinary classifier diagnostics use operation/task/step correlation, per-run mail index, contract
and response hashes, estimates, native scores, acceptance rules, timing and fixed failure codes.
They omit mail body, sender/subject, prompts, reasoning and arbitrary provider error text.

The shipped task uses StopOnError. An outer Retry authored by a user repeats this read-only
collection and classification batch and incurs the full cost again. Live FR/EN quality evaluation
and reference-installation latency targets require separate explicit authorization; no production
mail benchmark, model download, deployment or task migration is part of installation of this code.

Provider reference: [Ollama Tev1 API](https://ollama.com/library/tev1) and
[Ollama model lifecycle](https://docs.ollama.com/api/generate).
