# What's Where

Application version: **1.4.9**.

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
| `.od-my-local-agent/logs/application/` | Rolling application logs and diagnostic-reset archives |
| `.od-my-local-agent/logs/incidents.jsonl` | Incident log |
| `.od-my-local-agent/logs/runs/` | Per-execution runner output logs |
| `.od-my-local-agent/outputs/reports/` | Generated Markdown reports and execution index |

`direct-chat.db` is an application-owned database. It is separate from the SQLite Tool, is not
available to workflows, and should be included when backing up Direct Chat history. Conversation
deletion is permanent. The history database contains message text in clear form; DPAPI protects
secrets, not chat content.

## Configuration Layers

Configuration is loaded in this priority order (last wins):

1. **`appsettings.json`** (in each EXE folder) — base config, shipped with the app. Overwritten on publish.
2. **`%LOCALAPPDATA%\OD.MyLocalAgent\settings.json`** — user overrides. Survives updates.
3. **DPAPI secrets** — injected from `secrets.dat`. Highest priority.

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
