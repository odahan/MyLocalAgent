# OD.MyLocalAgent

OD.MyLocalAgent is a native Windows application for defining and running scheduled, AI-driven tasks locally through Ollama, deterministic workflows, and explicitly permitted tools.

## Build status by stage

| Stage | Scope | Status |
|---|---|---|
| Stage 0 | Solution foundation and toolchain pinning | ✅ |
| Stage 1 | LLM providers and connectors | ✅ |
| Stage 2 | Core services | ✅ |
| Stage 3 | Workflows and runner | ✅ |
| Stage 4 | UI foundation | ⬜ |
| Stage 5 | UI pages | ⬜ |
| Stage 6 | Hardening and documentation | ⬜ |

## Core behavior

Configuration is loaded beside the executable from `appsettings.json`, then overridden by
`%LOCALAPPDATA%\OD.MyLocalAgent\settings.json`. Secrets are stored separately in a
DPAPI CurrentUser-protected file and added as the highest-priority in-memory configuration
layer. The OpenAI API key and Dropbox tokens must never be written to either JSON settings
file.

Tasks are stored as one validated JSON file per task under
`%LOCALAPPDATA%\OD.MyLocalAgent\tasks`. Windows Task Scheduler is a mirror of that store:
each registration contains only the next cron occurrence. After a task executes, the runner
will compute and register the following occurrence. `StartWhenAvailable` covers missed starts,
and the Stage 3 `resync-schedules` command will repair missing or stale registrations.

When `Scheduler:RunWhenLoggedOff` is `false`, tasks use the current user's interactive token.
When it is `true`, tasks use S4U. Complete Google OAuth consent in the UI first: S4U cannot
perform interactive consent, and scheduled tasks must run under the same Windows account so
the application data and DPAPI scope match.

## Safety

Email sending, file deletion, and HTTP POST are disabled by default. They require,
respectively, `Safety:AllowSendEmail`, `Safety:AllowFileDelete`, and
`Safety:AllowHttpPost`. Local file paths are relative to the absolute
`FileSystem:RootPath` sandbox; rooted paths and parent traversal are rejected.

`Http:AllowedHosts` is an exact host allowlist. An empty list permits requests to every HTTP
or HTTPS host, which is convenient but increases the risk of unintended network access.
Configure an explicit list for unattended tasks.

## Workflow syntax

A workflow is an ordered array. A no-argument step can use string form:

```json
"gmail.readToday"
```

Object form supplies arguments, an optional output id, and an optional error policy:

```json
{
  "step": "filesystem.write",
  "id": "writeReport",
  "args": {
    "path": "reports/mail-summary.md",
    "content": "{{ai.summarize.output}}"
  },
  "onError": "Retry",
  "retryCount": 3,
  "retryBackoffSeconds": 2
}
```

String-form AI skills receive the preceding step's output automatically. Placeholders include
`{{previous.output}}`, `{{stepId.output}}`, `{{task.id}}`, `{{task.name}}`, and
`{{task.prompt}}`.

Workflows are the recommended unattended execution path because their tool and skill order is
deterministic. A task that declares both `prompt` and `workflow` executes the workflow; the
prompt remains available as `{{task.prompt}}`.

Mail summary to a local report:

```json
{
  "id": "daily-mail-summary",
  "name": "Daily mail summary",
  "schedule": "0 7 * * *",
  "prompt": "Summarize today's important messages.",
  "tools": ["gmail", "filesystem"],
  "workflow": [
    "gmail.readToday",
    "ai.summarize",
    {
      "step": "filesystem.write",
      "args": {
        "path": "reports/daily-mail-summary.md",
        "content": "{{ai.summarize.output}}"
      }
    }
  ],
  "enabled": true,
  "errorPolicy": "StopOnError"
}
```

Unread mail to a calendar event and Dropbox backup:

```json
{
  "id": "unread-mail-follow-up",
  "name": "Unread mail follow-up",
  "schedule": "0 9 * * 1-5",
  "prompt": "Find follow-ups in unread mail.",
  "tools": ["gmail", "calendar", "filesystem", "dropbox"],
  "workflow": [
    "gmail.readUnread",
    "ai.extractActionItems",
    {
      "step": "calendar.create",
      "id": "createReview",
      "args": {
        "summary": "Review unread mail actions",
        "start": "2026-07-30T09:00:00+02:00",
        "end": "2026-07-30T09:30:00+02:00",
        "description": "{{ai.extractActionItems.output}}"
      }
    },
    {
      "step": "filesystem.write",
      "id": "writeBackup",
      "args": {
        "path": "reports/unread-actions.md",
        "content": "{{ai.extractActionItems.output}}"
      }
    },
    {
      "step": "dropbox.upload",
      "args": {
        "localPath": "reports/unread-actions.md",
        "dropboxPath": "/OD.MyLocalAgent/unread-actions.md"
      }
    }
  ],
  "enabled": true,
  "errorPolicy": "StopOnError"
}
```

## Headless runner

The runner uses the same configuration and dependency-injection composition as the desktop
application. Configuration is rooted beside the executable, so Windows Task Scheduler may
assign any working directory without changing behavior.

```powershell
dotnet run --project src/OD.MyLocalAgent.Runner -- list
dotnet run --project src/OD.MyLocalAgent.Runner -- run --task daily-mail-summary
dotnet run --project src/OD.MyLocalAgent.Runner -- test-prompt --task daily-mail-summary
dotnet run --project src/OD.MyLocalAgent.Runner -- test-tool --alias filesystem.list --args '{ "path": "." }'
dotnet run --project src/OD.MyLocalAgent.Runner -- resync-schedules
```

Exit codes are `0` for success, `1` for a task or tool failure, `2` for configuration errors,
and `3` when the requested task does not exist. Concurrent `run` commands for the same task
are rejected through a global named mutex.

Every task execution writes a Markdown report under
`%LOCALAPPDATA%\OD.MyLocalAgent\reports\<taskId>` and appends a compact entry to
`%LOCALAPPDATA%\OD.MyLocalAgent\reports\index.jsonl`. After a scheduled or manual `run`, the
runner registers the task's next cron occurrence.
