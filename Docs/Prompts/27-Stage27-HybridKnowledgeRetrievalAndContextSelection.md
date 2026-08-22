# STAGE 27 — Hybrid Knowledge Retrieval and Context Selection

> This stage is the retrieval-quality continuation of Stage 26. It adds lexical retrieval through SQLite FTS5, deterministic hybrid rank fusion, diversity-aware selection, and bounded small-to-big context expansion to the embedded Knowledge library. It does not add an LLM reranker, a new embedding model, a vector server, automatic RAG injection, or a new authorization path.
>
> The result must improve exact-term retrieval and reduce redundant model context while preserving Stage 26's workspace ownership, tag-scoped deny-by-default authorization, active-revision atomicity, portable exact-cosine baseline, and ordinary `knowledge.search` tool contract.

## 0. Read first (mandatory)

Read these files completely before changing code:

- `Docs/Prompts/00-Shared-Specification.md`;
- `Docs/Prompts/18D-Stage18D-DeclarativeDynamicTooling.md`;
- `Docs/Prompts/26-Stage26-EmbeddedKnowledgeAndDeclarativePromptTools.md`;
- `Docs/ARCHITECTURE-LEDGER.md`;
- `README.md` Knowledge and build/test sections;
- current implementations of `KnowledgeSearchService`, `SqliteKnowledgeStore`, `KnowledgeOptions`, `KnowledgeChunker`, `KnowledgeTool`, `KnowledgeSearchResponse`, `KnowledgeSearchResult`, `KnowledgeLibraryService`, `WorkspacePaths`, `PathSandbox`, and all Knowledge tests.

Use these primary technical references when implementing FTS5:

- SQLite FTS5 documentation: <https://www.sqlite.org/fts5.html>;
- SQLite FTS5 auxiliary functions, including `bm25()`: <https://www.sqlite.org/fts5.html#_5_auxiliary_functions>;
- SQLite query syntax and tokenizers: <https://www.sqlite.org/fts5.html#_3_full_text_query_syntax>.

The repository state and architecture ledger are authoritative if an older prompt differs. Do not copy obsolete application-data paths or weaken any Stage 26 invariant.

## 1. Mission

Improve the existing tool-driven Knowledge retrieval pipeline in this order:

1. retrieve an authorized lexical candidate list through SQLite FTS5 and an authorized semantic candidate list through the existing exact-cosine path;
2. combine the two ordered lists with deterministic Reciprocal Rank Fusion (RRF), without comparing or normalizing their incomparable raw scores;
3. choose a relevant but non-repetitive result set using Maximal Marginal Relevance (MMR);
4. expand only selected anchor chunks into a bounded local context when adjacent chunks from the same section/revision make the answer more intelligible.

The feature remains inside the existing `knowledge.search` service and ordinary declarative tool. Tasks, prompt steps, and Direct Chat keep exactly the same group/tag authorization and retrieve only when the model elects to invoke the tool.

## 2. Locked product decisions

