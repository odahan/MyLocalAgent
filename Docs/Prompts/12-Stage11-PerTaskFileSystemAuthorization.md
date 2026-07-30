# OD.MyLocalAgent — STAGE 11 — PER-TASK FILE-SYSTEM AUTHORIZATION

Read `Docs/Prompts/00-Shared-Specification.md` and
`Docs/ARCHITECTURE-LEDGER.md` completely before changing any implementation file. The shared
specification, including §14, is normative. Record the finished stage in the architecture
ledger before completion.

## Goal

Keep `FileSystem:RootPath` as the global sandbox for relative paths and add task-owned
absolute authorized directories with read-only or read-write access. Apply the same immutable
policy to deterministic workflows, agent-selected tools, prompt-only tasks, and Dropbox local
paths without changing Tool Map aliases or arguments.

## Scope

1. Add the `fileSystemAccess` task contract, string access enum, validation, and canonical
   serialization described by shared specification §14.
2. Replace the single-root path resolver with an execution-context-aware resolver that keeps
   relative `RootPath` behavior and authorizes absolute paths only through the current task.
3. Reject traversal, sibling-prefix confusion, and reparse-point escapes. Validate both sides
   of moves and the local side of Dropbox transfers.
4. Propagate immutable execution context through `IToolRegistry`,
   `SequentialWorkflowEngine`, `IMyLocalAgentService`, Runner commands, prompt testing, and
   taskless interactive tool use.
5. Add the MVVM **Authorized locations** task-editor section and folder selection service.
6. Update task samples, README, both offline manuals, configuration guidance, acceptance
   material, and tests.

## Product decisions

- The application has no deployed task population; no runtime schema migration is required.
- Relative paths continue to resolve beneath the required global `FileSystem:RootPath`.
- Empty `fileSystemAccess` means no absolute-path grants.
- Access values are `ReadOnly` and `ReadWrite`.
- A more-specific nested rule wins.
- A saved but currently unavailable directory is allowed by validation and fails only when an
  operation needs it.
- Tool tester and AI playground have no additional absolute grants unless a future stage adds
  explicit session-scoped authorization.
- No new package or external service is permitted.

## Engineering constraints

- Preserve the project dependency graph and the static Tool Map as the single source of truth.
- Do not place mutable task state in singleton tools or registries.
- Keep every public type and member XML-documented in English.
- Keep UI text, log messages, identifiers, and comments in English.
- Use CommunityToolkit.Mvvm source generators only.
- Preserve `Safety:AllowFileDelete`, tool-group whitelisting, HTTP allowlisting, incident
  routing, Guardrails behavior, and runner/UI execution parity.
- Update earlier-stage files only where this cross-cutting authorization context requires it.

## Required tests

- Task JSON round-trip and invalid-rule validation.
- Relative access beneath `RootPath`.
- Authorized and unauthorized absolute paths.
- Read-only versus read-write operations.
- Multiple roots and nested-rule precedence.
- Move source and destination authorization.
- Delete location plus Safety gate.
- Dropbox upload/download local authorization.
- Traversal, sibling-prefix, symbolic-link, junction, and reparse-point escape attempts.
- Workflow, prompt-only agent, prompt-step agent, Runner direct-tool, Tool tester, and AI
  playground context behavior.
- Concurrent tasks with disjoint rules cannot observe each other's authorization.
- Task-editor load, add, edit, remove, validation, and save behavior.

## Documentation deliverables

- Shared specification §14.
- Architecture ledger Stage 11 entry.
- README and both offline manuals.
- Canonical task samples and acceptance guidance.

## Gate

Run and iterate until both commands succeed:

```powershell
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release
dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
```

Do not claim completion without the executed command output.
