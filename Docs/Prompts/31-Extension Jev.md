# Stage 31 — Local decision models for Gmail triage

Date: 2026-10-06
Status: implemented; automated gates passed; live-mail quality/latency evaluation pending separate authorization
Product name in the UI: **Gmail decision reader**

## 1. Mission and expected outcome

Add a new, opt-in Gmail reader that classifies messages using a local Jev-style decision model,
asks the configured local LLM for a second opinion only when necessary, and returns useful,
structured results suitable for a deterministic report. The first supported decision provider is
Ollama's `/v1/systemone`, initially exercised with `tev1:latest` (4B).

The primary objective is to make important messages visible and separate them from advertising,
newsletters and other noise. Preserve client/prospect exchanges even when no immediate reply is
required. Optimize the complete operation, including collection, loading, arbitration and report
creation; a fast classifier followed by an expensive reporting pipeline does not satisfy this stage.

Implement a separate tool and execution path. Existing `gmail.readToday`, `gmail.readUnread`,
`gmail.readRecent`, `gmail.send`, persisted tasks and their protection contracts retain their
current behavior. Do not automatically migrate Daily Gmail analysis or edit its schedule.
Deployment and production-task migration are separate, explicitly authorized operations.

## 2. Dependencies and explicit supersessions

Read shared specification 00, declarative tooling 18A/18D, compact Guardrails 18, hierarchical
generation settings 15, workflow editor 28, routing 29 and repetition 30 before implementation.
Use current connector, authorization, execution-gate, incident and structured-response services.

This stage introduces a narrowly scoped exception to Stage 18's mandatory external-content LLM
screening and summary-only Gmail reads: bounded source mail text may be supplied directly to
isolated, tool-free decision/classification requests inside the new workflow-only reader. It is
never returned as a workflow output or injected into an agent session. This exception applies
equally to decision inference, second opinion and lightweight fallback. No global switch disables
Guardrails, and no existing Gmail/HTTP or agentic protection is weakened.

Stage 26's ability to invoke tools from `ai.prompt` does not apply to these internal classifiers.
They never receive tool aliases, Knowledge/RAG, embedding context, task prompts, accumulated
workflow context or another message. No provider auto-switches to the cloud.

English product text, MVVM Toolkit, readable formatting and English XML documentation remain
required. Use existing libraries unless a demonstrated requirement justifies a dependency.

## 3. Locked product decisions

1. Classification and report generation are separate operations; reports are rendered by code.
2. One read call fetches its selected messages once, then classifies them sequentially. No
   per-message Gmail refetch, intermediate summary, guardrail LLM pass or final report LLM pass.
3. The model is configured in Settings, independently of the application's general LLM and
   embeddings models. Questions, answer choices and acceptance thresholds are workflow arguments.
4. Availability failure and ambiguous decisions are different events with different policies.
5. A local, lightweight conventional-LLM classifier is a required prerequisite for fallback.
   Never silently fall back to the current multi-pass Gmail analysis pipeline.
6. No mail is excluded merely because it is difficult to classify. Technical failure preserves
   bounded metadata and an explicit unresolved status; it never manufactures a classification.
7. Tev1 remains resident briefly between requests and is released after the operation. A short
   keep-alive is not an eviction-priority guarantee.
8. Scores are model scores, not calibrated probabilities of correctness. Threshold defaults are
   starting settings, not assertions of classification quality.

## 4. Settings and configuration hierarchy

Add a persisted `GmailDecision` section to app defaults, user settings, configuration binding,
validation and Settings MVVM. New installations and existing settings without this section use
the defaults below; feature enablement is false until the user opts in.

