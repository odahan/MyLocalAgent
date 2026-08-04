# STAGE 25 — Human Workflow Text Editing

> This stage makes human-readable workflow text the default Task editor surface while retaining
> JSON as the persisted runtime format and as an expert editing mode. It requires the existing
> declarative Tool catalog, Stage 17 authoring aids, and the current workflow validation path. It
> is independent of Stages 19, 22, 23 and 24, although their metadata must participate when they
> are present.

## 0. Read first (mandatory)
- Docs/Prompts/00-Shared-Specification.md — full read, including addenda §12–§19. §19 is the normative contract for this stage.
- Docs/ARCHITECTURE-LEDGER.md — full read; recorded APIs, privacy boundaries, protocol channels, and concurrency constraints are binding.
- The post-Stage-18D implementations

## 1. Mission

Replace the default large raw-JSON workflow textbox with a text-first workflow editor that a
user can read and edit without typing JSON structure. The editor has a deterministic parser and
canonical renderer. AI Helper remains an optional natural-language authoring aid; it is not the
parser, validator, or reverse converter.

JSON remains the sole persisted workflow representation and is available in a clearly labeled
**JSON edit (advanced)** mode for users who need complete low-level freedom.

## 2. Locked product decisions

| # | Decision |
|---|---|
| S25-D1 | JSON remains the persisted `ScheduledTask.Workflow` format, runner input, import/export format, runtime contract and canonical serialization. This stage adds no XML task format and performs no task-file migration. |
| S25-D2 | The default editor is **Workflow text**. It is a deterministic domain-specific projection of the existing workflow model, not a natural-language prompt and not an LLM response. |
| S25-D3 | The canonical renderer is code, never an AI call. `JSON → workflow text → JSON` must preserve the validated workflow semantics exactly. |
| S25-D4 | `workflow text → JSON → workflow text` normalizes presentation. It need not preserve the user’s spacing, capitalization of labels, blank lines, or property order, but it must preserve valid semantics. |
| S25-D5 | JSON edit remains available for advanced users. It uses the existing validation, formatting and assisted-change safety rules; no valid JSON capability is removed merely because the text projection does not expose it conveniently. |
| S25-D6 | AI Helper accepts free user text as today, produces a validated workflow proposal, and then shows the canonical Workflow text result. It must never claim that its proposal was parsed from Workflow text. |
| S25-D7 | AI Explain remains an explanatory tool. It does not serve as the inverse of AI Helper and is not a persistence or conversion format. |
| S25-D8 | Long user content is edited in true multi-line controls. Prompt, SQL, Markdown and plain multiline values are never shown as JSON-escaped text in the default experience. |
| S25-D9 | The parser is strict and diagnostic. It gives a line and column plus a content-free explanation; it never guesses a missing tool, id, placeholder, quote, indentation level, or error policy. |
| S25-D10 | The JSON and Workflow text modes share one in-memory draft. Switching modes validates the outgoing mode; an invalid draft remains visible and is not silently replaced. |
| S25-D11 | Structural punctuation must not be required for ordinary authoring. The default surface offers step insertion, action selection, output-reference insertion, and contextual argument controls. |
| S25-D12 | Every registered tool argument declares a UI editor hint. The hint changes editing only; the existing JSON argument shape and runtime typing remain unchanged. |

## 3. Deterministic workflow-text grammar

Use a compact, English-label, indentation-sensitive grammar with LF line endings. It must not
require braces, brackets, JSON quotes, pipe characters or placeholder typing for normal editing.
Labels are case-insensitive on input and canonicalized on output.

Canonical example:

```text
Step search-dotnet
  Action: Search the web
  Query: .NET MAUI release notes
  Max results: 5

Step evaluate
  Action: AI prompt
  Prompt:
    Retain only significant changes for experienced .NET developers.
    Return concise reasons.
  Include prior results: Yes
  Temperature: 0.1

Step store-report
  Action: Write file
  Path: reports/daily.md
  Content: Output of evaluate
```

The renderer uses a stable action display name from the discovered catalog but includes the
canonical alias in an inspectable details line or tooltip. The parser accepts `Action alias:` as
an explicit advanced alternative where a display name is ambiguous. Step ids are shown and can
be edited, but Add step generates a valid unique id automatically.

Scalar values are represented after a label. A multi-line value begins with its label ending in a
colon and is indented by four spaces until the next non-indented step property or step. A literal
line that would be syntactically ambiguous is entered through the dedicated field editor; the
renderer emits the smallest unambiguous escaped form and explains it in the field tooltip.

Arguments whose JSON kind is Object or Array use a focused structured argument dialog. This
preserves arbitrary valid JSON expressiveness without forcing every workflow author to type JSON.
The dialog presents a JSON mode only for that one value and validates it independently.

`Output of <step id>` is a display projection of a whole-output placeholder. The reference picker
also supports choosing a documented JSON field/index path. Its renderer emits a readable textual
reference and its parser resolves it deterministically to the existing workflow placeholder.

## 4. Core contracts

Add non-WPF, fully unit-testable contracts:

