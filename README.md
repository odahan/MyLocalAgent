# OD.MyLocalAgent

OD.MyLocalAgent is a Windows desktop app for running AI tasks and automating repeatable workflows. Use a local Ollama model by default, or configure an OpenAI-compatible endpoint in **Settings**.

## What you can do

- Build workflows that combine prompts with actions on local files, documents, spreadsheets, and SQLite databases. Use conditions and loops to process items such as messages or files.
- Run tasks on demand or schedule them with Windows Task Scheduler. The headless runner executes tasks, and the desktop app shows run history, logs, and reports.
- Use Direct Chat with saved conversations and file attachments. Index documents in the Knowledge library and control which collections a task or conversation can search with access tags.
- Connect Gmail, Google Calendar, Dropbox, or web search when a task needs them. The app uses per-task permissions for sensitive actions and screens supported external content with Guardrails before it reaches a model.

## Quick start on Windows

To build from source, use Windows 10 or 11, the .NET 10 SDK, and PowerShell. For local inference, install and start [Ollama for Windows](https://ollama.com/download/windows). The supplied configuration uses `gemma4:12b`. Download that model or select another installed tag in **Settings**.

```powershell
git clone https://github.com/odahan/MyLocalAgent.git
Set-Location MyLocalAgent
ollama pull gemma4:12b
.\scripts\publish.ps1
```

You can also double-click [publish.bat](publish.bat). The publisher builds self-contained x64 executables for the desktop app and runner under `%LOCALAPPDATA%\Programs\OD.MyLocalAgent`. Use [OpenProdFolder.Bat](OpenProdFolder.Bat) to open that folder, then launch `UI\OD.MyLocalAgent.UI.exe`.

1. In **Settings**, confirm the model and endpoint and select **Test connection**. Choose the workspace root where local tasks will read and write files.
2. In **Tasks**, select **New**. Enter a name and a simple prompt, such as “Write a three-item checklist for preparing a meeting.” Clear **Enabled and mirrored to Windows Task Scheduler** for a manual trial. Select **Save task**, then **Run now**.
3. Watch the output in **Tasks** and review the run in **Runs**. Add workflow steps or a schedule when you want to automate the task.

See the first-run guide for deployment options and connector setup. [English](Docs/FirstRun-EN.md) · [Français](Docs/FirstRun.md)

## Web search and other connectors

Web search is disabled on a fresh install. **Tavily is the preferred search provider**, and Ollama Web Search is available as an alternative. In **Tools → Web search**, select **Enable web search**, choose **Tavily**, enter a Tavily API key, select **Save Web Search**, then **Test connection**. Search queries are sent to the selected provider, and the Tavily connection test uses a search credit. [Tavily API key setup](https://docs.tavily.com/)

On the task, enable **Web search (web) (cloud)** for `web.search` or **Web Discovery** for `research.discover`.

Gmail and Google Calendar require Google OAuth. Dropbox requires Dropbox app credentials. Set up only the integrations your workflows use. [Connector setup guide](Docs/FirstRun-EN.md)

## Examples

| Workflow | What it does |
| --- | --- |
| [Local file workflow](Docs/samples/acceptance-filesystem.json) | Creates, writes, and reads a file in the workspace without cloud credentials. |
| [Translate a local file](Docs/samples/translate-local-file.json) | Reads a file, translates it with the configured model, and writes the result. |
| [Daily web brief](Docs/samples/web-daily-brief.json) | Searches the web and turns the results into a brief. |
| [Knowledge search](Docs/samples/knowledge-rag-example.json) | Uses excerpts from an authorized collection to answer a question. |

Browse the [sample workflows](Docs/samples/) for Gmail, SQLite, and editorial monitoring examples.

## Documentation

- **User manuals** · [English](Docs/User-Manual.en.html) · [Français](Docs/User-Manual.html). Download and open the HTML files in a browser for the full offline manuals.
- **Reference** · [Technical manual](Docs/Technical-Manual.html) · [Repository layout](Docs/WhatsWhere.md) · [Publishing script](Docs/PublishExplained.md).
- **Decision workflows** · [Gmail decision reader](Docs/Gmail-Decision-Reader.en.md) · [Decision evaluation](Docs/Decision-Evaluation.en.md).

## Build and test

The solution targets .NET 10 and uses WPF for the desktop app. Run these commands from the repository root on Windows.

```powershell
dotnet build OD.MyLocalAgent.sln -m:1
dotnet test OD.MyLocalAgent.sln -m:1 --filter "Category!=Integration"
```
