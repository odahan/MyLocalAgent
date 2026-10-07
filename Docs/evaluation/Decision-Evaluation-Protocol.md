# Stage 32 pilot protocol (phase C executor and controlled pilot)

The first execution is complete; see the [English results](Stage32C-Results.en.md) and
[French results](Stage32C-Results.fr.md). Appointment criteria were not met. The adapted public
pilot is descriptive; production settings and thresholds were not changed.
The owner subsequently supplied Qwen3.5 4B and 9B for the same tests. Their additional runs keep
the corpus, annotations, general instructions and explicit options frozen, using process-only
model overrides and new output directories. Compare choice agreement separately from native
acceptance. Quantization, inherited sampling defaults and runtime variants must be recorded;
equal parameter counts alone do not establish a controlled specialization-only comparison.
If a model alias changes during execution, retain the evidence but exclude that run and record
a replacement. General identities are now resolved by the server-declared runner and checked
before and after each inference. See the bilingual `Stage32C-Qwen-Comparison` reports.

Experiment findings are recorded separately from this frozen protocol. The corpus contains 30 paired French/English cases
(60 inputs): 20 development pairs and 10 held-out pairs, fixed before tuning. All content is
synthetic; addresses use `example.test`. Labels and rationales are proposed author annotations.
Before execution, a bilingual reviewer must check semantic equivalence, policy interpretation and
labels. Record reviewer/date, disagreements and acceptable alternative labels
in a versioned review artifact; retain disputed cases instead of relabeling to match a model.
The first pilot records the owner's explicit annotation/criteria acceptance in
`decision-readiness-approval.v1.json`; no separate blind review is claimed.

## Reproducible offline preparation

From the repository root, prepare disabled workflow inputs with no model calls:

```powershell
./tools/Prepare-DecisionEvaluation.ps1 -OutputDirectory ./artifacts/decision-pilot-inputs-v1
```

The output directory must be empty. The manifest captures the fixture SHA-256, question, option
order, language, partition, reference label and source. Without `-OutputDirectory`, the script
prints this plan only. Generated tasks contain one `decision.evaluate` step, no other tools,
and remain disabled. Preparation does not import, schedule or run them. The runner for actual
comparative experiments and automated result aggregation is now available under
`tools/DecisionEvaluation`; this preparation script remains an offline fixture runner.
See that tool's README for `plan`, explicitly approved `run` and offline `analyze` commands.

Use the same English policy and option descriptions for both languages initially, isolating
source-language differences. Translated policy experiments are separate conditions. Required
facts are one supported service, a calendar date, time and contact address. `review` takes
priority over `clarify` for conflicting/unsupported/unrelated requests. A semantic `review`
label is separate from the evaluator's `uncertain` and `unavailable` statuses.

## Freeze before calling models

1. Complete and version the label review. Seal held-out inputs and references from the tuning
   process; a frozen partition in a public pilot file is not a claim of blind evaluation.
2. Record a use-case decision: maximum wrong accepted route rate, minimum coverage, median/p95
   latency budget and acceptable resource interference. The owner must agree these criteria
   before runs; no universal numbers are inferred from the default 80%/20 pp settings.
3. Record Git revision, corpus/config hashes, Ollama version, hardware, memory/VRAM, endpoints,
   configured aliases and resolved model digests, full policy/options, thresholds and input bounds.
   Do not copy production secrets or application secret stores into experiment artifacts.
4. Explicitly authorize phase C model execution. Preparation and deterministic tests do not
   authorize live-mail collection, downloads, integration tests or production task changes.

## Execution conditions

The phase C executor uses `IDecisionEvaluationService` so native scoring uses the
application's actual validation, budgeting, gate, identity checks and cleanup. Run sequentially,
with Ctrl+C cancellation, bounded calls and checkpointed artifacts. Use fixed case order or
record a shuffle seed; keep each language pair in the same partition. At least three repetitions
per input help characterize variation; record every failure and partial run.

