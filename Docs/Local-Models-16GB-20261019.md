# Local model review — October 19, 2026

## Scope and observed environment

This is a source-based shortlist, not a local comparative benchmark. The inspected machine has an NVIDIA RTX 4080 with 16,376 MiB VRAM and Ollama 0.40.6. Installed models include Gemma 4 12B (8.0 GB), Granite 4.2 3B and EmbeddingGemma 300M. No candidate was downloaded or made the application default during this review.

The October 19 editorial failure is established by local telemetry: Gemma 4 12B, thinking medium, 1,833 input tokens, 14,550 generated tokens, 16,383 total against a 16,384-token context, finish reason `length`, zero final-text characters. This demonstrates a failure of the current configuration; it is not a comparative model-quality measurement.

## Candidates worth testing

Sizes below are the published Ollama download sizes, not measured GPU footprints. Fit is an engineering estimate that must be checked at the actual context length, with embedding models, display usage and concurrent applications included.

| Candidate | Published size | Proposed role and assessment |
|---|---:|---|
| Bonsai 2 27B | 8.3 GB | Most interesting larger-capacity experiment within the weight budget. Ternary weights make a 27B-class model plausible on this card. Validate the exact Ollama build, tool parsing, thinking control and French fidelity before considering migration. |
| Granite 4.2 8B | 5.3 GB | General assistant and structured workflows. IBM lists French among tested languages and supports non-thinking and low-effort modes. A priority candidate for predictable automation, not a proven winner over Gemma. |
| Ornith 1.5 9B | 6.6 GB | Priority candidate for longer tool-driven tasks. The developer reports substantial coding-agent gains over Qwen3.5 9B. These are publisher results with specific harnesses, not evidence of better translation or reliability in MyLocalAgent. |
| LFM2.5 8B-A1B | 5.2 GB | Fast tool use, extraction and routing. The official card identifies 8.3B total and 1.5B active parameters, includes French, and recommends retrieval for knowledge-heavy tasks. It is reasoning-tuned, so thinking overhead still needs measurement. |
| Ministral 3 14B | 9.1 GB | Useful non-reasoning-oriented comparison for French writing, structured output and function calls. It is less recent than the candidates above, but model age should not replace task-specific evaluation. |
| TranslateGemma 12B | 8.1 GB | Translation-only comparison for TextAid and research translation. A specialized model may be preferable to forcing one general agent to perform every task. Requires its translation-oriented input format; no TextAid settings were changed. |

Sources: [Bonsai 2 catalog](https://ollama.com/library/bonsai-2), [Granite 4.2 catalog](https://ollama.com/library/granite4.2), [IBM model card](https://huggingface.co/ibm-granite/granite-4.2-8b), [Ornith catalog](https://ollama.com/library/ornith-1.5), [Ornith model card](https://huggingface.co/ornith-ai/Ornith-1.5-9B), [LFM catalog](https://ollama.com/library/lfm2.5), [Liquid AI model card](https://huggingface.co/LiquidAI/LFM2.5-8B-A1B), [Ministral catalog](https://ollama.com/library/ministral-3), [TranslateGemma catalog](https://ollama.com/library/translategemma), [Google model card](https://huggingface.co/google/translategemma-12b-it).

Qwen3.8 27B is more recent, but its default Ollama artifact is 18 GB before runtime memory. It is not a comfortable full-GPU default for this card. Smaller GGUF variants may be testable, with quality and compatibility tradeoffs; no specific unverified quantization is recommended here. Likewise, an MoE's active parameter count reduces computation, not the amount of total model weights that must be resident. [Qwen catalog](https://ollama.com/library/qwen3.8)

## Hugging Face GGUF expands the choices, with runtime constraints

Ollama supports `ollama run hf.co/owner/repository:quantization` and explicit GGUF filenames. Pin the repository, filename, quantization and model digest in benchmark results. A mutable `latest` tag is insufficient for reproducibility. Check chat templates, tool-call parsing and separation of reasoning from final output. [Hugging Face documentation](https://huggingface.co/docs/hub/en/ollama)

Do not assume arbitrary GGUF files work just because Ollama can fetch them. Prism's Bonsai 2 card requires its llama.cpp fork for the published `PTQ1_0` and `PQ2_0` packings. However, the newer Ollama implementation provides a supported `Q2_0` route: upstream llama.cpp b11669, shipped in Ollama 0.40.5, can execute those Bonsai 2 files; Ollama PR #18967 adds the missing tensor/file-type recognition in its own GGUF reader and ships in 0.40.6. The installed 0.40.6 therefore includes the official integration; no custom Ollama patch is required for the library artifact. The PR reports Apple Silicon tests, not a test on this machine's Windows/CUDA stack. The older blanket warning about stock `Q2_0` support in Prism's card should be read against this newer integration. [Prism model card](https://huggingface.co/prism-ml/Ternary-Bonsai-2-27B-gguf), [Ollama PR #18967](https://github.com/ollama/ollama/pull/18967), [Ollama 0.40.6 release](https://github.com/ollama/ollama/releases/tag/v0.40.6).

## Evaluation and adoption decision

Keep Gemma 4 12B available as a baseline, with reasoning disabled for editorial drafting. Test Bonsai 2 27B and Granite 4.2 8B first for the general workflow, Ornith 1.5 9B for tool-driven work, and TranslateGemma 12B separately for translation. LFM is the speed comparison. This ordering is a recommendation inferred from the published capabilities and memory budget, not an observed ranking.

Use frozen inputs rather than live web results. Include the failed editorial reservation, three-subject drafting, sparse discoveries, strict JSON selection, preservation of source IDs, rejection of unsupported facts, and injected instructions inside source text. For translation include negation, conditional statements, API identifiers, Markdown, numbers, version strings, false friends and long technical paragraphs, in both English-to-French and French-to-English directions. Human review must assess omissions and semantic changes; automated JSON checks alone do not measure translation quality.

For each candidate, run repeated cases at 8K and 16K contexts, then test 32K only if measured headroom permits it. Record final-text availability, JSON validity, fact and identifier preservation, task success, tool-call correctness, latency, output/reasoning token counts, finish reason and GPU offload. Distinguish warm and cold calls. Test thinking off and the lowest supported effort rather than assuming one effort vocabulary works for every model.

A migration requires no empty or truncated responses in the acceptance corpus, no invented facts or changed translation meanings, and an acceptable repeated-run latency and memory footprint. A small corpus cannot establish universal reliability, but can eliminate unsuitable candidates before production. Preserve deterministic reservation, source validation and immutable export even with a stronger model: these are durable workflow guarantees, independent of model intelligence.
