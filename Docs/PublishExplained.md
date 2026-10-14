# Publish Script Explained

Application version: **2.6.8**.

Step-by-step breakdown of `scripts/publish.ps1`.

## 1. Script Parameters

```powershell
[CmdletBinding()]
param(
    [string]$DestinationPath = (Join-Path $env:LOCALAPPDATA 'Programs\OD.MyLocalAgent'),
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$NoRestore
)
```

| Parameter | Default | Purpose |
|-----------|---------|---------|
| `DestinationPath` | `%LOCALAPPDATA%\Programs\OD.MyLocalAgent` | Where the app is installed |
| `RuntimeIdentifier` | `win-x64` | Target platform for the self-contained build |
| `NoRestore` | `false` | Skip `dotnet restore` (useful for CI) |

## 2. Strict Mode and Error Handling

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
```

- `Set-StrictMode` catches typos and undefined variables at runtime.
- `$ErrorActionPreference = 'Stop'` turns all errors into terminating exceptions so the script fails fast.

## 3. Path Resolution

```powershell
$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$uiProject = Join-Path $repositoryRoot 'src\OD.MyLocalAgent.UI\OD.MyLocalAgent.UI.csproj'
$runnerProject = Join-Path $repositoryRoot 'src\OD.MyLocalAgent.Runner\OD.MyLocalAgent.Runner.csproj'
$appSettingsPath = Join-Path $repositoryRoot 'appsettings.json'
$resolvedDestination = [System.IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($DestinationPath))
$destinationRoot = [System.IO.Path]::GetPathRoot($resolvedDestination)
```

Resolves all paths to absolute form. `GetFullPath` normalizes separators and resolves `..`. Environment variables in `DestinationPath` are expanded (e.g. `%LOCALAPPDATA%`).

## 4. Safety Guards

```powershell
if ([string]::IsNullOrWhiteSpace($destinationRoot) -or
    $resolvedDestination.TrimEnd('\', '/') -eq $destinationRoot.TrimEnd('\', '/')) {
    throw 'DestinationPath must identify a dedicated application directory, not a drive root.'
}

if ($resolvedDestination.TrimEnd('\', '/') -eq $repositoryRoot.TrimEnd('\', '/')) {
    throw 'DestinationPath must not be the repository root.'
}
```

Prevents two destructive mistakes:
- Publishing to a drive root (e.g. `C:\`)
- Publishing into the repo itself (which would overwrite source files)

## 5. Staging and Backup Paths

```powershell
$destinationParent = Split-Path -Parent $resolvedDestination
New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null

$operationId = [Guid]::NewGuid().ToString('N')
$stagingRoot = Join-Path $destinationParent ".OD.MyLocalAgent-publish-$operationId"
$deploymentOutput = Join-Path $stagingRoot 'OD.MyLocalAgent'
$uiOutput = Join-Path $deploymentOutput 'UI'
$runnerOutput = Join-Path $deploymentOutput 'Runner'
$backupPath = Join-Path $destinationParent ".OD.MyLocalAgent-backup-$operationId"
```

Publishing uses a two-step strategy to avoid partial failures:

1. Build everything into a **staging directory** (unique GUID per run).
2. If the build succeeds, **move** staging to the final destination (atomic-ish swap).
3. If anything goes wrong, **restore from backup**.

Directory layout during publish:
```
%LOCALAPPDATA%\Programs\
  .OD.MyLocalAgent-publish-<guid>\     ← staging (fresh build)
    OD.MyLocalAgent\
      UI\
      Runner\
  .OD.MyLocalAgent-backup-<guid>\      ← previous deployment (safety net)
  OD.MyLocalAgent\                     ← live deployment
    UI\
    Runner\
```

## 6. The `Invoke-ProjectPublish` Function

```powershell
function Invoke-ProjectPublish {
    param(
        [string]$ProjectPath,
        [string]$OutputPath
    )

    $arguments = @(
        'publish',
        $ProjectPath,
        '-c', 'Release',
        '--runtime', $RuntimeIdentifier,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        '-p:GenerateDocumentationFile=false',
        '-m:1',
        '--nologo',
        '--output', $OutputPath
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for '$ProjectPath' with exit code $LASTEXITCODE."
    }
}
```

Each MSBuild property explained:

| Property | Effect |
|----------|--------|
| `PublishSingleFile=true` | Bundles all managed DLLs and native libraries into a single EXE |
| `IncludeNativeLibrariesForSelfExtract=true` | Extracts native libs to a temp dir at runtime (required for WPF and WebView2) |
| `DebugType=none` | Suppresses PDB generation — no `.pdb` files in output |
| `GenerateDocumentationFile=false` | Suppresses XML documentation files — no `.xml` files in output |

Other flags:
- `-m:1` — single-threaded build to avoid file locking issues.
- `--nologo` — suppresses the MSBuild banner.

## 7. Publish Both Projects

```powershell
Invoke-ProjectPublish -ProjectPath $uiProject -OutputPath $uiOutput
Invoke-ProjectPublish -ProjectPath $runnerProject -OutputPath $runnerOutput
```

Publishes UI first, then Runner. Each gets its own output folder inside the staging directory.

## 8. Preserve User Settings

```powershell
foreach ($outputPath in @($uiOutput, $runnerOutput)) {
    $publishedSettingsPath = Join-Path $outputPath 'appsettings.json'
    $existingSettingsPath = Join-Path $resolvedDestination (
        $outputPath.Substring($deploymentOutput.Length + 1) + '\appsettings.json')
    if (Test-Path -LiteralPath $existingSettingsPath) {
        Copy-Item -LiteralPath $existingSettingsPath -Destination $publishedSettingsPath
    }
    else {
        Copy-Item -LiteralPath $appSettingsPath -Destination $publishedSettingsPath
    }
}
```

For each project (UI and Runner):
1. Check if `appsettings.json` already exists at the **final destination** (i.e. from a previous deployment).
2. If it exists — copy the **existing** one into the staging directory (preserves user customizations).
3. If not (first install) — copy the **repo default**.

This prevents a publish from overwriting a user's edited `appsettings.json` with blank defaults.

## 9. Validate Executables

```powershell
$uiPath = Join-Path $uiOutput 'OD.MyLocalAgent.UI.exe'
$runnerPath = Join-Path $runnerOutput 'OD.MyLocalAgent.Runner.exe'
if (-not (Test-Path -LiteralPath $uiPath -PathType Leaf)) {
    throw "The published UI executable was not produced at '$uiPath'."
}
if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) {
    throw "The published runner executable was not produced at '$runnerPath'."
}
```

Safety check: if `dotnet publish` succeeded but didn't produce the expected EXEs, fail before moving files into place.

## 10. Atomic Deployment Swap

```powershell
if (Test-Path -LiteralPath $resolvedDestination) {
    Move-Item -LiteralPath $resolvedDestination -Destination $backupPath
}

try {
    Move-Item -LiteralPath $deploymentOutput -Destination $resolvedDestination
}
catch {
    if (Test-Path -LiteralPath $backupPath -PathType Container) {
        Move-Item -LiteralPath $backupPath -Destination $resolvedDestination
    }
    throw
}
```

Three-step swap:
1. **Backup** the current deployment (move to `backup-<guid>`).
2. **Move** the staging directory into the final location.
3. If step 2 fails — **restore** the backup and re-throw the error.

This ensures a failed publish never leaves the app in a broken state.

## 11. Cleanup

```powershell
if (Test-Path -LiteralPath $backupPath -PathType Container) {
    Remove-Item -LiteralPath $backupPath -Recurse -Force
}
```

If the deployment succeeded, remove the backup. The old deployment is no longer needed.

## 12. Summary Output

```powershell
Write-Host "Published OD.MyLocalAgent to: $resolvedDestination"
Write-Host "Deployment mode: self-contained single-file ($RuntimeIdentifier)"
Write-Host "UI path: $publishedUiPath"
Write-Host "Resolved runner path: $publishedRunnerPath"
Write-Host 'Settings -> Scheduler:RunnerPath = ""'
Write-Host 'The empty value auto-resolves the adjacent runner or the standard UI/Runner layout.'
```

Prints the final deployment info. The last two lines remind the user that `Scheduler:RunnerPath` defaults to empty, which auto-resolves the runner at `../Runner/OD.MyLocalAgent.Runner.exe` relative to the UI.

## 13. Final Cleanup (finally block)

```powershell
finally {
    if (Test-Path -LiteralPath $stagingRoot -PathType Container) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
```

Regardless of success or failure, the staging directory is always removed. It is no longer needed after the swap (or after a failed build).