| # | Decision |
|---|---|
| S27-D1 | Hybrid retrieval is an internal enhancement of `knowledge.search`; its public alias, input JSON, task/workflow contracts, tool group, and authorization lattice remain unchanged. |
| S27-D2 | Every lexical and semantic candidate query is tag-prefiltered before ranking. A global FTS/vector search followed by post-filtering is forbidden. |
| S27-D3 | FTS5 is embedded in `knowledge.db`; do not add a search server, listening port, external index, NuGet search engine, Docker service, or native extension. |
| S27-D4 | FTS indexes only active revision content owned by chunks: heading and chunk text. Document name, description, original filename, canonical filename, document tags, paths, vectors, and administrative metadata are not indexed. |
| S27-D5 | The lexical query is derived deterministically from the caller's existing `query` string. Stage 27 does not call an LLM to rewrite, translate, expand, decompose, or generate queries. |
| S27-D6 | User/model text is never executed as raw FTS5 query syntax. The application creates a safe literal token query; malformed or punctuation-only text produces an empty lexical candidate list, not a SQL/FTS failure. |
| S27-D7 | Vector cosine similarity and FTS5 BM25 score are never arithmetically combined. Candidate lists are fused by rank using RRF with a validated global constant. |
| S27-D8 | Hybrid fusion uses candidates that meet the existing semantic minimum-similarity rule on the semantic side. A lexical-only hit may participate in fusion even without a vector score. |
| S27-D9 | MMR runs after hybrid fusion on a bounded authorized pool and uses stored normalized vectors only to measure redundancy. It never calls the embedding provider a second time. |
| S27-D10 | MMR diversity applies across every selected chunk, including different documents. It replaces Stage 26's adjacent-overlap check as the primary selection rule, but exact duplicate/overlap safety remains a deterministic last guard. |
| S27-D11 | Small-to-big expansion occurs only after anchor selection. It may add contiguous chunks of the same active revision and logical section; it never retrieves an unrelated chunk merely to fill a budget. |
| S27-D12 | Expansion never crosses a heading boundary, a page boundary when both chunks have page metadata, a revision boundary, or a document boundary. Missing headings are one implicit section only within the same revision. |
| S27-D13 | The complete text actually returned, including expanded neighbors, counts against the result token budget and final JSON character cap. An expansion that does not fit is omitted while its anchor remains. |
| S27-D14 | Stage 27 preserves meaningful excerpts and citations. It does not summarize, rewrite, merge unrelated sources, truncate a selected span solely to fill budget, or use an LLM contextual compressor. |
| S27-D15 | Existing default result count, minimum similarity, output status values, error behavior, untrusted-content boundary, and privacy/logging rules remain compatible. New settings have bounded global defaults. |
| S27-D16 | A library migration builds/rebuilds its FTS index transactionally from active chunks. Migration failure leaves the prior schema usable; no document content, vectors, revisions, or files are discarded. |
| S27-D17 | Replacement, deletion, per-document reindex, full-library reindex, startup recovery, and concurrent UI/Runner reads keep FTS consistent with `documents.active_revision_id`. No stale, pending, retired, or partial revision is lexically searchable. |
| S27-D18 | The user-facing score remains the existing cosine `Similarity` only when the result originated from semantic retrieval. Stage 27 must not expose BM25, RRF, MMR, candidate-pool ranks, vectors, or internal retrieval diagnostics to models or normal logs. |
| S27-D19 | No reranker, cross-encoder, ONNX runtime, Ollama reranker model, query cache, HyDE, multi-query retrieval, parent-document summaries, graph retrieval, ANN vector index, or automatic pre-retrieval context injection is introduced. |
| S27-D20 | Retrieval-quality claims require a committed deterministic evaluation fixture and report. Do not claim improvement from a successful build or anecdotal UI test alone. |

## 3. Terminology and retrieval pipeline

Use these terms consistently:

- **authorized corpus**: active chunks whose documents match at least one effective tag, exactly as Stage 26 defines;
- **semantic candidate**: an authorized chunk ranked by exact cosine similarity and meeting the configured minimum similarity;
- **lexical candidate**: an authorized chunk matched by the safely constructed FTS5 query and ranked by FTS5 BM25;
- **candidate pool**: the deduplicated bounded union of both candidate lists;
- **fusion rank**: a candidate's rank after RRF; it is internal only;
- **anchor**: a candidate selected by MMR before contextual expansion;
- **expanded context**: the anchor plus permitted contiguous chunks from its active revision;
- **result unit**: one output result, anchored at the selected chunk, whose text may comprise a contiguous expanded span and whose citation identifies its source.

The normative order is:

```text
owning tags ∩ optional call tags
  → validate effective tags before embedding/database access
  → safe lexical query + one query embedding
  → tag-prefiltered FTS5 candidates + tag-prefiltered cosine candidates
  → deterministic RRF union
  → MMR anchors
  → duplicate/overlap guard
  → bounded same-section neighbor expansion
  → excerpt/citation serialization and final JSON cap
```

