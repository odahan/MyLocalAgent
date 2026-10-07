# Stage 32 — General decision evaluation with a global engine choice

Date: 2026-10-07
Status: phases A–B implemented; phase C pilot and Qwen follow-up complete; phase C2 global dual-engine adaptation implemented and deterministically verified; phase D implemented; phase E open and on hold pending a demonstrated use case and more effective decision engines than Tev1
Product names in the UI: **Decision models**, **Evaluate a decision**

## 1. Mission

Expose decision evaluation as a reusable application capability with one application-wide
choice: the configured central LLM or a dedicated native decision model. The common authoring
experience is: select content, ask a question, define options and inspect the answer, with scores
when the selected engine supplies them. Workflows use the answer with deterministic routing.

Start with a generic service and a simple workflow tool. Add a convenient editor and executable
examples, evaluate concrete use cases, then consider integrated routing and agentic exposure.
Do not implement all phases at once or make later phases prerequisites of the first release.

The pilot supports using the central LLM for ordinary application decisions while retaining
the native path for Tev1 and future compatible decision models. Numerical score precision does
not establish correctness or calibration. Native specialization can benefit some tasks; neither
the small public pilot nor synthetic appointment results establish production reliability.

This revision follows the owner's agreement after evaluation. It preserves completed A–B code
and original experiment evidence. C2 now implements the global dual-engine behavior described
below; phase D is delivered; live quality experiments and optional E remain separate work.

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

Current behavior before C2 must be represented accurately:

- `gmailDecision.readClassify` uses decision settings and native decisions in `Hybrid` mode.
- `LlmOnly` intentionally uses local structured LLM classification without Tev1.
- `gmailDecision.renderReport` is deterministic and does not call a model.
- Both Gmail decision tools are currently workflow-only.

The generic tool must not require Gmail collection, mail report mappings, Gmail credentials or
a mail classifier. C2 must explicitly address Gmail's historical mode/arbitration settings;
they must not remain a competing engine selector or silently override the global choice.

## 3. Delivery order and scope

| Phase | Deliverable | State / completion condition |
| --- | --- | --- |
| A | Common native service, settings and workflow-only `decision.evaluate` | Implemented and verified; retained foundation |
| B | Editor, explicit test, score display and examples | Implemented and verified; adapt to optional scores in C2 |
| C | Controlled pilot and public/Qwen comparisons | Complete; appointment criteria not met, public observations descriptive |
| C2 | Global central-LLM/native selection, two adapters, versioned results, UI and Gmail migration | Implemented; both paths deterministically verified without engine overrides or implicit fallback |
| D | Integrated **Route by decision** experience | Implemented; exhaustive decisionRoutes, status-first routing, editor and graph/authorization checks |
| E | Optional exposure to agentic tasks and `ai.prompt` tool use | Open, on hold until both a demonstrated use case and more effective decision engines than Tev1 are available; separate owner authorization required |

Do not redo A–B or rename archived phase C runs. C2 reuses those foundations and the isolated
structured comparison work; the experiment harness is not itself a production LLM adapter.
D is delivered following the owner's request; E remains optional, not a prerequisite of C2. Further quality experiments
are separately authorized; existing held-out data must not become an undisclosed tuning set.

No scheduled production task is migrated and no model is downloaded automatically. No deployment,
Git publication or live-mail evaluation is part of implementing this specification.

## 4. Common service and configuration

Evolve `IDecisionEvaluationService` in Core into the common entry point for application decisions.
Resolve one immutable effective configuration at workflow-run/batch start and use it for all
decision calls in that execution. A Settings change affects subsequent runs, not half of a batch.
An explicit editor test takes its own snapshot. Keep generic validation and native acceptance
separate from Gmail collection, report mappings and deterministic cross-question policy.

Retain the canonical `Decision` section and **Decision models** Settings area. Add one global
`Engine` selector with two values and English UI labels:

- `CentralLlm`: **Use application LLM**. Resolve the provider/model from the application's central
  AI settings, not from the task, step, conversation override or a second decision-specific LLM
  selector. One bounded, isolated structured request supplies only explicit source and questions.
