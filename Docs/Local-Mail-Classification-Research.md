# Local mail classification research

Research date: 2026-10-06. This is a source review, not a benchmark on the user's hardware. No runtime, model, or production configuration was installed or changed.

## Product objective

Produce a useful daily Gmail triage report quickly. Highlight urgent and important messages, distinguish reply requests from informational business exchanges, and separate advertising and routine noise. Preserve source identifiers and links. Classification must run locally; cloud fallback must never be implicit.

The target pipeline is deterministic collection and text preparation, isolated classification without tools, and deterministic report rendering. Generated summaries, extended reasoning, and model-generated report formatting are unnecessary for the initial classification-only version. Keep message size bounds, response validation, authorization, and safe rendering.

## Candidates

| Candidate | Local execution and mechanism | Integration and limitations |
| --- | --- | --- |
| Ollama decision API with Tev1 or Nimble | Ollama 0.35 adds `/v1/systemone`. Tev1 has 0.8B and 4B variants; Nimble is 9B. Returns typed choices and distributions. | Lowest additional runtime burden for this application. Tev1 is experimental, uses roughly 2,000-token contexts, and its authors have not fully tested languages other than English. French Gmail quality needs independent evaluation. Use the decision endpoint, not ordinary chat. |
| GLiClass Multilang Mini | Approximately 288M parameters; multilingual classification with scores rather than generated prose. The authors provide French examples and measurements. | Strong lightweight candidate. A Python local worker or an independently verified export/runtime path is needed. The default Ollaya `gliclass` tag identifies a different model; do not assume it is Multilang Mini. |
| Laya Multilingual through Ollaya | Laya uses a 322M encoder and decision head without autoregressive generation. Ollaya documents Windows x64, CPU/ONNX, and GPU support. | Attractive small local runtime. The model card documents overconfidence and weak zero-shot performance on some typed-decision tasks. Default input limit is 1,024 tokens; larger limits require explicit configuration and accuracy testing. |
| Kev | Qwen-based LoRA adapters and pointer heads; one forward pass without text generation. Open checkpoints include 0.8B, 4B, and larger variants. | Useful semantic decision alternative if smaller classifiers fail. Extra serving and memory burden; French triage behavior is not established by the reviewed sources. |
| AnyJev | Extracts decisions from an open LLM's label logits or an intermediate hidden-state head. | A real readout adapter, not merely a JSON prompt. L0 can require one prefill per option rotation; L1 needs labeled calibration data; L2 needs a task/model-specific fitted head and hidden-state access. More complex than the first two routes. |

GLiNER2/2.5 multilingual is another local encoder-based option if the product later needs structured extraction such as explicitly mentioned dates. It is not necessary to add extraction to the first triage-only experiment.

## What an adapter can save

An ordinary JSON wrapper still performs autoregressive generation. Turning off thinking and limiting output can make that baseline substantially cheaper, but the wrapper itself does not remove generation.

A restricted label-logit readout avoids long completion decoding, but still pays for encoding the input through its backbone. Position-bias correction can multiply that cost. A trained classification head can also avoid decoding and, depending on implementation, stop before the final backbone layers. A compact dedicated encoder often has the lowest compute burden, but quality must be measured separately.

## Recommended first evaluation

Compare Tev1 4B through Ollama's existing local service, GLiClass Multilang Mini, and a short tool-free local LLM baseline with thinking disabled. Include Tev1 0.8B only to measure its size/quality tradeoff. Consider Nimble, Kev, or a task-adapted Laya if these candidates cannot distinguish the required categories adequately.

Use the same representative French/English messages and labels for every candidate. Include real urgent cases, prospect replies, informational client updates, newsletters with personal salutations, promotional urgency, ambiguous messages, and long reply chains. Do not upload evaluation messages to hosted playgrounds.

Measure:

- End-to-end time for the full batch, cold startup separately from warm inference, per-message median and p95 latency, and peak RAM/VRAM.
- Recall for urgent/important/reply-required messages, especially messages incorrectly hidden in noise; also report per-category precision and recall.
- Valid decision rate, technical failure rate, truncated-input rate, and uncertain decision rate.
- Category stability when options are reordered, and confidence behavior on held-out examples.

Keep a provisional category when classification is uncertain instead of replacing most of the report with human-review fallbacks. Technical failures and ambiguous classifications are distinct outcomes. Confidence scores from different model families are not interchangeable.

## Integration boundary

Introduce a dedicated classification service and a declarative workflow step, separate from the agent/chat interfaces. The request contains bounded text and label descriptions. The result contains a stable category, optional scores, the effective model/version, duration, and an explicit technical status. The application keeps identifiers, sender, subject, dates, and Gmail links; the model does not regenerate them.

For Ollama decision models, call `/v1/systemone` directly through the existing HTTP client infrastructure rather than assuming the current Ollama chat adapter supports it. For encoder models, load a persistent local worker once per run or keep it resident to avoid loading the checkpoint per message. Preserve an explicit local-provider policy and deterministic report generation.

## Local observation

The Ollama CLI available on PATH reports client version **0.35.1**, meeting the documented client generation for decision support. The CLI could not verify a running server from this execution environment. Server support and successful decision inference remain unverified. Windows hardware inventory was also inaccessible here, so no CPU/GPU performance claim is made.

## Primary sources

- [Ollama decision-model announcement](https://ollama.com/blog/ollama-now-supports-jev-style-decision-models)
- [Tev1 model documentation and limitations](https://ollama.com/library/tev1)
- [Nimble model](https://ollama.com/library/nimble)
- [GLiClass repository](https://github.com/Knowledgator/GLiClass)
- [GLiClass Multilang Mini model card](https://huggingface.co/knowledgator/gliclass-multilang-mini)
- [Ollaya runtime](https://github.com/ollaya-dev/ollaya)
- [Laya Multilingual model card](https://huggingface.co/convaiinnovations/laya-multilingual)
- [Kev repository](https://github.com/jaredpalmer/kev)
- [AnyJev repository and readout levels](https://github.com/nokia-applied-research/AnyJev)
- [GLiNER2 repository](https://github.com/fastino-ai/GLiNER2)
