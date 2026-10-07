# Stage 32 — General local decision evaluation

Date: 2026-10-07
Status: phases A–B implemented and deterministically verified; phase C fixtures/protocol prepared, no model experiments executed; phases D–E deferred
Product names in the UI: **Decision models**, **Evaluate a decision**

## 1. Mission

Expose the installed local decision model as a reusable application capability. The common
authoring experience is: select content, ask a question, define options, inspect the answer and
scores. Workflows can use that answer with their existing deterministic routing steps.

Start with a generic service and a simple workflow tool. Add a convenient editor and executable
examples, evaluate concrete use cases, then consider integrated routing and agentic exposure.
Do not implement all phases at once or make later phases prerequisites of the first release.

The objective is to discover where Tev1 provides a useful quality, latency, or resource tradeoff.
Its presence in Settings is not evidence that it should replace the general LLM. Difficult Gmail
classification does not establish that the model is useless for narrower decisions. Conversely,
successful synthetic examples do not establish production quality.

French-language quality and the effects of complex mail context are hypotheses to evaluate,
not diagnosed causes of the current mail experience.

## 2. Existing implementation and dependencies

Read shared specification 00 and stages 18/18A/18D, 26, 28, 29, 30 and 31 before implementation.
Inspect the actual contracts and tests before finalizing configuration and service extraction.

Existing reusable foundations include:

- `ILocalDecisionClient` and `OllamaLocalDecisionClient`: bounded local requests to
  `/v1/systemone`, model identity checks and structured provider responses.
- `DecisionQuestionSet`: typed questions and native answer/distribution validation.
- `DecisionModelResidencyCoordinator` and the shared execution gate: coordination with other
  inference and embedding operations, including across Runner processes.
- `flow.decision`: deterministic scalar and numeric comparisons over previous JSON outputs.
- Declarative tool metadata, tool-group permissions, workflow validation and the step editor.

Current behavior must be represented accurately:

- `gmailDecision.readClassify` uses decision settings and native decisions in `Hybrid` mode.
- `LlmOnly` intentionally uses local structured LLM classification without Tev1.
- `gmailDecision.renderReport` is deterministic and does not call a model.
- Both Gmail decision tools are currently workflow-only.

The new generic tool must not require Gmail collection, mail report mappings, Gmail credentials,
mail-specific arbitration rules or a mail classifier. Existing Gmail behavior remains compatible.

## 3. Delivery order and scope

| Phase | Deliverable | Completion condition |
| --- | --- | --- |
| A | Common decision service, generic settings and workflow-only `decision.evaluate` | Contract and compatibility tests pass; tool works with existing routing |
| B | Simple step editor, test action, score display and workflow examples | A user can author and inspect a decision without writing provider JSON |
| C | Controlled evaluation of narrow use cases, including French | Reproducible results identify useful and unsuitable cases, or record inconclusive evidence |
| D | Optional integrated **Route by decision** experience | Separate routing contract preserves existing graph and authorization guarantees |
| E | Optional exposure to agentic tasks and `ai.prompt` tool use | Explicit opt-in, bounded calls and provenance; no claim of mandatory tool use |

Phases A and B form the first implementation increment. Prepare evaluation fixtures alongside
them; run actual model experiments in phase C. Phases D and E are roadmap items, not features
implicitly enabled by this specification. Reassess their priority after the first increment.

No scheduled production task is migrated and no model is downloaded automatically. No deployment,
Git publication or live-mail evaluation is part of implementing this specification.

## 4. Common service and configuration

Introduce a common decision evaluation service in Core, backed by the existing local provider
client. Separate generic answer validation and acceptance from Gmail report mappings and
cross-question mail rules. Prefer extraction and reuse over duplicating provider transport,
validation, execution gates or model-memory coordination.

Create a generic `Decision` configuration section and a **Decision models** Settings area.
Move ownership of native provider settings here: provider, endpoint, installed model, keep-alive,
cleanup, cold/warm inference timeouts, gate wait, bounded operation timeout, input budget and
default acceptance thresholds. Preserve existing validated bounds where applicable. Document
each new generic limit explicitly; do not reinterpret a mail-count limit as a generic call budget.

The default provider is `OllamaSystemOne`, model `tev1:latest`, with loopback-only endpoints.
An omitted endpoint inherits the configured local Ollama endpoint. No cloud substitution,
automatic pull, implicit Knowledge retrieval or inherited agent tool execution is allowed.

