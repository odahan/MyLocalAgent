# STAGE 23 — Web Discovery, Retrieval & Research Storage

> This stage is independent of Stage 19 (generic SQLite tool) and Stage 22. It extends the
> existing Stage 8 web-search capability into a bounded, provider-neutral discovery and
> retrieval pipeline. It also introduces application-owned SQLite storage spaces. Stage 19
> must later consume the read-only bridge defined here; this stage must not depend on the
> Stage 19 public tool surface.

## 1. Mission

Add a reusable Web research foundation for workflows that need to discover current documents,
retrieve a bounded subset, avoid repeated work, and retain a compact decision history. The
foundation is not an editorial product: an editorial watch, security watch, competitive watch,
and technical research task must all be able to use it.

The stage adds:

- provider-neutral search-hit and retrieved-document contracts;
- declarative research profiles that compile themes into deterministic queries;
- a freshness policy which starts at two days and expands only to four then seven days when the
  minimum number of eligible documents has not been reached;
- application-owned named storage spaces backed by SQLite;
- compact research history, deduplication and retention;
- the `research.discover` workflow tool, with bounded loops implemented in code rather than in
  workflow JSON.

No full Web page, article body, social-media post body, credential, raw prompt, or raw search
payload is persisted by this stage.

## 2. Read first

1. `Docs/Prompts/00-Shared-Specification.md`, including the Stage 23–25 addendum added by this
   stage.
2. `Docs/Prompts/09-Stage8-WebSearch.md`, `18-Stage18-CompactExternalContentGuardrails.md`,
   `18D-Stage18D-DeclarativeDynamicTooling.md`, and `20-Stage20-AgentPromptPlaceholders.md`.
3. `Docs/ARCHITECTURE-LEDGER.md`.
4. The existing WebSearch connector/tool, HTTP tool, Guardrails delivery contracts, task store,
   task editor, workflow engine, tool catalog, path sandbox, Settings persistence, and tests.

## 3. Locked product decisions

| # | Decision |
|---|---|
| S23-D1 | Discovery, retrieval, normalization, ranking and persistence are distinct responsibilities. A search theme never becomes a URL directly: it is compiled to queries, the provider returns URLs, and only selected URLs are retrieved. |
| S23-D2 | `web.search` remains a generic single-query capability. It gains stable hit metadata but does not accept editorial themes, retention settings, or a storage-space identifier. |
| S23-D3 | `research.discover` is the generic orchestration capability. It accepts a research-profile id and an optional storage-space id, runs bounded code-owned loops, and returns compact structured candidates. It is not an LLM agent loop. |
| S23-D4 | The initial freshness window is exactly two calendar days, inclusive of the local run date. Expansion steps are exactly 2, 4, and 7 days, never exceeding seven. Expansion happens only when the count of unique, eligible, dated documents is below the profile threshold. |
| S23-D5 | A document with an unknown publication date may be returned as `dateStatus: Unknown`, but it never satisfies the minimum eligible-document threshold and is ranked below dated documents. The system must not infer a date from a search-result position. |
| S23-D6 | Query creation is deterministic C# from persisted profile terms and templates. A local model may propose profile edits in a future feature, but it must not invent queries during a scheduled run. |
| S23-D7 | Search snippets and fetched content remain untrusted external data and preserve the existing Guardrails boundary. Metadata is never presented as proof of page content. |
| S23-D8 | Every retrieval is bounded by profile and global limits. The component fetches only the highest-priority unique hits needed to meet a fixed per-query and per-run candidate budget; it never crawls arbitrary links found in a page. |
| S23-D9 | Storage spaces are application-owned data stores. A task references one by immutable `storageSpaceId`; it never supplies a managed database path. Missing `storageSpaceId` resolves to the built-in `default` space. |
| S23-D10 | Stage 23 uses `Microsoft.Data.Sqlite` 10.0.10, pinned centrally. This is private application persistence, not the Stage 19 generic SQLite tool. |
| S23-D11 | Managed storage stores only compact metadata: canonical URL, title, domain, document kind, published date/status, short bounded summary, tags, discovery provenance, score/decision and timestamps. No article or post body is retained. |
| S23-D12 | Research history is retained for a configurable default of 18 months. Purge is deterministic by age and occurs only for research records; publication records introduced by Stage 24 are never automatically purged. |
| S23-D13 | Managed schema names and columns are stable, documented public tables for this product version. Do not add SQL views solely as an abstraction layer. Schema change is a deliberate future compatibility decision with migrations and documentation. |
| S23-D14 | Managed database files are reserved from generic task filesystem grants. Stage 19 later permits read-only access through `storageSpaceId`, not a guessed or copied managed path. |
| S23-D15 | All code, UI text, error text, comments and XML documentation are English. The user-facing concept is “Storage space”; “SQLite database” is an advanced implementation detail. |

