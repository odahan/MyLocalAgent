# Decision evaluation — Stage 32 C2 and D

Choose one global engine in **Settings → Decision models**: **Use application LLM** (`CentralLlm`)
or **Use a decision model** (`NativeDecision`). Generic workflows, explicit editor tests and Gmail
decisions use the same selection. Each workflow captures settings, central provider/model and bounds
before its first step; changes apply to later runs. An explicit editor test captures its own settings.
There are no step, task, conversation, provider or model overrides, second opinions or implicit fallbacks.

## Author and inspect

Enable **generic workflow evaluation** and grant the task's separate `decision` group. Both are disabled
by default. `decision.evaluate` remains workflow-only and read-only. Required arguments are nonempty
`source`, a literal `question` (up to 1000 characters) and `options` (2–24 literal stable identifiers
mapped to descriptions of up to 400 characters). Only source supports prior-output placeholders.
Unknown arguments, duplicates and output-derived policy are rejected before inference.

```json
{"step":"decision.evaluate","id":"readiness","args":{
  "source":"{{request.output}}",
  "question":"Is essential information present?",
  "options":{"process":"Essential facts are present.","clarify":"Essential facts are missing.","review":"Human review is needed."}
}}
```

The **Evaluate a decision** editor shows the global engine and model as read-only information.
**Decision settings** opens Settings while preserving the draft. Native advanced thresholds and score
rows apply only to native mode. Existing explicit acceptance stays visible and blocks central execution;
**Remove explicit native acceptance** deliberately removes that requirement. Saving never infers.
**Test decision** requires explicit sample text, enablement and task permission. It is cancellable,
ignores workflow placeholders, runs no surrounding step and persists neither sample nor result.
Late responses from a cancelled/closed editor cannot replace the displayed result.

## Version 2 output and routing

Results include `schemaVersion: 2`, `engine`, `capabilities.scores`, `acceptanceBasis`, `status`, `choice`,
`probabilities`, `topProbability`, `margin`, `confidence`, `acceptance`, `reasons`, `provider`, `model`
and `timing` (`gateWaitMs`, `inferenceMs`, `totalMs`, `cold`). Native scores use [0,1]; percentages are
only a UI representation. Concentration is not calibrated correctness.

| Engine | Acceptance basis | Scores | Status semantics |
| --- | --- | --- | --- |
| NativeDecision | nativeThresholds | Validated native distributions | accepted meets inclusive thresholds with a unique top; ties/failed thresholds are uncertain |
| CentralLlm | validatedChoice | probabilities, topProbability, margin, confidence and acceptance are all null | accepted is a strictly validated allowed choice; invalid/unsupported/bounded failures are unavailable |

`review` or another semantic option is an ordinary central choice, not native uncertainty. Neither
basis guarantees correctness or authorizes side effects. Unavailable results have null choice/scores
and retain the selected engine/basis and available identity/timing. Cancellation propagates; invalid
authoring, disabled tools and missing permission remain errors.

Optional `acceptance` contains `minTopProbability` and `minMargin` in [0,1]. Native overrides can only
tighten Settings defaults (initially .80 and .20). Global optional concentration checks remain native.
Any explicit acceptance object, including `{}`, is incompatible with central mode and must be edited.

Use unchanged `flow.decision` to route on `status`, then `choice` on accepted paths. The
[choice sample](samples/decision-readiness.json) works with either engine. The
[guarded numeric sample](samples/decision-native-score.json) checks status, `capabilities.scores` and
nullability before comparing `topProbability`. Its no-score branch requires explicit review rather
than bypassing the numeric requirement. A central mode switch rejects unguarded native numeric
routes before inference. Consumers must read version 2 and handle null scores explicitly.

## Route by decision (phase D)

In the **Evaluate a decision** editor, enable **Route by decision**, fill each option's destination
and select explicit **Uncertain** and **Unavailable** destinations. Leave **Next** blank. Destinations
are literal existing step ids or `@end`. Apply validates the whole graph and saves only the draft;
**Test decision** evaluates the sample without executing any destination.

Persist the mapping beside `args`, using `decisionRoutes`:

```json
"decisionRoutes": {
  "choices": { "process": "handleProcess", "clarify": "handleClarify", "review": "handleReview" },
  "uncertain": "handleUncertain",
  "unavailable": "handleUnavailable"
}
```

Every authored option needs exactly one destination; multiple options may share a destination.
Only accepted results follow an option destination. Uncertain candidates never select that route;
technical unavailability always follows its explicit destination. Semantic `review` remains an
ordinary option with its own route. Both engines use the same contract and global snapshot;
central accepted results retain `validatedChoice` and null scores. The full version 2 result remains
available as `{{stepId.output}}`, including engine, acceptance basis and provider/model.

The [disabled complete example](samples/decision-route-readiness.json) works with either engine.
Existing `flow.decision` workflows remain valid. Native acceptance overrides retain their checks
and are incompatible with central mode. No numeric requirement is added by the route mapping.
Normal permissions, cycle/reachability checks, guaranteed outputs and cancellation still apply.
Do not combine `decisionRoutes` with `next` or `forEach`. Execution exceptions retain Stop/Retry/
ContinueOnError; ContinueOnError follows declaration order after a failed step, and graph validation
includes that edge. An unavailable evaluation is a successful tool result with a dedicated route.