- `NativeDecision`: **Use a decision model**. Use the existing native configuration and validated
  distributions. Preserve `OllamaSystemOne`, `tev1:latest` and loopback defaults, while allowing
  another installed compatible native model without hard-coding Tev1 in authoring/routing.

Tools never receive an engine/provider/model override. There is no automatic second opinion,
native-to-LLM fallback or alternate provider selection. Failure of the selected engine is visible
as unavailable. Existing feature enablement and task group grants remain independent.

Keep native provider/model/endpoint, residency, timeouts, budget and thresholds persisted when
the central LLM is selected; show their controls as inactive. Native-only settings and model
installation must not prevent a valid central-LLM operation. Central mode needs its own bounded
operation/output limits and a full prompt budget based on the actual central provider; it must
not inherit Tev1's 2050-token ceiling or unload the primary model after every call.

Central mode reuses the existing central provider abstractions and tool-free structured-call
path, without inheriting chat history, tools, RAG, task prompts or secret-bearing settings as
prompt content. Use a provider-supported output schema and strict response validation. Unsupported
capabilities must fail explicitly, without switching providers. Reasoning is disabled where
supported for this bounded decision path; record effective limits rather than assuming the
laboratory's 8192-token/512-output settings suit every provider. Cloud execution is possible only
where the configured central provider and existing data-authorization rules permit it; choosing
central mode grants no new right to export protected source content.

Gmail's enablement, collection limits, report mappings and business rules remain mail-specific.
Its decision calls must use the same global engine snapshot. The Stage 31 raw-mail exception is
local-only: central mode with a cloud provider must not send that raw source off-device. Report
the incompatible configuration explicitly, without silently choosing a local substitute.

Migration requirements for C2:

1. New profile creation explicitly selects `CentralLlm`; do not switch existing installations
   simply because the new field is missing. Existing profiles without `Engine` retain the native
   A–B choice until migration/user selection. Distinguish new-profile initialization from loading
   older files; bundled defaults must not mask persisted legacy settings.
2. Preserve the existing canonical-versus-legacy native resolution: explicit `Decision` is
   authoritative; otherwise retain effective `GmailDecision` native values. Preserve inactive
   native settings and unrelated settings when saving. No migration runs inference.
3. Keep generic/Gmail enablement and task authorization independent and disabled by default.
4. Remove per-tool engine selection from new authoring. Define and test an explicit compatibility
   mapping/versioned migration for legacy Gmail `Hybrid`/`LlmOnly`, arbitration and fallback
   fields. They cannot silently defeat the global selection. Report incompatible persisted
   contracts and guide migration; do not rewrite scheduled production tasks automatically or
   discard historical settings. The new execution contract calls only the selected engine.
5. Existing explicit native `acceptance` overrides and numeric score consumers must not silently
   lose their checks when central mode is chosen. Validate capability compatibility and surface
   required workflow edits. No pretend scores, ignored thresholds or hidden fallback.
6. Cover new profiles, omitted engine in existing files, legacy-only, canonical-only, both present,
   neither present, retained inactive settings and global changes across multiple tools in tests.

Native per-step acceptance overrides may tighten global defaults; they never select an engine.
In central mode, an explicitly supplied native acceptance override is incompatible and must
produce a clear validation error rather than be ignored. Common decisions without overrides
work with either engine. Generic execution limits remain independent from mail-count budgets.

## 5. Common tool contract: `decision.evaluate`

Register a declarative `decision` tool group, opt-in under existing task authorization. The first
release is workflow-only. The tool is a read-only evaluation with no connector or file side effects.

Required arguments:

- `source`: non-empty text to evaluate, normally a previous step output or selected field.
- `question`: non-empty instruction describing one decision.
- `options`: object mapping 2–24 stable option identifiers to non-empty descriptions.

Use the existing identifier restrictions and description bounds where reusable. The first release
supports a single choice question. Its selected adapter builds either the native question named
`decision` or an isolated structured LLM request. Users do not need to know the provider envelope.
Reject unknown arguments and malformed or duplicate option identifiers before inference.

An optional `acceptance` object supports `minTopProbability` and `minMargin`, both in [0, 1].
Omitted values inherit defaults, converting legacy percentages exactly once. Defaults of 0.80
and 0.20 are initial acceptance settings, not claims of quality. If the existing optional
concentration check is enabled globally, retain it and explain it in advanced UI. These fields
are native-only; central mode cannot satisfy an explicit probability/margin requirement. Do not
add per-call `engine`, `provider`, `model` or engine-specific prompt arguments.

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