## 4. Data contracts

### 4.1 Search and document contracts

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
```

`WebDocument` contains the stable hit id, source URL, final URL, canonical URL, title, domain,
kind, nullable publication time, date status, compact protected text, content type, trust
metadata, discovery query ids, and retrieval time. Canonical URL normalization removes fragment
identifiers and known non-semantic tracking parameters, normalizes host casing and default ports,
and never changes a path’s semantic case.

The existing `web.search` output must preserve `protectedResults` and its Guardrails delivery
semantics. Add an ordered `hits` array containing `id`, `rank`, `url`, `domain`, and
`protectedContentIndex`; the index maps a hit to the existing protected-result item. Never put an
unprotected title or snippet into the new metadata array.

### 4.2 Research profile

Persist profiles as validated JSON under the application data directory, one file per immutable
profile id. A profile has a mutable display name and description plus:

```json
{
  "id": "dotnet-editorial",
  "displayName": "Dotnet editorial research",
  "themes": [
    {
      "id": "dotnet-platform",
      "terms": [".NET", "C#", "ASP.NET Core"],
      "includeTerms": ["release", "preview", "performance", "breaking change"],
      "excludeTerms": ["job", "course"]
    }
  ],
  "languages": ["en"],
  "preferredDomains": ["learn.microsoft.com", "devblogs.microsoft.com", "github.com"],
  "minimumEligibleDocuments": 8,
  "maximumResultsPerQuery": 5,
  "maximumRetrievalsPerRun": 24
}
```

Validate ids with the existing task-id grammar. Require 1–20 themes, at least one term per theme,
1–50 preferred domains, a minimum count from 1–100, a per-query maximum from 1–10, and a run
retrieval maximum from 1–100. A profile is data, not executable instructions.

For each theme, compile exactly two query intents: `current change` and `practical engineering`.
Templates incorporate its terms, include/exclude terms, selected language and the current
window. Preferred domains are a ranking signal and may produce a third domain-targeted query
only when the global query budget permits it. Record every generated query and its profile/theme
origin in the run output and history; never log the query’s returned content.

Add a **Research profiles** Settings section with list, create, edit, duplicate and delete
commands. A profile referenced by an enabled task cannot be deleted until that task is reassigned
or disabled. The editor uses normal controls for themes, terms, source-domain preferences and
limits; it never asks the user to write JSON. A read-only advanced view may copy the canonical
profile JSON, but JSON is not the normal editing surface.

### 4.3 Storage spaces

Add `StorageSpaceDefinition` and `IStorageSpaceRegistry`. A definition has immutable `Id`, mutable
`DisplayName`, an existing local `DatabaseDirectory`, and a simple `.db` `DatabaseFileName`.
`default` is created on first run at `%LOCALAPPDATA%\OD.MyLocalAgent\storage\default.db`; it
cannot be deleted or renamed.

Validate a custom directory as a local, absolute, non-device, non-UNC path without a reparse-point
escape. Validate file names as plain file names ending in `.db`. Do not silently create arbitrary
parent directories; the Storage Spaces UI creates a user-chosen local directory explicitly.

The registry exposes a frozen execution snapshot. Task validation verifies that a declared
`storageSpaceId` exists. A storage-space id is copied into `ToolExecutionContext` and is never
read from mutable Settings after a run has started.

Deletion is blocked while a task references the space. Moving a space is a dedicated UI command:
close connections, create a verified backup, copy atomically, validate schema, update the
registry only after success, and preserve the old file on failure. Normal task execution never
moves, deletes, or creates storage spaces.

Persist the registry through the existing atomic user-Settings mechanism under a `StorageSpaces`
section. The shipped state contains only `default`; custom entries contain `id`, `displayName`,
`databaseDirectory`, and `databaseFileName`. Store the research retention default under the
`Research` Settings section. Secrets are never valid storage-space properties.

### 4.4 Managed tables

Create and migrate the following tables per storage space. Names and listed columns are public for
this version and must be documented in the README.

```text
research_runs(
  run_id, profile_id, started_utc, completed_utc, initial_window_days,
  final_window_days, eligible_document_count, status)

research_documents(
  document_id, canonical_url, source_url, domain, title, document_kind,
  published_utc, publication_date_status, short_summary, first_seen_utc,
  last_seen_utc, expires_utc)

research_document_themes(document_id, profile_id, theme_id)