| Setting | Default | Contract |
| --- | --- | --- |
| `Enabled` | `false` | Enables this new reader; no effect on existing Gmail tools. |
| `DecisionProvider` | `OllamaSystemOne` | Only supported provider in this stage. |
| `DecisionEndpoint` | `null` | Inherit the local Ollama endpoint when absent. Reject non-loopback hosts in this stage. |
| `DecisionModel` | `tev1:latest` | Editable installed-model name; no automatic pull. |
| `KeepAliveSeconds` | `10` | Integer 0–60. Zero unloads after each request; UI explains the loading cost. |
| `UnloadAfterBatch` | `true` | Always release the decision model at batch completion/failure/cancellation. |
| `DecisionColdTimeoutSeconds` | `120` | 1–300, includes initial loading/inference, excluding separately measured gate wait. |
| `DecisionWarmTimeoutSeconds` | `15` | 1–120 for subsequent decision requests. |
| `ArbitrationEnabled` | `true` | Enables second opinion on ambiguous results. |
| `ArbitrationModel` | `null` | Inherit the configured Ollama chat model, e.g. Gemma4; never inherit a cloud provider. |
| `ArbitrationTimeoutSeconds` | `15` | 1–120 per inference, independently bounded by operation deadline. |
| `UnavailableBehavior` | `LightweightLlmFallback` | Enum `LightweightLlmFallback` or `Fail`. |
| `FallbackModel` | `null` | Inherit effective arbitration model; independent override allowed. |
| `FallbackTimeoutSeconds` | `15` | 1–120 per inference. |
| `BatchTimeoutSeconds` | `180` | 10–3600 for the complete tool operation, including collection and gate wait. |
| `GateWaitTimeoutSeconds` | `30` | 1–300, additionally constrained by remaining batch time. |
| `MaxMessagesPerOperation` | `100` | 1–500, absolute bound independent of existing Guardrails settings. |
| `MaxProjectionCharacters` | `4000` | 2048–12000; upper bound, not a promise that this fits a model's token window. |
| `MinTopProbabilityPercent` | `80` | 0–100, default for choice/rubric acceptance. |
| `MinMarginPercentagePoints` | `20` | 0–100, difference between first and second probability. |
| `MinConfidencePercent` | `0` | 0 disables the additional concentration check; otherwise 0–100. |
| `MinNoulCertaintyPercent` | `80` | 50–100, acceptance for either side of a yes/no decision. |
| `MaxArbitrationPercent` | `100` | 0–100, optional second-opinion budget, not an acceptance score. |
| `MaxArbitrationCalls` | `0` | 0 means all ambiguous items, bounded by message count/deadline; otherwise 1–500. |
| `QualityWarningPercent` | `20` | Warn when unresolved messages exceed this percentage. |

Decision endpoint and inherited chat endpoint must be loopback HTTP(S), using explicit URI and
DNS/redirect validation; do not rely on host substring matching. Do not modify server-wide
Ollama environment variables. Enablement validates configuration, not network availability.

Snapshot effective settings, model names and question contract once per operation. Precedence
for allowed policies is workflow override, then Settings default. Null/missing means inherit;
zero remains an explicit value. No provider/model/endpoint/generation override belongs in tool
arguments. Internal LLM classification uses fixed non-streaming, tool-free, reasoning-disabled
generation, temperature 0, bounded context and output. Task/global thinking settings cannot
reactivate deep reasoning. If a provider cannot disable thinking or honor the contract, report
incompatibility rather than silently accepting an expensive path.

Reject a decision model that resolves to the general chat, arbitration, fallback or embedding
model's same identity at the same endpoint: lifecycle cleanup must never unload a shared primary
model. Compare resolved installed identities, not only tag spelling, where aliases share a model.
Arbitration and fallback may use the same conventional model.

Changing Settings saves locally without a model load, network probe or waiting for another
LLM execution. Provide explicit **Refresh models** and **Test decision model** buttons.

## 5. New tool group and aliases

Add a distinct task-whitelist group `gmailDecision`, UI label **Gmail decision reader**, opt-in
and disabled by default. It reuses Gmail read OAuth credentials and `IGmailConnector`; it grants
no Gmail send/delete/mark-read capability. Existing Gmail group membership does not implicitly
enable this group, nor does this group enable existing Gmail operations.

Expose two declarative aliases:

- `gmailDecision.readClassify`: fetch and classify a bounded batch.
- `gmailDecision.renderReport`: validate a prior reader result and generate Markdown locally.

Both are workflow-only, have `SupportsForEach = false`, and have no agent function. Add explicit
execution-surface metadata to the declarative tool model if required, with current tools keeping
their existing defaults. Apply it consistently to discovery, registry, Task Editor, Tool catalog,
AI Workflow Helper, Direct Chat, agent tool listing and `ai.prompt.toolAliases` validation.
Runtime authorization must also reject calls from agent/taskless contexts; hiding a UI entry is
insufficient. The Tool tester may invoke them only through an explicit authorized workflow test
context with the same permissions. `renderReport` does not write files; use `filesystem.write`
with existing per-task path authorization.

Extend the immutable execution context with an explicit invocation surface where needed; a task
id alone does not distinguish a workflow from an agent running that same task. Preserve the
surface through authorization projections and nested calls. Existing tools retain compatibility.

### 5.1 Reader arguments