Gmail enablement, collection limits, report-related settings, arbitration and lightweight LLM
fallback stay under `GmailDecision`. Both callers ultimately use the same native provider
configuration, without changing Gmail's effective behavior during migration.

Migration requirements:

1. When an explicit `Decision` section exists, it is authoritative for shared native settings.
2. When it is absent, resolve the shared native values from legacy `GmailDecision`, preserving
   effective defaults. Saving settings persists the canonical section through the existing store.
3. The generic tool remains disabled by default, including when the legacy Gmail reader is enabled.
   Its enablement and per-task tool-group permission are separate from Gmail enablement.
4. Existing Gmail workflows continue to run with the same effective native settings and their
   existing arbitration/fallback policy. `LlmOnly` does not newly require Tev1 installation.
5. Update embedding coordination, UI bindings, validation and documentation to use the canonical
   native configuration. No competing sources of truth or independent residency coordinators.
6. Cover legacy-only, new-only, both-present and neither-present configurations in tests.

Inference settings are inherited. Authoring a decision must not require repeating model names or
transport settings. Optional per-step acceptance overrides may tighten global defaults in the
first increment; execution limits may only shorten configured bounds.

## 5. First tool contract: `decision.evaluate`

Register a declarative `decision` tool group, opt-in under existing task authorization. The first
release is workflow-only. The tool is a read-only evaluation with no connector or file side effects.

Required arguments:

- `source`: non-empty text to evaluate, normally a previous step output or selected field.
- `question`: non-empty instruction describing one decision.
- `options`: object mapping 2–24 stable option identifiers to non-empty descriptions.

Use the existing identifier restrictions and description bounds where reusable. The first release
supports a single choice question. It internally constructs a validated native question named
`decision`; users do not need to know `state`, `criteria`, or the provider question envelope.
Reject unknown arguments and malformed or duplicate option identifiers before inference.

An optional `acceptance` object supports `minTopProbability` and `minMargin`, both in [0, 1].
Omitted values inherit defaults, converting legacy percentages exactly once. Defaults of 0.80
and 0.20 are initial acceptance settings, not claims of quality. If the existing optional
concentration check is enabled globally, retain it and explain it in advanced UI.

Example persisted step:

```json
{
  "step": "decision.evaluate",
  "id": "readiness",
  "args": {
    "source": "{{request.output}}",
    "question": "Does this request contain enough information to proceed?",
    "options": {
      "process": "Essential information is present and the request can proceed.",
      "clarify": "Essential information is missing and clarification is needed.",
      "review": "The request requires human review or does not fit the other options."
    }
  }
}
```

The general service should retain the existing provider support for Boolean (`noul`), ordered
rubric (`score`) and named multiple questions. Exposing these modes through the generic public
contract is a later extension; do not add overlapping argument forms in the first increment.

## 6. Stable output and uncertainty

Return validated JSON with a schema version and one of three statuses:

- `accepted`: a valid unique top choice meets all effective acceptance checks.
- `uncertain`: the native response is valid but is tied or fails an acceptance check.
- `unavailable`: bounded evaluation cannot produce a valid native result, for example model
  missing, timeout, invalid provider output or an input that cannot fit the allowed context.

Illustrative output only; these scores are not measured results:

```json
{
  "schemaVersion": 1,
  "status": "accepted",
  "choice": "clarify",
  "probabilities": { "process": 0.08, "clarify": 0.87, "review": 0.05 },
  "topProbability": 0.87,
  "margin": 0.79,
  "confidence": null,
  "acceptance": { "minTopProbability": 0.80, "minMargin": 0.20 },
  "reasons": [],
  "provider": "OllamaSystemOne",
  "model": "tev1:latest",
  "timing": { "gateWaitMs": 0, "inferenceMs": 320, "totalMs": 340, "cold": false }
}
```

For `uncertain`, retain validated scores and the provider's top candidate, with reason codes;
`choice` is a candidate, not permission to proceed. Ties always produce `uncertain` even when
numeric thresholds are zero. Show alternatives and their scores in the UI.

For `unavailable`, `choice`, `probabilities`, `topProbability`, `margin` and `confidence` are null.
Return sanitized stable reason codes and available timing, never fabricate scores or label a
technical failure as an ordinary option. A malformed authoring contract, disabled feature or
missing task permission remains a validation/authorization error. User cancellation propagates
through existing cancellation handling; it is not swallowed into a successful tool result.

