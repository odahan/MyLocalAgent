# MyLocalAgent — first-run guide

Application version: **1.5.1**.

This guide summarizes the setup used to build the application, publish it, and
configure its connectors. It does not cover creating a business task, setup
incidents, or intermediate fixes.

## 1. Prerequisites

- Windows 10 or Windows 11, x64;
- .NET SDK 10;
- PowerShell 5.1 or PowerShell 7;
- Ollama installed and running, with the selected model downloaded;
- a Google, Dropbox, and/or Ollama.com account only for connectors that will
  actually be used.

For the default local model:

```powershell
ollama pull gemma4:12b
ollama list
```

The model listed by `ollama list` must exactly match the model selected in
MyLocalAgent.

## 2. Build and verify the source code

From the repository root (`E:\OD.MyLocalAgent`):

```powershell
dotnet restore OD.MyLocalAgent.sln -m:1
dotnet build OD.MyLocalAgent.sln -m:1 --no-restore --configuration Release
dotnet test OD.MyLocalAgent.sln -m:1 --no-build --no-restore --configuration Release
```

The build and tests must complete without errors. An Ollama integration test
may be skipped when it depends on the local environment.

## 3. Publish the application

Publish the self-contained Windows version:

```powershell
.\scripts\publish.ps1 -NoRestore
```

The script publishes the **Release** version as self-contained `win-x64` output
to:

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent
```

It produces, in particular:

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\UI\OD.MyLocalAgent.UI.exe
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\Runner\OD.MyLocalAgent.Runner.exe
```

To deploy a later change, rerun the same script after building and testing.
Close MyLocalAgent before publishing.

## 4. First launch and local settings

Start:

```text
%LOCALAPPDATA%\Programs\OD.MyLocalAgent\UI\OD.MyLocalAgent.UI.exe
```

In **Settings**:

1. Verify the local Ollama endpoint, normally `http://localhost:11434`.
2. Verify or select the local model, then use **Refresh models** if needed.
3. Select **Test connection** and verify that both the endpoint and the model
   are available.
4. Verify the filesystem root. Relative task paths stay confined to this
   directory.
5. Leave `Scheduler:RunnerPath` empty: the application will automatically find
   the published Runner beside the UI.
6. Use **Create desktop shortcut** if a Windows desktop shortcut is wanted.

The copy buttons separately place the reports directory and the executable
directory on the clipboard.

Task reports are always written in the workspace's centralized storage, below
`<FileSystem:RootPath>\.od-my-local-agent\outputs\reports\<taskId>\`. The configured root
remains the task sandbox: neither the installation directory nor `%LOCALAPPDATA%` is a fallback
report destination. A prompt-only task produces ordinary Markdown there, without an empty
workflow-steps section.

The `.od-my-local-agent` directory is reserved for the application and hidden by Windows. Managed
file tools cannot display or access it, even when an absolute authorization covers it; recursive
or destructive operations against a parent directory are also rejected. Hiding is only a visual
safeguard. The `PowerShellExecution` and `ShellExecute` capabilities start unrestricted local
processes and must be granted only to a trusted task.

### Project-wide AI instructions

In **Settings → AI provider**, **Project-wide AI instructions** holds stable conventions such as
the response language, output format, or naming rules. Keep it short; requirements specific to
one task belong in that task's prompt.

Every task applies these instructions by default. In the Task editor, clear **Apply project-wide
AI instructions to this task** to opt out for that task only, then select **View system prompt** to
inspect the configured text or confirm that it will not be added. Built-in safety rules, allowed
tools, and the task's instructions remain authoritative.

### Temporary model for a task

The model selected in **Settings** remains the default. For a one-off task, add
`"model": "model-name"` to its JSON; for one step only, add the same property to an
object-form `ai.prompt` step. The step value takes precedence. It is limited to that execution
and the default is restored automatically. With Ollama, install the exact tag first with
`ollama pull model-name`: the application warms it up before execution and fails without changing
Settings if it is missing.

Warm-up applies only to the local Ollama provider. It is never sent to OpenAI, OpenRouter, or any
other cloud endpoint, so it does not consume cloud credits.

### Model reasoning

Turn **Reasoning** on or off in **Direct Chat** for the next reply, including agent mode. For an
agentic task, use **Enable reasoning for agentic task calls** in the Task editor. Reasoning is on
by default for these two uses and off for bounded operations: Trust Filter, `ai.prompt` steps,
history compression, skills, internal helpers, and warm-up.

When a provider returns readable reasoning text, Direct Chat shows it in the expandable **Model
reasoning** panel. It is not part of the assistant answer and is not stored in conversation history.
Some models return no readable reasoning; the ordinary answer remains available. If a provider does
not support this preference, the application continues with that provider's default behavior.

### Trust Filter speed and large documents

Trust Filter requests a short JSON decision without tools or reasoning. This avoids spending time
on hidden deliberation before the model returns a score. For a long web page or PDF, its output
capacity grows with the text size (from 1,024 to 8,192 tokens) to retain distinct suspicious
quotes. On a timeout or failure, content remains protected: it is delivered only as marked Base64,
and the screen identifies the configured timeout when it caused the result.

## 5. Validate the local filesystem pipeline

The following acceptance sample creates, writes, and reads a file below the
configured filesystem root, then produces a report:

```powershell
$runner = Join-Path $env:LOCALAPPDATA `
  'Programs\OD.MyLocalAgent\Runner\OD.MyLocalAgent.Runner.exe'

Copy-Item `
  -LiteralPath '.\Docs\samples\acceptance-filesystem.json' `
  -Destination (Join-Path $env:LOCALAPPDATA `
    'OD.MyLocalAgent\tasks\acceptance-filesystem.json')

