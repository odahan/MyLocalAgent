# Provider interoperability, model resolution, and diagnostics

## Scope

OD.MyLocalAgent supports two mutually exclusive active LLM providers per process:

- local **Ollama**;
- an **OpenAI-compatible** HTTP endpoint.

Both configurations remain persisted when the user switches the active provider. `AI:Provider`
is read through monitored options whenever `ILlmClientFactory` creates a client, so newly created
tests, chats, and task executions use the current selection without restarting the UI. An already
running request retains its own immutable client/options snapshot.

The provider choice is global. A task or workflow step may select a different *model* within that
provider, but may not silently route a single task to another provider.

## Explicit multimodal capabilities

Protocol compatibility does not imply media support. Each provider declares independent flags for
text chat, image input, image output, audio output, and future video output. Ollama and generic
OpenAI-compatible configurations remain text-only unless a capability is explicitly enabled for
the exact endpoint. Unsupported Direct Chat actions are hidden.

The first concrete output adapter targets the documented OpenAI APIs without routing media through
chat completion:

- image output: authenticated `POST /images/generations`, with the configured image model and a
  base64 image response;
- audio output: authenticated `POST /audio/speech`, with the configured speech model and voice,
  requesting WAV output.

Requests are cancellable. Sanitized logs contain operation kind, model, duration, status, and byte
count only. They exclude API keys, prompts, returned media, and raw provider error bodies.

## Settings and secret handling

The Settings page presents the **Active provider** selector beside **Test connection**. The test
therefore unambiguously addresses the selected provider. Provider endpoint, model, and timeout are
non-secret reloadable settings. The OpenAI-compatible API key is replaced only through the
write-only Settings field and is stored under the `OpenAI:ApiKey` DPAPI secret key; it is not
written to `settings.json` or to logs.

Media capability flags and media model/voice identifiers are non-secret settings. They never carry
the API key. Generated cloud media is external data and is labeled as such in Direct Chat.

For OpenRouter, the normal base endpoint is:

```text
https://openrouter.ai/api/v1
```

The model id must be copied exactly. Provider-specific qualifiers are part of the id, for example
`nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free`.

## Health and model discovery contract

`OpenAIClientService` implements both `ILlmClientService` and `IOpenAIModelCatalog`.

- Health sends authenticated `GET /models`, parses the standard `{ "data": [ { "id": ... } ] }`
  response, and confirms that the configured id is present.
- The catalog uses the same call and returns non-empty ids, distinct case-insensitively and sorted
  case-insensitively.
- The Settings command exposes the returned catalog as an editable combo box. A catalog failure is
  shown as a non-fatal status; manual model entry remains possible.

This intentionally does not use `GET /models/{model}`. OpenRouter returns HTTP 404 for that route,
even when the catalog and chat-completions endpoints are valid.

## Execution path and timeout behavior

OpenAI-compatible `ai.prompt` workflow steps use the streaming client path and concatenate content
fragments in order. This is required for endpoints whose streaming response is supported while the
SDK's non-streaming adapter cannot materialize a normal completion choice. Ollama retains its
existing path.

`DynamicOpenAIChatClient` propagates `OpenAIOptions.TimeoutSeconds` into
`OpenAIClientOptions.NetworkTimeout`; the effective network timeout is therefore the setting rather
than the SDK's hidden default. A received HTTP response header does not mean generation is complete:
free or reasoning models can spend most of the elapsed time generating the stream after headers.

For each streamed prompt, the log records a sanitized `AI prompt streaming metrics` event:

```text
operation, provider, model, input characters, first content milliseconds,
content fragments, output characters, completed, total milliseconds
```

The event contains no prompt, response text, endpoint, task content, or secret. `first content`
helps separate provider queue/first-token delay from generation duration; the remaining fields make
input size, output size, and an incomplete stream visible.

`DynamicOpenAIChatClient` rejects a stream that does not end with the OpenAI-compatible `stop` or
`tool_calls` finish reason. This protects Direct Chat from silently accepting a partial answer when
a provider closes a successful HTTP stream incorrectly. The resulting incident keeps the provider
error and stack trace locally, while the UI marks that message as failed and remains usable for the
next request. HTTP 429 remains a provider throttle/quota result: it is distinct from this
completion-integrity check and may be returned even when a per-minute limit has not been reached.

## Reasoning preference and safe fallback

Reasoning is controlled by the caller surface, independently of provider selection. Direct Chat
retains its on/off control. Tasks and AI workflow steps can additionally carry an open-ended
provider-specific effort keyword; `ai.prompt.args.thinking` accepts the same string or a boolean.
Omission inherits the resolved policy. Trust Filter, history compression, skills, authoring
helpers, AI Playground generation, and warm-up explicitly disable reasoning so their bounded
operations do not spend latency or cloud output budget on reasoning tokens.

`GenerationPolicyChatClient` expresses standard preferences with the provider-neutral
`ChatOptions.Reasoning` contract. Because neither protocol exposes a reliable per-model effort
vocabulary, custom values remain exact strings: Ollama receives native string-valued `think`, and
OpenAI-compatible chat completions receive `reasoning_effort` through raw completion options.
The UI suggestions (`low`, `medium`, `high`) are conveniences rather than a closed enum.

Task save validates each explicit provider/model/value tuple with a minimal completion request.
Successful and rejected tuples are cached; transport or server failures remain retryable. A model
rejection removes the value from the draft, while an unavailable probe warns without preventing
save. Execution does not repeat this probe and retains the safe fallback below.

If a provider rejects a reasoning option before it returns the first response update (HTTP 400/422
or a reasoning-specific rejection), the wrapper makes one clean retry with the reasoning options
removed. This protects providers and models with partial compatibility without duplicating a
partially streamed response. Cancellations, timeouts, and ordinary transport failures do not use
this retry path. A model that mandates reasoning may therefore continue with its provider default
after a user requests it off; the request does not fail solely because of that preference.

Direct Chat consumes `TextReasoningContent` separately from normal text updates. It shows readable
reasoning in its transient panel but persists only the final assistant answer. Providers may return
protected or no reasoning text; neither is treated as an application failure.

## Effective model resolution

The scoped `ModelExecutionScope` and `ModelOverrideChatClient` make the model id request-local and
restore the prior selection on success, failure, cancellation, and concurrent execution:

```text
ai.prompt step `model` → task `model` → active-provider Settings model
```

The Task Editor writes the middle value through **Task model override**. The Workflow Prompt Editor
writes the first value through **Model override (empty = task or Settings)** for `ai.prompt` only.
Empty UI values serialize as no override. `model` is invalid on a non-`ai.prompt` workflow step.
For Ollama, a scoped model is warmed before task/step execution and must exist locally. Warm-up is
not invoked for an OpenAI-compatible endpoint, including OpenRouter, so no cloud warm-up request
or credit consumption occurs. For such endpoints, provider catalog and model availability remain
authoritative.

## Validation

Unit coverage verifies `/models` health/catalog behavior, OpenAI-compatible streamed `ai.prompt`
execution, accepted and rejected streaming completion reasons, timeout propagation, task/step
override persistence, and workflow-editor serialization.
The default test filter excludes network integration tests. A real OpenRouter workflow also
validated the streamed endpoint path; its observed delay was dominated by the remote model's
generation, not file-write workflow overhead.
