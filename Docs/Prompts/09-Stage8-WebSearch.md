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
| D1 | **Provider = Ollama Web Search only in v1** (`https://ollama.com/api/web_search`). `WebSearch:Provider` selects a provider; `""` (default) disables the tool. Provider resolution remains inside Connectors behind the stable `IWebSearchConnector` contract so a future provider such as SearXNG can be added without changing `WebSearchTool`, its output envelope, the Tool Map, or workflows. An unknown provider is an actionable configuration error. |
| D2 | **Guardrails protection is mandatory and non-bypassable**: every returned result set passes through `IContentTrustService` before entering tool output (agent or workflow), including an empty canonical `[]` payload. Security takes precedence over any conflicting delivery rule. `Guardrails:ScreeningEnabled=false` disables only the LLM call: delivery remains protected with the mandatory marked Base64 envelope, `screened:false`, `trustStatus:"ScreeningDisabled"`, and `disposition:"EncodeBase64"`. A screening failure uses the same marked-Base64 fallback with `trustStatus:"Failed"` and is reported exactly once. Raw fallback is forbidden. |
| D3 | **One composite screening call per search** — a single score per result set. The compact canonical result payload is built and bounded by `Guardrails:MaxContentChars` *before* screening, and Guardrails receives exactly every title/snippet character that may be delivered. Results that do not fit are deterministically truncated or omitted with explicit counters; no unscreened overflow may enter tool output. URLs remain excluded from screening as documented metadata. Per-result or multi-call screening is a documented future refinement, not v1 (latency on a 12B local model). |
| D4 | **New whitelist tool group `web`** (§8.1 validation extended: the six groups are now `gmail`, `calendar`, `dropbox`, `filesystem`, `http`, `web`). Disabled-by-default behavior comes from config, not a `Safety` flag. |
| D5 | **API key via the secret pipeline only** (`WebSearch:ApiKey` bound from configuration, fed by `ISecretStore`/DPAPI at bootstrap — spec §8.5/§9-D). Never in `appsettings.json`, never in `settings.json`, never logged. |
| D6 | **One compact protected output block per result set.** Titles and snippets are serialized once as compact indexed JSON inside `protectedResults`, then delivered through `ContentTrustEnvelope`; URLs are mapped by index in `sources`. Agent-facing output exposes only `suspiciousIndicatorCount`, never verbatim suspicious indicators. `Withhold` returns no sources or protected external data. |

## 4. Critical constraints (restated)

- Layering per spec §6: the raw HTTP client lives in **Connectors** (no dependencies);
  the tool in **Core** (connector + Guardrails service + ToolMap); no cycle, no incident
  references in Connectors (typed exceptions only, spec §9-A).
- Ollama Web Search is a **cloud call** (queries leave the machine): the UI must label it
  "cloud" exactly like the OpenAI provider opt-in (R1 spirit). Document in the README.
- Tool Map stays the single source of truth; the existing reflection-based consistency test
  must keep passing with the new row — do not special-case it.
- The exact delivered title/snippet payload, not merely a prefix of the provider response, is
  the unit screened by Guardrails. A budget overflow must reduce delivered data, never screening
  coverage.
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

`WebSearchOptions` is hot-reloadable. Validate `Provider` as `""` or `"Ollama"` in v1,
`1 <= DefaultMaxResults <= MaxResultsCap`, `TimeoutSeconds > 0`, and an absolute HTTP/HTTPS
endpoint when a provider is set. The UI warns that a custom endpoint receives the API key and
the search queries.

### 5.2 `OD.MyLocalAgent.Connectors` — provider boundary + `OllamaWebSearchConnector`

- `IWebSearchConnector` is the stable facade consumed by Core. Provider selection and provider
  registrations live entirely in Connectors; v1 registers only Ollama. A future provider is
  added behind this boundary, not through a `WebSearchTool` switch.
- `HttpClient` via `IHttpClientFactory`, named client with the options' timeout.
- Request: `POST {Endpoint}` with `Authorization: Bearer {WebSearch:ApiKey}` and JSON body
  `{ "query": <query>, "max_results": <maxResults> }`.
- Response: tolerant parse of `{ "results": [ { "title", "url", "content" } ] }` — unknown or
  missing item fields tolerated; an absent or non-array `results` property → empty `Items`
  (not an error). A malformed JSON document is a typed provider/protocol failure, not a
  successful empty search.
- Typed errors: `WebSearchNotConfiguredException` (provider empty), `ConnectorConfigurationException`
  (provider set but API key missing), `WebSearchApiException` (non-2xx, carrying the status code;
  401/403 message must say the key is likely missing/invalid). Transport errors, provider
  timeout, and malformed JSON are also wrapped in `WebSearchApiException`; caller cancellation
  propagates unchanged.
- DI: extend `AddConnectors(IConfiguration)`.

### 5.3 `OD.MyLocalAgent.Core` — `WebSearchTool` + ToolMap + whitelist

