# MyLocalAgent — first-run guide

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
dotnet restore
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

## 9. Tools without credentials

- **Filesystem**: no account is required. Relative paths remain under the
  configured root; a task can receive additional explicit permissions for
  absolute paths.
- **HTTP**: no key is required. Define allowed HTTP hosts in **Settings** before
  running an unattended task. An empty list allows all HTTP/HTTPS hosts and is
  suitable only for a controlled test.
- Sensitive operations remain disabled by default: sending email, deleting
  files, and HTTP POST. Enable them only when a specific task justifies them.

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
- **Edit prompts**: the prompt editor lists the `ai.prompt` steps and lets you
  write a multiline prompt comfortably, together with the `includeContext`
  option. **Apply** writes every change at once. A prompt that is empty or only
  whitespace is rejected.
- **AI Helper**: model-assisted generation, best reserved for cases where an
  English description is faster than writing the workflow directly.

A step coming from a tool group that the task does not allow shows a warning and
an **unchecked** box: authorization is never widened silently. Safety settings are
displayed read-only.

**Undo assisted change** reverts the last assisted application in one operation,
whatever its origin, until save or reload. As with any editor change, only
**Save task** persists the result.
