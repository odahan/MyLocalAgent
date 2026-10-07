# Stage 32C — Controlled decision evaluation pilot

The subsequent [Qwen 4B/9B comparison](Stage32C-Qwen-Comparison.en.md) adds two installed
general comparators without changing this initial pilot's evidence or model identities.

The executor and first controlled pilot are complete. Tev1 does **not** meet the frozen
appointment-routing criteria. Public human-annotated data is a useful additional test: the
adapted VitaminC pilot gives Tev1 85% reference-label agreement versus 80% for the configured
general LLM, but this small result does not establish a general advantage or justify deployment.

## Evidence and conditions

On 2026-10-07, the owner's approval froze the [appointment annotations and criteria](decision-readiness-approval.v1.json):
zero wrong accepted routes, at least 80% accepted coverage, native total p95 at most 180 seconds
when cold and 30 seconds when warm. This was owner review and acceptance of the proposed
annotations; a separate blind bilingual review was not performed or claimed. No annotation,
question, rule or production threshold was adjusted after observing the runs.

The application corpus contains 30 FR/EN pairs: 20 development and 10 held out. Each of its 60
inputs was evaluated three times by frozen rules, the common native service and one structured,
tool-free call to the configured LLM. The public pilot contains 40 English inputs in 11 complete
VitaminC families, each evaluated three times by the two models. There are **780 observations,
including 600 model calls**. Repetitions are correlated and do not increase independent sample
size. Every input produced a valid scored result; technical unavailability was zero. Choices and
acceptance statuses were identical across the three repetitions of each input/backend.

Runs used Ollama 0.40.0, .NET 10.0.12, an Intel Core i7-13700KF (24 logical processors), about
31.7 GiB physical memory and an NVIDIA RTX 4080 with 16,376 MiB VRAM. Installed models were:

| Model | Quantization | Recorded digest |
| --- | --- | --- |
| `tev1:latest`, 4.2B | Q8_0 | `9b5bb969e46c4b776826d6f2d401e22893205693f172653af6254897255025b8` |
| `gemma4:12b`, 11.9B | Q4_K_M | `4eb23ef187e2c5462566d6a1d3bbbc2f1346d0b4327cbb66d58fffbcc9b2b05c` |

The standalone [executor](../../tools/DecisionEvaluation/README.md) enables decisions only in its
own process. Native inference uses the real service, budget, execution gate, residency checks
and bounded cleanup. The general comparator uses equivalent source/question/options, temperature
zero, thinking disabled, an 8192-token context, a 512-token output bound and a ten-second lease.
Neither comparator receives reference labels. No application settings, task or production
threshold was saved; no model was downloaded and no live mailbox or integration test was used.

## Appointment results

The primary assessment is the held-out partition: **20 unique inputs in 10 bilingual pairs**,
with 60 observations per backend. Choice agreement includes uncertain native candidates.
Accepted coverage and wrong accepted rate divide by all attempts; accepted agreement divides
only by accepted results. Rules and the general LLM have no native confidence distribution:
their 100% coverage means a valid emitted choice, not passing a calibrated acceptance threshold.

| Backend | Choice agreement | Accepted coverage | Agreement among accepted | Wrong accepted observations | Uncertain |
| --- | ---: | ---: | ---: | ---: | ---: |
| Frozen rules | 90% (54/60) | 100% | 90% | 6/60 | 0 |
| Tev1, top ≥ 0.8, margin ≥ 0.2 | 60% (36/60) | 50% (30/60) | 80% (24/30) | 6/60 | 30 |
| Gemma4 | 60% (36/60) | 100% | 60% | 24/60 | 0 |

Tev1's six wrong accepted observations correspond to two distinct English inputs repeated three
times: withdrawn contact information (`p26-en`) and an unsupported repair request with missing
facts (`p29-en`). French held-out accepted coverage was 40% with no observed wrong accepted
route; English coverage was 60% with six. This small paired pilot does not support a broad
language-quality claim. Across all 30 pairs, Tev1 choices differed between languages on six
pairs and acceptance statuses on six; the LLM choices differed on three; rules on none.

Across development and held-out inputs together, choice agreement was 93.33% for rules, 65%
for Tev1 and 78.33% for Gemma4. The rule baseline has known lexical failures on unrelated or
adversarial content; its high score on this narrow synthetic policy does not establish general
language understanding. Those failures were retained rather than corrected after the test.

The [frozen assessment](results/readiness-v1.criteria-assessment.json) fails both quality criteria:
coverage is below 80% and wrong accepted rate is above zero. Native latency meets both budgets.
Descriptive held-out threshold sweeps give 30% coverage with zero observed wrong accepted
routes at top ≥ 0.9, and 15% at top ≥ 0.95. These are post-run observations, **not selected or
validated replacements**; neither reaches the required coverage. Production thresholds remain
unchanged. Tev1 is not recommended for automatic routing of this appointment policy.

## Public data: VitaminC