- **ToolMap** new row: alias `web.search` ↔ agent function `web_search` ↔
  `WebSearchTool.SearchAsync` ↔ args `query` (required), `maxResults` (optional int; default
  from options, clamped to the inclusive range `1..MaxResultsCap`). Extend the generic Tool Map
  metadata/binder to represent optional arguments; do not special-case `web.search`, and keep
  the existing reflection consistency test unchanged.
- Flow per call: connector search → build the compact canonical payload
  `[{"index":1,"title":"...","content":"..."}, ...]` (URLs excluded) → apply the deterministic
  delivery budget below → screen that exact payload once → apply
  `ContentTrustPolicy.Decide`. An empty provider response uses the canonical payload `[]` and
  still follows the same single-call policy:
  - `PassThrough` → `protectedResults` is the canonical payload wrapped once by
    `ContentTrustEnvelope.WrapPlaintext`;
  - `EncodeBase64` → `protectedResults` is the same canonical payload wrapped once by
    `ContentTrustEnvelope.WrapBase64`;
  - `Withhold` → `sources` is empty, `count` is zero, and `protectedResults` contains only the
    application-authored withhold marker.
- **Deterministic delivery budget:** serialize items in provider order. Include complete items
  while they fit within `Guardrails:MaxContentChars`. If the next item does not fit, use the
  remaining budget for a deterministically truncated title/snippet with an explicit
  application-authored truncation marker when useful; otherwise omit it. Omit all later items.
  The marker and JSON syntax count toward the limit. Never deliver a title/snippet character
  absent from the screened canonical payload.
- A caught `GuardrailScreeningException` is reported once through `IIncidentReporter` and its
  marked-Base64 `FallbackResult` is delivered. Caller cancellation is never converted into a
  fallback or incident.
- Output compact JSON envelope (agent-budget aware):

```json
{
  "provider": "Ollama",
  "query": "...",
  "providerCount": 5,
  "count": 3,
  "omittedBecauseOfBudget": 2,
  "truncated": true,
  "sources": [
    { "index": 1, "url": "https://example.test/one" },
    { "index": 2, "url": "https://example.test/two" },
    { "index": 3, "url": "https://example.test/three" }
  ],
  "protectedResults": "NON-NEGOTIABLE SECURITY RULE: ... <<<UNTRUSTED-EXTERNAL-DATA nonce>>> ...",
  "guardrails": {
    "screened": true,
    "trustStatus": "Screened",
    "trustScore": 92,
    "suspiciousIndicatorCount": 0,
    "disposition": "PassThrough",
    "contentEncoding": "none"
  }
}
```

  `providerCount` is the connector item count; `count` is the number of indexed items actually
  delivered after both budgeting and Guardrails disposition. On `Withhold`: `"sources": []`,
  `"count": 0`, no external data in `protectedResults`, plus `"withheld": "score N"`.
  When LLM screening is disabled or fails, `protectedResults` remains the marked Base64
  envelope, `screened:false`, `trustScore:null`, and `contentEncoding:"base64"`.
- Whitelist/validation: allow `"web"` in task `tools` (§8.1); workflow step validation via the
  ToolMap pickup; the Task editor page gains the sixth checkbox **Web search (web)** labeled
  "(cloud)".

### 5.4 UI — Tools configuration page (new block, after Dropbox)

**Web search (Ollama)** — labeled "(cloud — queries leave this machine)"; Enable toggle (writes
`WebSearch:Provider` = `"Ollama"` / `""` to `settings.json`, hot via `IOptionsMonitor`); API
key box writing to `ISecretStore` only (masked display + "configured: yes/no" indicator);
Endpoint (defaults pre-filled) with a warning that custom endpoints receive the key and
queries; **Test** button → runs a fixed probe query through the connector and displays only
latency + result count or the typed-error explanation, never raw snippets. The existing
**Tool tester** page picks up `web.search` automatically via the ToolMap and verifies the full
connector → Guardrails envelope path.

All writes to `settings.json` use the central atomic settings pipeline. Saving the Settings
page after configuring Web search must preserve the `WebSearch` section, and saving Web search
must preserve every unrelated settings section. The API key remains exclusively in DPAPI.

### 5.5 Tests (deterministic — mocked `HttpMessageHandler`, stubbed Guardrails; no real API)

- Connector: request shape (method, URL, Bearer header, JSON body fields), DTO mapping,
  tolerant parse (missing/unknown fields), malformed JSON typed failure, 401 →
  `WebSearchApiException` mentioning the key, provider empty →
  `WebSearchNotConfiguredException`, unknown provider and missing key →
  actionable configuration errors, timeout honored, transport error wrapped, caller
  cancellation propagated.
- Provider boundary: Ollama is selected hot in v1; Core depends only on
  `IWebSearchConnector`; the selection test proves a future provider does not require a Core
  switch.
