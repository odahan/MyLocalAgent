# Stage 32C — Qwen3.5 4B and 9B comparison

The additional experiments requested by the owner are complete. **Tev1 and Qwen3.5 4B tie on
the held-out appointment cases; Tev1 matches more annotations on the adapted VitaminC pilot.**
Qwen3.5 9B is below both on those two partitions in this fixed non-thinking protocol. These
small observations neither establish a universal ranking nor measure main-model chat quality.

## Frozen comparison

On 2026-10-07 the owner supplied both installed Qwen sizes for testing. Each evaluated the same
60 appointment inputs (30 FR/EN pairs, including 10 held-out pairs) and the same 40 public
VitaminC inputs (11 complete families), three times each. The extension adds 600 valid model
calls. All choices were valid; technical unavailability was zero. Every case/model had identical
choice and status across its three repetitions. Repetitions do not enlarge the independent
sample. The initial [Tev1/Gemma4 results](Stage32C-Results.en.md) are reused without rerunning or
replacing them, with separate manifests and dates for each comparator.

The sources, annotations, questions, option descriptions and general instruction were unchanged.
The general models use exactly one schema-constrained JSON call: temperature zero, thinking
disabled, context 8192, output cap 512 and a ten-second lease. No retrieved knowledge, tools,
additional reasoning call or invented confidence is provided. Inputs remain within the original
native budget. Native Tev1 uses its existing decision interface, context and acceptance policy.
General models emit a valid choice on every input; this is not confidence-based acceptance.
Application settings and the main model were never saved or changed. No download, mailbox,
integration test or deployment was performed by the executor.

| Installed comparator | Parameters reported | Quantization | Runner | Digest |
| --- | ---: | --- | --- | --- |
| `qwen3.5:4B` | 4.2B | Q4_K_M | llamacpp | `d8b0f5e9760cd1682034f292d7ef72ec46f432149be0df7574bf2d6e92e38c04` |
| `qwen3.5:9b` (valid replacement runs) | 9.7B | Q4_K_M | llamacpp | `c97eb11d70b1acdc88af01eef566c1fe4f7fbe93eb1afc06871132f293ff425a` |

Tev1 is 4.2B Q8_0; equal parameter count does not mean equal quantization or identical inference
interfaces. Both Qwen model declarations include presence penalty 1.5, top-k 20 and top-p 0.95;
only the explicit request options above are overridden. Inherited sampling defaults, templates
and quantization differ across models. This practical comparison does not isolate specialization
as the sole cause of any observed difference. Hardware/runtime match the initial pilot.

## Reference-label agreement

Counts below use unique inputs because all three repetitions agree. The underlying summaries
retain all observations and their confusion matrices, language breakdowns and timing.
Native choice agreement includes uncertain candidates.

| Backend | Appointments: all 60 inputs | Held-out: 20 inputs / 10 pairs | Public VitaminC: 40 inputs / 11 families |
| --- | ---: | ---: | ---: |
| Frozen appointment rules | 93.33% (56/60) | 90% (18/20) | Not applicable |
| Tev1 4B | 65% (39/60) | **60% (12/20)** | **85% (34/40)** |
| Qwen3.5 4B | 61.67% (37/60) | **60% (12/20)** | **77.5% (31/40)** |
| Qwen3.5 9B | 68.33% (41/60) | **50% (10/20)** | **67.5% (27/40)** |
| Gemma4 12B | 78.33% (47/60) | **60% (12/20)** | **80% (32/40)** |

Tev1 and Qwen4B do not make identical held-out errors: both are right on nine inputs, Tev1 alone
on three, Qwen4B alone on three and both wrong on five. Their choices differ on eight of twenty
inputs. The shared 60% therefore does not imply interchangeable predictions. On VitaminC, Tev1
matches three more unique annotations than Qwen4B and seven more than Qwen9B. Eleven families
and an adapted subset cannot establish a general or full-suite advantage.

Qwen4B held-out agreement is 60% in each language; Qwen9B is 40% French and 60% English. Across
all thirty bilingual pairs, Qwen4B choices differ between languages on eight pairs and Qwen9B
on three. This small sample does not establish general language performance. The complete
appointment corpus includes development cases and must not replace the held-out assessment.