## Settings, migration and bounds

New profiles explicitly select CentralLlm; existing profiles with no Engine field retain NativeDecision.
An explicit `Decision` section is authoritative; otherwise historical Gmail native values are resolved.
The bundled template keeps legacy native defaults to avoid masking persisted overrides. Saving retains
inactive native fields, historical mail fallback/arbitration data, unrelated settings, enablement and grants.
No inference, model download or scheduled-task rewrite occurs during migration.

| Setting | Default | Range / meaning |
| --- | --- | --- |
| Engine | CentralLlm for new profiles; NativeDecision when omitted in old files | One application-wide selection |
| CentralTimeoutSeconds | 180 | 1–3600; whole central operation |
| CentralGateWaitSeconds | 30 | 1–300; separate execution-gate wait bound |
| CentralMaxOutputTokens | 512 | 64–8192; structured output reserve/limit |
| OperationTimeoutSeconds | 180 | 1–3600; native operation |
| MaxInputTokens | 2050 | 512–2050; native conservative full-prompt budget |
| DecisionProvider / DecisionModel | OllamaSystemOne / tev1:latest | Installed compatible dedicated native model |
| DecisionEndpoint | null | Inherits local Ollama; native requires loopback |
| KeepAliveSeconds / UnloadAfterBatch | 10 / true | Native 0–60 seconds and cleanup required |
| DecisionColdTimeoutSeconds / DecisionWarmTimeoutSeconds | 120 / 15 | Native 1–300 / 1–120 seconds |
| GateWaitTimeoutSeconds | 30 | Native 1–300 seconds |
| MinTopProbabilityPercent / MinMarginPercentagePoints | 80 / 20 | Native 0–100, converted once to normalized units |
| MinConfidencePercent / MinNoulCertaintyPercent | 0 / 80 | Native concentration 0–100 (0 disables), Boolean certainty 50–100 |

Central mode uses the configured central provider/model and global declared context, with one isolated
schema-constrained, tool-free request. It receives no chat history, task/system prompts, RAG or secrets
as prompt content. Reasoning is disabled without retry. Budgeting conservatively counts UTF-8 bytes of
instructions, serialized source/questions and schema, plus output reserve and envelope allowance.
Native mode counts source/questions plus 400 tokens of reserve. Neither path silently truncates an
oversized generic input; `InputTooLarge` is unavailable. Provider/context incompatibility is explicit.
Central local calls retain ordinary primary-model residency; native cleanup has its own five-second
bound and respects other owners. Cold loading is reported only when observable; unknown remains null.

Gmail requires `contractVersion: 2`, independent mail enablement and `gmailDecision` permission.
Remove obsolete arbitration/unavailableBehavior fields deliberately. Historical Hybrid is compatible
only with NativeDecision, LlmOnly only with CentralLlm; neither selects an engine. Omit classificationMode
in new authoring. Native acceptance/alwaysArbitrateChoices and nondefault probability-based reply
mappings require review. Raw Gmail stays local-only, including checks against remotely backed Ollama
aliases; no substitute local provider is selected for a cloud configuration. Central Boolean/rubric
answers are semantic Boolean values and zero-based levels, never fake probability distributions.
The deterministic renderer supports archived schema 1 and new schema 2 reports. See the
[Gmail guide](Gmail-Decision-Reader.en.md) and [settings example](decision-settings.example.json).

## Verification and remaining evaluation

Unit tests use synthetic provider responses, actual configuration monitors, the tool registry and
workflow engine. They do not require Ollama, Gmail credentials or a live mailbox. Run:

```powershell
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore
dotnet test OD.MyLocalAgent.sln -m:1 --no-restore --filter "Category!=Integration"
```

Provider contract checked on 2026-10-07 against [Ollama's Tev1 documentation](https://ollama.com/library/tev1):
`/v1/systemone`, short `keep_alive` and 2–24 recommended options. The locally observed
decision context is separate from the published catalog context. During phase A the installed CLI reported 0.40.0 but could not reach the
server. Phase C subsequently reached that runtime and performed authorized local experiments.
The 2050-token ceiling
comes from the [existing local probe](Tev1-Local-Feasibility.md), not the download catalog's context.
The [phase C results](evaluation/Stage32C-Results.en.md) distinguish implementation verification
from measured model quality: Tev1 did not meet the frozen appointment criteria. The adapted
public VitaminC pilot is descriptive and does not establish a general benefit.

Phase B prepares [30 synthetic French/English pairs](evaluation/decision-readiness-pairs.v1.json),
with 20 development and 10 held-out pairs. The owner approved proposed annotations before phase C;
no separate blind bilingual review is claimed. The [evaluation protocol](evaluation/Decision-Evaluation-Protocol.md)
defines review, baselines, acceptance criteria, identities, repetitions, timing and reporting.
`tools/Prepare-DecisionEvaluation.ps1` validates the corpus offline and can generate 60 disabled,
single-step workflow inputs plus a hashed manifest. It never imports or runs tasks. The actual
comparative [executor](../tools/DecisionEvaluation/README.md) now runs isolated approved pilots
and produces checkpointed observations, aggregate metrics and criterion assessments. No live
mailbox, integration test or deployment was part of the pilot. Integrated routing is now delivered
in phase D; agent exposure remains deferred.
