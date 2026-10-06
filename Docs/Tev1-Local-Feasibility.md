# Tev1 local feasibility probe

Date: 2026-10-06. Endpoint: `http://localhost:11434/v1/systemone`.
Model: installed `tev1:latest`, reported as Qwen35 4.2B Q8_0.

These are synthetic technical probes, not a quality benchmark on the user's Gmail messages. No application deployment, task edits, model downloads, embedding requests or Gemma4 inference were performed.

## Measured results

| Probe | Observed result |
| --- | --- |
| First decision request, model initially absent | 24.473 seconds, including loading and inference |
| Seven subsequent single-category requests | 110.9–123.0 milliseconds each |
| Separate batch of 37 sequential requests, three questions each | 20.915 seconds overall, including HTTP residency checks |
| First request of that batch after Tev1 had expired | 9.215 seconds |
| Remaining 36 requests in the batch | Median 312.9 ms; nearest-rank p95 351.1 ms; range 286.3–414.0 ms |
| Example three-question usage | 1,215 input tokens, 4 output tokens |

The 37-request probe cycles through the same small synthetic fixture set, adding an accented French message and a roughly 3,000-character thread. Repetition and input caching can affect performance. This is not a prediction of runtime on 37 distinct real messages.

## Response and classification

The endpoint returned structured `choice`, `probabilities`, `confidence`, `noul` and `score` fields directly. All 45 calls completed without HTTP or response parsing errors. A `noul` value is a score for true, not a Boolean; an application must define its decision rule. Priority was tested using a three-level rubric, producing a weighted score between 0 and 2.

Seven of eight initial category choices matched the informal expected categories: urgent client incident, prospect reply request, newsletter, promotional urgency, client information, embedded instruction attempt, and English reply request. The ambiguous message was assigned `information` rather than `uncertain`, with confidence 0.493. This illustrates why a named uncertain option alone does not establish an uncertainty policy. The confidence field is not a calibrated probability of correctness. One injection fixture does not establish general resistance.

## Memory lifecycle

Each initial request explicitly used `keep_alive: "30s"`; the batch used `keep_alive: "10s"`. Tev1 occupied approximately 4.35 GiB of VRAM with a reported context length of 2,050 tokens. Gemma4 remained resident throughout all recorded post-request checks. Short keep-alive successfully caused Tev1 to expire between the two probes.

The final residency check, saved in `Docs/tev1-final-residency.json`, confirmed that Tev1 had automatically unloaded after the batch and only Gemma4 remained resident.

For integration, use a short idle lifetime between consecutive mail classifications and explicitly unload only Tev1 at the end of the batch, including failure/cancellation cleanup. Do not unload after each individual message, because repeated loading would dominate inference time. A short lifetime is not a scheduler eviction-priority guarantee: release Tev1 before requesting embeddings when memory pressure is expected.

## Artifacts

- `tools/Test-Tev1Decision.ps1`: repeatable single-category synthetic probe, with a stop condition if Gemma4 disappears.
- `Docs/tev1-feasibility-results.json`: fixtures, question, full responses, timings and model residency snapshots.
- `Docs/tev1-batch-feasibility-results.json`: 37-request probe, three questions, full responses and residency snapshots.

## Sources

- [Tev1 endpoint and keep-alive documentation](https://ollama.com/library/tev1)
- [Ollama model lifecycle and memory scheduling](https://docs.ollama.com/faq)