Validate allowed choices, selected-top consistency, finite values, complete distributions and
sum tolerance using the existing native response checks. Keep all normalized scores in [0, 1];
display percentages only at the UI boundary. `margin` is the top score minus the second score.
Native `confidence`, when present, is distribution concentration, not calibrated correctness.

The first release does not arbitrate or silently fall back to a conventional LLM. Any future
fallback must have explicit provenance and must not manufacture native probabilities.

## 7. Existing workflow routing

Use `flow.decision` unchanged in the first increment. Provide an executable example that first
routes on `status`, then routes on `choice` only along an accepted path. Send uncertainty and
technical unavailability to explicit handling branches. A second example may compare a validated
score numerically, but must still check status first.

Preserve existing forward-only acyclic routing, reachability, guaranteed-output dependency
analysis, `next` semantics and error policies. Do not introduce an expression language or loops.
The tool's status output is consumable workflow data; it is not a control transfer by itself.

Validate example workflows with the application's actual validator. Avoid examples that rely on
an output from a step bypassed on one route. Update the tool catalog and AI Workflow Helper so
they can author valid steps and explain the required acceptance check.

## 8. Authoring and inspection UI

Extend the existing MVVM Toolkit step editor with **Evaluate a decision**:

- Source field supporting existing output placeholders and field-selection aids.
- Question text area.
- Editable option rows with stable identifier and description; 2–24 options.
- Collapsed advanced acceptance settings with visible inherited effective values.
- **Test decision** action using explicit sample text and the real configured service.
- Result display showing status, candidate, each option score, margin, reasons and timing.

Testing must not run the surrounding workflow, resolve arbitrary connector calls, perform file
writes or alter persisted tasks. A placeholder without a run context requires explicit sample
text. The action is cancellable, respects enablement and permissions, and warns through normal
validation when the model or settings are unavailable. Do not run inference automatically while
editing or saving.

Scores must be understandable without reading provider JSON. Do not generate an explanation
and imply that it is Tev1's reasoning: the model supplies structured decisions, not a verified
rationale. English product text and English XML code documentation are required; provide FR/EN
user documentation in the repository's established format.

## 9. Input protection, bounded execution and memory

The tool evaluates only explicitly supplied content. Do not automatically include task prompts,
accumulated workflow history, Knowledge, embeddings, other documents or other messages.

Existing protection rules for upstream Gmail, HTTP, file and other tools remain applicable.
Stage 31's special access to bounded raw mail remains confined to its isolated Gmail pipeline;
this generic tool does not create a new bypass for external-content collection or screening.

Budget the full provider prompt, including source, question, all option descriptions and output
reserve. Reuse conservative bounded budgeting where possible. Character limits alone do not
establish token fit. Reject an oversized input with `unavailable` and an `InputTooLarge` reason;
do not silently truncate it or summarize it with an extra LLM call in the first release.

Use the current execution gate and residency coordination. Preserve local model identity checks,
the separation from chat/embedding models, bounded gate wait and operation deadline. Cleanup on
success, failure and cancellation must not unload a model owned by another active operation.
Do not add a new shared mutable singleton execution context.

Measure cold loading separately from warm inference. The existing mail probe observed a large
cold/warm difference; generic one-shot decisions must be evaluated including cleanup and reload
costs. Do not keep the model indefinitely resident to hide this cost or weaken embedding
coordination. A future workflow-scoped residency lease requires a separate explicit design.

Logs record model identity, question/configuration version or hash, status, sanitized reasons and
timings. Do not log input bodies by default. Store annotated evaluation fixtures deliberately,
with no production secrets or personally identifying mail data.

## 10. Evaluation: establish where Tev1 is useful

Prepare a reproducible fixture runner and a written evaluation protocol. Model experiments are
separate from deterministic unit tests and must be explicitly launched. Respect the repository's
integration-test authorization rule; do not start live model or live-mail runs as part of ordinary
automated verification.

Begin with narrow decisions over short inputs:

1. Request completeness: proceed, ask for essential information, or review.
2. Intent routing: distinguish a small set of operational destinations.
3. Applying a short explicit policy to facts supplied in the same input.
4. Later, judging a short draft against a clearly described rubric.

Build paired French/English fixtures with equivalent meaning and labels reviewed before model
execution. Include ordinary cases, boundary cases, genuinely ambiguous requests, negation,
missing facts, overlapping options, irrelevant content and embedded instructions. Start with at
least 30 paired cases for the first use case; this is a pilot, not a production-quality guarantee.
Reserve separate held-out cases before tuning questions and thresholds.

