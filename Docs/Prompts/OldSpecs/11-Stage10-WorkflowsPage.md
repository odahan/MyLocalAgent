# STAGE 10 — Workflows page: table-based graph editor & task binding

> Run only after Stage 9's gate is green. UI-only stage: the graph engine, library store,
> parameters and `test-workflow` command already exist and are proven.

## 1. Read first (mandatory)

1. `docs/prompts/00-Shared-Specification.md` — full read incl. **§14 addendum**; §7.6, §8.3.
2. `docs/ARCHITECTURE-LEDGER.md` — full read; the Stage-9 entry defines the exact
   `IWorkflowEngine` API, validation-error shapes, `IWorkflowStore`, ToolMap metadata.
3. The current file tree + `docs/theme-audit.md`.

## 2. Mission

Give workflows their home: a dedicated **Workflows page** to create, edit (structured,
table-based — no drawing canvas), duplicate, test and delete library workflows, and rewire
the Task editor so a task binds a library workflow with its parameters. Ends with:
"program a workflow like a task" = create/edit it here, then schedule it via a task —
including one-click **"Create scheduled task…"** from a workflow row.

## 3. Design decisions (locked by the product owner — do not re-open)

| # | Decision |
|---|---|
| D1 | **Table-based editor, no canvas**: nodes list + per-node transitions table + plain-text flow preview + read-only JSON view. No drag-and-drop drawing in v1. |
| D2 | **Structured composer + live JSON preview** (read-only, generated §14 JSON). |
| D3 | **Delete blocked while referenced** (dialog lists the referencing tasks); `id` immutable after creation (UI omits id editing once created; Duplicate generates a new id). |
| D4 | Scheduling stays on tasks (ADR B + §14); this page integrates with the Task editor rather than duplicating it. |

## 4. Critical constraints (restated)

- Stage-4/5 MVVM rules: source generators only; minimal code-behind; messenger for cross-page
  refresh (workflow saved → Task editor dropdown refreshed; task saved → "used by" counts).
- The editor never hand-builds validation: it consumes the engine's load-time validation and
  renders errors verbatim (rule id + fix hint), plus lightweight field-level checks.
- ToolMap drives all pickers (aliases, arg names/required flags, safety-gate markers ⚠) —
  no hard-coded alias lists in the UI.
- Dark theme per `docs/theme-audit.md`; extend it with new control types.
- Warning-clean, complete files, XML docs, English only.

## 5. Scope

### 5.1 Workflows list page (new nav entry "Workflows", placed after "Tasks")

Columns: id, name, node count, parameter count, **"used by" (N tasks)**(computed from the task
store), last modified. Row actions: New / Duplicate (new id, suffixed uniquely) / Edit /
Delete (per D3) / **Test** (§5.3) / **Create scheduled task…** (opens the Task editor
pre-filled: `workflowId` selected, required tool groups pre-checked — from the store helper —
and one `workflowArgs` row per parameter with defaults pre-filled, required ones flagged).

### 5.2 Workflow editor page (structured composer)

- **Header**: id (editable only at creation; immutability hint), name, description.
- **Parameters table**: name / required / default / description; add/remove; inline validation
  (naming, uniqueness); tooltip: referenced as `{{param.name}}`; required parameters without
  defaults are highlighted (tasks must supply them).