| Argument | Required/default | Meaning |
| --- | --- | --- |
| `selection` | `Recent` | Enum `Today`, `Unread`, `Recent`, matching existing connector semantics. |
| `daysBack` | `1` for Recent | 0–3650; reject it when explicitly supplied with another selection. |
| `maxMessages` | optional | 1–effective operation maximum. Collector pagination must respect the hard maximum. |
| `questions` | required object | Named decision questions, described below. |
| `acceptance` | optional object | Overrides default thresholds and individual-question rules. |
| `reportMapping` | required object | Maps answers to visible report categories and protection ranks. |
| `arbitration` | optional object | Enabled override, ambiguity rules and second-opinion budget. |
| `unavailableBehavior` | optional | Override Settings behavior. |
| `batchTimeoutSeconds` | optional | May shorten, never extend, Settings operation deadline. |

Resolve ordinary authored workflow placeholders using existing strict workflow rules before
validating the arguments. Question/answer keys are literal identifiers: placeholders are not
allowed in keys, policy numbers, enum values or report category mappings. No mail-derived content
may mutate the questions or acceptance policy. Reject unknown argument/contract properties.

### 5.2 Question contract

`questions` is an object with 1–8 unique named entries. Names and choice keys use
`[A-Za-z][A-Za-z0-9_]{0,63}`; reject duplicate keys, including casing collisions, before binding.
Each question has `type`, `instructions`, and type-specific `criteria`:

- `choice`: 2–24 named answers mapped to non-empty descriptions.
- `noul`: optional `true`/`false` descriptions, supplied together; output remains a score.
- `score`: 2–10 ordered non-empty rubric descriptions; preserve order and zero-based levels.

Instructions and descriptions are editable in the workflow, support French and English, and
are bounded (instructions 1000 characters, each description 400, question JSON 24 KiB UTF-8).
The adapter may impose stricter model/request limits before inference; never silently drop
questions, answers or instructions to fit a request. These strings define classification only:
they cannot request tools, fetch links, change model configuration or start workflows.

### 5.3 Complete example

```json
[
  {
    "step": "gmailDecision.readClassify",
    "id": "mailTriage",
    "args": {
      "selection": "Recent",
      "daysBack": 1,
      "maxMessages": 100,
      "questions": {
        "category": {
          "type": "choice",
          "instructions": "Classify this professional email. Preserve client and prospect exchanges. Promotional urgency is not an operational emergency.",
          "criteria": {
            "urgent": "Real incident or imminent deadline requiring immediate attention.",
            "action": "Relevant request for a reply or action, without immediate urgency.",
            "information": "Relevant professional information, including client exchanges without an action request.",
            "noise": "Advertising, newsletters, unsolicited sales or irrelevant notifications.",
            "uncertain": "The available message does not support a clear classification."
          }
        },
        "reply": {
          "type": "noul",
          "instructions": "Does the recipient need to reply or take a concrete action?"
        },
        "priority": {
          "type": "score",
          "instructions": "Evaluate real professional urgency, ignoring promotional pressure.",
          "criteria": ["No urgency", "Normal work queue", "Immediate attention"]
        }
      },
      "acceptance": {
        "minTopProbabilityPercent": 80,
        "minMarginPercentagePoints": 20,
        "minConfidencePercent": 0,
        "minNoulCertaintyPercent": 80,
        "perQuestion": {
          "category": {
            "alwaysArbitrateChoices": ["uncertain"],
            "perChoice": {
              "noise": {
                "minTopProbabilityPercent": 90,
                "minMarginPercentagePoints": 30
              }
            }
          }
        }
      },
      "reportMapping": {
        "categoryQuestion": "category",
        "replyQuestion": "reply",
        "priorityQuestion": "priority",
        "replyTrueAtPercent": 50,
        "categories": {
          "urgent": { "label": "Urgent", "attentionRank": 3 },
          "action": { "label": "Reply or action", "attentionRank": 2 },
          "information": { "label": "Relevant information", "attentionRank": 1 },
          "noise": { "label": "Newsletters and advertising", "attentionRank": 0 },
          "uncertain": { "label": "Uncertain classification", "attentionRank": 1 }
        }
      },
      "arbitration": {
        "enabled": true,
        "maxPercent": 100,
        "maxCalls": 0,
        "conflicts": [
          {
            "choiceQuestion": "category",
            "choice": "noise",
            "noulQuestion": "reply",
            "noulAtLeastPercent": 70
          }
        ]
      },
      "unavailableBehavior": "LightweightLlmFallback"
    },
    "onError": "StopOnError"
  },
  {
    "step": "gmailDecision.renderReport",
    "id": "mailReport",
    "args": {
      "source": "{{mailTriage.output}}",
      "title": "Gmail watch",
      "includeNoise": true
    }
  },
  {
    "step": "filesystem.write",
    "args": {
      "path": "C:\\Reports\\gmail-watch.md",
      "content": "{{mailReport.output}}"
    }
  }
]
```

