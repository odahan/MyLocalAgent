# STAGE 0 — Solution foundation & toolchain pinning

> Paste this as the first prompt. Run it after the shared specification has been placed at
> `docs/prompts/00-Shared-Specification.md` in the repository.

## 1. Read first (mandatory)

1. Read `docs/prompts/00-Shared-Specification.md` **completely**. It is normative; this prompt
   only scopes Stage 0.
2. Inspect the current repository state before writing anything.

## 2. Mission

Create the complete, **compilable** skeleton of OD.MyLocalAgent: solution, 7 projects,
centralized build/package configuration, configuration template, docs scaffolding, and minimal
runnable entry points. Nothing functional yet — but `dotnet build` and `dotnet test` must pass.

## 3. Critical constraints (restated from the spec)

- .NET 10 / C# 14 everywhere; `Nullable` + `ImplicitUsings` enabled; warnings are errors.
- Complete files only: no `// TODO`, no placeholders, no references to files you do not emit.
- English only (identifiers, docs, comments).
- Never invent package names: every `PackageReference` must restore. If a name or version fails
  `dotnet restore`, look up the correct one and use it.
- Work in the repository: write real files, then **run the gate commands** (§6) and iterate.
- Do not implement any business logic from Stages 1–6 — skeleton only.

## 4. Scope

**In scope**

1. `global.json` — SDK `10.0.100`, `rollForward: latestFeature`. Verify the machine fulfills
   it (`dotnet --version`); if no .NET 10 SDK is present, stop and report instead of guessing.
2. `Directory.Build.props` at repo root: `LangVersion` 14 (or `latest` pinned by SDK),
   `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`,
   `GenerateDocumentationFile true`. Do **not** set `TargetFramework` here (per-project).
3. `Directory.Packages.props` at repo root (Central Package Management). Add every package
   listed in spec §5 with a pinned version chosen per the spec's version policy (latest stable
   compatible with net10; prerelease only where no stable exists — expected for
   `Microsoft.Agents.AI` and `System.CommandLine`; FluentAssertions pinned to its last
   permissively-licensed line, 7.x, after checking the license status). `System.CommandLine`:
   take the newest available and note in the ledger that its API differs from older betas.
4. `OD.MyLocalAgent.sln` + 7 projects exactly as spec §6:
   - `src/OD.MyLocalAgent.Ollama` (net10.0, class library)
   - `src/OD.MyLocalAgent.Connectors` (net10.0)
   - `src/OD.MyLocalAgent.Core` (net10.0)
   - `src/OD.MyLocalAgent.Workflows` (net10.0)
   - `src/OD.MyLocalAgent.Runner` (net10.0, console)
   - `src/OD.MyLocalAgent.UI` (net10.0-windows, `UseWPF`)
   - `tests/OD.MyLocalAgent.Tests` (net10.0, xunit + Moq + FluentAssertions + coverlet.collector)
   - Project references exactly per spec §6 (no cycles). Test project references all src
     projects.
5. Minimal compilable entry points:
   - Runner: `Program.cs` printing name/version (a real `System.CommandLine` root command may
     be deferred to Stage 3 — for now a minimal but complete `Main`).
   - UI: `App.xaml`/`App.xaml.cs` merging the MaterialDesign dark `BundledTheme`
     (`BaseTheme.Dark`) plus an empty `MainWindow` with the MaterialDesign defaults — the full
     shell comes in Stage 4.
   - Libraries: a single `AssemblyInfo`-free placeholder-free minimal structure is fine —
     e.g., one `internal static class ProjectInfo { public const string Name = "..."; }` per
     library so projects are not empty (documented with XML docs).
6. `appsettings.json` at repo root, **exactly** spec §8.2; add a `CopyToOutputDirectory` item
   to the UI and Runner csproj files.
7. `.gitignore` (standard .NET: `bin/`, `obj/`, `.vs/`, …).
8. `docs/samples/daily-mail-summary.json` — exactly spec §8.1.
9. `docs/ARCHITECTURE-LEDGER.md` — initialize with:
   - a header explaining its role (binding project memory; each stage appends one entry);
   - the seven **Key architecture decisions** of spec §9, copied verbatim (A–G);
   - a `Package versions` section listing every package with the version you actually pinned;
   - the solution-wide conventions (naming, `TimeProvider`, configuration layering summary).
10. `README.md` — skeleton only: title, one-paragraph description, a "Build status by stage"
    placeholder table (Stage 0 ✅ … Stage 6 ⬜). Full content in Stage 6.

**Out of scope** (later stages): LLM services, connectors, tools, workflows, runner commands,
UI pages, real tests beyond one trivial smoke test per gate mechanics (allowed: a single
xunit test asserting the test infrastructure works — it will be deleted in Stage 1).

## 5. Working agreements

- Use the `dotnet` CLI to scaffold (or write files directly — your choice), but the final
  state must match spec §6 exactly.
- After every package addition, run `dotnet restore` to prove the version exists.
- Keep the smoke test minimal; name it clearly (e.g., `InfrastructureSmokeTests`).

## 6. Stage gate (all must pass — show the actual command output)

1. `dotnet --version` satisfies `global.json`.
2. `dotnet restore OD.MyLocalAgent.sln`
3. `dotnet build OD.MyLocalAgent.sln` — succeeds, **zero warnings**.
4. `dotnet test OD.MyLocalAgent.sln` — succeeds (the smoke test passes).
5. Launch sanity: `dotnet run --project src/OD.MyLocalAgent.Runner` prints the banner;
   the UI project builds as `net10.0-windows`.

## 7. Before you finish

1. Append the **Stage 0** entry to `docs/ARCHITECTURE-LEDGER.md`: date, files created, all
   pinned package versions, any deviation from the spec (with reason).
2. End your reply with a short **stage report**: tree of what was created, the gate output
   summary (pass/fail per command), and any open question for the next stage.
