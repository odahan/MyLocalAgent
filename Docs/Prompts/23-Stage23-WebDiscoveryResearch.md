# STAGE 23 — Web Discovery Tool

> This stage adds the `research.discover` workflow tool to the existing Stage 8
> web-search capability. It provides a bounded discovery loop with inline search
> configuration, no persistent profiles, and no storage.

## 0. Read first (mandatory)

- `Docs/Prompts/00-Shared-Specification.md` — full read, including addenda §12–§19.
  §19 is the normative contract for this stage.
- `Docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs, privacy boundaries,
  protocol channels, and concurrency constraints are binding.

## 1. Mission

Add a single workflow tool, `research.discover`, that takes search themes as inline
parameters, compiles them to queries, retrieves documents, deduplicates, applies a
freshness window, and returns structured candidates. The tool reuses the existing
`web.search` and `http.getText` infrastructure. No profiles, no database, no
Settings UI.

## 2. Locked product decisions

| # | Decision |
|---|---|
| S23-D1 | `research.discover` accepts all search parameters inline in the workflow step. There are no persisted profiles. |
| S23-D2 | `web.search` remains a generic single-query capability. It gains stable hit metadata but does not accept themes, retention settings, or a storage-space identifier. |
| S23-D3 | `research.discover` runs bounded code-owned loops. It is not an LLM agent loop. |
| S23-D4 | `freshnessWindow` controls authoritative local date filtering. `"sliding"` collects once with a seven-day provider hint, then evaluates the 2, 4, and 7-day windows locally in memory until `minimumDocuments` is reached. `"none"`: no date filter; query syntax is only a provider hint. |
| S23-D5 | Sliding mode never delivers old, future, or unknown-date documents. `dateStatus: Unknown` may be returned only in `none` mode. |
| S23-D6 | Query creation is deterministic C# from the inline theme terms. No model may invent queries during a run. |
| S23-D7 | Paid search is the constrained resource: a run executes at most eight search requests and accepts at most eight themes. It executes one primary request per theme and only then an optional single fallback per eligible theme while budget remains. Page retrieval stays bounded by a compatibility safeguard for runtime, memory, remote-server politeness, and pathological-result protection; it is not treated as a financial quota. The tool never crawls arbitrary links found in a page. |
| S23-D8 | `sourceLanguage` is a search qualifier (BCP-47 tag, default `"en"`). `language` is a translation target. Translation is allowed only after the final source payload receives a clean Guardrails verdict; the exact translated payload is screened again before delivery. Disabled, failed, caution, or withheld source screening skips translation. |
| S23-D9 | Search, retrieval, parsing, date filtering, deduplication, and ranking remain deterministic C# and run before Guardrails. Only the exact bounded final title/summary payload is screened. External titles and summaries never appear outside `protectedResults`; disabled or failed screening still forces marked Base64. |
| S23-D10 | All code, UI text, error text, comments and XML documentation are English. |

## 3. Data contracts

### 3.1 Search and document contracts

Add immutable public contracts in Core or Connectors, with XML documentation:

```csharp
public sealed record SearchHit(
    string Id,
    int Rank,
    string Url,
    string Domain,
    int ProtectedContentIndex);

public enum WebDocumentKind
{
    Article,
    Post,
    Documentation,
    ReleaseNote,
    Repository,
    Video,
    Other
}

public enum PublicationDateStatus
{
    Verified,
    Unknown
}

