# Stage 8 final report — Ollama Web Search

Date: 2026-07-30
Target: Windows x64, Release
Status: implementation and deterministic gate complete; live-key/UI checks remain human

## Delivered scope

- Disabled-by-default, reloadable Web Search configuration with Ollama as the only Stage-8
  provider and a provider-neutral extension boundary.
- Documented Ollama cloud POST/Bearer request, tolerant response parsing, managed timeout,
  typed configuration/API failures, and caller-cancellation propagation.
- `web.search` / `web_search`, sixth `web` whitelist group, optional `maxResults`, and Runner
  configuration-error classification.
- One compact Guardrails-protected result block. The exact canonical ordered
  index/title/content array is bounded before one screening call; source index/URL is the
  only metadata outside it.
- PassThrough, Base64, and Withhold policy paths; marked Base64 for disabled/failed screening;
  exactly-once incident reporting; no raw fallback or agent-facing verbatim indicators.
- Deterministic truncation/omission with explicit marker/counts. An impossible
  `MaxContentChars < 2` fails before any cloud query because even canonical `[]` needs two
  characters.
- DPAPI-only `WebSearch:ApiKey`, atomic merged settings persistence, immediate reload, Tools
  cloud/privacy controls, Task editor checkbox, and Tool tester optional-argument support.
- README, shared specification ADR I/§13, both offline manuals, theme audit, acceptance
  report, architecture ledger, fixtures, and `web-daily-brief` sample.

## File-tree delta

```text
Docs/
├── samples/web-daily-brief.json
└── stage8-final-report.md
src/
├── OD.MyLocalAgent.Connectors/
│   ├── IWebSearchConnector.cs
│   ├── IWebSearchProvider.cs
│   ├── OllamaWebSearchConnector.cs
│   ├── WebSearchApiException.cs
│   ├── WebSearchConnector.cs
│   ├── WebSearchNotConfiguredException.cs
│   ├── WebSearchOptions.cs
│   ├── WebSearchResult.cs
│   └── WebSearchResultItem.cs
├── OD.MyLocalAgent.Core/
│   ├── IWebSearchTool.cs
│   └── WebSearchTool.cs
└── OD.MyLocalAgent.UI/
    └── AssemblyInfo.cs
tests/OD.MyLocalAgent.Tests/
├── Fixtures/
│   ├── web-search-injection-lure.json
│   └── web-search-normal.json
├── UserSettingsServiceTests.cs
├── WebSearchConnectorTests.cs
└── WebSearchToolTests.cs
```

Existing composition, options validation, Tool Map/registry/groups, secret bootstrap, Runner
classification, Tools/Task editor/Tool tester, user settings, appsettings, tests, README, both
manuals, specifications, audit, acceptance, and ledger files were updated only for Stage-8
integration.

## Executed gate

```powershell
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release
dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
```

- ✅ Build: all eight projects succeeded; 0 warnings, 0 errors.
- ✅ Tests: 132 passed, 0 failed, 1 skipped (`OllamaIntegrationTests` requires a configured
  local model); 133 total.
- ✅ `git diff --check`: clean.
- ✅ Both offline manuals: 22 sections, 30 ids, no duplicate id, no missing internal anchor,
  no external resource, and valid embedded JavaScript.
- ✅ `appsettings.json`, the Web Search sample, and both provider fixtures parse as valid JSON.
- ✅ Ollama's current official documentation matches the implemented request and result
  fields; no assumed-payload deviation was needed.

## Deterministic evidence

- Connector capture proves URL, POST, Bearer header, `query`, and `max_results`.
- Response tests cover normal, partial/unknown, malformed, HTTP 401, missing key, timeout,
  caller cancellation, disabled provider, and unknown provider.
- Tool tests prove one exact screened/delivered canonical string, empty `[]`, all three trust
  dispositions, disabled/failed Base64 fallback, exactly one incident, no cancellation
  incident, URL exclusion, source removal on withholding, clamping, and pre-screen budget.
- Tool Map reflection consistency remains green; whitelist rejection and optional argument
  invocation are covered.
- Task validation accepts `web`/`web.search`.
- Settings tests read the final JSON and prove DPAPI key exclusion plus Web Search/general/
  unrelated-section preservation in both save orders.

## Stage-8 acceptance addendum

- ✅ Contracts §5.1 exact; connector request-shape test proves auth + body fields.
- ⏳ Guardrails screening provably in the search path: the 3 dispositions verified (stubbed)
  and `screened:true` visible in a real probe; disabled/failed screening is marked Base64;
  no raw fallback exists. All deterministic portions pass; the real probe needs a user key.
- ✅ Budget regression proves every delivered title/snippet character was in the one screened
  canonical payload; overflow is explicitly truncated/omitted and counted.
- ✅ API key in DPAPI only (checked by reading `settings.json`, not by trust).
- ⏳ Fresh-install default: tool disabled with an actionable error; enabling + key + test in
  the UI under 5 minutes. Default/error are automated; live timing requires a user key.
- ⏳ README "Web search (Ollama)" section + sample task committed; Tool Map table updated.
  The files and table are complete in the working tree; no Git commit was requested or
  created.

## Human/environment follow-ups

1. In **Tools**, supply a real Ollama Web Search key, enable the provider, save, and confirm
   the fixed connection test reports only latency and result count.
2. Run `test-tool --alias web.search --args '{"query":"ollama web search"}'` and confirm
   `screened:true`, `protectedResults`, aggregate Guardrails fields, and budget fields.
3. Run `web-daily-brief` through the Runner and verify its Markdown workflow report.
4. Inspect the Web Search Tools card, typed error states, and sixth Task checkbox in Dark and
   Light.

## Residual risks

- Ollama rate limits are not a stable documented contract; bursts may be throttled.
- Trust scoring is probabilistic and Base64 is spotlighting, not isolation.
- Whitelisted source URLs are intentionally metadata and are not screened.
- Budget truncation/omission can remove relevant information.
- Queries and snippets leave the machine for Ollama; if OpenAI-compatible is the active AI
  provider, the bounded canonical result also leaves for Guardrails screening.
- Safety gates, per-task whitelist, filesystem sandbox, and HTTP allowlist remain the hard
  controls.