The example requires `gmailDecision`, the existing filesystem group and task write permission
for the report path. It does not require existing `gmail` tool exposure. Confirm enum spelling
against the implementation before publishing the sample; adapt only syntax, not policy.

## 6. Acceptance and arbitration mathematics

Convert native scores in [0,1] into percentages exactly once for policy evaluation. Retain native
scores in outputs. Threshold comparison is inclusive; use unrounded values. Percentages and
percentage points are distinct. With top scores 0.81 and 0.16, the margin is 65 percentage points.

For each required question (all questions in this stage):

- `choice`: accept only when top probability >= its effective minimum, margin >= its effective
  minimum, and optional native concentration confidence >= its effective minimum. The selected
  choice must be a top-scoring configured key; ties trigger arbitration. Per-choice rules override
  per-question rules, which override workflow defaults, which override Settings. An explicitly
  designated `alwaysArbitrateChoices` value always triggers arbitration.
- `noul`: let p be the true score. Certainty is `100 * max(p, 1-p)`. Accept only if certainty >=
  the effective minimum. This does not convert p into a calibrated probability of correctness.
  The report's true/false boundary defaults to 50%, independently of acceptance certainty.
- `score`: apply the top-level probability and margin checks to rubric levels, including optional
  confidence, and preserve the provider's weighted score. Validate its consistency with the
  distribution. Do not compare a weighted score in [0,N-1] with a percentage threshold.

Any failed acceptance rule or matching conflict rule makes the mail ambiguous. Conflict rules
are a bounded typed list (maximum 16) of a choice match plus a noul threshold or a score-level
probability threshold. OR across rules, AND within each rule. No script or general expression
language; validate all referenced question types/keys/levels before collection. Rules concerning
score urgency compare the probability of the named rubric level, not the weighted numeric score.

Threshold overrides may not contain contradictory/impossible type-specific properties. Validate
all limits statically; unavailable models do not excuse invalid questions or policies.

For M collected messages, the arbitration cap is
`min(M, floor(M * maxPercent / 100), maxCalls if maxCalls > 0 else M)`.
Count attempted second opinions, including failed attempts. Caps never limit lightweight fallback
for an unavailable decision provider. Default budgets permit all ambiguous messages, subject to
the operation deadline. Budget exhaustion is explicit and preserves provisional classification;
it never means the message is confidently accepted.

## 7. Second opinion and final selection

For one ambiguous mail, call the effective local arbitration model once with the canonical bounded
mail projection and the same questions/options. Do not show Tev1's answers or scores in the prompt;
obtain an independent opinion. No tools, summaries, reasoning trace or unrelated mail/context.

Use strict structured output with exactly the configured question names. `choice` answers are
configured keys; `noul` answers are Boolean; `score` answers are discrete rubric levels. Permit one
optional short justification (maximum 240 characters) for internal diagnosis, not a generated
summary. Never use LLM-generated confidence as a substitute for classifier calibration. Validate
the response locally. Empty output, fences, extra keys, invented options or malformed JSON fail.

Arbitration yields the final answers when structurally valid, except for this deterministic
visibility rule: if either model selected a mapped category with attention rank >= 1 and the
other selected rank 0, retain the higher-attention category and set `disagreement = true`.
Otherwise retain the LLM category and its answers, with disagreement recorded. `alwaysArbitrateChoices`
still unresolved by the LLM remain provisional. Final reply/priority are the LLM values; a
visibility override does not silently rewrite those answers, and its reason is recorded.

On unavailable, disabled, exhausted or failed arbitration, retain any valid Tev1 answers as
provisional and visibly mark their reason. On unresolved disagreement or ambiguous classification,
do not duplicate the message into multiple sections: place it once in its retained category with
an uncertainty badge. If there is no valid category, use a dedicated unresolved section.

## 8. Decision-model availability and lightweight fallback

Check the cached installed-model catalog and endpoint compatibility at most once per operation;
the first decision inference is authoritative. Do not assume a model is a decision model merely
because it appears in `/api/tags`, and do not call the chat endpoint as a substitute for SystemOne.
Support loading on the first request; no separate warmup inference is necessary.

Classify failures explicitly:

| Failure | Required handling |
| --- | --- |
| Feature disabled | Configuration error; no silent fallback bypassing user enablement. |
| Missing configured decision model / unsupported SystemOne route / incompatible decision model | Open an operation-scoped circuit and use fallback if allowed. |
| Provider transport failure, overload or inference timeout | At most one transient retry within deadline; then open circuit and use fallback. |
| Invalid response | Trace the validation cause; fall back for the affected mail. Open circuit after two consecutive invalid responses. |
| Invalid workflow contract or request too large because questions alone exceed limits | Fail validation; do not hide an authoring error behind fallback. |
| Mail cannot fit decision context after bounded projection | Use larger-context lightweight LLM for that mail, with truncation metadata. |
| Caller cancellation | Cancel; no retries/fallback or successful partial report. |
| Operation deadline | Return explicit partial coverage if collection succeeded; fail if no usable result exists. |

