# Stage 30 — Limited step-level `forEach`

Date: 2026-08-25
Status: implemented

## 1. Mission

Allow one supported workflow step to run once for each element of an array already present in
workflow context. Keep the feature intentionally smaller than a sub-workflow system: the repeated
unit is the current step only.

## 2. Locked product decisions

- `forEach` is an optional top-level property of an object-form step, not a tool argument.
- Only `ai.prompt` and `filesystem.write` opt in through declarative Tool Map metadata.
- The source is exactly one prior named-output placeholder, including an optional structured path.
- Items run sequentially in source order; parallelism is not provided.
- `{{foreach.item}}` exposes the current JSON value, structured selectors descend into it, and
  `{{foreach.position}}` is one-based.
- One repeated step accepts at most 100 items.
- The successful step output is one ordered JSON array containing every item result.
- Empty input succeeds with `[]`. A non-array or oversized input fails before the first item.
- `Retry` is isolated to the current item. A final failure is fail-fast for the batch and exposes
  no partial output. `ContinueOnError` applies to the failed outer step and does not resume items.
- Completed external effects are not transactional and are not rolled back.

## 3. Persisted contract

```json
{
  "step": "ai.prompt",
  "id": "analyses",
  "forEach": "{{mails.output.messages}}",
  "args": {
    "prompt": "Analyze #{{foreach.position}}: {{foreach.item.content}}"
  },
  "onError": "Retry",
  "retryCount": 2,
  "retryBackoffSeconds": 2
}
```

The selector must reference an earlier output that dominates the consumer on every validated
route. `previous.output`, surrounding text, task/date/model placeholders, and `foreach.*` values
are forbidden in `forEach`. Iteration placeholders are forbidden unless the current step declares
`forEach`.

## 4. Runtime and failure contract

The engine resolves the source once, validates that it is an array within the item limit, then
creates an immutable iteration context for each item. Ordinary task, model, date, prior-output,
and workflow-context placeholders keep their existing semantics. Only after all items succeed is
the aggregated JSON array committed as the step output.

If an item fails under `Retry`, only that item is invoked again using the existing exponential
backoff. After its final failure, later items are skipped. A repeated write may therefore leave
files created by earlier items even though the step has no successful context output.

## 5. Authoring contract

The workflow step editor exposes a repeat checkbox and source field only for tools whose metadata
sets `SupportsForEach`. The placeholder catalog advertises the three iteration forms. The editor
warns, but does not reject, a repeated `filesystem.write` whose `path` contains no
`{{foreach.*}}` marker. AI Workflow Helper generation and explanation prompts describe the same
syntax and boundaries.

## 6. Verification

Automated coverage must include JSON round-trip, catalog support, static source and scope
validation, graph dominance, editor state, dependency analysis, empty/non-array/oversized input,
ordered aggregation, repeated prompt execution, retry isolation, fail-fast behavior, and the
same-path warning. The non-integration solution test command is:

```powershell
dotnet test OD.MyLocalAgent.sln -m:1 --no-restore --filter "Category!=Integration"
```

The implemented gate completed with 797 passed tests, no failures, and one expected
environment-dependent Ollama probe skipped. `git diff --check` was clean.
