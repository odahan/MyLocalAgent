# What's Where

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

## User Data Directory

Default: `%LOCALAPPDATA%\OD.MyLocalAgent`

| Path | Contents |
|------|----------|
| `settings.json` | User settings (overrides `appsettings.json`) |
| `secrets.dat` | DPAPI-protected secrets (API keys, OAuth tokens) |
| `direct-chat.db` | Direct Chat conversations, messages, attachment content, and cumulative memory summaries (SQLite, not encrypted) |
| `tasks/` | Persisted task definitions (JSONL files) |
| `logs/` | Root log directory |
| `logs/application-*.log` | Rolling application log |
| `logs/incidents.jsonl` | Incident log |
| `logs/runs/` | Per-execution runner output logs |

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
| `<FileSystem:RootPath>\reports\<taskId>\*.md` | One Markdown report per execution of a task |
| `<FileSystem:RootPath>\reports\index.jsonl` | Append-only execution index used by Dashboard and Runs |

Reports belong to the configured filesystem root, not to the installation directory or the
application user-data directory. The root is therefore the single default location for
task-produced files when a task does not specify another destination.

## Windows Task Scheduler

| Setting | Default | Purpose |
|---------|---------|---------|
| `Scheduler:TaskFolder` | `OD.MyLocalAgent` | Folder in Windows Task Scheduler for scheduled tasks |
