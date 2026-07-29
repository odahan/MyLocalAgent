# STAGE 9 — Graph workflow engine, workflow library & parameters

> Run only after **Stages 0–8** are finished (`docs/acceptance-report.md` complete).
> This stage is backend-only. The Workflows editor page comes in Stage 10.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read incl. addenda §12/§13;
   **§7.4, §8.1, §8.3, §9-B/G** are being extended here.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; recorded APIs are binding.
3. The current file tree.

## 2. Mission

Upgrade the workflow concept from a **sequential chain** to a **conditional graph of blocks**
(nodes) connected by **tested transitions** (edged routing), and turn workflows into
**first-class reusable entities** stored in a library, bindable from tasks, with named
parameters. Compatibility rule: the existing §8.3 syntax remains valid — a chain is a graph
where every node has a single default transition.

## 3. Design decisions (locked by the product owner — do not re-open)

| # | Decision |
|---|---|
| D1 | **Library + reference**: `WorkflowDefinition` stored one JSON per workflow in `%LOCALAPPDATA%\OD.MyLocalAgent\workflows\<id>.json`; a task references it via a new `workflowId` field (mutually exclusive with inline `workflow`). Scheduling stays on tasks. |
| D2 | **Routing = deterministic predicates + an LLM judge** (`ai.judge` block). Judge output is strict bounded JSON; a judge node **must declare a `fallback` target** used when judging fails. |
| D3 | **DAG only**: cycle detection at validation rejects cyclic graphs; termination is guaranteed by construction (plus existing per-node Retry caps). Repeat behavior is expressed via `Retry` or task re-scheduling, not loops. |
| D4 | **Two stages**: this one is engine + library + params + runner + tests; the editor UI is Stage 10. |
| D5 | **Parameters** (validated earlier): `{{param.x}}` placeholders resolved at binding time from `task.workflowArgs` merged over parameter defaults. |
| D6 | **Integrity** (validated earlier): deleting a referenced workflow is blocked (error lists referencing tasks); workflow `id` is immutable after creation. |

## 4. Critical constraints (restated)

- Layering: `Workflows → Core` (+ `→ Guardrails` added this stage, recorded); Guardrails stays
  free of workflow types. No cycles. Typed exceptions across boundaries (spec §9-A).
- The judge reuses Stage-7 plumbing discipline: tool-less single-turn call, strict JSON,
  low temperature, tolerant parse, typed failure — **never** a free-form verdict.
- Backward compatibility: `dotnet test` Stage-3 engine tests keep passing (adapted only where
  the engine API itself is superseded — recorded in the ledger); existing sample tasks run
  unchanged.
- Warning-clean, complete files, XML docs, English only, `TimeProvider` injected.

## 5. Scope

### 5.1 Contracts (normative)

**Library workflow file** (`workflows/<id>.json`):

```json
{
  "id": "daily-brief",
  "name": "Daily brief",
  "description": "Search, judge novelty, summarize, save.",
  "parameters": [
    { "name": "topic", "required": true, "description": "Search topic" },
    { "name": "reportPath", "required": false, "default": "reports/daily-brief.md" }
  ],
  "graph": {
    "entry": "search",
    "nodes": [
      {
        "id": "search",
        "step": "web.search",
        "args": { "query": "{{param.topic}}", "maxResults": 5 },
        "onError": "Retry", "retryCount": 2, "retryBackoffSeconds": 2,
        "transitions": [ { "default": "judgeNovelty" } ]
      },
      {
        "id": "judgeNovelty",
        "step": "ai.judge",
        "args": {
          "rubric": "Do these results contain significant NEW information worth a daily brief?",
          "input": "{{search.output}}",
          "verdicts": ["FRESH", "NOTHING_NEW"]
        },
        "fallback": "end",
        "transitions": [
          { "when": { "field": "verdict", "op": "eq", "value": "FRESH" }, "to": "summarize" },
          { "default": "end" }
        ]
      },
      {
        "id": "summarize",
        "step": "ai.summarize",
        "args": { "text": "{{search.output}}" },
        "transitions": [ { "default": "writeReport" } ]
      },
      {
        "id": "writeReport",
        "step": "filesystem.write",
        "args": { "path": "{{param.reportPath}}", "content": "{{summarize.output}}" },
        "transitions": [ { "default": "end" } ]
      }
    ]
  }
}
```