Retain native support for Boolean (`noul`), ordered rubric (`score`) and named multiple questions.
Gmail currently uses these forms, so C2 must implement semantically equivalent structured LLM
answers for every mode that Gmail requires, using a shared adapter rather than keeping its
independent mail-only LLM transport. A Boolean answer or selected rubric level is a semantic
value, not a probability or a native weighted expected score. Score-dependent mail math must
have explicit engine-aware handling; no conversion of an LLM level into a fake distribution.
Exposing additional modes through the generic public tool remains a later extension.

## 6. Stable output and uncertainty

The implemented A–B result is schema version 1 and native-only. C2 introduces version 2,
retaining the three statuses but explicitly recording their acceptance basis:

- `accepted`: native mode has a valid unique top choice meeting effective acceptance checks;
  central mode has one strictly validated allowed choice. These are distinguishable bases,
  neither a correctness guarantee nor authorization for side effects.
- `uncertain`: the native response is valid but is tied or fails an acceptance check.
- `unavailable`: the selected engine cannot produce a valid bounded result, including missing
  model/provider capability, timeout, malformed output or input exceeding its context budget.

Version 2 adds `engine`, `capabilities` and `acceptanceBasis`. Use `nativeThresholds` for native
responses and `validatedChoice` for central responses; retain the selected basis on unavailable
results for provenance. Capabilities indicate score availability for the selected adapter.
In central mode, `probabilities`, `topProbability`, `margin`, `confidence` and `acceptance` are
null, even when accepted. A self-reported LLM confidence or explanation never fills these fields.
Central mode does not generate native-style uncertainty; a supplied semantic option such as
`review` is an ordinary choice, and invalid output is unavailable. Native ties and threshold
failures retain their existing uncertain semantics.

Update serializers, renderers, placeholders, editor results and workflow examples together.
Explicitly support/version existing consumers; do not label the changed result as schema 1.
Guard score-dependent routes with capability/null checks and reject incompatible explicit
acceptance requirements before inference. A global mode switch must not quietly disable an
authored numeric gate. The selected provider and actual resolved model are always recorded.

Illustrative output only; these scores are not measured results:

