# Stage 32C local decision evaluation

This separate console tool compares the configured native decision model, one structured
tool-free call to the configured local general LLM, and a frozen lexical appointment baseline.
It uses the application service, conservative budget, global inference gate and native cleanup.
It neither loads secrets nor imports/schedules tasks, collects mail, downloads models or changes
application settings. Its process-local `Enabled` override authorizes only the explicit experiment.

The first pilot is documented in the [English results](../../Docs/evaluation/Stage32C-Results.en.md)
and [French results](../../Docs/evaluation/Stage32C-Results.fr.md), with preserved aggregate
provenance. Appointment criteria were not met; the adapted public results are descriptive.

## Build and prepare without inference

```powershell
dotnet build tools/DecisionEvaluation/DecisionEvaluation.csproj -m:1
dotnet tools/DecisionEvaluation/bin/Debug/net10.0/DecisionEvaluation.dll plan `
  --corpus Docs/evaluation/decision-readiness-pairs.v1.json `
  --output artifacts/decision-evaluation/plan-v1 --repetitions 3
```

`plan` validates fixtures and writes a frozen corpus/manifest without contacting Ollama. All
output directories must be empty; evidence is never overwritten. Configuration resolves the
explicit base `--config` file (default `appsettings.json`) plus the application's user settings,
but only sanitized decision, local model and inference configuration enter manifests. Cloud
providers and non-loopback endpoints are rejected. Native and general models must already be
installed. Missing models fail preflight rather than initiating a download.

## Execute an explicitly authorized experiment

```powershell
dotnet tools/DecisionEvaluation/bin/Debug/net10.0/DecisionEvaluation.dll run `
  --corpus Docs/evaluation/decision-readiness-pairs.v1.json `
  --approval Docs/evaluation/decision-readiness-approval.v1.json `
  --output artifacts/decision-evaluation/readiness-new-run --repetitions 3
```

The approval must match the corpus SHA-256 and record human label/execution approval and bounded
coverage/error/latency criteria. The shipped readiness approval records the owner's explicit
acceptance in this conversation, not a claim of blind review. For changed inputs obtain and
record an actual new review; do not just update the checksum. `--split development` or
`--split held-out` selects a partition; default `all` runs the frozen pilot without tuning.

Backends run sequentially, then repetitions and cases in fixed fixture order. Ctrl+C cancels
the in-flight operation. Each completed observation is flushed to `rows.jsonl`; manifests mark
completed/cancelled/failed runs and offline summaries retain partial evidence. There is no
automatic retry or resume: a new run uses a new directory. Reanalyze checkpoints offline:

```powershell
dotnet tools/DecisionEvaluation/bin/Debug/net10.0/DecisionEvaluation.dll analyze `
  --output artifacts/decision-evaluation/readiness-new-run
```

Outputs include model digests, configuration/source hashes, exact general instructions/options,
Ollama/runtime/OS information, model residency snapshots, per-result identifiers and timings,
confusion counts, coverage and accepted risk, paired disagreements, cold/warm observations,
category breakdowns, descriptive threshold sweeps, and a held-out criteria assessment.
Percentiles use nearest rank. Repetitions are correlated; the independent sample remains the
unique inputs/families. Threshold sweeps do not change application defaults or justify tuning
against held-out labels.

Use `--general-model qwen3.5:4B --backends llm` to add an installed general comparator without
rerunning native/rule backends. The override is confined to this process and never changes the
application's main model. `--backends` accepts unique comma-separated available backends;
the rule backend is available only for the appointment corpus. Each added comparator uses a
new output directory and the same frozen corpus approval. Manifests record model override,
digest, quantization metadata and declared model sampling defaults alongside explicit request
options. Unspecified sampling options remain inherited from each installed model; no claim of
identical quantization or all sampling defaults is made. Native threshold sweeps are absent in
LLM-only runs and native acceptance criteria are not applied to their emitted choices.
For general models, the runner declared by `/api/show` must match one unique digest in
`/api/tags`. This handles servers exposing multiple engines for the same tag without choosing
an arbitrary entry. The identity is checked before and after every general inference call;
a changed identity stops the run, whose partial comparison must be excluded. Keep an audit
sidecar and use a new output directory for a replacement run. Original evidence is retained.

