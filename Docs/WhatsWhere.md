# What's Where

Application version: **2.6.8**.

All file and folder locations used by OD.MyLocalAgent after deployment.

## Installation Directory

Default: `%LOCALAPPDATA%\Programs\OD.MyLocalAgent`

| Path | Contents |
|------|----------|
| `UI\OD.MyLocalAgent.UI.exe` | UI executable |
| `UI\appsettings.json` | Base configuration (UI) |
| `Runner\OD.MyLocalAgent.Runner.exe` | Runner executable |
| `Runner\appsettings.json` | Base configuration (Runner) |

These files are replaced on each publish. Do not edit `appsettings.json` here — use user settings instead.

## Global User Configuration Directory

Default: `%LOCALAPPDATA%\OD.MyLocalAgent`

| Path | Contents |
|------|----------|
| `settings.json` | User settings (overrides `appsettings.json`) |
| `secrets.dat` | DPAPI-protected secrets (API keys, OAuth tokens) |
| `Auth/Google/` | Google OAuth token data |

This directory is tied to the Windows user and survives workspace changes. Application-owned
tasks, conversations, logs, examples, and outputs are stored in the configured workspace.

## Workspace Data Directory

Default workspace: `C:\OD.MyLocalAgent`

| Path | Contents |
|------|----------|
| `.od-my-local-agent/tasks/` | Persisted task definitions (one JSON file per task) |
| `.od-my-local-agent/tasks/backups/` | Archived task definitions retained for reference |
| `.od-my-local-agent/examples/tasks/` | Ready-to-copy task examples |
| `.od-my-local-agent/data/direct-chat.db` | Direct Chat conversations, messages, attachment content, and cumulative memory summaries (SQLite, not encrypted) |
| `.od-my-local-agent/data/knowledge/` | Private Knowledge / RAG library root |
| `.od-my-local-agent/data/knowledge/knowledge.db` | Knowledge document metadata, revisions, chunks, embedding vectors, active-chunk FTS5 index, index contract, and cleanup queue (SQLite, not encrypted) |
| `.od-my-local-agent/data/knowledge/documents/` | Canonical copies of imported source documents, named by the application |
| `.od-my-local-agent/data/knowledge/staging/` | Temporary files used while importing, replacing, or reindexing documents; not user content storage |
| `.od-my-local-agent/logs/application/` | Rolling application logs and diagnostic-reset archives |
| `.od-my-local-agent/logs/incidents.jsonl` | Incident log |
| `.od-my-local-agent/logs/runs/` | Per-execution runner output logs |
| `.od-my-local-agent/outputs/reports/` | Generated Markdown reports and execution index |

`.od-my-local-agent` is marked hidden on Windows and is reserved for application services.
Managed task and Direct Chat file tools omit it from listings and reject direct, recursive, and
parent-destructive access. The hidden attribute is a visual safeguard, not a security boundary.
The unrestricted `PowerShellExecution` and `ShellExecute` capabilities run outside the managed
path policy and should be enabled only for trusted workloads.

`direct-chat.db` is an application-owned database. It is separate from the SQLite Tool, is not
available to workflows, and should be included when backing up Direct Chat history. Conversation
deletion is permanent. The history database contains message text in clear form; DPAPI protects
secrets, not chat content.

The Knowledge / RAG library is also application-owned and separate from the SQLite Tool. Its
database and imported files are not encrypted at rest; include the complete
`.od-my-local-agent/data/knowledge/` directory when backing up the library. Do not alter its
contents manually: imports, replacements, deletions, and reindexing are managed from the
Knowledge page.

Editorial proposal-memory databases are ordinary task-owned files at the path chosen in the
workflow, for example
`C:\Users\<user>\Dropbox\VeilleAutomatique\editorial-proposals.db`. They never live implicitly
under `.od-my-local-agent`, are not global application state, and must be covered by the task's
ReadWrite Authorized location. Back them up or remove them with the other user-owned files in
that directory. Only `sqlite.createProposalDatabase` may create or migrate their schema;
`research.discover` opens them ReadOnly. The 2.5.1 editorial workflow reserves selections,
prepares reports, and confirms exports through dedicated deterministic SQLite aliases.