Once a circuit opens, no repeated probes or model loads for remaining mails. Preserve completed
decisions; only affected and remaining messages enter fallback, using the already fetched source.
The next top-level operation can probe again. Under `Fail`, reject technical decision-provider
unavailability with a actionable incident; valid low-confidence answers still follow arbitration.

The lightweight fallback is conventional existing local LLM infrastructure, simplified for
classification: one bounded, tool-free, reasoning-disabled structured call per mail, the same
questions, and the same validator used for arbitration. No screening/summarization/report passes.
Implement a reusable local mail classification service rather than copying the old workflow's
special-case fallback. Existing Gmail tools continue using their current protected read path;
using this lightweight service in a later replacement of the legacy workflow is a separate
opt-in change. Do not advertise automatic fallback as complete until this service is implemented.

Fallback results share the normal output contract, set `decisionSource = lightweightLlm`, and
expose the triggering reason. A valid fallback classification does not require human review solely
because Tev1 was unavailable. Never reread Gmail or run both fallback and arbitration for the same
mail: fallback already supplies the conventional LLM opinion.

Disable nested provider retries and workflow Retry in the shipped example to prevent request
multiplication. User-authored outer retries may rerun this read-only batch; document their cost.

## 9. Collection, source projection and local isolation

Reuse existing date/time semantics, Gmail pagination and read authorization. Fetch no attachments,
follow no links and perform no mutation. Honor explicit maximums and protection limits separately;
if a next page remains or a bound excluded messages, output `collectionPartial = true`. Do not
invent a total count when the provider did not supply one. Handle empty inbox as a valid empty result.

Reuse deterministic normalization and quoted-history reduction where possible, without changing
the existing projector contract. Bound sender/subject and prioritize current message text.
Source text lives only inside the authorized reader invocation; no raw body, snippet, alternate
projection, LLM prompt or reasoning text appears in downstream output or ordinary logs.

Account for the actual decision context, the rendered question set, provider template overhead,
repeated question scoring and output reserve. Tev1 reported a 2050-token inference context during
the probe; the download catalog's larger advertised context is not the effective decision budget.
Character limits alone are insufficient. Use a verified model-aware budget estimator/tokenizer;
if unavailable, a documented conservative bound, never an optimistic chars/4 assumption. Reject
question sets that leave no useful mail budget. Shorten source deterministically with an omission
marker before sending; record removed history separately from current-content truncation. Never
silently allow provider truncation of question definitions. Significant current-content truncation
forces LLM fallback/arbitration even when Tev1 scores appear clear.

Downstream sender and subject remain untrusted display text. Escape them during Markdown/HTML
rendering; they do not become prompts, instructions, executable links or report template fragments.
Gmail links are built by code from validated opaque message identifiers. No generated URL is accepted.

## 10. Memory lifecycle, concurrency and deadlines

Route decision and conventional LLM requests through the existing cross-process execution gate.
Record wait time separately from inference time; acquire a lease per request, release between
items and never acquire the gate recursively for arbitration/fallback. UI saving is independent.

Add a narrowly scoped decision-model residency coordinator shared across application/Runner
processes for endpoint+model. Hold an operation lease while the model can be used. Before explicit
unload, ensure no other active reader owns or is issuing a decision-model request. Handle process
death using operating-system lease semantics; do not leave a persistent lock file as ownership.
Document and test lock order to avoid waiting for an operation owner while holding the LLM gate.

Use explicit short keep-alive on every decision call. Unload only the selected decision model,
using the supported Ollama lifecycle API with keep-alive zero, after the last owner releases it.
Cleanup is bounded (5 seconds), observes an independent cleanup token after caller cancellation,
and logs a warning if it fails; short keep-alive remains the safety net. Never unload Gemma4 or
the embedding model and never alter their residency configuration.

Use the cold-request timeout again after a known unload/embedding handoff or when a residency
check shows the model absent; the warm timeout applies only to a model believed resident. Keep
the total operation deadline authoritative, so repeated reloads cannot extend the batch indefinitely.