[Ollama's announcement](https://ollama.com/blog/ollama-now-supports-jev-style-decision-models)
points to a suite of thirteen public subsets. The importer follows the
[Bespoke guide pinned at `dcfdbd9`](https://github.com/bespokelabsai/nimble/blob/dcfdbd9a64f0d869f658d7a72f1beaee32737773/docs/PUBLIC_BENCHMARKS.md).
VitaminC is a three-choice evidence/claim task with human annotations. Its pinned development
subset has 599 rows in 171 families. The importer verifies source SHA-256
`a3258bc959754c84bade150d3bf447fd9c91a529d93bb676f034fdafda5f26e5`, then chooses complete
families in manifest order without consulting labels, stopping within forty rows.
The [upstream manifest](results/vitaminc-v1.upstream-manifest.json) preserves source checksum,
subset identities and annotation metadata separately from our adapted fixture hash.

| Backend | Choice agreement | Accepted coverage | Agreement among accepted | Wrong accepted observations | Uncertain |
| --- | ---: | ---: | ---: | ---: | ---: |
| Tev1 | 85% (102/120; 34/40 unique) | 70% (84/120) | 85.71% (72/84) | 12/120; four unique inputs | 36 |
| Gemma4 | 80% (96/120; 32/40 unique) | 100% | 80% | 24/120; eight unique inputs | 0 |

Tev1 matches two more unique annotations than Gemma4. Eleven related families provide little
independent evidence; there is no public held-out split or frozen success criterion for this
task, so its assessment is descriptive. Appointment rules and criteria do not apply here.

The recipe's question and option descriptions are retained. To fit the application contract, the
state object becomes literal JSON source and `NOT ENOUGH INFO` is renamed `NOT_ENOUGH_INFO`.
The conservative application budget remains active without truncation. These adaptations,
the subset size and the Choice-only mode mean this is **not an exact reproduction of published
benchmark scores**. It cannot be compared directly with the full-suite aggregate. Boolean and
rubric subsets, other domains and the complete suite remain future experiments.

[VitaminC](https://huggingface.co/datasets/tals/vitaminc) is CC BY-SA 3.0. Local imported inputs
carry attribution and adaptation notes. Raw public inputs and per-input records remain in the
ignored `artifacts/decision-evaluation/` directory; the repository retains aggregate counts and
provenance. Public annotations are reference labels, not a guarantee of truth.

## Latency and residency

Total native time includes the common service's cleanup. Percentiles use nearest rank.
Cold/warm are observed residency conditions, not forced loading conditions.

| Task/backend/condition | Calls | Median total | p95 total |
| --- | ---: | ---: | ---: |
| Appointments / Tev1 / cold | 19 | 2.587 s | 4.442 s |
| Appointments / Tev1 / warm | 161 | 0.206 s | 0.253 s |
| Appointments / Gemma4 / resident before call | 180 | 0.227 s | 0.238 s |
| VitaminC / Tev1 / cold | 120 | 3.088 s | 3.390 s |
| VitaminC / Gemma4 / cold | 1 | 6.557 s | 6.557 s |
| VitaminC / Gemma4 / resident before call | 119 | 0.225 s | 0.336 s |

Gemma4 was already resident before the appointment run, but the first comparator call took
6.664 seconds while replacing its previous 16384-token context with 8192. The general cold flag
checks digest residency before each call and cannot prove that a resident model avoids reload.
There is no genuinely cold general sample in that run. Short leases and native cleanup cause
different observed loading patterns between runs; these tables do not isolate inference speed.

Ollama residency snapshots showed Tev1 at about 4.35 GiB VRAM and Gemma4 around 7.8 GiB,
sometimes resident together in the appointment run. These are observations, not peak memory
measurements. Native cleanup was requested for every operation but unloading is asynchronous;
many appointment calls still found Tev1 resident. The public native run found it cold each time.
The comparator changed Gemma4's cached context and lease, although persistent settings were not
changed. No concurrent chat or embedding workload was introduced or measured, so freedom from
interference is not established.

## Reproducibility and implementation verification

Preserved original [appointment manifest](results/readiness-v1.manifest.json) and
[public manifest](results/vitaminc-v1.manifest.json) record source hashes at execution, fixture
and sanitized configuration hashes, model digests, runtime, prompts, timestamps and residency
snapshots. Git revision is the Stage 32B base; the pilot executor was uncommitted at execution.
Later offline analysis corrected the public threshold-sweep partition without rerunning models;
the [appointment summary](results/readiness-v1.summary.json) and
[public summary](results/vitaminc-v1.summary.json) separately record analysis time and assembly
hash. Original run provenance was not retroactively replaced. Future runs also record execution
assembly hashes and the explicit cold-flag interpretation.

Build completed with zero warnings/errors. The full default suite with explicit `-m:1` passed
**1073 tests, zero failed, one expected probe skipped**. New deterministic cases cover metrics,
threshold ties, label isolation, single-call comparison, malformed output, cancellation,
criteria assessment, the importer/checksum/family selection and transport compatibility.
Model-quality judgments remain experiment results rather than unit-test assertions.

Phase C's first pilot is complete with an unfavorable appointment outcome and limited public
evidence. Optional integrated routing (D) and agent exposure (E) remain deferred. Further work
should freeze a new policy/corpus or broaden independently annotated public families before
another run; the held-out cases above must not become an unreported tuning set.