```powershell
dotnet tools/DecisionEvaluation/bin/Debug/net10.0/DecisionEvaluation.dll run `
  --corpus Docs/evaluation/decision-readiness-pairs.v1.json `
  --approval Docs/evaluation/decision-readiness-approval.v1.json `
  --output artifacts/decision-evaluation/readiness-qwen-new-run `
  --general-model qwen3.5:4B --backends llm --repetitions 3
```

Native cold/warm is the application's provider observation before inference. For the general
LLM the flag means whether its digest was resident before the call; a resident model may still
reload for a changed context. General inference is temperature 0, thinking disabled, context
8192, output cap 512, keep-alive 10 seconds; it has no invented probabilities or uncertainty
threshold. `accepted` for rule/LLM comparisons means a valid emitted choice, not calibrated
confidence. The lexical baseline is used only for `readiness-pairs-v1`; its known limitations
are retained rather than tuned after evaluation. Warm native batches are not implemented.

## Public VitaminC pilot

The importer reconstructs complete families from the pinned Bespoke manifest, checking the
exact upstream dev-file SHA-256. It performs no download itself and executes no upstream code.
Download only the published dataset and manifest explicitly, then:

```powershell
dotnet tools/DecisionEvaluation/bin/Debug/net10.0/DecisionEvaluation.dll extract-vitamin `
  --source artifacts/decision-evaluation/public-raw/vitaminc.zip `
  --output artifacts/decision-evaluation/public-raw/dev.jsonl
dotnet tools/DecisionEvaluation/bin/Debug/net10.0/DecisionEvaluation.dll import-vitamin `
  --source artifacts/decision-evaluation/public-raw/dev.jsonl `
  --manifest artifacts/decision-evaluation/public-raw/vitaminc-dev-manifest.json `
  --output artifacts/decision-evaluation/vitaminc-new-inputs --limit 40
```

Sources: [VitaminC archive](https://raw.githubusercontent.com/TalSchuster/talschuster.github.io/master/static/vitaminc.zip),
[pinned manifest](https://raw.githubusercontent.com/bespokelabsai/nimble/dcfdbd9a64f0d869f658d7a72f1beaee32737773/docs/assets/public-benchmarks/subsets/vitaminc-dev-manifest.json),
[public benchmark guide](https://github.com/bespokelabsai/nimble/blob/dcfdbd9a64f0d869f658d7a72f1beaee32737773/docs/PUBLIC_BENCHMARKS.md).

The default pilot selects the first complete families within forty rows, without consulting
labels. It preserves source annotations and question/criterion wording, but serializes the state
object into the application's literal source and renames `NOT ENOUGH INFO` to `NOT_ENOUGH_INFO`.
These changes and the smaller selection prevent claiming an exact reproduction of published
scores. Inputs are checked by the actual simple-choice contract; the application's stricter byte
budget is retained, so any `InputTooLarge` result must be counted rather than truncated/excluded.
Only native/general backends apply; appointment rules and acceptance criteria do not apply to
this unrelated task. Human reference annotations require no model-generated relabeling.

VitaminC is CC BY-SA 3.0. Imported inputs carry attribution and adaptation notes; retain them
when sharing and comply with the upstream license. Raw/converted public inputs and per-input
experiment records remain local under the ignored `artifacts/decision-evaluation/` directory.
The repository keeps aggregate findings/provenance only. This first importer does not support
the suite's Boolean/rubric datasets or all thirteen benchmarks and does not download Jev, Nimble
or invoke a hosted model. A future exact benchmark reproduction should use the original modes,
questions, ids, tokenizer limits and full subset definitions as a separately recorded experiment.