**Task binding** (extends §8.1): optional `workflowId` (string) + optional `workflowArgs`
(object). Exactly one of `workflow` / `workflowId` may be present (load-time validation);
`prompt` semantics unchanged (`{{task.prompt}}`).

**New ToolMap row**: alias `ai.judge` ↔ args `rubric` (required), `input` (optional, defaults
to `{{previous.output}}`), `verdicts` (required, 2–5 unique strings) → output
`{ "verdict": "<one of verdicts>", "reason": "<short>" }`. Not an agent tool (like skills);
engine-level block.

**Condition grammar (closed set — one condition per transition, no boolean algebra in v1):**

```json
{ "when": { "source": "<optional placeholder ref; default = {{previous.output}}>",
            "field": "<optional dot path into JSON output>",
            "op": "...", "value": "...", "caseSensitive": false } }
```

- Text ops (no `field`): `contains`, `notContains`, `startsWith`, `endsWith`, `equals`,
  `notEquals`, `matches` (regex, validated at load), `isEmpty`, `isNotEmpty`.
- JSON ops (`field` present; source parsed as JSON, dot path extracted):
  `eq`, `ne`, `gt`, `ge`, `lt`, `le` (numeric fields compared as numbers; booleans/strings for
  `eq`/`ne`), `exists`, `notExists`. Case-insensitive by default for string comparisons.
- A node failed-but-continued (`ContinueOnError`) outputs `{ "error": "<message>" }`, so
  transitions can route remediation via `{ "field": "error", "op": "exists" }`.

**Execution semantics**: start at `graph.entry`; after each node, evaluate `transitions` in
order — first `when` true wins, else the (mandatory, last) `default`; target `end` finishes.
Judge failure (`JudgeFailedException`) → node `fallback` target (mandatory for `ai.judge`,
validation-enforced), the engine records the fallback reason in the trace. `WorkflowResult`
gains an ordered **execution trace**: nodeId, status, durationMs, output size, transition
taken (`to` + matched condition or `default`/`fallback`). Report Markdown renders the route map.

**Validation (load-time, deterministic — one test per rule at minimum):**
1. `entry` exists; node ids unique, `[a-z][a-zA-Z0-9]*`; ids immutable is a *store* rule. 
2. Every `to`/`fallback` target exists or is `end`.
3. Exactly one `default` transition per node, as the last entry.
4. No cycles (DFS).
5. Every node reachable from `entry`; every node can reach `end`.
6. Aliases resolve in the ToolMap; closed op set; `matches` regex compiles.
7. `ai.judge`: `rubric` non-empty, `verdicts` 2–5 unique, `fallback` present.
8. Parameters: names `[a-z][a-zA-Z0-9]*`, unique; every `{{param.*}}` referenced exists;
   binding: value = `task.workflowArgs[x]` ?? `default` ?? (required → load error listing the
   parameter; else empty string).
9. Task-level: `workflow`/`workflowId` exclusivity; `workflowId` exists in store; required
   **tool groups of all node aliases ⊆ task `tools`** (error lists missing groups; `ai.*`
   steps need no group); unknown `workflowArgs` keys rejected.

### 5.2 Components