& $runner run --task acceptance-filesystem
```

Verify the `Success` status and the report path printed by the Runner. Remove
or unregister this acceptance task once the verification is complete.

## 6. Configure Google: Gmail and Calendar

### In Google Cloud

1. Create or select a Google Cloud project.
2. Enable **Gmail API** and **Google Calendar API**.
3. Configure the OAuth consent screen and add the account in use as a test user
   while the application remains in testing.
4. Create an OAuth client of type **Desktop app**.

### In MyLocalAgent

1. Close the application.
2. Open `%LOCALAPPDATA%\OD.MyLocalAgent\settings.json`.
3. Add the client values without removing other sections:

```json
"Google": {
  "ClientId": "YOUR_DESKTOP_CLIENT_ID",
  "ClientSecret": "YOUR_DESKTOP_CLIENT_SECRET"
}
```

4. Restart MyLocalAgent and open **Tools**.
5. Select **Connect** and complete browser consent with the same Windows account
   that will run scheduled tasks.
6. In **Tools**, run **Test connection** separately for Gmail and Calendar.

Refreshable Google authorization data is stored in
`%LOCALAPPDATA%\OD.MyLocalAgent\Auth\Google`. The Runner cannot open a browser,
so consent must be completed from the UI.

## 7. Configure Dropbox with a refresh token

This approach is suitable for scheduled tasks: the refresh token automatically
renews short-lived access tokens.

### In the Dropbox App Console

1. Open <https://www.dropbox.com/developers/apps> and create an application.
2. Choose **Scoped access**.
3. Choose **App folder** to limit access, or **Full Dropbox** if the task must
   access pre-existing files throughout Dropbox.
4. In **Permissions**, enable:
   - `files.metadata.read`;
   - `files.content.read`;
   - `files.content.write`.
5. Select **Submit**.
6. Keep the **App key** available. Do not disclose the App secret.

### Obtain the refresh token using PKCE

The following script opens Dropbox authorization and copies the refresh token to
the clipboard. It does not write the token to disk:

```powershell
.\scripts\get-dropbox-refresh-token.ps1
```

1. Enter the App key when prompted.
2. Authorize the application in the browser.
3. Copy the code shown by Dropbox and paste it into PowerShell.
4. Confirm the message stating that the refresh token is in the clipboard.

### In MyLocalAgent

1. Open **Tools → Dropbox**.
2. Leave **Replace Dropbox access token** empty.
3. Paste the refresh token into **Replace Dropbox refresh token**.
4. Enter the App key in **Replace Dropbox app key**.
5. Select **Save credentials**, then **Test connection**.

Dropbox credentials are encrypted by Windows DPAPI for the current user and are
not written to `settings.json`.

## 8. Configure Web Search (Ollama cloud)

This feature is optional: queries and search-result snippets are sent to
Ollama.com.

1. Sign in to <https://ollama.com/settings/keys> and create an API key.
2. Copy the key without disclosing it.
3. In **Tools → Web search (Ollama)**, select **Enable Ollama Web Search**.
4. Keep the official endpoint:

```text
https://ollama.com/api/web_search
```

5. Paste the key into **Replace Ollama Web Search API key**.
6. Select **Save Web Search**, then **Test connection**.

The key is protected by Windows DPAPI. Enable the **Web search (web) (cloud)**
tool group only for tasks that need it.

For scheduled multi-theme monitoring, enable **Web Discovery** and use
`research.discover`. Its default `sliding` mode verifies publication dates locally after
retrieval and expands from 2 to 4 to 7 local calendar days only when necessary. Old, future,
and undated pages are excluded. Final titles and summaries are delivered only through the
Guardrails-protected `protectedResults` block; disabled or failed screening still produces
marked Base64. Optional translation runs only after a clean verdict and is screened again.
Provider failures are not treated as an empty collection: `searchStatus` distinguishes available,
degraded, unavailable, and rate-limited search. A provider-wide HTTP 429 stops the remaining
queries and the workflow states clearly that source availability could not be determined.
The bundled editorial examples fail closed when sources are missing: the LinkedIn watch produces
no posts with zero candidates. The Sunday article focuses on the available source when there is
one, becomes a limited cross-analysis with two, and a full multi-source article from three. Only
zero candidates produce a diagnostic without an article.

## 9. Tools without credentials

- **Filesystem**: no account is required. Relative paths remain under the
  configured root; a task can receive additional explicit permissions for
  absolute paths.
- **HTTP**: no key is required. Define allowed HTTP hosts in **Settings** before
  running an unattended task. An empty list allows all HTTP/HTTPS hosts and is
  suitable only for a controlled test.
- **SQLite**: use only a disposable local database file beneath an authorized root.
  Reads still require path authorization; a write also requires the task-owned
  `SqliteWrite` capability. Each task stores whether its bounded SQLite read results are
  screened by Guardrails; requested but unavailable screening withholds the result.
- **Local execution**: `filesystem.shellExecute` requires `ShellExecute`, while
  `filesystem.powerShell` requires the separate `PowerShellExecution` task capability.
- Sensitive operations remain disabled by default: sending email, deleting
  files, HTTP POST, SQLite writes, and process execution. Enable them only when a specific task
  justifies them.

### First safe SQLite task

Use a disposable database below the configured filesystem root, for example
`data/sample.db`. Never provide a connection string, URI, network path, or
`:memory:`.

1. In the Task editor, select the **SQLite** group in **Tool whitelist**.
2. For a read-only trial, keep only ReadOnly access to the path; relative paths
   remain below `FileSystem:RootPath`.
3. To create the demonstration database, temporarily grant ReadWrite access to
   the same location and enable `SqliteWrite` under **Sensitive operation permissions**.
   The group, ReadWrite access, and task capability are all required.
4. In **Tool tester**, select `sqlite.execute` and create the table with one
   statement:

```json
{
  "databasePath": "data/sample.db",
  "sql": "CREATE TABLE items (id INTEGER PRIMARY KEY, name TEXT NOT NULL)",
  "createIfMissing": true
}
```

5. Disable SQLite writes again, then select `sqlite.query` and verify the read
   with:

```json
{
  "databasePath": "data/sample.db",
  "sql": "SELECT id, name FROM items WHERE id >= @minimumId ORDER BY id",
  "parameters": { "@minimumId": 1 },
  "maxRows": 25
}
```

The read result is bounded and can be protected by Guardrails. The **Protect
SQLite read results with Guardrails** option belongs to each task; its default in
**Settings → SQLite** applies only to new tasks. With an OpenAI-compatible
provider, screening can send bounded database content to the cloud provider.
Clearing screening for a task delivers plaintext data to the workflow or later
model. Remove the demonstration database afterward or leave write permission
disabled.

## 10. Final check before the first task

In **Tools**, verify that each required connector displays `Connected`:

- Gmail;
- Calendar;
- Dropbox;
- Web Search, only when it is used.

The first task can then be created with only the tool groups and permissions
needed for its purpose.

For the first trial, use **Run now**. The output follows new lines while
**Auto-scroll** remains selected. Clear it to keep the current reading position,
then select it again to return to the latest line. For a workflow,
**Show workflow context** displays the outputs accumulated after
each successful step and remains available until the next task starts. This
context can contain data processed by the task.

For one-off diagnosis, select **Debug (next run)** before starting: that run’s
LLM requests/responses and tool arguments are visible only in Runner Output. The
content can include task data and must not be shared; configured secrets remain
excluded. **Large** adds 300 pixels to the panel for easier reading and does not resize the
application window.

## 11. Authoring a workflow without starting from scratch

The **Workflow JSON** card of the task editor offers three aids. The first two are
entirely deterministic: they call no model, tool, file, or network.

- **Tool catalog**: the searchable reference of every Tool Map alias, with its
  description, typed arguments, examples, allowed values, and placeholders.
  Selecting an entry previews it, then **Insert** inserts it at the caret position
  and keeps the window open so several steps can be accumulated. **Cancel**
  discards the whole session. If the JSON cannot be parsed, the snippet is
  inserted verbatim with a manual-repair warning.
- **Edit workflow text**: the metadata-driven editor lists and edits eligible text arguments
  already present in declaration order, including `ai.prompt.prompt`,
  `filesystem.write.content`, and `filesystem.powerShell.script`. **Apply** writes every change atomically.
- **AI Helper**: model-assisted generation, best reserved for cases where an
  English description is faster than writing the workflow directly.

A step coming from a tool group that the task does not allow shows a warning and
an **unchecked** box: authorization is never widened silently. Required task capabilities are
reported and must be granted explicitly in the task editor.

The Task editor **Tool whitelist** is generated from the catalog built into the application.
A future version can therefore add a group without another hard-coded list; the group appears
cleared and remains separate from path authorization and Safety. This is a compiled catalog,
not an external runtime plug-in loader.

**Undo assisted change** reverts the last assisted application in one operation,
whatever its origin, until save or reload. As with any editor change, only
**Save task** persists the result.

## 12. Authoring a dated prompt-only task

For a saved task with no workflow, MyLocalAgent can expand `{{Today}}`, `{{Now}}`,
`{{task.id}}`, and `{{task.name}}` immediately before the prompt reaches the agent. For example:

```text
Create the Markdown report with the filesystem tool at:
C:\Reports\Sort-{{Today[yyyyMMdd]}}.md
```

At local execution time `2026-08-03T14:30:25+02:00`, the path becomes
`C:\Reports\Sort-20260803.md`. Save and schedule registration keep the template unchanged;
the execution time supplies the date. `{{Now[yyyyMMdd-HHmmss]}}` can add seconds when several
runs may occur on the same day.

The same expansion is used by scheduled/CLI `run`, UI **Run now**, CLI `test-prompt`, and
Prompt editor **Test the prompt**, including current unsaved editor text. Replacement is
single-pass. Unknown or workflow-only markers remain unchanged with a non-failing warning;
use `{{previous.output}}` only inside a workflow. Durable warning records contain categories
and counts, not prompt or marker text. AI Playground and other taskless surfaces are unchanged.

A resolved absolute path still requires the task’s File system whitelist and matching
read-write authorized location, plus Windows access. Placeholder expansion does not guarantee
that the model will select the tool or that the tool operation will succeed.

## 13. Understand compact Gmail and HTTP content

Gmail reads minimize every message before any AI call, then process the projections in batches.
A successful read contains only projected From, To, Subject, and Received headers with a
protected factual summary. Snippet, Body, quoted history, and omitted suffixes never reach the
workflow. `http.getText` likewise returns only a protected summary; `http.get` retains one
protected compact prefix, never the complete response.

The `originalCharacterCount`, `projectedCharacterCount`, and `omittedCharacterCount` metadata
make reduction visible. Compaction is a minimization boundary, not evidence that omitted text
was safe. A generated summary can miss details; review the original in Gmail or at the source
URL whenever completeness matters.
# Stage 22 — local files

When creating a task, **Archives**, **Word documents**, **Spreadsheets**, and **PDF documents** are
unchecked by default. Enable only the needed group, then separately grant read or read/write
locations. ZIP extraction and creation also require the task-owned `ArchiveWrite` capability.
Extracted text is untrusted data; no OCR, passwords, or formula recalculation is used.

# Per-task sensitive permissions

Sensitive operations are denied by default. There is no global Safety setting: each task must
explicitly grant the required capability in addition to its tool group and `fileSystemAccess`
paths. PowerShell and Shell Execute are separate permissions. A legacy task without a
`capabilities` field remains readable but receives no sensitive permission. Use **Edit workflow
text** to edit long AI prompts and PowerShell scripts without changing step structure.
# Embedded Knowledge

In **Settings → Knowledge embeddings**, select and test the embedding provider/model independently from the chat model; Ollama defaults to the pinned multilingual `embeddinggemma:300m`. Then open **Knowledge**, enter a name and at least one authorization tag, and import a TXT, Markdown, DOCX, or text PDF document. OCR is not supported. Enable the `knowledge` tool group and select explicit tags on each task or Direct Chat conversation; an empty tag scope denies access. After changing the embedding or chunking contract, use **Reindex library**: new revisions are prepared first and activated together only after the complete rebuild succeeds.
