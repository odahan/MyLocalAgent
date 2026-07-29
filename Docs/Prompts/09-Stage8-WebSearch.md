# STAGE 8 — Web search tool (Ollama Web Search) with mandatory Guardrails screening

> Run only after **Stages 0–7** are finished (`docs/acceptance-report.md` complete, Guardrails
> in place). Web search depends on the content trust filter from Stage 7.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read, incl. the **§12 Guardrails
   addendum**; **§4 (R1, R5, R9), §6, §7.2, §7.3, §8.1–8.3, §8.5, §9** frame this stage.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs are binding
   (especially `IContentTrustService`, `ContentTrustPolicy`, `ISecretStore`, ToolMap).
3. The current file tree.

## 2. Mission

Give the app a **real web search capability** — not just named-page fetching: a new tool
`web.search(query, maxResults)` backed by the **Ollama Web Search API**, whose results are
**systematically screened by Guardrails before reaching the agent**. Configured via the Tools
page (free ollama.com API key, stored in DPAPI), disabled by default.

## 3. Design decisions (locked by the product owner — do not re-open)

| # | Decision |
|---|---|
| D1 | **Provider = Ollama Web Search only** (`https://ollama.com/api/web_search`). A `WebSearch:Provider` config key keeps the door open to future providers; `""` (default) = tool disabled. |
| D2 | **Guardrails screening is mandatory and non-bypassable**: every set of search results passes through `IContentTrustService` before entering tool output (agent or workflow). There is no config to turn this off while Guardrails is enabled; when Guardrails is disabled globally, search results pass through with `screened:false` (same rule as Gmail/HTTP). |
| D3 | **One composite screening call per search** (titles + snippets joined, bounded by `Guardrails:MaxContentChars`) — a single score per result set; the same trust envelope as Gmail applies. Per-result screening is a documented future refinement, not v1 (latency on a 12B local model). |
| D4 | **New whitelist tool group `web`** (§8.1 validation extended: the six groups are now `gmail`, `calendar`, `dropbox`, `filesystem`, `http`, `web`). Disabled-by-default behavior comes from config, not a `Safety` flag. |
| D5 | **API key via the secret pipeline only** (`WebSearch:ApiKey` bound from configuration, fed by `ISecretStore`/DPAPI at bootstrap — spec §8.5/§9-D). Never in `appsettings.json`, never in `settings.json`, never logged. |

## 4. Critical constraints (restated)

- Layering per spec §6: the raw HTTP client lives in **Connectors** (no dependencies);
  the tool in **Core** (connector + Guardrails service + ToolMap); no cycle, no incident
  references in Connectors (typed exceptions only, spec §9-A).
- Ollama Web Search is a **cloud call** (queries leave the machine): the UI must label it
  "cloud" exactly like the OpenAI provider opt-in (R1 spirit). Document in the README.
- Tool Map stays the single source of truth; the existing reflection-based consistency test
  must keep passing with the new row — do not special-case it.
- Warning-clean, complete files, XML docs, English only, `TimeProvider` injected.

## 5. Scope

### 5.1 Contracts & configuration (normative)

Add to the `appsettings.json` template (§8.2):

```json
"WebSearch": {
  "Provider": "",
  "Endpoint": "https://ollama.com/api/web_search",
  "DefaultMaxResults": 5,
  "MaxResultsCap": 10,
  "TimeoutSeconds": 30
}
```

Contracts (implement exactly):

```csharp
// Connectors project
public sealed record WebSearchResultItem(string Title, string Url, string Content);
public sealed record WebSearchResult(IReadOnlyList<WebSearchResultItem> Items, string Provider);

public interface IWebSearchConnector
{
    Task<WebSearchResult> SearchAsync(string query, int maxResults, CancellationToken ct);
}
```

`WebSearchOptions` (validated: cap ≥ 1, endpoint absolute URL when provider set).

### 5.2 `OD.MyLocalAgent.Connectors` — `OllamaWebSearchConnector`

- `HttpClient` via `IHttpClientFactory`, named client with the options' timeout.
- Request: `POST {Endpoint}` with `Authorization: Bearer {WebSearch:ApiKey}` and JSON body
  `{ "query": <query>, "max_results": <maxResults> }`.
- Response: tolerant parse of `{ "results": [ { "title", "url", "content" } ] }` — unknown or
  missing fields tolerated; absent/invalid `results` → empty `Items` (not an error).
- Typed errors: `WebSearchNotConfiguredException` (provider empty), `ConnectorConfigurationException`
  (provider set but API key missing), `WebSearchApiException` (non-2xx, carrying the status code;
  401/403 message must say the key is likely missing/invalid).
- DI: extend `AddConnectors(IConfiguration)`.

### 5.3 `OD.MyLocalAgent.Core` — `WebSearchTool` + ToolMap + whitelist

- **ToolMap** new row: alias `web.search` ↔ agent function `web_search` ↔
  `WebSearchTool.SearchAsync` ↔ args `query` (required), `maxResults` (optional int; default
  from options, clamped to `MaxResultsCap`).
- Flow per call: connector search → **Guardrails** (D2/D3): composite text =
  per-item `"<i>. <title>\n<content>"` joined (URLs excluded — documented), screened once
  → apply `ContentTrustPolicy.Decide` to the result set:
  - `PassThrough` → items as-is;
  - `EncodeBase64` → `title` and `content` of every item Base64-encoded;
  - `Withhold` → `results` emptied + marker (see envelope).
  Guardrails disabled → pass-through with `screened:false`.