public enum FreshnessWindow
{
    Sliding,
    None
}
```

`WebDocument` contains the stable hit id, source URL, final URL, canonical URL,
title, domain, kind, nullable publication time, date status, compact text, content type,
discovery query ids, and retrieval time. Trust metadata belongs to the final composite
delivery rather than each deterministically collected document. Canonical URL
normalization removes fragment identifiers and known non-semantic tracking parameters,
normalizes host casing and default ports, and never changes a path's semantic case.

The existing `web.search` output must preserve `protectedResults` and its Guardrails
delivery semantics. Add an ordered `hits` array containing `id`, `rank`, `url`,
`domain`, and `protectedContentIndex`. Never put an unprotected title or snippet into
the new metadata array.

### 3.2 Tool input schema

```text
research.discover(
    themes,
    freshnessWindow?,
    sourceLanguage?,
    language?,
    preferredDomains?,
    minimumDocuments?,
    maxResultsPerQuery?,
    maxRetrievalsPerRun?
)
```

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `themes` | `Theme[]` | yes | — | 1–8 search themes; more than 8 are rejected before contacting the provider |
| `freshnessWindow` | `FreshnessWindow` | no | `"sliding"` | `"sliding"`: 2→4→7 day expansion. `"none"`: no date filter. |
| `sourceLanguage` | `string` | no | `"en"` | BCP-47 language tag. Search qualifier that filters results by language. |
| `language` | `string` | no | empty | BCP-47 language tag. If set, translates title and short_summary of each candidate to this language via internal LLM call after collection. If empty, content is returned in original language. |
| `preferredDomains` | `string[]` | no | `[]` | 1–50 domains used as ranking signal |
| `minimumDocuments` | `int` | no | `8` | 1–100; target unique dated docs. Ignored when `freshnessWindow` is `"none"`. |
| `maxResultsPerQuery` | `int` | no | `10` | Advanced compatibility setting, 1–10; normally omitted from workflows |
| `maxRetrievalsPerRun` | `int` | no | `40` | Advanced compatibility safeguard, 1–100; normally omitted from workflows |

```csharp
public sealed record Theme(
    string[] Terms,
    string[]? IncludeTerms,
    string[]? ExcludeTerms);