An empty lexical list, semantic list, fused pool, or final result list is valid and returns the existing successful zero-result response. An FTS query failure is an `index_unavailable` or `configuration_error` failure; it must not silently broaden scope or substitute an unbounded SQL search.

## 4. Persisted schema and migration

Advance the Knowledge schema from version 3 to version 4. Maintain WAL, foreign keys, finite busy timeout, and the Stage 26 no-downgrade rule.

### 4.1 FTS table

Create an application-owned FTS5 virtual table, for example:

```sql
CREATE VIRTUAL TABLE chunk_fts USING fts5(
    chunk_id UNINDEXED,
    revision_id UNINDEXED,
    document_id UNINDEXED,
    heading,
    text,
    tokenize = 'unicode61 remove_diacritics 2'
);
```

The exact names may follow repository conventions, but the following are mandatory:

- one row per searchable chunk, identified by stable chunk id;
- searchable heading and text only;
- unindexed identity columns sufficient to join back to the active revision/chunk row;
- `unicode61 remove_diacritics 2` or an equivalently documented Unicode-aware tokenizer so French diacritics do not create an avoidable exact-term failure;
- no external-content FTS table, trigger magic, or user-controlled SQL.

Use explicit application-owned insert/delete/rebuild operations inside the same transactions that activate, replace, retire, or delete revisions. Do not depend on SQLite triggers to infer active-revision semantics.

### 4.2 Migration and repair

The schema-3 → schema-4 migration must:

1. create the FTS table in a transaction;
2. populate it exclusively from chunks joined to their document's current active revision;
3. set `PRAGMA user_version=4` only after all rows are present;
4. roll back fully on failure;
5. be idempotent under the existing initialization gate.

At startup, validate that every FTS row refers to an active chunk and every active chunk has one FTS row. If a bounded consistency check fails after an interrupted legacy migration, rebuild the FTS table transactionally from active chunks and emit one sanitized operational diagnostic with counts only. Do not expose text, filenames, tags, or SQL in the diagnostic.

Normal import/replacement/reindex paths must never leave repair work for ordinary reads. Repair is recovery for an unexpected legacy/interrupted state, not the normal indexing strategy.

### 4.3 Store responsibilities

Keep SQL in `SqliteKnowledgeStore` or an application-owned store collaborator. Add narrow operations along these lines:

```csharp
Task<IReadOnlyList<KnowledgeLexicalCandidate>> SearchLexicalCandidatesAsync(
    IReadOnlyList<string> authorizedTags,
    KnowledgeLexicalQuery query,
    int maximumCandidates,
    CancellationToken cancellationToken);

Task<IReadOnlyList<KnowledgeCandidate>> GetCandidatesByChunkIdsAsync(
    IReadOnlyList<string> chunkIds,
    CancellationToken cancellationToken);

Task<IReadOnlyList<KnowledgeContextChunk>> GetContiguousContextAsync(
    string documentId,
    string revisionId,
    int anchorOrdinal,
    int maximumNeighborsEachSide,
    CancellationToken cancellationToken);
```

Exact names are not prescribed. Interfaces must make authorization inputs explicit and must not accept raw SQL, raw FTS syntax, document paths, unrestricted document ids, or mutable singleton scope. The lexical query executes an `EXISTS`/join tag predicate as part of the FTS query.

## 5. Safe lexical retrieval

### 5.1 Query construction

Create an internal immutable `KnowledgeLexicalQuery` from the existing semantic query. It must:

- normalize Unicode and whitespace consistently with existing text handling;
- extract bounded non-empty terms using a culture-invariant Unicode-aware tokenizer;
- remove no user terms merely because they are short, numeric, code-like, or French;
- cap term count and each term length with documented settings;
- quote/escape every term as an FTS5 literal token or phrase component;
- use deterministic OR semantics between extracted terms in V1, so one exact identifier can retrieve a document even when the rest of a natural-language question is absent;
- return `IsEmpty` when there is no safe term.