- `WebSearchTool`: compact envelope shape; exact canonical JSON round-trip; `maxResults`
  default + lower/upper clamping; optional argument omission through the generic binder;
  deterministic budget with complete-item, truncated-item, and omitted-item cases; assert that
  every delivered title/snippet character is present in the single screened input.
- Empty-result regression: canonical `[]` is screened exactly once and returns zero counts,
  empty sources, and a protected empty payload without inventing a special trust status.
- Guardrails dispositions via stubbed `IContentTrustService`
  (PassThrough/EncodeBase64/Withhold including exact marked-envelope round-trip and withhold
  marker); disabled screening and screening failure both produce marked Base64 with
  `screened:false`; failure is reported exactly once; caller cancellation produces neither
  fallback nor incident; agent output contains `suspiciousIndicatorCount` and no verbatim
  indicators.
- Disabled-provider error message points to the Tools page; whitelist rejection without the
  `web` group; **ToolMap consistency test keeps passing unmodified**.
- Validation regression: task JSON accepting `"web"`, still rejecting unknown groups.
- Settings regression: Tools-page Web search save followed by Settings-page save preserves
  `WebSearch`; the reverse preserves unrelated sections; neither file contains `ApiKey`.
- Fixtures: 2 canned Ollama API responses (normal / injection-lure snippet) — the latter is
  for the *manual* e2e narrative only, never LLM-gated.

### 5.6 Docs & spec upkeep

- New sample task `docs/samples/web-daily-brief.json` (`web.search` → `ai.summarize` →
  `filesystem.write` — becomes the README's third workflow example).
- Append **§13 "Web search addendum"** to `docs/prompts/00-Shared-Specification.md`: D1–D6,
  contracts, config block, envelope, and **ADR I** in §9: "Web search is a cloud opt-in
  (Ollama API); key via DPAPI; results are untrusted content and always Guardrails-screened;
  disabled/failed screening remains marked Base64; the exact bounded delivered payload is
  screened once; a local alternative (e.g. SearXNG) fits the provider boundary later."
- README: new section "Web search (Ollama)" — create a free ollama.com account → API key →
  Tools page paste; cloud labeling + privacy note; honest rate-limit remark (free tier for
  individuals, limits undocumented — expect throttling on bursts); Tool Map table updated with
  `web.search`; explain budget truncation/omission and provider extensibility; the
  three-example workflow list completed.
- Update both offline user manuals and `docs/theme-audit.md` for the new configuration block,
  cloud/privacy warning, task checkbox, output envelope, and controls.
- `docs/acceptance-report.md`: add the Stage-8 addendum table from §7.

**Out of scope**: implementing other providers (SearXNG…), Ollama `web_fetch` (HttpTool already
covers named pages), per-result or multi-call screening, query rewriting, search-result
caching.

## 6. Working agreements

- Smallest diffs (R10); every pre-Stage-8 file touched gets a ledger line.
- The HTTP contract of the Ollama API is verified against the live docs/source you can reach;
  if the real payload differs from §5.2, adapt the tolerant parser and record the deviation.
- Keep the result envelope within the compact-JSON budget used by the other tools.
- Never log an API key or full search query. Tool logs contain only sanitized metadata such as
  query length, requested result count, duration, and outcome.

## 7. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green incl. §5.5.
3. Probes (real key if available, else flag human verification): `test-tool --alias web.search
   --args '{ "query": "ollama web search" }'` → compact `protectedResults` envelope with
   `guardrails` and budget fields; with `Provider:""` → clean
   `WebSearchNotConfiguredException` message; Tools page save followed by Settings save →
   `WebSearch` preserved and `settings.json` verified to contain **no** API key (DPAPI only).
4. Manual e2e (mark human-verifiable if needed): sample `web-daily-brief` runs via
   `test-prompt`/runner and writes a report; Guardrails fields present in the workflow result.

**Stage-8 acceptance addendum for `docs/acceptance-report.md`:**

- [ ] Contracts §5.1 exact; connector request-shape test proves auth + body fields.
- [ ] Guardrails screening provably in the search path: the 3 dispositions verified (stubbed)
  and `screened:true` visible in a real probe; disabled/failed screening is marked Base64;
  no raw fallback exists.
- [ ] Budget regression proves every delivered title/snippet character was in the one screened
  canonical payload; overflow is explicitly truncated/omitted and counted.
- [ ] API key in DPAPI only (checked by reading `settings.json`, not by trust).
- [ ] Fresh-install default: tool disabled with an actionable error; enabling + key + test in
  the UI under 5 minutes.
- [ ] README "Web search (Ollama)" section + sample task committed; Tool Map table updated.

## 8. Before you finish

1. Append the **Stage 8** entry to the ledger: contracts, envelope, config keys, ToolMap row,
   docs touched, deviations (esp. actual vs assumed Ollama API payload), residual risks
   (rate limits; probabilistic screening; URL metadata excluded from screening; information
   loss from budget truncation/omission — documented).
2. Stage report: file tree delta, gate summary, acceptance addendum verbatim with ✅/⏳.