```

Each theme must have at least one term. `IncludeTerms` and `ExcludeTerms` are
optional filters applied to query compilation.

## 4. Query compilation

For each theme, compile one primary `current change` query. A single `practical
engineering` fallback may be compiled later if the primary collection is insufficient. Templates incorporate the terms, include/exclude terms, the
`sourceLanguage` qualifier, and an absolute `after:yyyy-MM-dd` hint when `sliding` mode is
active (no date qualifier for `none`). The hint is never trusted as enforcement; the local
post-retrieval filter is authoritative. Preferred domains affect local ranking only and never
create an additional query.

Record every generated query and its theme index in the run output; never log the
query's returned content.

## 5. Discovery and retrieval behavior

### 5.1 Mode `sliding`

1. Compile one primary query per theme with a single seven-day absolute date hint.
2. Execute the primary queries via `web.search`, subject to the eight-request run ceiling.
3. Normalize URLs, deduplicate hits, and retrieve them rank-by-rank across themes so one
   large result set cannot consume the entire retrieval safeguard.
4. Retrieve candidates (normally using the default safeguard) through the
   existing safe HTTP infrastructure. Extract canonical URL, title, publication date
   from supported HTML/JSON-LD/Open Graph metadata, and main text projection.
5. Count unique verified documents in the seven-day window. If below `minimumDocuments`,
   execute at most one fallback query per successful primary theme, in theme order, while
   both search and retrieval safeguards allow it. Re-evaluate after each fallback.
6. Evaluate the 2, 4, and 7-day windows locally from this one collection and select the
   first window that reaches `minimumDocuments`.
7. If still below threshold at seven days, return a successful but explicitly
   insufficient result; do not backfill with older or unknown-date documents.
8. Rank eligible documents and build the exact bounded canonical title/summary payload.
9. Screen that payload immediately before delivery. If `language` is set and the
   disposition is `PassThrough`, translate it through one internal tool-less LLM call and
   screen the exact translated payload again. Otherwise skip translation.
10. Deliver titles and summaries only through `protectedResults`.

### 5.2 Mode `none`

1. Compile one primary query per theme without any date filter.
2. Execute the primary queries via `web.search`, subject to the eight-request run ceiling.
3. Normalize URLs, deduplicate hits, and retrieve them rank-by-rank across themes.
4. If a successful primary query yields no usable document, execute at most one fallback
   for that theme while safeguards allow it. Failed primary queries are not retried.
5. Retrieve candidates through the
   existing safe HTTP infrastructure. Extract canonical URL, title, publication date
   from supported HTML/JSON-LD/Open Graph metadata, and main text projection.
6. Rank documents and build the exact bounded canonical title/summary payload.
7. Apply the same late Guardrails and optional protected translation boundary as sliding mode.
8. Deliver titles and summaries only through `protectedResults`.

`minimumDocuments` is ignored in this mode. Documents with `dateStatus: Unknown` are
returned and counted normally.

### 5.3 Common result shape

The result contains the active mode and effective UTC boundary, generated queries, the fixed
`searchRequestLimit` of 8, retrieval
and rejection counts, safe candidate metadata, budget counters, explicit insufficiency state,
per-window `windowHistory` diagnostics, one `protectedResults` block, aggregate Guardrails
metadata, and translation status. Titles and summaries occur only inside `protectedResults`.
A search-provider failure is distinct from a successful query with no hits: `searchStatus`,
successful and failed query counts, and the last HTTP status make that state explicit. HTTP 429,
401, and 403 stop all later search requests in the run.
A run may be cancelled at any point.

## 6. Workflow integration

The tool is decorated with group `research`, display order 1200, and alias
`research.discover`. It requires the `web` and `http` groups to be enabled by the
task; validation reports both missing dependencies clearly.

The deterministic collection pipeline never runs a model. The only optional model operation is
the bounded tool-less translation after a clean source verdict. The tool cannot crawl links or
call arbitrary URLs found inside fetched content.

### 6.1 Example workflow step

```json
{
  "tool": "research.discover",
  "params": {
    "themes": [
      {
        "terms": [".NET", "C#", "ASP.NET Core"],
        "includeTerms": ["release", "preview", "breaking change"],
        "excludeTerms": ["job", "course"]
      }
    ],
    "freshnessWindow": "sliding",
    "sourceLanguage": "en",
    "language": "fr",
    "preferredDomains": ["learn.microsoft.com", "devblogs.microsoft.com"],
    "minimumDocuments": 8
  }
}
```

### 6.2 Example without freshness and no translation

```json
{
  "tool": "research.discover",
  "params": {
    "themes": [
      {
        "terms": ["Rust", "Cargo"],
        "excludeTerms": ["job"]
      }
    ],
    "freshnessWindow": "none"
  }
}
```

## 7. Privacy, safety and observability

- Search queries and protected content follow the Stage 8 privacy disclosure: they
  leave the machine when the configured cloud search provider is enabled.
- Normal logs contain ids, counts, domains only when already public metadata, durations,
  window sizes and outcomes; never URLs with query parameters, titles, summaries, or
  page content.
- No HTTP POST, login automation, link crawling, sitemap crawling, robots-policy bypass
  or arbitrary domain expansion is introduced.

## 8. Tests and gate

Add deterministic tests for query compilation, sourceLanguage qualifier injection,
one-collection 2/4/7 local evaluation (sliding mode), the eight-request ceiling, fair
rank-by-rank retrieval, no-filter behavior (none mode), date eligibility,
unknown dates, budget limits, canonicalization, deduplication, no-old-content backfill,
translation step (language set vs empty), Guardrails delivery contract, tool dependency
validation, and protected output. Use fixture HTML and fake search/HTTP providers; no
live Web or model dependency is allowed.

Run:

```text
dotnet restore OD.MyLocalAgent.sln -m:1
dotnet build OD.MyLocalAgent.sln --configuration Release --no-restore -m:1
dotnet test OD.MyLocalAgent.sln --configuration Release --no-build --no-restore --filter "Category!=Integration" -m:1
```

Integration tests remain opt-in because they require the configured Web Search credential.

Update the architecture ledger, README, Tool Map documentation, and privacy text.
