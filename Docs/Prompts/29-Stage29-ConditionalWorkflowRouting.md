# Stage 29 — Conditional workflow routing

## Purpose

Stage 29 introduces a small, deterministic choice mechanism in workflows. It lets a tool-free
`ai.prompt` return a validated JSON object and lets `flow.decision` route execution from a value
in that object or from any other prior output.

This stage deliberately does not add a general expression language, loops, or list operators.

## Persisted syntax

Object-form workflow steps gain two optional properties:

- `next`: the id of the next step, or `@end`. When absent, execution continues with the next
  item in the workflow array. On a decision, this is the false route.
- `responseSchema`: a portable JSON-schema subset, accepted only by `ai.prompt` without
  `toolAliases`.

The new internal control step has this form:

```json
{
  "step": "flow.decision",
  "id": "route",
  "args": {
    "source": "{{classification.output}}",
    "path": "score",
    "operator": "greaterThanOrEqual",
    "value": 0.8,
    "caseSensitive": false,
    "negate": false,
    "target": "accept",
    "fallback": "manualReview"
  },
  "next": "reject"
}
```

`source`, `operator`, and `target` are required. `path`, `caseSensitive`, `negate`, and
`fallback` are optional. `value` is required only by operators that compare against a value.

Supported operators are:

- equality: `equals`;
- text: `contains`, `startsWith`, `endsWith`, `matches`;
- presence and emptiness: `exists`, `isNull`, `isEmpty`, `isNotEmpty`;
- numbers: `lessThan`, `lessThanOrEqual`, `greaterThan`, `greaterThanOrEqual`.

True follows `target`. False follows `next`, or the next array item. `fallback` is followed only
when the condition cannot be evaluated, such as invalid JSON, a missing path for an operator other
than `exists`, an incompatible type, an invalid number, or a regular-expression timeout. Without `fallback`, evaluation fails
under the normal step error policy.

## Structured AI response

```json
{
  "step": "ai.prompt",
  "id": "classification",
  "args": { "prompt": "Classify: {{task.prompt}}" },
  "responseSchema": {
    "type": "object",
    "properties": {
      "category": { "type": "string", "enum": ["sales", "support", "other"] },
      "score": { "type": "number", "minimum": 0, "maximum": 1 }
    },
    "required": ["category", "score"],
    "additionalProperties": false
  }
}
```

The schema is passed to the configured provider through the Microsoft.Extensions.AI response
format abstraction. MyLocalAgent then parses and validates the returned JSON locally before it
becomes step output. Markdown fences or surrounding prose are rejected.

The portable subset supports:

- types `object`, `array`, `string`, `number`, `integer`, `boolean`, and `null`;
- `properties`, `required`, `additionalProperties`, and `items`;
- `enum`, `const`, `minimum`, `maximum`, `minLength`, `maxLength`, and `description`.

The root must be an object. Schemas are limited to 65,536 UTF-8 bytes and 16 nesting levels.
Unsupported keywords are rejected.

## Graph and execution rules

Before a workflow can be saved or run:

- every explicit route must reference an existing step id or `@end`;
- every step must be reachable from the first step;
- the graph must be acyclic;
- a referenced step output must be guaranteed on every path to its consumer;
- a `flow.decision` technical output cannot be referenced.

`flow.decision` requires no tool permission and does not alter `previous.output` or
`workflow.context`. A handled fallback adds a warning to the execution result.

## Delivered surface

- persisted workflow contracts and strict JSON conversion;
- catalog metadata and the visual workflow-step editor;
- sequential engine routing and local decision evaluator;
- provider-neutral structured `ai.prompt` execution plus local response validation;
- static target, reachability, cycle, and guaranteed-output validation;
- AI Workflow Helper instructions;
- README, architecture ledger, French and English user manuals, and technical manual;
- unit and non-integration regression coverage.

## Deferred intentionally

- `foreach` and all other loop constructs;
- compound Boolean expression trees (`and` / `or` groups);
- arbitrary scripting or user-defined operators;
- dynamic route ids;
- structured responses combined with step-level tool calls;
- provider-specific schema extensions.

These omissions keep Stage 29 small, auditable, provider-neutral, and compatible with the
current array-based workflow architecture.