Before loading embeddings through MyLocalAgent, the same coordinator must request release of
idle decision models. During active decision inference, embeddings wait for the request to finish;
the reader yields residency between items, allows pending embedding work to proceed and reloads
Tev1 only afterwards. Enforce this across Runner processes rather than relying on keep-alive to
choose an eviction victim. Waiting consumes the batch deadline. Changes are limited to residency
coordination and must not change embedding results or normal LLM generation behavior.

Distinguish an operation's intent to use Tev1 from an active inference/residency lease. On embedding
handoff, all reader operations suspend their residency leases at request boundaries while retaining
their progress. Unload is allowed once no active inference/residency lease remains, even if those
operations have further mails to process. After embeddings release their usage, waiting readers
may reacquire residency. Bound waits by cancellation/deadline and avoid starving embeddings by
immediately reacquiring a lease for the next mail.

An unrelated external Ollama client is outside this coordination boundary. Detect unexpected
reloads and record them; do not claim absolute server eviction priority. Support CPU spill only
as configured by Ollama, and expose residency/VRAM observations when available without requiring
GPU inventory permission.

## 11. Stable output contract and coverage

Return a versioned JSON object; one item for every collected/selected mail, in source order:

```json
{
  "schemaVersion": 1,
  "status": "CompletedWithWarnings",
  "selection": { "kind": "Recent", "daysBack": 1 },
  "collectedAt": "2026-10-06T18:00:00+02:00",
  "collectionPartial": false,
  "effectiveModels": { "decision": "tev1:latest", "arbitration": "gemma4:12b" },
  "counts": {
    "collected": 37,
    "processed": 37,
    "classified": 36,
    "decisionAccepted": 30,
    "arbitrationAttempted": 6,
    "arbitrationResolved": 5,
    "lightweightFallbackResolved": 1,
    "provisional": 1,
    "unresolved": 0
  },
  "messages": [
    {
      "id": "opaque-gmail-id",
      "sender": "client@example.test",
      "subject": "Contract review",
      "receivedAt": "2026-10-06T09:00:00+02:00",
      "gmailUrl": null,
      "status": "Classified",
      "category": "action",
      "decisionSource": "arbitrated",
      "answers": { "category": "action", "reply": true, "priority": 1 },
      "decisionScores": {
        "category": {
          "choice": "action",
          "probabilities": { "urgent": 0.05, "action": 0.6, "information": 0.25, "noise": 0.05, "uncertain": 0.05 },
          "confidence": 0.4
        }
      },
      "arbitrationReasons": ["TopProbabilityBelowThreshold"],
      "disagreement": false,
      "requiresHumanReview": false,
      "projection": { "currentContentTruncated": false, "quotedHistoryRemoved": true },
      "timing": { "gateWaitMs": 0, "decisionMs": 120, "arbitrationMs": 1800 }
    }
  ],
  "warnings": []
}
```

This illustrates an individual message, not the complete 37-item array. Counts in actual output
must be derived from the full array. Define count invariants: `classified + provisional +
unresolved = collected`; `processed <= collected`; deadline-skipped collected items are unresolved
with `NotProcessedBeforeDeadline`. `classified` counts final answers only. Arbitration/fallback
counts are events, may overlap provisional states, and are not added to coverage counts.

Per-message status is `Classified`, `Provisional` or `Unresolved`; source is `decision`, `arbitrated`,
`lightweightLlm` or `none`. Include all valid native decision scores, canonical final answer types,
finite bounded numbers and fixed diagnostic codes. Native noul is converted to Boolean using the
configured report threshold; native rubric output is retained in scores and final decision level
uses the highest-probability level, ties requiring arbitration. Returned mapping contains the
validated labels/ranks required by the renderer. Never let the model rewrite ids or metadata.

`requiresHumanReview` is true for provisional/unresolved items, false for valid resolved fallback.
Operation status is `Completed`, `CompletedWithWarnings` or `Partial`; expose warnings through
the existing workflow execution-warning mechanism, not only inside JSON. Missing model with
successful fallback is a degradation warning, not a failed classification. More than the effective
quality-warning percentage provisional/unresolved causes a prominent quality warning. Cancellation
does not publish a successful output; connector/authentication failures fail the outer step.

Bound output size before committing workflow context (512 KiB UTF-8). If necessary omit detailed
probability distributions first, retaining selected scores, statuses and coverage; expose
`scoreDetailsOmitted = true`. Never drop message rows to satisfy the bound. If required metadata
still exceeds it, fail with a precise size incident rather than emit a misleading partial JSON.

## 12. Deterministic report renderer

`gmailDecision.renderReport` accepts `source` (reader JSON), `title` (max 200 chars), and
`includeNoise` (default true). It validates version, mapping, count consistency, row identities,
answer types and limits. It neither calls a model nor retrieves Gmail. It has no direct file effect.