Compare native Tev1 4B with one structured, tool-free call to the configured local general LLM,
using equivalent source, question and options. Include a deterministic rule baseline where the
task can reasonably be expressed as rules. Record prompts and model identities, not just model
aliases. If labels are disputed, retain that disagreement rather than adjusting labels to match
the model.

Report:

- Overall choice accuracy and confusion matrix against reference labels.
- Coverage accepted at each threshold and accuracy/error rate within accepted results.
- Rates of uncertainty, unavailable results and incorrect accepted decisions.
- French versus English outcomes, with paired disagreements.
- Cold latency, warm latency, total workflow latency and median/p95 where sample size permits.
- Model residency/resource observations and any effect on chat or embeddings.
- Fixture count, execution conditions, repeated runs and limits of the evidence.

Separately vary source length and complexity while preserving the question. Optional experiments
may compare French with an explicitly translated English version, or another installed model
size, but translation cost and changed meaning must be recorded. Neither translation nor a model
switch becomes an implicit product dependency.

Set use-case success criteria before evaluation: acceptable wrong-route rate, required accepted
coverage and latency budget. These are use-case decisions, not universal thresholds. Recommend
Tev1 for a case only when held-out evidence supports the required behavior and an actual benefit
over the baseline. An inconclusive or unfavorable result is a valid outcome of this phase.

## 11. Deferred extensions

### Integrated routing

After evaluating the first increment, specify **Route by decision** as an authoring convenience
over the common evaluator: map stable option ids to destinations and supply explicit destinations
for uncertainty and unavailability. An `other`/`review` semantic option is distinct from those
two technical/policy routes. Keep ordinary workflow validation and authorization guarantees.
Decide its persisted representation in that follow-up design; do not introduce an alias now.

### Agentic tasks

Expose the same evaluator through opt-in `decision` permissions and existing agent tool plumbing.
Add per-run bounded invocation budgets to avoid repeated scoring loops. Inference remains
isolated and tool-free even when the caller is an agent.

An agent may formulate questions and interpret results, but availability of a tool does not
guarantee its use. A required decision checkpoint must be enforced by orchestration and specified
separately. Tev1 does not expand connector permissions or authorize consequential actions.

### Advanced question modes and second opinions

Consider Boolean questions, ordered rubric scores and multiple questions only after the simple
choice UX is established. A rubric's weighted score is distinct from its most probable level.
Optional LLM arbitration requires a separate contract with visible provenance, costs and budgets.

## 12. Verification and acceptance of the first increment

Use meaningful deterministic tests with fake provider responses for:

- Contract validation and normalized score validation, including ties and malformed responses.
- Threshold inheritance, percentage/unit conversion and exact threshold boundaries.
- Distinct accepted/uncertain/unavailable paths; cancellation and authorization failures.
- Settings migration and unchanged effective Gmail behavior, including `LlmOnly`.
- Gate/deadline handling, cleanup ownership and embedding coordination.
- Workflow routing examples, output dependencies and permission enforcement.
- Editor serialization, option identifiers, inherited settings and explicit test action.

Run the applicable repository checks. Every `dotnet build` and `dotnet test` command must include
`-m:1`. Default tests: `dotnet test -m:1 --filter "Category!=Integration"`. Integration tests
require explicit user request. Do not turn nondeterministic model-quality judgments into ordinary
unit-test pass/fail assertions.

The first increment is complete when the generic workflow tool, migration, simple editor and
validated examples are available; tests pass; and user/technical documentation explains scores,
uncertainty, limits and the next evaluation phase. Completion of implementation must not be
reported as proof that Tev1 is useful or reliable for a particular use case.

## 13. References

- [Stage 31 — Gmail decision reader](31-Extension%20Jev.md)
- [Stage 29 — Conditional workflow routing](29-Stage29-ConditionalWorkflowRouting.md)
- [Local feasibility probe](../Tev1-Local-Feasibility.md)
- [Local mail classification research](../Local-Mail-Classification-Research.md)
- [Ollama Tev1 API and limitations](https://ollama.com/library/tev1)

Provider limits and behavior must be rechecked against the installed Ollama version during
implementation. Existing local observations are measurements of those fixtures and that runtime,
not guarantees for future workflows.
