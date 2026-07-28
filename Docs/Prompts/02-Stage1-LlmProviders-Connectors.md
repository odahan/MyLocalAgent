# STAGE 1 — LLM providers + external connectors

> Run only after Stage 0's gate is green.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read; **§7.1, §7.2, §8.2, §8.5, §9-A/D**
   are your scope.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; respect every pinned version and decision.
3. The current file tree.

## 2. Mission

Implement the two leaf layers of the solution: the LLM client layer
(`OD.MyLocalAgent.Ollama`) and the low-level API connectors
(`OD.MyLocalAgent.Connectors`), with unit tests, so that Stage 2 can build the Core on solid,
tested foundations.

## 3. Critical constraints (restated)

- No cycle: **neither project references incident logging** — errors surface as typed
  exceptions only (spec §6, §9-A). Incident reporting is the caller's job (Stage 2).
- Secrets never appear here: `OpenAI:ApiKey` and `Dropbox:*` arrive through plain options
  binding (spec §9-D). No DPAPI in these projects.
- `HttpClient` only via `IHttpClientFactory`; async + `CancellationToken` on every I/O method.
- Warning-clean; XML docs on every public type/member; complete files only.
- Earlier stages' files are read-only unless a recorded change is strictly required (spec §3.4).

## 4. Scope

### 4.1 `OD.MyLocalAgent.Ollama`

Implement spec §7.1 completely:

- Options records with validation: `AIOptions` (`Provider`), `OllamaOptions`
  (`Endpoint`, `Model`, `TimeoutSeconds` 300), `OpenAIOptions` (`Endpoint`, `Model`,
  `TimeoutSeconds`, `ApiKey` — bound from configuration, which Stage 2's bootstrap feeds).
- `LlmHealthResult` record: `IsReachable`, `ModelAvailable`, `LatencyMs`, `Error` (nullable).
- `ILlmClientService`, `ILlmClientFactory` exactly per spec — `Create()` reads
  `IOptionsMonitor<AIOptions>` on **each call** (hot provider switch); invalid config →
  `LlmConfigurationException` with an actionable message.
- `OllamaClientService`: health = endpoint reachable **and** model present in the local model
  list (use OllamaSharp's high-level API if it exposes local-model listing; otherwise a raw
  `HttpClient` call to the Ollama tags endpoint); typed `OllamaUnavailableException` /
  `OllamaModelMissingException`; `ChatOptions.ModelId` from options per call.
- `OpenAIClientService`: official `OpenAI` SDK via `.AsIChatClient()`; custom base URL via
  `OpenAIClientOptions.Endpoint`; the API key from bound options (absent →
  `LlmConfigurationException`).
- DI extension `AddLlmProviders(IConfiguration)` per spec §7.1.

### 4.2 `OD.MyLocalAgent.Connectors`

Implement spec §7.2 completely:

- Options records: `GoogleOptions` (`ClientId`, `ClientSecret`), `DropboxOptions`
  (`AccessToken`, `RefreshToken`, `AppKey` — bound from configuration).
- DTOs exactly per spec: `EmailMessageDto`, `CalendarEventDto`, `DropboxItemDto` (records).
- Google auth helper (`IGoogleCredentialProvider`): full `GoogleWebAuthorizationBroker` flow
  with `FileDataStore` under `%LOCALAPPDATA%\OD.MyLocalAgent\Auth\Google`; expose both the
  interactive path (used by the UI Connect button) and a silent path (cached/refreshing
  credential used by the runner); missing client id/secret → typed
  `ConnectorConfigurationException`.
- `GmailConnector`, `CalendarConnector`, `DropboxConnector` implementing the spec interfaces;
  plain-text body extraction for Gmail DTOs; typed exceptions (`GmailApiException`,
  `CalendarApiException`, `DropboxApiException`) carrying status/context.
- DI extension `AddConnectors(IConfiguration)`.

### 4.3 Tests (`OD.MyLocalAgent.Tests` — delete the Stage 0 smoke test)

- `OllamaClientService`: happy path, endpoint down, model missing, timeout, streaming
  concatenation — against a mocked `HttpMessageHandler`.
- `LlmClientFactory`: selects Ollama by default and OpenAI when configured (via a mutable
  options monitor test double); unknown provider / missing key → `LlmConfigurationException`.
- Connectors: DTO mapping from representative Google API payloads to `EmailMessageDto`
  (static JSON fixtures — no live calls); options validation failures typed correctly.

**Out of scope**: Core services, tools, agent, anything WPF, runner commands.

## 5. Working agreements

- Verify every external API shape **by compiling** (`Microsoft.Agents.AI` is a prerelease;
  check the real signatures instead of relying on memory — spec R8).
- You may add packages only if already pinned in `Directory.Packages.props`; a genuinely
  missing package requires a pinned addition + ledger note.
- Keep public API surface minimal and ledger-documented.

## 6. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green.
3. Quick manual probe (report output, do not gate on it if Ollama is not running): construct
   `OllamaClientService` against `http://localhost:11434` via a tiny xunit **skipped-by-default**
   integration test or a documented `dotnet run` snippet, and record the observed behavior.

## 7. Before you finish

1. Append the **Stage 1** entry to the ledger: public types added (fully qualified), exceptions
   taxonomy, deviations (especially any `Microsoft.Agents.AI` / OllamaSharp API difference vs
   spec wording), test list.
2. Stage report: files created/changed, gate summary, open questions for Stage 2.