- **Blocks list**: one card per node. Display order is free (editor convenience; execution
  order is the graph's); **entry node** chosen via a dropdown (badge on the card).
- **Node card**: id field; **alias picker** grouped by tool group, fed by ToolMap (shows ⚠ on
  gated aliases); **args form** generated from ToolMap metadata (required args = labeled text
  boxes; optional args addable; complex args = JSON text box with syntax error indicator);
  `onError` selector + retry fields (enabled when `Retry`); placeholder insertion helper per
  arg (menu: `{{previous.output}}`, `{{task.*}}`, `{{param.*}}` from the parameters table,
  `{{nodeId.output}}` of other nodes).
- **ai.judge card extras**: `rubric` (multiline), `input` (optional), `verdicts` editor
  (2–5 chips, unique), **fallback target dropdown (required, red-framed until set)**.
- **Transitions table** per node: ordered rows; each = condition editor (source dropdown:
  previous/other nodes; optional `field` text box with dot-path hint; op dropdown = closed §14
  set, filtered text-ops vs JSON-ops when `field` filled; value; case toggle) + target dropdown
  (nodes + `end`); move up/down, delete; the **default row** pinned last (target dropdown only,
  non-deletable). Judge nodes show a reminder that `fallback` ≠ default.
- **Right pane**: live **validation panel** (engine errors verbatim + friendly links to the
  offending card), **flow preview** (plain-text route map, e.g.
  `search → judgeNovelty ─FRESH→ summarize → writeReport → end · ╰─default→ end`), and the
  **read-only JSON preview** synced on change.
- **Save** = store write only if engine validation passes; errors block saving (list stays
  visible; partial state kept in-memory per page).

### 5.3 Testing & task integration

- **Test** action: spawn `OD.MyLocalAgent.Runner.exe test-workflow --id <id> [--args '<json>']`
  via the existing `IRunnerService` plumbing (args editor dialog: one row per parameter);
  streamed console output docked; cancellable; on completion, offer "open report".
- **Task editor rework** (page 3): the workflow section becomes a three-way choice —
  *None* / *Inline* (existing JSON area, now validated as a chain graph) / *Library* (dropdown
  of workflows + parameters table auto-generated (`workflowArgs`, defaults editable, required
  flagged) + "Open in Workflows page" link). Saving validates per §14 rule 9 (exclusivity,
  existence, params, whitelist containment with the missing groups listed).
- **Navigation messages**: workflow saved/deleted → refresh Task editor dropdown and
  list-page "used by" counts via messenger.

**Out of scope**: canvas visualization; boolean condition composition; loops; new tools;
any engine change (Stage 9 owns it — if the editor exposes an engine gap, record it and
surface a readable error, never patch around silently).

## 6. Working agreements

- All new XAML must compile; verify pickers against real ToolMap metadata at runtime.
- Reuse Stage-4/5 shell mechanics (busy overlay, snackbar, runner streaming, dialog patterns).
- `docs/theme-audit.md`: check off every control introduced (cards, chip editor, split panes…).

## 7. Stage gate (all must pass — show command output)

1. `dotnet build OD.MyLocalAgent.sln` — zero warnings.
2. `dotnet test OD.MyLocalAgent.sln` — all green (add UI-free tests only where natural, e.g.
   a JSON-sync round-trip helper if extracted).
3. Manual launch (flag human-verifiable if no GUI): create the §14 daily-brief graph entirely
   in the editor (validation panel stays clean), save, Test it from the page, **Create
   scheduled task…** → task editor shows pre-checks + parameter rows; duplicate then delete a
   referenced workflow → **blocked with the referencing task listed**; delete an unreferenced
   one → gone; inline task from Stage 3 still opens/edits/saves unchanged.
4. `docs/theme-audit.md` updated; docs sanity (README workflow chapter matches the editor).

**Stage-10 acceptance addendum for `docs/acceptance-report.md`:**

- [ ] A graph with a judge node and two branches buildable end-to-end without touching JSON
  by hand (JSON preview matches what the engine accepts).
- [ ] Judge card refuses save without `fallback` (per §14 rule 7), with a readable message.
- [ ] Task binding: exclusivity inline/library enforced; required params flagged; whitelist
  gaps listed before save.
- [ ] Delete-block dialog lists referencing tasks; `used by` counts and dropdowns refresh via
  messenger without restart.

## 8. Before you finish

1. Append the **Stage 10** entry to the ledger: pages/VMs added, messenger messages, editor
   ↔ engine contract as consumed (validation error rendering), theme-audit delta, deviations,
   GUI-verification status.
2. Stage report: file tree delta, gate summary, acceptance addendum verbatim ✅/⏳, and —
   honestly — which canvas-only conveniences users will miss (for the potential future stage).
