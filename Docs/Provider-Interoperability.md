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

## Settings and secret handling

The Settings page presents the **Active provider** selector beside **Test connection**. The test
therefore unambiguously addresses the selected provider. Provider endpoint, model, and timeout are
non-secret reloadable settings. The OpenAI-compatible API key is replaced only through the
write-only Settings field and is stored under the `OpenAI:ApiKey` DPAPI secret key; it is not
written to `settings.json` or to logs.

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

## Effective model resolution

The scoped `ModelExecutionScope` and `ModelOverrideChatClient` make the model id request-local and
restore the prior selection on success, failure, cancellation, and concurrent execution:

```text
ai.prompt step `model` → task `model` → active-provider Settings model
```

The Task Editor writes the middle value through **Task model override**. The Workflow Prompt Editor
writes the first value through **Model override (empty = task or Settings)** for `ai.prompt` only.
Empty UI values serialize as no override. `model` is invalid on a non-`ai.prompt` workflow step.
For Ollama, a scoped model is warmed before task/step execution and must exist locally. For an
OpenAI-compatible endpoint, provider catalog and model availability remain authoritative.

## Validation

Unit coverage verifies `/models` health/catalog behavior, OpenAI-compatible streamed `ai.prompt`
execution, timeout propagation, task/step override persistence, and workflow-editor serialization.
The default test filter excludes network integration tests. A real OpenRouter workflow also
validated the streamed endpoint path; its observed delay was dominated by the remote model's
generation, not file-write workflow overhead.