```csharp
public enum WorkflowEditorKind
{
    SingleLine,
    Multiline,
    Sql,
    Markdown,
    JsonObject
}

public sealed record WorkflowTextParseResult(
    bool IsValid,
    IReadOnlyList<WorkflowStepDefinition> Steps,
    IReadOnlyList<WorkflowTextDiagnostic> Diagnostics);
```

Add `WorkflowTextParser`, `WorkflowTextRenderer`, `WorkflowTextDiagnostic`, and
`WorkflowTextProjection`. They take catalog snapshots and the existing placeholder catalog;
they do not depend on WPF, LLM services, Settings, files or a current task singleton.

Extend declared `ToolArgument` metadata with `WorkflowEditorKind`. Existing arguments default to
`SingleLine`. `ai.prompt` is `Multiline`; Stage 19 SQL is `Sql`; Markdown/document content is
`Markdown`; Object is `JsonObject`. The Tool catalog and all assisted authoring surfaces must use
the same metadata. Do not create per-tool UI type tables.

Unknown fields and aliases are an error in Workflow text. JSON edit retains the current raw JSON
behavior and is the escape hatch for every supported model feature not yet represented by the
text grammar. The parser must preserve all legal `onError`, retry, generation-options,
include-context, object argument and placeholder semantics.

## 5. User interface

Replace the primary Task editor workflow card with:

- a **Workflow text** / **JSON edit (advanced)** segmented switch;
- a validated multi-line Workflow text editor as the default;
- **Add step** and **Insert output reference** commands that work at the active caret or through
  a focused property editor;
- an action picker generated from the Tool catalog and existing Tool catalog dialog;
- field editors generated from argument metadata, including full multi-line Prompt, SQL and
  Markdown editors;
- concise inline parse diagnostics with line/column and a short error summary;
- existing Validate, AI Helper, AI Explain, tool-group proposal, authorization proposal, Undo and
  Save flows, all operating on the shared draft.

The raw JSON text box is collapsed unless the advanced mode is selected. It must retain formatted
JSON, direct keyboard editing, Validate, and the current Tool catalog insertion behavior. When a
JSON-only feature cannot be represented by the first text grammar version, keep it editable in
JSON mode and display a non-blocking explanation before switching to text; never discard it.

Prompt/SQL/Markdown field editors have LF-canonical working text, line numbers, keyboard-friendly
tab behavior, Copy and Paste commands, and no automatic content rewriting other than canonical
line endings. SQL editing is presentation only; the Stage 19 one-statement and parameter rules
remain the execution validation authority.

## 6. Conversion and assisted authoring behavior

When switching from JSON mode, parse JSON through the existing task validator, render canonical
Workflow text, and replace the text only after success. When switching from Workflow text, parse
the text, validate its constructed workflow through the existing validator, serialize canonical
JSON, and replace JSON only after success.

AI Helper continues to receive the user’s free-form request and current authorized catalog. Its
response is validated by the existing deterministic workflow-validation path. A valid proposal is
serialized to JSON and immediately rendered as Workflow text. Invalid output remains a helper
failure with the established safe error handling; it is never partially interpreted as text.

AI Explain receives the normalized workflow and offers a concise explanation. It must not echo
raw prompt or SQL values in logs, diagnostics, or errors. The user may request it from either
editor mode; results are semantically identical.

## 7. Privacy and accessibility

- Workflow text, JSON, prompt, SQL, Markdown, paths, output references and diagnostics are
  sensitive task content. They never enter normal logs, incidents, telemetry, clipboard or
  persistence except through explicit Save/Copy operations.
- All commands are keyboard reachable, have English accessible names, retain focus predictably,
  work at 100–200% scaling, and have visible error contrast in both available themes.
- Do not auto-run, auto-save, call the LLM, enable a tool group, grant a path, or alter Safety
  settings when converting modes.

## 8. Tests and gate

Add broad deterministic parser/renderer tests covering every built-in tool, all argument kinds,
multi-line prompt/SQL/Markdown values, explicit ids, display-name ambiguity, unknown aliases,
blank/invalid indentation, quotes, output references, placeholders, generation options, retries,
object arguments and JSON-only preservation. Add property-style round-trip fixtures:

```text
validated JSON → Workflow text → validated JSON = semantic equality
valid Workflow text → JSON → Workflow text = canonical text equality
```

Add ViewModel tests for switching modes, invalid draft preservation, helper result projection,
raw JSON escape hatch, generated field editors, caret insertion, undo and no-sensitive-log
behavior. Existing Stage 17 tests must remain valid or be updated only where this stage
intentionally replaces its primary raw-JSON presentation.

Run:

```text
dotnet build OD.MyLocalAgent.sln --configuration Release --no-restore -m:1
dotnet test OD.MyLocalAgent.sln --configuration Release --no-build --no-restore -m:1
```

Update the architecture ledger, shared specification addendum, README, First Run guide, workflow
authoring documentation, theme audit and samples. Document JSON edit explicitly as an advanced
mode, not as a deprecated or unsupported escape hatch.