Render collection period/coverage, classified/provisional/unresolved counts, local models, fallback
usage and timing. Group rows by mapped category, descending attention rank; retain source order
within a category. Each row includes bounded sender, subject, received time, Gmail link when
available, reply flag, urgency and any uncertainty badge. Show unresolved rows explicitly.
With `includeNoise = false`, show their count and state that those rows are hidden intentionally.

For non-empty collection the report must contain visible mail rows or an explicit explanation
for every intentionally hidden row. An empty heading-only report is invalid. Empty collection
produces an explicit **No messages found** result. No summary is needed for triage; no LLM formatting
pass follows rendering. Escape Markdown metacharacters, HTML and URI characters in display values;
apply the existing secure Markdown renderer on preview.

## 13. Authoring and diagnostics UX

Settings exposes **Local decision models** with enablement, endpoint inheritance, decision-model
selector, arbitration/fallback local-model selectors, keep-alive/unload controls, timeouts and
default acceptance percentages. Advanced controls contain batch limits and quality budgets.
Labels distinguish **Top answer score (%)**, **Minimum lead (percentage points)**, **Concentration
score (%)** and **Second-opinion budget (%)**. Explain that scores are not accuracy guarantees.

Explicit test uses synthetic text, a tiny question set, effective keep-alive and cleanup; shows
availability, model identity, cold/warm elapsed time and typed response. No real mailbox read from
Settings test, no auto-install, and no background probe on Save. UI stays responsive while testing.

Workflow step editor exposes selection and maximum, editable named questions, answer descriptions,
ordered score levels, report mappings and per-question/per-choice thresholds. Start with the
triage example; structured JSON remains available for all object arguments. Validate inline and
round-trip without loss/reordering. Do not create an unrelated global question-profile library.
Tool catalog and AI Helper describe workflow-only scope, local fallback and deterministic reports.

Logs and Runs expose a phase-aware timeline: collection, gate wait, loading/inference, acceptance,
arbitration, fallback, rendering and cleanup. Show processed/total, source counts and quality
warnings. Progress must not block Settings or task Save. Display **Completed with warnings** when
applicable rather than hiding unresolved coverage behind generic success.

## 14. Trace requirements

For every inference record operation/step correlation, opaque per-run mail index, effective model,
question-contract hash, projection character/token estimates, omission flags, gate wait, elapsed
time, phase, available token usage/finish reason, response validation result and final provenance.
Record selected scores, margin, exact acceptance rule and why arbitration/fallback occurred.
Record cold/reload inference separately when observable; otherwise label duration as combined
load+inference, never invent loading time from total latency.

Sanitized failure categories include model missing, route unsupported, incompatible model,
connection/HTTP/timeout, context budget exceeded, empty response, invalid JSON, missing answer,
unknown option, invalid/unnormalized probability, selected/top mismatch, LLM output truncation,
arbitration cap, operation deadline, memory cleanup failure and lock contention. Capture bounded
structural diagnostics and exception stack, provider status and a response hash; omit mail body,
sender/subject, prompts, reasoning and arbitrary provider error bodies from default logs. Validate
probability distributions (finite values in [0,1], keys exactly matching options, sum within 0.01
of 1). Do not normalize a malformed response into apparent success.

Aggregate collection count, each final status, fallback/arbitration rate, gate/loading/inference
durations, cleanup outcome and excluded/hidden rows. Ensure identities/counts can be traced without
persisting source content. Reuse existing incident retention/privacy settings. Diagnostic fixtures
are synthetic by default; recording real content requires separate explicit opt-in.

## 15. Implementation boundaries and deliverables

Suggested contracts (names may follow existing conventions): `GmailDecisionOptions`,
`ILocalDecisionClient`, `DecisionQuestionSet`, `DecisionAcceptancePolicy`,
`ILightweightMailClassifier`, `GmailDecisionTool`, `DecisionModelResidencyCoordinator`, and a
pure report renderer. Place provider HTTP/lifecycle code in Ollama, collection/contracts in Core
and orchestration integration in existing workflow infrastructure. No HTTP details in ViewModels.
Keep configuration, acceptance evaluation and rendering deterministic and unit-testable.

Deliver Settings persistence/UI, declarative metadata/runtime restrictions, question/policy
validation, local provider adapter, lightweight fallback, arbitration, lifecycle coordination,
coverage output, renderer, Runs traces, catalog/editor/helper integration, example workflow and
FR/EN user documentation. Update shared-spec addendum and architecture ledger with the scoped
Stage 18 exception. Do not claim support for AnyJev, GLiClass or other runtimes in this stage;
the client boundary merely permits a future provider without changing workflow questions.