```json
{
  "schemaVersion": 2,
  "engine": "NativeDecision",
  "capabilities": { "scores": true },
  "acceptanceBasis": "nativeThresholds",
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

Both adapters strictly validate response shape and allowed choices. Validate native selected-top
consistency, finite values, complete distributions and sum tolerance with existing checks.
Keep all normalized native scores in [0, 1];
display percentages only at the UI boundary. `margin` is the top score minus the second score.
Native `confidence`, when present, is distribution concentration, not calibrated correctness.

Illustrative central result (also not a measured prediction):

```json
{
  "schemaVersion": 2,
  "engine": "CentralLlm",
  "capabilities": { "scores": false },
  "acceptanceBasis": "validatedChoice",
  "status": "accepted",
  "choice": "clarify",
  "probabilities": null,
  "topProbability": null,
  "margin": null,
  "confidence": null,
  "acceptance": null,
  "reasons": [],
  "provider": "Ollama",
  "model": "gemma4:12b",
  "timing": { "gateWaitMs": 0, "inferenceMs": 220, "totalMs": 230, "cold": false }
}
```

C2 selects the LLM as a first-class engine rather than a fallback. It never calls both engines
implicitly. A future second-opinion feature requires a separate contract and visible provenance.

## 7. Existing workflow routing

Use `flow.decision` unchanged. Provide executable examples for both engines that first
routes on `status`, then routes on `choice` only along an accepted path. Send uncertainty and
technical unavailability to explicit handling branches. A second example may compare a validated
score numerically, but must check native score capability, nullability and status first. Display
the result basis when central accepted choices are routed; validation of form is not reliability.

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

C2 preserves source/question/options editing. Show the globally selected engine and effective
model as read-only information, with a route to Settings; provide no per-step engine selector.
Show native thresholds and score bars only when supported. Preserve their persisted configuration
when changing the global engine. Existing incompatible explicit overrides remain visible and
block execution until deliberately edited, rather than being silently discarded. Central
results display choice, validated-choice basis, provider/model and timing with scores unavailable,
without empty score bars, fabricated percentages or an unrequested explanatory model call.

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

Budget the full selected provider prompt, including source, question, all option descriptions and output
reserve. Reuse conservative bounded budgeting where possible. Character limits alone do not
establish token fit. Reject an oversized input with `unavailable` and an `InputTooLarge` reason;
do not silently truncate it or summarize it with an extra LLM call in the first release.

Use existing provider execution boundaries, bounded gate wait and operation deadlines. Native
mode retains the dedicated-model identity/separation checks and bounded cleanup. Central local
mode intentionally uses the shared primary model and its ordinary residency policy; it must not
apply the native incompatible-primary check or unload chat's model after the call. Cleanup on
success, failure and cancellation must not unload a model owned by another active operation.
Do not add a new shared mutable singleton execution context.

Measure cold loading separately from warm inference. The existing mail probe observed a large
cold/warm difference; generic one-shot decisions must be evaluated including cleanup and reload
costs. Do not keep the model indefinitely resident to hide this cost or weaken embedding
coordination. A future workflow-scoped residency lease requires a separate explicit design.

Logs record model identity, question/configuration version or hash, status, sanitized reasons and
timings. Do not log input bodies by default. Store annotated evaluation fixtures deliberately,
with no production secrets or personally identifying mail data.

## 10. Evaluation: evidence for engine selection

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

The initial pilot and subsequent [Qwen comparison](../evaluation/Stage32C-Qwen-Comparison.en.md)
are complete. On twenty held-out appointment inputs, Tev1, Gemma4 and Qwen4B each agree with
60% of annotations; Qwen9B agrees with 50%. On forty adapted VitaminC inputs, agreement is
85%, 80%, 77.5% and 67.5%, respectively. Repetitions are correlated, Tev1/Qwen quantization
differs, and native accepted coverage/error remain separate from unfiltered LLM choices.
These observations motivate the global choice without proving any engine fit for production.
Keep original manifests and the excluded unstable-identity Qwen9B passage's audit unchanged.
C2 deterministic completion requires no new live inference. A later authorized experiment may
verify the production adapter against the frozen protocol; tuning requires new reserved cases.

## 11. Deferred extensions

C2 is required before implementing either extension below. Both use the same common service,
global configuration snapshot and version 2 result, never an additional engine selector.

Phase C's first pilot is recorded in the [English results](../evaluation/Stage32C-Results.en.md)
and [French results](../evaluation/Stage32C-Results.fr.md). The owner approved annotations and
criteria before execution; no blind independent review is claimed. The isolated executor ran
30 FR/EN pairs and an adapted 40-input VitaminC public pilot, three repetitions per backend.
Held-out native accepted coverage was 50% with two unique wrong accepted inputs, so the frozen
appointment criteria were not met despite acceptable latency. Public results are descriptive,
not an exact reproduction of the complete published benchmark. No production threshold or
application setting was changed. An unfavorable pilot is a valid phase C outcome; extensions
are separately scoped; delivered D provides authoring convenience and no automatic-routing
recommendation follows from these observations.

### Integrated routing — phase D delivered

After C2, specify **Route by decision** as an authoring convenience
over the common evaluator: map stable option ids to destinations and supply explicit destinations
for uncertainty and unavailability. An `other`/`review` semantic option is distinct from those
two technical/policy routes. Keep ordinary workflow validation and authorization guarantees.
Route native uncertain and any unavailable result explicitly. Central results have no native
threshold uncertainty; a semantic review option must retain its own destination. Any native
score requirement must remain capability-aware. Persist a top-level `decisionRoutes` object on
`decision.evaluate`: `choices` maps every option id to a literal destination; `uncertain` and
`unavailable` are required literal destinations. Targets are existing ids or `@end`. Do not combine
it with `next` or `forEach`. The version 2 evaluation remains step output. Existing Stop/Retry/
ContinueOnError semantics remain; graph validation includes declaration-order continuation after
an execution failure under ContinueOnError. The editor provides an opt-in **Route by decision**
checkbox and destinations; explicit testing never executes branches.

### Agentic tasks — phase E open, on hold

Phase E remains an open possibility, not planned implementation. Reconsider it only when both
conditions are met:

1. A concrete, demonstrated use case establishes a useful benefit from optional agent access
   to the evaluator over direct agent reasoning or an explicit workflow decision checkpoint.
2. Decision engines more effective than Tev1 are available for that use case, with evidence of
   improved decision quality and practical cost/latency for the application's small workloads.

Existing agent-tool infrastructure alone does not justify this extension. An agentic task does
not guarantee that it calls the evaluator. When evaluation uses the same central LLM as the
agent, an isolated structured call supplies a constrained response and provenance, not an
independent opinion or proof of greater correctness. Required decision checkpoints remain
explicit workflow/orchestration steps.

MyLocalAgent will not connect to JEV through a paid subscription for two or three workflows
and a handful of emails. A JEV subscription or hosted connector is outside this roadmap;
phase E must not introduce that dependency. Future compatible decision engines may be
considered when their benefit and operating cost fit this scale of use.

If both conditions are met and the owner separately authorizes implementation, expose the
same evaluator through opt-in `decision` permissions and existing agent tool plumbing. Add
per-run bounded invocation budgets to avoid repeated scoring loops. Inference must remain
isolated and tool-free even when the caller is an agent.

An agent may formulate questions and interpret results, but availability of a tool does not
guarantee its use. A required decision checkpoint must be enforced by orchestration and specified
separately. Tev1 does not expand connector permissions or authorize consequential actions.

### Advanced question modes and second opinions

Consider Boolean questions, ordered rubric scores and multiple questions only after the simple
choice UX is established. A rubric's weighted score is distinct from its most probable level.
Optional LLM arbitration requires a separate contract with visible provenance, costs and budgets.

## 12. Verification and acceptance

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

### C2 required verification

- One global selection across generic decisions, editor tests and Gmail decision calls; no
  tool/step/conversation override, hidden fallback or mid-run engine/model switch.
- New-profile central default and existing-profile native preservation; canonical/legacy
  precedence, inactive-setting retention, no enablement or task-grant expansion on save.
- Native regression behavior and validated central answers with no probabilities, score-derived
  confidence, extra model call, tools, history or retrieval. Missing native model/settings do
  not block central mode; unsupported central capability fails without provider substitution.
- Version 2 basis/capabilities/null scores, legacy consumers, schema validation, semantic review,
  explicit native acceptance incompatibility and guarded numeric routes.
- Shared central model residency, native cleanup ownership, cancellation, gate/operation/output
  bounds, provider-specific full context budgeting and stable resolved identities.
- Gmail legacy mode/report migration, local-only raw-mail enforcement and meaningful central
  handling of Boolean/rubric/multiple questions without treating levels as probabilities.
- Editor no-inference-on-save, explicit isolated tests, cancellation and late-response protection,
  native/central displays, disabled native controls and incompatible override feedback.

C2 is complete when both adapters, global Settings, generic/Gmail compatibility, versioned
outputs, examples and bilingual documentation are implemented and deterministic tests pass.
It is not complete merely because a benchmark can call Gemma4. D is now implemented following
the owner's request; E remains deferred unless separately requested. Do not rerun historical model experiments or change their conclusions as
part of this adaptation, and do not automatically migrate production schedules.

## 13. References

- [Stage 31 — Gmail decision reader](31-Extension%20Jev.md)
- [Stage 29 — Conditional workflow routing](29-Stage29-ConditionalWorkflowRouting.md)
- [Local feasibility probe](../Tev1-Local-Feasibility.md)
- [Local mail classification research](../Local-Mail-Classification-Research.md)
- [Stage 32C pilot results](../evaluation/Stage32C-Results.en.md)
- [Qwen follow-up results](../evaluation/Stage32C-Qwen-Comparison.en.md)
- [Isolated experiment executor](../../tools/DecisionEvaluation/README.md)
- [Ollama Tev1 API and limitations](https://ollama.com/library/tev1)

Provider limits and behavior must be rechecked against the installed Ollama version during
implementation. Existing local observations are measurements of those fixtures and that runtime,
not guarantees for future workflows.