- **Guardrails project** (`Judging` namespace): `IConditionJudgeService.JudgeAsync(rubric,
  input, verdicts, ct)` → `JudgeResult { Verdict, Reason }`; tool-less single-turn via
  `ILlmClientFactory.Create()`, strict JSON + tolerant parse; unparseable/invalid verdict →
  typed `JudgeFailedException`. Routed prompt constant versioned (same bar as the screener's).
- **Core**: `IWorkflowStore` / `JsonWorkflowStore` (same pattern as `JsonTaskStore`: lock,
  schema+graph validation on load, `FileSystemWatcher`, `DeleteAsync` throws
  `WorkflowInUseException` listing referencing tasks — D6). Task model + loader extended per
  §5.1 (parse both bindings); required-tool-groups computation helper on a workflow.
- **Workflows project**: `GraphWorkflowEngine` supersedes the sequential one (chain syntax
  compiles to a chain graph in memory — keep a public converter, it serves both legacy tasks
  and tests); context/placeholder resolution now includes `{{param.*}}` (binding-time) and
  per-node `{{nodeId.output}}`; trace collection; graph validation per §5.1 rules.
- **Runner**: new `test-workflow --id <workflowId> [--args '<json>']` — loads the library
  workflow, binds args over defaults, derives the whitelist as the union of required tool
  groups (documented: production tasks use their own `tools`), executes, prints the trace +
  final output; `run --task` resolves `workflowId` via the store (missing/blocked → exit 1
  with clear message).

### 5.3 Samples & docs

- `docs/samples/` — library pair: `workflows/mail-triage.json` (graph routing on
  `guardrails.trustScore` via JSON-field predicates) + task `mail-triage.json` (binding with
  `workflowArgs`); keep existing chain samples working.
- Spec addendum **§14 “Graph workflows addendum”** + **ADR J** in §9: graph superset rule;
  judge discipline & mandatory fallback; DAG-only rationale; chain = special case; judge
  placed in Guardrails/Judging because that project owns tool-less LLM evaluation.
- README: rewrite the workflow chapter — chain form, graph form, condition grammar table,
  routing-on-structured-output guidance, two graph examples (daily brief with novelty gate;
  mail triage), `test-workflow` reference.
- `docs/acceptance-report.md`: Stage-9 addendum table (§7).

**Out of scope (Stage 10 or later):** any UI; visual canvas; boolean composition of
conditions; loops; `web_fetch`-style new tools.

## 6. Working agreements

- The sequential engine's public API may be superseded, not silently broken: keep chain
  execution semantics observably identical (same results on Stage-3 scenarios) — adapt tests
  only where the API shape is replaced, and record every such change.
- Judge prompt is security/quality-critical: short, strict, versioned; changes = ledger entry.
- Graph validation must produce **enumerable, user-readable** errors (rule id + what + how to
  fix) — Stage 10's editor will surface them verbatim.

## 7. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green, incl. every §5.1 validation rule and the
   chain-compatibility regression tests.
3. Runner probes: `test-workflow --id mail-triage --args '{ }'` behaviors recorded (with
   Ollama up where possible — else flag human verification): predicate routing verified via
   fixtures; judge fallback path demonstrated with a stubbed/failing judge in tests;
   chain-format sample task still runs end-to-end unchanged.

**Stage-9 acceptance addendum:**

- [ ] Chain syntax backward compatible: Stage-3 scenarios produce identical results.
- [ ] Judge nodes provably cannot run without `fallback`; judge failure takes it, traced.
- [ ] DAG rules: cyclic/ unreachable/ no-default graphs rejected with readable errors.
- [ ] Params: precedence arg > default > missing-required-error verified; `{{param.*}}`
  resolution traced in reports.
- [ ] Store integrity: deleting a referenced workflow blocked with task list; id immutable.

## 8. Before you finish

1. Append the **Stage 9** entry to the ledger: graph contracts (normative JSON), condition
   grammar table, validation rule ids, judge service API, store APIs, engine API supersessions,
   deviations, residual risks (judge flakiness classes observed, if any).
2. Stage report: file tree delta, gate summary, acceptance addendum verbatim ✅/⏳, and the
   exact `IWorkflowEngine` + validation-error shapes Stage 10's editor must consume.