Tev1's appointment acceptance outcome is unchanged: 50% held-out coverage, 80% agreement among
accepted results, and two distinct wrong accepted inputs. It fails the frozen criteria.
Qwen4B emits eight wrong held-out choices and Qwen9B ten, without an uncertainty gate. None of
these findings justifies unvalidated automatic appointment routing. The rule baseline remains
limited to its narrow synthetic policy.

## Latency and resources

Each valid Qwen run starts with one observed cold call, followed by resident calls. Cold timings
below are single observations, not a reliable cold p95 estimate. The cold flag observes digest
residency and does not prove a resident model will avoid context reload. General timing excludes
the additional catalog/identity checks; it includes the comparator operation and inference gate.

| Task / comparator | Resident calls | Resident median | Resident p95 | Single cold call |
| --- | ---: | ---: | ---: | ---: |
| Appointments / Qwen4B | 179 | 0.127 s | 0.159 s | 3.573 s |
| VitaminC / Qwen4B | 119 | 0.131 s | 0.150 s | 3.088 s |
| Appointments / Qwen9B | 179 | 0.218 s | 0.244 s | 4.320 s |
| VitaminC / Qwen9B | 119 | 0.226 s | 0.254 s | 4.343 s |

Ollama snapshots report about 3.11 GiB resident VRAM for Qwen4B and 5.34 GiB for Qwen9B, at
context 8192. These are resident observations rather than peak memory measurements. The initial
report separately records native cleanup cost and its different loading pattern; directly
attributing the timing difference to fine-tuning would be unjustified. No concurrent chat or
embedding workload was measured. The bounded lease changes caches, not persistent configuration.

## Invalidated passage and retained provenance

During the first Qwen9B appointment passage, the alias changed from digest `6488c96…` to a new
variant. Its final resident digest did not match the preflight identity, and the server then
exposed two same-name entries for `ggml` and `llamacpp`. The 180 observations from that passage
are excluded, including its misleading cold flags. The following public preflight failed on
duplicate names before making any inference call. The [original manifest](results/readiness-qwen9b-v1.invalidated-manifest.json)
and [exclusion audit](results/readiness-qwen9b-v1.audit.json) are retained, without replacing or
silently incorporating those results.

The executor now resolves the runner declared by `/api/show` against matching `/api/tags`
entries, requires one unique digest, and checks it before and after each general inference.
Ambiguity or a change aborts the comparison. The two valid Qwen9B `v2` runs used the same stable
`llamacpp` digest. Qwen4B's preflight and final resident identities agree. Its earlier runs
preceded this stronger per-call check; that limitation is retained rather than retroactively
claiming it was checked at every call.

Preserved machine-readable evidence:

- Qwen4B appointments: [manifest](results/readiness-qwen4b-v1.manifest.json), [summary](results/readiness-qwen4b-v1.summary.json).
- Qwen4B public: [manifest](results/vitaminc-qwen4b-v1.manifest.json), [summary](results/vitaminc-qwen4b-v1.summary.json).
- Qwen9B appointments: [manifest](results/readiness-qwen9b-v2.manifest.json), [summary](results/readiness-qwen9b-v2.summary.json).
- Qwen9B public: [manifest](results/vitaminc-qwen9b-v2.manifest.json), [summary](results/vitaminc-qwen9b-v2.summary.json).

Original approvals still freeze the unchanged labels and native criteria. LLM-only assessment
files mark native criteria inapplicable; they do not report native success. Raw public inputs
and per-input observations remain ignored locally with VitaminC attribution/license notes.
Assembly/source/configuration/fixture hashes, declared defaults and residency metadata accompany
the additional manifests. The [executor README](../../tools/DecisionEvaluation/README.md)
documents process-only overrides and backend selection.

Final build: zero warnings/errors. Default tests with explicit `-m:1`: **1078 passed, zero failed,
one expected probe skipped**, including unique/ambiguous runner identities and isolated model
selection. No model-quality assertion was added to unit tests.

The experiment supports keeping specialization as a useful laboratory hypothesis: Tev1 can
outperform its general base-family comparator on supplied-evidence classification. It shows no
held-out appointment choice gain here, and its probabilities still fail the required acceptance
policy. Main-model selection, thinking-enabled performance and equal-quantization comparisons
require separate evaluations with new reserved cases if instructions are tuned.
