# Generic local decisions — Stage 32 phases A–B

`decision.evaluate` evaluates explicitly supplied text using the installed local decision model.
It is a read-only workflow tool, with no Gmail collection, Knowledge retrieval, tools, file access,
cloud substitution, model download or conventional LLM fallback. Enable **Settings → Decision
models → Enable generic workflow evaluation**, and independently grant the task's **Decision
evaluation** tool group. Gmail enablement does not enable this tool. Agent prompts, Direct Chat,
`ai.prompt.toolAliases` and `forEach` cannot call it.

The workflow step editor exposes **Evaluate a decision** with source, question, ordered option
rows and collapsed acceptance overrides. Provider JSON is not needed to author or inspect a choice.

## Author and test a decision

1. Insert or edit `decision.evaluate` in the workflow step editor. Enter source text or use the
   existing placeholder picker, then write a literal question and 2–24 option identifiers and
   descriptions. Add/remove/reorder controls preserve the option order.
2. Inspect inherited and effective minimums. Advanced overrides use percentages for top
   probability and percentage points for margin, with a decimal point. They can only tighten
   Settings; workflow JSON continues to store normalized [0,1] values. Distribution concentration
   remains inherited from Settings.
3. Enable generic evaluation in Settings and grant the task's `decision` group. A missing grant
   appears as an unchecked authorization proposal; explicitly accepting it authorizes the draft's
   test without persisting the task.
4. Enter **Explicit sample text for this test**, then choose **Test decision**. The sample is
   mandatory, including when the source is a placeholder. Testing uses only that sample and the
   current question/options/acceptance, including pending field edits. It does not resolve outputs,
   invoke connectors, execute surrounding steps or save the task. Sample text and results are
   never serialized. Use **Cancel test** to cancel; closing or changing tools also cancels and
   suppresses late results.
5. Inspect status, candidate, every option score, top probability, margin, concentration, sanitized
   reasons, applied thresholds and gate/inference/total timings. Unavailable scores are displayed
   as unavailable. The display is a snapshot of the last explicit test; editing never reruns it.
   No fabricated model rationale is generated. Check status before routing on a candidate.
6. Choose **Apply**, then save the task through its normal editor. Neither action performs inference.

**Arguments / raw JSON** remains available and initially collapsed for this tool. Editing options
or acceptance JSON reloads the corresponding draft fields and discards pending option/acceptance
field edits. The latest explicit edit is authoritative. Malformed JSON is retained for correction
and cannot be silently replaced by partial fields. An unchanged edit preserves original optional
acceptance presence and values.

```json
{
  "step": "decision.evaluate",
  "id": "readiness",
  "args": {
    "source": "{{request.output}}",
    "question": "Does the request contain enough information to proceed?",
    "options": {
      "process": "Essential information is present.",
      "clarify": "Essential information is missing."
    },
    "acceptance": { "minTopProbability": 0.9, "minMargin": 0.3 }
  }
}
```

Source must be non-empty. Questions are literal text, at most 1000 characters. Supply 2–24
literal option identifiers matching `[A-Za-z][A-Za-z0-9_]{0,63}`, each with a non-empty description
of at most 400 characters. Duplicate identifiers, including case collisions, and unknown arguments
are errors. Only source accepts previous-output placeholders; external output cannot author the
question, options or thresholds. Upstream content protection remains unchanged.

The result has `schemaVersion: 1` and a status:

| Status | Meaning | Routing |
| --- | --- | --- |
| `accepted` | Unique top candidate meets every effective threshold | Route on choice after checking status |
| `uncertain` | Valid scores tie or fail a threshold | Handle explicitly; choice remains only a candidate |
| `unavailable` | Missing model, invalid response, timeout or oversized input | Handle the technical reason; all score fields and choice are null |

`probabilities`, `topProbability`, `margin`, `confidence` and acceptance thresholds use [0,1].
Margin is the top probability minus the runner-up. Optional confidence measures distribution
concentration, not calibrated correctness. Defaults are 0.80 for the top probability and 0.20 for
the margin; these are acceptance settings, not evidence of accuracy. Overrides can only tighten
defaults. Ties are uncertain even with zero thresholds. User cancellation propagates as cancellation.
Malformed authoring, disablement and missing task permission remain errors rather than successful
unavailable results. Reasons are sanitized codes and timings separate gate wait, inference and total
duration, including cleanup. Cold inference includes loading when the provider reports it.