Compare native Tev1 with exactly one structured, tool-free call to the configured local general
LLM using the same facts, policy and options. Resolve and record the general model's identity.
Do not manufacture native-style probabilities for the LLM: compare its choice accuracy and
latency separately, recording any explicitly designed abstention policy. Implement and freeze
a deterministic baseline using explicit service/date/time/contact matching and contradiction
rules on development cases; record its language coverage and limitations. No retrieval,
translation, extra reasoning calls or hidden arbitration is allowed in this initial comparison.

The primary native condition is the product's one-shot call including cleanup. Record provider
`cold`, gate wait, inference and total time; cold inference may include loading. Do not claim
a separately measured loading time when the provider does not expose it. Warm batches, if
explicitly authorized, are a separate experimental condition using bounded leases and peer
ownership protection, with cleanup after the batch. Do not alter product residency to improve
benchmark numbers. Record resident model identities and resource observations before/after,
and any observed impact on chat or embeddings. No production workload is introduced for this.

Each result record includes run/case/pair id, language, partition, repetition, expected label,
actual result, model digest, configuration hash and timings. Preserve sanitized technical reasons;
never convert technical unavailability into a semantic `review` prediction. Retain raw validated
probabilities for threshold analysis. Keep data records separate from labels and review notes.

## Analysis and decision

Tune policy and thresholds on development pairs only, then freeze a new version before the
held-out run. Report per-model choice accuracy and confusion matrix, abstention/unavailability,
native accepted coverage, accepted accuracy and wrong accepted route rate. Show top/margin
threshold sweeps as analysis, preserving ties and global concentration checks; do not silently
lower production thresholds. Separate technical unavailability from uncertainty in denominators.

Report French and English results, paired disagreements, category breakdowns and all counts.
Report cold versus warm medians and p95 with the sample size and percentile method; include total
one-shot cleanup cost. A pilot of ten held-out pairs has large statistical uncertainty: state it,
especially if zero wrong accepted routes are observed. Record repeated-run variation, resource
observations and unresolved label disagreement. Source length/complexity variations and translated
policy comparisons must be separately versioned, preserving labels and accounting for costs.

Recommend a use case only if held-out evidence meets the frozen criteria and demonstrates a
benefit over baselines. An inconclusive or unfavorable result is acceptable. Implementation
completion and synthetic editor tests provide no evidence of Tev1's classification quality.

## Public benchmark extension

Following the owner's request, evaluate public human-annotated data alongside the application
pilot. Ollama points to Bespoke Labs' suite of thirteen subsets; it includes five Choice, five
Boolean and three rubric subsets. The common application tool currently exposes Choice only.
The first public importer targets VitaminC evidence/claim verification with three options.

Pin the Bespoke recipe revision and source checksum, preserve annotations and complete contrastive
families, and choose families without looking at labels. The first pilot selects forty rows from
eleven families in the pinned manifest's order, with three repetitions. Its English-only results
are separate from the French/English appointment corpus and from the full published benchmark.
The recipe's original question/criterion wording is retained, but the application serializes the
state object as literal source and normalizes the option id `NOT ENOUGH INFO` to `NOT_ENOUGH_INFO`.
Record these adaptations and retain the application's conservative input budget without truncation.

Use public annotations as reference-label agreement, not verified truth. Do not apply appointment
success criteria or the appointment rule baseline to VitaminC. Do not translate public inputs and
claim the same benchmark. Other datasets, Boolean/rubric modes and exact full-suite replication
remain separate work. Never compare a small adapted subset to the published aggregate as if they
were the same test. Record upstream license/attribution; raw public inputs remain local, with only
aggregate findings and provenance in the application repository.

Sources: [Ollama announcement](https://ollama.com/blog/ollama-now-supports-jev-style-decision-models),
[pinned Bespoke public benchmark guide](https://github.com/bespokelabsai/nimble/blob/dcfdbd9a64f0d869f658d7a72f1beaee32737773/docs/PUBLIC_BENCHMARKS.md),
[VitaminC dataset and license](https://huggingface.co/datasets/tals/vitaminc).