| Proposal-memory implementation | Responsibility |
|---|---|
| `src/OD.MyLocalAgent.Core/ProposalMemoryService.cs` and `ProposalMemoryService.Editorial.cs` | Schema creation/migration, deterministic reservation/export lifecycle, normalization, local embeddings, and exact cosine filtering |
| `src/OD.MyLocalAgent.Core/ResearchDiscoverTool.cs` | Optional ReadOnly proposal-memory filtering and aggregate diagnostics |
| `src/OD.MyLocalAgent.Core/SqliteTool.cs` | Authorized proposal-memory, reservation, report, historical-import, and embedding-rebuild aliases |
| `Docs/samples/editorial-watch-daily-db.json` | Complete scheduled editorial workflow with deterministic anti-repetition and confirmed export |

## Configuration Layers

Configuration is loaded in this priority order (last wins):

1. **`appsettings.json`** (in each EXE folder) — base config, shipped with the app. Overwritten on publish.
2. **`%LOCALAPPDATA%\OD.MyLocalAgent\settings.json`** — user overrides. Survives updates.
3. **DPAPI secrets** — injected from `secrets.dat`. Highest priority.

The `Knowledge` section is stored in `settings.json` when changed from **Settings → Knowledge
embeddings**. It controls the independent embedding provider, model, chunking, import, and search
bounds. By default it uses Ollama with `embeddinggemma:300m`; OpenAI-compatible embedding
credentials are stored in `secrets.dat`, never in `settings.json`.

## File System Sandbox

| Setting | Default | Purpose |
|---------|---------|---------|
| `FileSystem:RootPath` | `C:\OD.MyLocalAgent` | Workspace root where the agent operates |

## Task Reports

| Path | Contents |
|------|----------|
| `<FileSystem:RootPath>\.od-my-local-agent\outputs\reports\<taskId>\*.md` | One Markdown report per execution of a task |
| `<FileSystem:RootPath>\.od-my-local-agent\outputs\reports\index.jsonl` | Append-only execution index used by Dashboard and Runs |

Reports belong to the configured workspace, not to the installation or global user-configuration
directories. Explicit output paths in task definitions remain relative to the workspace root.

## One-time Migration

Existing test installations can preview the move with:

```powershell
.\scripts\Migrate-DataToWorkspace.ps1 -WhatIf
```

Close the UI and disable its Windows scheduled tasks before running the command without
`-WhatIf`. The script retains a verified backup under
`.od-my-local-agent\migration\backup-<timestamp>` until it is removed manually.

## Windows Task Scheduler

| Setting | Default | Purpose |
|---------|---------|---------|
| `Scheduler:TaskFolder` | `OD.MyLocalAgent` | Folder in Windows Task Scheduler for scheduled tasks |
# Embedded Knowledge and RAG

| Location | Responsibility |
|----------|----------------|
| `src/OD.MyLocalAgent.Core/Knowledge*.cs` | Knowledge contracts, tag authorization, extraction, chunking, embedding generation, library management, safe lexical query construction, and hybrid retrieval |
| `src/OD.MyLocalAgent.Core/SqliteKnowledgeStore.cs` | SQLite persistence for the Knowledge library and its index/revision lifecycle |
| `src/OD.MyLocalAgent.Core/KnowledgeTool.cs` | Declarative `knowledge.search` tool exposed in the `knowledge` tool group |
| `src/OD.MyLocalAgent.Core/WorkspacePaths.cs` | Definitions of the private Knowledge library paths in the configured workspace |
| `src/OD.MyLocalAgent.Core/HostApplicationBuilderExtensions.cs` | Registration of Knowledge services and binding of the `Knowledge` configuration section |
| `src/OD.MyLocalAgent.UI/Pages/KnowledgePage.xaml` | Knowledge administration page (documents, preview, import, search, and maintenance) |
| `src/OD.MyLocalAgent.UI/ViewModels/KnowledgeViewModel.cs` | View-model for Knowledge administration operations |
| `src/OD.MyLocalAgent.UI/ViewModels/SettingsViewModel.cs` | Knowledge embedding and global hybrid-retrieval settings, validation, and endpoint test |
| `src/OD.MyLocalAgent.UI/Services/UserSettingsService.cs` | Persistence of the `Knowledge` settings section in the user settings file |
| `Docs/samples/knowledge-rag-example.json` | Ready-to-copy task example using `knowledge.search` and an agentic `ai.prompt` step |

Knowledge import accepts TXT, Markdown, HTML/HTM, DOCX, and text-based PDF. HTML extraction
reuses the internal structured-Markdown converter and removes active or non-content nodes before
chunking. The UI may select multiple files: the batch shares authorization tags, derives document
names from file names, leaves descriptions empty, and processes files sequentially so one duplicate
or failure does not stop later files. Progress is reported per file and phase; cancellation preserves
only documents whose atomic import already completed.