Do not pass query text directly to `MATCH`. Do not expose FTS operators (`AND`, `OR`, `NOT`, prefix `*`, column filters, `NEAR`, parentheses, quotes) as a hidden power feature to models. No stemming, synonyms, language detection, stop-word list, transliteration service, or spell correction is added in this stage.

### 5.2 Ranking and bounds

FTS5 returns at most `LexicalCandidatePoolSize` rows, ordered by ascending `bm25()` value (FTS5's better-match direction), then ordinal stable chunk id. The query joins the normal document-tag predicate and active revision identity before applying the limit. It retrieves all data necessary for later fusion/MMR in one bounded query, including the stored vector through the ordinary chunk/vector join or a subsequent chunk-id-bounded store read.

No lexical score threshold is used in V1. The bounded RRF/MMR/context stages are the relevance controls; semantic minimum similarity remains in force on the semantic leg.

## 6. Semantic candidates and rank fusion

### 6.1 Semantic retrieval

Retain the Stage 26 exact-cosine implementation as the correctness baseline:

- generate exactly one normalized query embedding after authorization validation;
- retrieve only authorized active vectors;
- reject dimension mismatch/non-finite values;
- apply `MinimumSimilarity` before the semantic candidate pool limit;
- retain deterministic chunk-id tie-breaking and bounded top-K behavior.

Increase its internal capacity only to the configured `SemanticCandidatePoolSize`, not to an unbounded sort. The public `maxResults` limit does not directly control either candidate pool.

### 6.2 Reciprocal Rank Fusion

Deduplicate candidates by chunk id and calculate:

```text
RRF(candidate) = Σ 1 / (RrfRankConstant + rank_in_each_list)
```

Ranks are one-based. A candidate absent from one list contributes zero for that list. Use `double` for calculation. Sort fused candidates by descending RRF score, then by best available semantic similarity descending, then ordinal chunk id. Do not serialize the RRF score.

The default `RrfRankConstant` is 60. It may be configurable within validated bounded limits, but it is global library retrieval policy, not a task/workflow/model-controlled value.

## 7. MMR and controlled small-to-big context

### 7.1 Anchor selection with MMR

Select anchors greedily from the fused candidate pool. For each candidate, calculate:

```text
MMR(candidate) = λ × normalized_fusion_relevance
                 − (1 − λ) × max cosine(candidate.vector, selected_anchor.vector)
```

Rules:

- `λ` is `MmrLambda`, a validated global setting in [0, 1]; default 0.72;
- normalize fusion relevance deterministically from the fused pool so MMR terms have compatible scale; a singleton pool has relevance 1;
- the first anchor is the first fused candidate;
- each later step chooses highest MMR; exact ties use fused ordering;
- do not select a candidate whose source span materially duplicates an existing selected anchor;
- stop at the caller's bounded result limit or when no candidate remains;
- MMR does not itself consume context tokens; anchors and expansion do;
- do not force one result per document. A second chunk from the same document is valid when it is sufficiently relevant and non-redundant.

Use the normalized stored vectors already present in `KnowledgeCandidate`. Do not generate embeddings for expanded neighbors solely to calculate MMR.

### 7.2 Expansion rules

For each selected anchor, load immediate ordinal neighbors in deterministic nearest-first order: previous then next, then next previous/next pair, up to `MaximumContextNeighborsEachSide`. Add a neighbor only when all conditions hold:

1. it belongs to the same active document revision;
2. it is contiguous in ordinal sequence;
3. it is in the same section according to the heading rule;
4. when both chunks have page numbers, they have the same page number;
5. it does not materially duplicate text already selected in any result unit;
6. adding its complete text stays within `DefaultMaximumResultTokens`, the final result character cap, and any caller result-count limit.

An anchor is never dropped merely because a neighbor cannot fit. Expansion must stop at the first non-contiguous or disallowed neighbor on that side. Do not skip across a boundary to reach a later chunk.

The response keeps one result object per anchor. Its `Text` is the ordered concatenation of the anchor and accepted contextual neighbors, separated by a stable visible delimiter. Retain the anchor `ChunkId`, `ChunkOrdinal`, `Section`, and `PageNumber` for compatibility. Add only backward-compatible output fields if necessary, such as `ContextStartChunkOrdinal` and `ContextEndChunkOrdinal`; they describe the returned span and never reveal paths, vectors, or internal revision ids.

If concatenated context needs bounded display excerpting, preserve complete included chunks where possible. Never create a misleading partial neighbor merely to spend residual tokens. Existing sentence-boundary excerpt behavior may shorten a single oversized anchor only under the Stage 26 meaning-preservation rule.

### 7.3 Budget accounting

Use the token estimator on the exact final text selected for a result unit, not merely the original anchor's stored `EstimatedTokens`. Count citation/result JSON overhead conservatively before admitting an expansion. Preserve the final serialized `MaximumToolResultCharacters` loop as defense in depth, but selection should ordinarily fit without removing previously selected results.

Result ranks are assigned after MMR and expansion in final response order. The `Similarity` field continues to report the anchor cosine similarity; for lexical-only anchors it is `null`/omitted via a deliberate backward-compatible contract update, never a fabricated score. If nullable output would break the established tool schema, retain a documented sentinel only after explicit contract tests; prefer nullable data over a misleading numeric value.

## 8. Settings and index contract

Extend `KnowledgeOptions` and the Settings persistence/validation/UI with global, reloadable retrieval policy settings:

| Setting | Default | Valid range | Meaning |
|---|---:|---:|---|
| `SemanticCandidatePoolSize` | 40 | 10–200 | Authorized cosine candidates before fusion. |
| `LexicalCandidatePoolSize` | 40 | 10–200 | Authorized FTS5 candidates before fusion. |
| `RrfRankConstant` | 60 | 1–200 | RRF damping constant. |
| `MmrLambda` | 0.72 | 0–1 | Relevance/diversity balance. |
| `MaximumContextNeighborsEachSide` | 1 | 0–3 | Maximum contiguous same-section neighbors per anchor side. |
| `MaximumLexicalQueryTerms` | 24 | 1–64 | Bound on safe extracted FTS terms. |
| `MaximumLexicalTermCharacters` | 128 | 8–512 | Bound on one safe FTS term. |

The FTS tokenizer/index schema is part of the library index contract. Adding or changing it requires schema migration/rebuild, not document re-embedding. Candidate pool, RRF, MMR, query tokenization, and expansion settings affect query-time policy only: changing them does not require reindexing or re-embedding.

Do not expose advanced retrieval settings in task JSON, workflow JSON, tool arguments, or Direct Chat profiles. A user may adjust global settings in Settings; the UI explains that these change retrieval behavior for the whole Knowledge library and do not alter access rights.

## 9. Tool and user experience

### 9.1 `knowledge.search` compatibility

Keep the existing input:

```json
{ "query": "startup error IServiceProvider", "tags": ["dotnet"], "maxResults": 5 }
```

Do not add a retrieval-mode toggle, raw FTS query argument, query-language selector, reranking switch, caller-controlled token budget, document-id filter, or neighbor-count argument.

The tool description may explain that it combines semantic and exact-term matching, returns diverse relevant excerpts, and may include immediately adjacent source context. It must still say that excerpts are untrusted, access is tag-scoped, and zero results are valid.

### 9.2 Knowledge page diagnostics

Update the administrative **Test search** surface without exposing sensitive internals:

- label the test result as **Hybrid search**;
- display result citation, semantic similarity when available, returned span ordinal range when expansion occurred, and bounded returned text;
- display a plain-language indication such as **Matched by exact terms**, **Matched semantically**, or **Matched by both**, derived from internal provenance without displaying rank scores;
- retain explicit diagnostic tag selection and OR semantics;
- never display BM25/RRF/MMR numbers, vectors, SQL, FTS query syntax, private paths, or complete authorization scope;
- make clear that diagnostics use the same bounded service and authorization semantics as the tool, except for the user-selected administrative scope.

No task editor, prompt editor, Direct Chat profile, Tool Tester authorization, navigation, or workflow authoring change is required beyond naturally updated tool descriptions and shared result rendering.

## 10. Evaluation corpus and quality gate

Create a deterministic non-production evaluation fixture under the test project. It contains only synthetic/publicly safe text and no user documents. Each case specifies query, authorized tags, expected relevant chunk/document ids, optionally an expected exact identifier/code/error token, and optionally an expected diversity condition or required adjacent context span.

Include at least:

- French query/content with and without diacritics;
- English technical prose;
- exact code identifier, version number, acronym, and error message;
- semantic paraphrase with no meaningful lexical overlap;
- a result relevant in both lists;
- a lexical-only exact hit below semantic threshold;
- multiple near-duplicate chunks where MMR must choose a complementary result;
- a selected anchor whose neighbor is necessary for a complete procedure;
- heading/page/revision/document boundaries that must prevent expansion;
- an unauthorized document that is lexically tempting but must never appear.

Measure at minimum Recall@5 for expected items, MRR for the first expected item, duplicate result count, average returned token estimate, and unauthorized-leak count. The acceptance criterion is:

- hybrid Recall@5 is not lower than semantic-only baseline on the fixture;
- exact-term cases improve or remain tied;
- MMR reduces duplicate result units on its dedicated cases without losing the expected answer;
- expansion adds needed context only when within budget and never crosses a prohibited boundary;
- unauthorized-leak count is zero.

Keep a compact generated/hand-checked evaluation report in `Docs/` that contains fixture ids, aggregate metrics, configuration, and pass/fail status—not corpus text, user document text, vectors, or production query logs.

## 11. Security, privacy, concurrency, and failure behavior

1. Validate requested tags before lexical query construction, query embedding, FTS5, or vector access. An unauthorized call performs none of these operations.
2. The FTS table remains inside the application-owned Knowledge database and is denied to managed filesystem/SQLite tools by the existing PathSandbox boundary.
3. FTS query construction is deterministic data handling, never a model instruction channel or an execution language exposed to users/models.
4. Query, candidate text, FTS match data, vector values, raw scores, RRF/MMR values, and expanded text remain absent from normal logs/incidents. Existing explicitly enabled debug tracing retains its documented privacy behavior.
5. Tag prefiltering applies equally to FTS and cosine retrieval; hybrid fusion cannot create a cross-scope side channel by observing rank, count, source, or score differences.
6. FTS writes occur in the same SQLite transaction as revision activation/retirement. Readers see the old complete index or new complete index, never a half-written hybrid corpus.
7. Search cancellation propagates to FTS and vector reads. Cancellation/error leaves no mutation in the read path.
8. A missing/corrupt FTS table is a clear bounded index error until startup recovery succeeds. Do not silently fall back to unrestricted `LIKE` scans or global vector retrieval.
9. No full-text search snippets supplied by SQLite are returned directly: response text continues to come from authorized stored chunks and bounded context expansion.

## 12. Tests

Add deterministic non-integration coverage for at least the following.

### 12.1 Migration and store correctness

- fresh schema 4 creation, schema-3 migration, idempotent initialization, newer-schema rejection, rollback on forced migration failure, WAL, foreign keys, and busy timeout;
- FTS contains exactly active revision chunks after import, replacement, deletion, reindex, full-library reindex, cancellation, and recovery;
- inactive/retired/pending revision chunks are absent from FTS and lexical candidates;
- active FTS/chunk consistency repair is bounded and does not alter source files/vectors;
- tokenization handles French diacritics and code-like identifiers as documented;
- literal FTS escaping rejects/neutralizes operators, quotes, wildcard, parentheses, column filters, null/control characters, and pathological length without SQL errors.

### 12.2 Retrieval and authorization

- semantic-only, lexical-only, and shared candidate fusion known-order tests;
- RRF formula, one-based ranks, deterministic ties, pool bounds, and no raw-score arithmetic;
- semantic threshold still excludes low-similarity semantic candidates;
- lexical candidate search applies active revision and authorized-tag filters before limiting;
- unauthorized tags perform no embedding, FTS, or vector database call;
- empty safe lexical query retains a correct semantic search;
- empty semantic list retains valid lexical retrieval;
- empty lists return the existing zero-result success response;
- MMR first selection, relevance/diversity trade-off, tie breaking, cross-document behavior, duplicate-span guard, and no extra embedding request;
- result rank, nullable semantic similarity/provenance compatibility, citation, token budget, output cap, and no internal score/vector/path leakage.

### 12.3 Context expansion

- previous/next deterministic order and maximum neighbors per side;
- same-revision/contiguity/heading/page/document boundary enforcement;
- no skipped chunk across a rejected boundary;
- anchor preserved when neighbor fails budget;
- exact final returned-text token accounting, complete-span citation/range, duplicate suppression, oversized anchor behavior, and output character cap;
- concurrent Reader/Writer tests prove atomic FTS revision visibility for UI and Runner-compatible connections.

### 12.4 Settings, UI, and evaluation

- settings validation/defaults/load-save/reload and no reindex requirement for query-time policy changes;
- schema/index status accurately distinguishes FTS migration/rebuild from embedding contract mismatch;
- Knowledge test-search diagnostics show safe provenance/range text, accessibility names, light/dark rendering, wrapping, keyboard navigation, and no raw scoring/SQL exposure;
- fixture evaluation computes all required metrics and passes the quality gate;
- normal test runs make no Ollama, OpenAI, or internet call. Any real-provider test remains explicitly `Integration`.

## 13. Documentation and architecture ledger

Update at implementation time:

- `README.md` Knowledge section: describe hybrid exact-term/semantic retrieval, diversified bounded context, unchanged tag authorization, and absence of automatic injection/reranker;
- `Docs/ARCHITECTURE-LEDGER.md`: schema 4, FTS5 tokenizer/table ownership, safe literal query policy, RRF/MMR/default settings, expansion rules, evaluation result, and public contract changes;
- `Docs/WhatsWhere.md`: FTS responsibility within `knowledge.db`;
- English/French user guides: plain-language hybrid retrieval, global settings, diagnostics, troubleshooting, and what exact-term matches do not imply;
- `Docs/Prompts/README.md`: Stage 27 entry and dependency on Stage 26;
- example task JSON only if a wording/example update helps: public `knowledge.search` JSON itself stays unchanged.

## 14. Explicit non-goals

Do not add in Stage 27:

- LLM/cross-encoder reranking, Ollama reranker models, ONNX Runtime, CUDA/provider-specific code, or a third resident model;
- model query rewriting, translation, expansion, decomposition, HyDE, multi-query, self-RAG, or contextual LLM compression;
- raw FTS5 syntax, boolean search UI, fuzzy matching, synonym dictionaries, stemming service, spell correction, language detection, or user-editable query DSL;
- automatic Knowledge context injection into tasks, prompt steps, or Direct Chat;
- changes to tags/authorization, wildcard access, document-id bypasses, new task/workflow scopes, or per-task retrieval policies;
- parent-document summaries, RAPTOR, graph RAG, metadata embedding, new document formats, OCR, web ingestion, folder synchronization, caches, or source-code-specific parsing;
- vector ANN/HNSW/sqlite-vec acceleration, external search/index services, Docker, or listening ports;
- changing the embedding provider/model, chunking algorithm, embeddings, or document-revision model merely to implement query-time retrieval policy.

## 15. Recommended implementation order

1. Add failing schema-migration, authorization, FTS-literal query, hybrid-ranking, MMR, and expansion tests with synthetic fixtures.
2. Add schema 4, transactional active-chunk FTS maintenance, migration, and recovery consistency checks. Prove import/replacement/reindex/delete atomicity before altering search behavior.
3. Add safe lexical query construction and tag-prefiltered bounded FTS retrieval.
4. Refactor the existing search service into bounded semantic and lexical candidate stages; retain semantic-only behavior behind focused tests.
5. Add deterministic RRF and MMR selection with exact duplicate safety.
6. Add same-section contiguous context expansion and exact final-text budget accounting.
7. Add global settings validation/persistence and Knowledge diagnostic UI. Do not put retrieval policy controls in task/workflow/Direct Chat surfaces.
8. Add and run the synthetic evaluation corpus; record results before claiming improvement.
9. Update README, guides, WhatsWhere, architecture ledger, prompt-pack index, examples, and release verification.
10. Run formatting, Release build, normal non-integration tests, clean publication verification, and manual UI tests.

## 16. Stage gate

The implementation is complete only when all of the following pass:

```powershell
dotnet build OD.MyLocalAgent.sln --configuration Release -m:1
dotnet test OD.MyLocalAgent.sln --configuration Release --no-build --filter "Category!=Integration" -m:1
```

Also verify in a clean self-contained single-file `win-x64` UI and Runner publication that:

- a schema-3 Knowledge fixture upgrades to schema 4 without re-embedding;
- authorized exact identifiers, French accent variants, and semantic paraphrases retrieve expected fixture chunks;
- a document with an unauthorized but lexically stronger match never appears;
- a replacement switches both vector and FTS results atomically while UI and Runner read;
- MMR removes redundant chunks and small-to-big expansion returns only permitted neighbors within the configured context budget;
- `knowledge.search` input JSON, task workflow JSON, Direct Chat authorization behavior, and normal no-tool paths remain compatible;
- normal logs and model-visible responses contain no raw BM25/RRF/MMR values, vectors, paths, or documents beyond returned authorized excerpts.

## 17. Acceptance checklist

- [ ] Stage 27 is an internal `knowledge.search` enhancement; public tool/task/workflow access contracts remain stable.
- [ ] FTS5 uses only active chunk heading/text in `knowledge.db`, with no external search service.
- [ ] Both lexical and vector retrieval are tag-prefiltered before ranking/limits.
- [ ] Raw user/model text cannot invoke FTS5 syntax or SQL behavior.
- [ ] RRF combines ranks, not raw BM25/cosine values, with deterministic ties and bounded pools.
- [ ] MMR selects complementary authorized anchors without extra embedding calls.
- [ ] Small-to-big expansion is same-document, same-revision, contiguous, same-section, page-safe, and budgeted.
- [ ] Final returned text—not an obsolete whole-chunk estimate—drives context budget decisions.
- [ ] Schema migration, reindex, replacement, deletion, recovery, and concurrent reads keep FTS/vector active revisions atomic.
- [ ] Existing privacy, untrusted-data, PathSandbox, tags, errors, and zero-result semantics remain intact.
- [ ] Evaluation shows no retrieval regression and zero unauthorized leaks before quality claims are made.
- [ ] No reranker/model/runtime/server/non-goal capability was introduced.
- [ ] Documentation, architecture ledger, clean publication, Release build, and non-integration test gate are green.

## 18. Before finishing implementation

Search production code for all of the following and reconcile every result:

```text
CurrentSchemaVersion
knowledge.db
GetCandidatesAsync
KnowledgeSearchService
KnowledgeOptions
KnowledgeSearchResult
KnowledgeChunk
active_revision_id
document_revisions
chunks
vectors
MinimumSimilarity
DefaultMaximumResultTokens
MaximumToolResultCharacters
PathSandbox
knowledge.search
```

Then prove by inspection and tests that all FTS reads apply active-revision/tag constraints in the same SQL operation, that raw caller query text is never passed to `MATCH`, and that no expanded context can cross an authorization, revision, document, heading, page, or configured-token boundary.