No automatic replacement of existing Gmail workflows, no deployment/restart, no global Guardrails
disable setting, no send/delete tools, no attachments, no embedding-based mail classifier, no
training, no persistent cross-run mail cache and no broad scheduler redesign.

## 16. Verification and acceptance gates

Automated non-network tests must cover:

- Settings migration/defaults, local endpoint validation, snapshot/override precedence, save
  without network/model probes and unchanged legacy Gmail contracts.
- Agent, Direct Chat, `ai.prompt.toolAliases` and taskless rejection; authorized workflow access;
  declarative catalog/editor binding, JSON round-trip and helpful validation failures.
- All question types, answer order, duplicate/unknown keys, percentage boundaries, ties,
  per-choice precedence, noul certainty, rubric scoring and exact arbitration-budget rounding.
- Cross-question conflicts, independent second-opinion prompt, strict response validation,
  protection-rank disagreement, provisional outcomes and no confidence fabrication.
- Missing model, unsupported endpoint, transient retry bound, consecutive invalid-response
  circuit, local fallback, no duplicate collection, no cloud switch and no fallback on cancellation.
- Empty collection, paging limits, unknown total, deadlines, output-size behavior, preserved row
  count, count invariants and surfaced quality/degradation warnings.
- Projection/question context budgeting, quoted-history removal, Unicode, truncation routing,
  no raw source in output/logs, and Markdown/HTML/link escaping.
- Cross-process request/lease ordering, two active readers, embedding handoff, process-death
  ownership release, cancellation cleanup and no unload of Gemma4/embeddings.
- Report groups, intentionally hidden noise, provisional/unresolved badges, and rejection of a
  heading-only result for non-empty visible input. Verify no summarization/Guardrails/report LLM
  calls and no tools/RAG in classification requests using recording fakes.

Run the existing non-integration suite using:

```powershell
dotnet test OD.MyLocalAgent.sln -m:1 --filter "Category!=Integration"
```

Every build/test invocation includes `-m:1`. Integration tests are run only on explicit request.
Use mocked inference for default tests; do not require Ollama, Gmail or model downloads for CI.

Separately, with user authorization, evaluate representative distinct French/English mails,
including important mail without an action, fake urgency, real deadlines, ambiguous requests,
quoted threads, long messages and instruction injection. Compare Tev1 alone, hybrid triage and
lightweight LLM alone. Measure important/actionable recall (especially messages hidden as noise),
per-category precision/recall, provisional rate, arbitration rate and end-to-end cold/warm batch
times. Prefer thresholds that preserve important messages rather than merely minimizing LLM calls.
Do not report repeated synthetic fixtures as independent quality evidence.

On the current reference installation, a representative 37-message short-text batch should target
under 60 seconds warm and under 120 seconds including cold loading with occasional arbitration;
these are product targets, not universal hardware guarantees. Report missed targets by phase.
Default total deadline is 180 seconds and must be respected. No operation may spend hours silently
retrying a handful of mails or declare a useful report when nearly all rows are unresolved.

Acceptance requires a useful row-bearing report, explicit degradation behavior, bounded requests,
no agentic bypass, preserved legacy behavior, memory cleanup and completed applicable tests/docs.

## 17. Evidence and references

Implementation verification (2026-10-06): Release build completed without warnings or errors;
the non-integration Release suite passed 980 tests, with zero failures and one expected Ollama
probe skipped. All build/test commands explicitly used `-m:1`. The suite includes actual
reader-to-report workflow execution and warning propagation, using synthetic recording fakes.
`git diff --check` passed. No live Gmail evaluation, integration tests, model download,
deployment/restart or production-task migration was performed.

Usage: [French guide](../Gmail-Decision-Reader.fr.md), [English guide](../Gmail-Decision-Reader.en.md),
and [disabled sample task](../samples/gmail-decision-watch.json).

The local 2026-10-06 technical probe completed 45 synthetic requests without HTTP/parsing failures.
One-category warm calls took 111–123 ms; a 37-request, three-question repeated-fixture batch took
20.9 seconds including a 9.2-second cold first request. Gemma4 stayed resident and Tev1 expired
after its configured idle lifetime. This establishes API feasibility, not production accuracy.

- [Local feasibility report](../Tev1-Local-Feasibility.md)
- [Local classifier research](../Local-Mail-Classification-Research.md)
- [Ollama Tev1 API and limitations](https://ollama.com/library/tev1)
- [Ollama decision-model announcement](https://ollama.com/blog/ollama-now-supports-jev-style-decision-models)
- [Ollama model lifecycle and concurrency](https://docs.ollama.com/faq)