`ScheduledTask.tools` stores the `knowledge` group name. An `ai.prompt` step's `toolAliases`
stores the exact `knowledge.search` alias. Knowledge tags are the document authorization boundary:
an empty scope authorizes no documents, and a prompt step may narrow its task scope but cannot
broaden it. Retrieval is always tool-driven; no RAG excerpt is injected automatically into a
prompt.

# Workflow step editor

| Location | Responsibility |
|----------|----------------|
| `src/OD.MyLocalAgent.Core/WorkflowStepEditingService.cs` | UTF-16 caret location and atomic canonical top-level insert, replace, and delete operations. |
| `src/OD.MyLocalAgent.Core/WorkflowStepDependencyAnalyzer.cs` | Read-only discovery of later output-placeholder dependencies before deletion. |
| `src/OD.MyLocalAgent.Core/ToolArgumentDefinition.cs` | Immutable reflected argument type, default, bounds, and editor metadata. |
| `src/OD.MyLocalAgent.UI/ViewModels/WorkflowStepEditorViewModel.cs` | Catalog-driven memory-only form, prompt scope validation, and explicit authorization proposals. |
| `src/OD.MyLocalAgent.UI/WorkflowStepEditorWindow.xaml` | Modal editor controls and context-sensitive sections. |

Existing aliases are immutable during edit. Delete and insert a new step to choose another tool.

# Workflow `forEach` execution

| Location | Responsibility |
|----------|----------------|
| `src/OD.MyLocalAgent.Core/WorkflowStepDefinition.cs` | Persisted top-level `forEach` selector. |
| `src/OD.MyLocalAgent.Core/WorkflowStepDefinitionJsonConverter.cs` | JSON round-trip and strict property handling. |
| `src/OD.MyLocalAgent.Core/ToolAttribute.cs` and `ToolDefinition.cs` | Declarative `SupportsForEach` capability exposed by the Tool Map. |
| `src/OD.MyLocalAgent.Core/ScheduledTaskValidator.cs` | Alias, source, scope, graph-dominance, and placeholder validation. |
| `src/OD.MyLocalAgent.Workflows/SequentialWorkflowEngine.cs` | Ordered execution, 100-item limit, retry isolation, fail-fast behavior, and output aggregation. |
| `src/OD.MyLocalAgent.Workflows/WorkflowForEachContext.cs` | Immutable current-item and one-based-position context. |
| `src/OD.MyLocalAgent.Workflows/WorkflowPlaceholderResolver.cs` | Resolution of `foreach.item`, structured item selectors, and `foreach.position`. |
| `src/OD.MyLocalAgent.UI/ViewModels/WorkflowStepEditorViewModel.cs` | Repeat controls and same-path overwrite warning. |
| `Docs/samples/foreach-mail-analysis.json` | Ready-to-copy two-stage repetition example. |

Only `ai.prompt` and `filesystem.write` currently opt in. Repetition remains a property of one
step: it does not create nested workflows, branches, parallel execution, or rollback semantics.

## Stage 31 — Gmail decision reader

| Area | Owner |
| --- | --- |
| Questions, acceptance, mapping and strict authoring | `Core/DecisionQuestionSet`, `DecisionAcceptancePolicy`, `GmailDecisionContractValidator` |
| Opt-in settings, collection orchestration and bounded source-free coverage | `Core/GmailDecisionOptions`, `GmailDecisionTool`, `GmailDecisionProjection`, `GmailDecisionResult` |
| Local SystemOne/chat HTTP, DNS/redirect protection, gate and memory leases | `Ollama/OllamaLocalDecisionClient`, `LocalDecisionEndpoint`, `DecisionModelResidencyCoordinator` |
| Embedding handoff at request boundaries | `Core/KnowledgeEmbeddingService` |
| Deterministic report validation and escaping | `Core/GmailDecisionReportRenderer` |
| Immutable workflow surface and correlation | `Core/ToolExecutionContext`, `ToolInvocationSurface`, `ToolRegistry`; workflow engine |
| Settings and named question/ordered answer fields | UI Settings and WorkflowStepEditor partial ViewModels; `DecisionQuestionEditorItem` |
| End-to-end warning display | Workflow execution warnings, Runner end record, RunLogCatalog and Runs ViewModels |
| User instructions and disabled starter | `Docs/Gmail-Decision-Reader.en.md`, `.fr.md`, `Docs/samples/gmail-decision-watch.json` |