- Output compact JSON envelope (agent-budget aware):

```json
{
  "provider": "Ollama",
  "query": "...",
  "count": 5,
  "results": [ { "title": "...", "url": "...", "content": "..." } ],
  "guardrails": {
    "screened": true,
    "trustScore": 92,
    "suspiciousIndicators": [],
    "disposition": "PassThrough",
    "contentEncoding": "none"
  }
}
```

  On `Withhold`: `"results": []`, `"count": 0`, plus `"withheld": "score N"`.
- Whitelist/validation: allow `"web"` in task `tools` (§8.1); workflow step validation via the
  ToolMap pickup; the Task editor page gains the sixth checkbox **Web search (web)** labeled
  "(cloud)".

### 5.4 UI — Tools configuration page (new block, after Dropbox)

**Web search (Ollama)** — labeled "(cloud — queries leave this machine)"; Enable toggle (writes
`WebSearch:Provider` = `"Ollama"` / `""` to `settings.json`, hot via `IOptionsMonitor`); API
key box writing to `ISecretStore` only (masked display + "configured: yes/no" indicator);
Endpoint (defaults pre-filled); **Test** button → runs a fixed probe query, shows latency +
result count or the typed-error explanation. The existing **Tool tester** page picks up
`web.search` automatically via the ToolMap (verify the envelope renders).

### 5.5 Tests (deterministic — mocked `HttpMessageHandler`, stubbed Guardrails; no real API)

- Connector: request shape (method, URL, Bearer header, JSON body fields), DTO mapping,
  tolerant parse (missing/unknown fields), 401 → `WebSearchApiException` mentioning the key,
  provider empty → `WebSearchNotConfiguredException`, missing key →
  `ConnectorConfigurationException`, timeout honored.
- `WebSearchTool`: envelope shape; `maxResults` default + cap clamping; guardrails
  dispositions via stubbed `IContentTrustService` (PassThrough/EncodeBase64/Withhold incl.
  Base64 round-trip and withhold marker); disabled-provider error message points to the Tools
  page; whitelist rejection without the `web` group; **ToolMap consistency test keeps passing
  unmodified**.
- Validation regression: task JSON accepting `"web"`, still rejecting unknown groups.
- Fixtures: 2 canned Ollama API responses (normal / injection-lure snippet) — the latter is
  for the *manual* e2e narrative only, never LLM-gated.

### 5.6 Docs & spec upkeep

- New sample task `docs/samples/web-daily-brief.json` (`web.search` → `ai.summarize` →
  `filesystem.write` — becomes the README's third workflow example).
- Append **§13 "Web search addendum"** to `docs/prompts/00-Shared-Specification.md`: D1–D5,
  contracts, config block, envelope, and **ADR I** in §9: "Web search is a cloud opt-in
  (Ollama API); key via DPAPI; results are untrusted content and always Guardrails-screened;
  local alternative (e.g. SearXNG) fits the `WebSearch:Provider` switch later."
- README: new section "Web search (Ollama)" — create a free ollama.com account → API key →
  Tools page paste; cloud labeling + privacy note; honest rate-limit remark (free tier for
  individuals, limits undocumented — expect throttling on bursts); Tool Map table updated with
  `web.search`; the three-example workflow list completed.
- `docs/acceptance-report.md`: add the Stage-8 addendum table from §7.

**Out of scope**: other providers (SearXNG…), Ollama `web_fetch` (HttpTool already covers
named pages), per-result screening, query rewriting, search-result caching.

## 6. Working agreements

- Smallest diffs (R10); every pre-Stage-8 file touched gets a ledger line.
- The HTTP contract of the Ollama API is verified against the live docs/source you can reach;
  if the real payload differs from §5.2, adapt the tolerant parser and record the deviation.
- Keep the result envelope within the compact-JSON budget used by the other tools.

## 7. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green incl. §5.5.
3. Probes (real key if available, else flag human verification): `test-tool --alias web.search
   --args '{ "query": "ollama web search" }'` → envelope with `guardrails` block; with
   `Provider:""` → clean `WebSearchNotConfiguredException` message; Tools page save →
   `settings.json` verified to contain **no** API key (DPAPI only).
4. Manual e2e (mark human-verifiable if needed): sample `web-daily-brief` runs via
   `test-prompt`/runner and writes a report; Guardrails fields present in the workflow result.

**Stage-8 acceptance addendum for `docs/acceptance-report.md`:**

- [ ] Contracts §5.1 exact; connector request-shape test proves auth + body fields.
- [ ] Guardrails screening provably in the search path: the 3 dispositions verified (stubbed)
  and `screened:true` visible in a real probe; no bypass when Guardrails enabled.
- [ ] API key in DPAPI only (checked by reading `settings.json`, not by trust).
- [ ] Fresh-install default: tool disabled with an actionable error; enabling + key + test in
  the UI under 5 minutes.
- [ ] README "Web search (Ollama)" section + sample task committed; Tool Map table updated.

## 8. Before you finish

1. Append the **Stage 8** entry to the ledger: contracts, envelope, config keys, ToolMap row,
   docs touched, deviations (esp. actual vs assumed Ollama API payload), residual risks
   (rate limits; snippet-level injection evading composite screening — documented).
2. Stage report: file tree delta, gate summary, acceptance addendum verbatim with ✅/⏳.