The [disabled routing sample](samples/decision-readiness.json) first checks status and only then
choice, using unchanged `flow.decision` routing. Every route has an explicit handling branch.
Its final `ai.summarize` steps use the configured general LLM to present branch handling text;
they are illustrative downstream actions, not classification fallback. Replace those branches
with the desired authorized workflow actions. Importing the example does not install a model,
enable settings or schedule a production task.

## Canonical configuration and migration

`Decision` owns native provider settings. An explicit section is authoritative, including defaults
for omitted fields. Without it, native settings come from legacy `GmailDecision`; generic enablement
still defaults to false. Save writes `Decision` through the existing settings store and removes
shared native fields from the saved Gmail section. Gmail retains its enablement, collection bounds,
batch deadline, report policy, arbitration and lightweight fallback. Its `LlmOnly` mode does not
require Tev1. Both callers and embeddings share the existing execution gate and residency coordinator.

The executable template intentionally retains the legacy section until the first settings save:
adding canonical defaults to the lower-priority template would mask existing users' legacy native
overrides. See the [canonical example](decision-settings.example.json) for the complete native section.

| Setting | Default | Valid range / meaning |
| --- | --- | --- |
| `Enabled` | false | Independent generic workflow enablement |
| `DecisionProvider` | OllamaSystemOne | Only this provider |
| `DecisionEndpoint` | null | Inherit local Ollama; explicit override must be loopback HTTP(S) |
| `DecisionModel` | tev1:latest | Already installed; dedicated identity distinct from chat and embeddings |
| `KeepAliveSeconds` | 10 | 0–60 seconds |
| `UnloadAfterBatch` | true | Required; cleanup after each generic operation, only when no other owner remains |
| `DecisionColdTimeoutSeconds` | 120 | 1–300 seconds |
| `DecisionWarmTimeoutSeconds` | 15 | 1–120 seconds |
| `GateWaitTimeoutSeconds` | 30 | 1–300 seconds |
| `OperationTimeoutSeconds` | 180 | 1–3600 seconds; catalog, ownership, gate and inference share this deadline |
| `MaxInputTokens` | 2050 | 512–2050; full prompt budget including a fixed 400-token reserve |
| `MinTopProbabilityPercent` | 80 | 0–100; converted once into normalized acceptance units |
| `MinMarginPercentagePoints` | 20 | 0–100; converted once into normalized acceptance units |
| `MinConfidencePercent` | 0 | 0–100; zero disables the concentration check |
| `MinNoulCertaintyPercent` | 80 | 50–100; preserved for Boolean native callers |

The budget counts one token per UTF-8 byte for source plus serialized question/options, with 400
tokens reserved for the native template, scoring and output. This deliberately conservative bound
reuses the existing native budgeting assumption and may reject inputs that would fit a tokenizer.
Oversized content returns `InputTooLarge` before network access; it is never truncated or summarized.
Gmail message counts and mail deadlines do not become generic limits. Cleanup has an independent
five-second bound after success, failure or cancellation; short keep-alive is the safety net.

## Verification and remaining evaluation

Unit tests use synthetic provider responses, actual configuration monitors, the tool registry and
workflow engine. They do not require Ollama, Gmail credentials or a live mailbox. Run:

```powershell
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore
dotnet test OD.MyLocalAgent.sln -m:1 --no-restore --filter "Category!=Integration"
```

Provider contract checked on 2026-10-07 against [Ollama's Tev1 documentation](https://ollama.com/library/tev1):
`/v1/systemone`, short `keep_alive`, 2–24 recommended options and an approximately 2000-token
decision context. The installed CLI reports 0.40.0; it could not reach a running server during
implementation, so no fresh runtime/context measurement was obtained. The 2050-token ceiling
comes from the [existing local probe](Tev1-Local-Feasibility.md), not the download catalog's context.
No model experiment was launched. Phase C must establish usefulness, French/English quality and
cold/warm latency with reviewed held-out fixtures. Implementation tests do not establish model quality.

Phase B prepares [30 synthetic French/English pairs](evaluation/decision-readiness-pairs.v1.json),
with 20 development and 10 held-out pairs. Labels are proposed and require independent bilingual
review before model execution. The [evaluation protocol](evaluation/Decision-Evaluation-Protocol.md)
defines review, baselines, acceptance criteria, identities, repetitions, timing and reporting.
`tools/Prepare-DecisionEvaluation.ps1` validates the corpus offline and can generate 60 disabled,
single-step workflow inputs plus a hashed manifest. It never imports or runs tasks. The actual
comparative model executor and results remain phase C work.