research_discoveries(
  discovery_id, run_id, document_id, query_id, rank, discovered_utc)

research_decisions(
  decision_id, document_id, profile_id, decision, score, reason, decided_utc,
  review_after_utc)
```

Use stable GUID/ULID-like application-generated ids, a unique index on `canonical_url`, foreign
keys, UTC instants, bounded text columns enforced before storage, and explicit indexes for
profile/date/decision/report queries. `short_summary` is application-authored or model-authored
but limited to 600 Unicode scalar values; it is never the original page text.

## 5. Discovery and retrieval behavior

1. Resolve the profile and selected storage-space snapshot.
2. Compile and execute the two-day query set, bounded by the profile limits.
3. Normalize URLs and deduplicate hits before retrieval.
4. Prioritize preferred domains, primary-source-looking URLs, rank, and records not already seen
   in the selected storage space.
5. Retrieve the bounded top candidates through the existing safe HTTP infrastructure. Extract
   canonical URL, title, publication date from supported HTML/JSON-LD/Open Graph metadata, and
   main text projection. `http.getText` may be refactored to share extraction code, but its
   established public output must remain compatible.
6. Apply Guardrails before a page projection is passed to a model or recorded as a summary.
7. Count unique documents whose date is verified and inside the active window. If below the
   threshold, repeat only the missing work for windows four then seven days.
8. If still below threshold at seven days, return a successful but explicitly insufficient result;
   do not backfill with older or unknown-date documents merely to reach a number.
9. Upsert compact records and purge expired research records in one application-owned transaction.

The result contains window information, queries, counts, candidate metadata, explicit
insufficiency state, and source URLs. Content values follow the existing protected-delivery
contract. A run may be cancelled at any point; cancellation rolls back only the current atomic
write and never removes prior history.

## 6. Tool surface and workflow integration

Decorate a contextual `ResearchTool` with group `research`, display order 1200, and alias:

```text
research.discover(profileId, storageSpaceId?, maxCandidates?)
```

`profileId` is required. `storageSpaceId` defaults to the task’s `storageSpaceId`, then `default`.
`maxCandidates` only lowers the profile’s persisted cap. The tool returns JSON, never runs a
model, and cannot call arbitrary URLs supplied by the model. It requires the `web` and `http`
groups to be enabled by the task; validation reports both missing dependencies clearly.

Add optional `storageSpaceId` to `ScheduledTask`, canonical task serialization, validation,
workflow/task execution context, Runner command output, task list item projections, and the Task
editor. Existing tasks deserialize with no explicit value and use `default` at execution.

## 7. Settings and UI

Add a **Storage spaces** Settings section with list, create, rename, move, delete and open-folder
commands. It must show the immutable id, mutable name and local location. Delete requires a
clear confirmation, lists referencing tasks, and is disabled for `default`.

The Task editor shows an optional **Storage space** selector. It defaults to `Default`, does not
write `storageSpaceId` when default is selected, and never exposes a raw database path.

The Tool tester has a research profile selector, optional storage-space selector and a concise
result summary. It must not expose raw fetched body content or database internals.

## 8. Privacy, safety and observability

- Search queries and protected content follow the Stage 8 privacy disclosure: they leave the
  machine when the configured cloud search provider is enabled.
- Normal logs contain ids, counts, domains only when already public metadata, durations, window
  sizes and outcomes; never URLs with query parameters, titles, summaries, page content, SQL,
  database paths, or schema values.
- An app-owned managed path is reserved. Filesystem grants and Stage 19 generic SQLite paths
  cannot target it. Later read-only access is by `storageSpaceId` only.
- No HTTP POST, login automation, link crawling, sitemap crawling, robots-policy bypass or
  arbitrary domain expansion is introduced.

## 9. Tests and gate

Add deterministic tests for query compilation, 2/4/7 expansion, date eligibility, unknown dates,
budget limits, canonicalization, deduplication, prior-history suppression, no-old-content
backfill, atomic persistence, retention, managed-path reservation, storage-space lifecycle,
task defaulting, context snapshot isolation, tool dependency validation, and protected output.
Use fixture HTML and fake search/HTTP providers; no live Web or model dependency is allowed.

Run:

```text
dotnet restore OD.MyLocalAgent.sln -m:1
dotnet build OD.MyLocalAgent.sln --configuration Release --no-restore -m:1
dotnet test OD.MyLocalAgent.sln --configuration Release --no-build --no-restore -m:1
```

Update the architecture ledger, README, First Run guide, Tool Map documentation, privacy text,
Settings and Task editor theme audit, and add a parseable research workflow sample.
