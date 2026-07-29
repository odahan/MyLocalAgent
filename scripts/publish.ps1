[CmdletBinding()]
param(
    [Parameter()]
    [string]$DestinationPath = (Join-Path $env:LOCALAPPDATA 'Programs\OD.MyLocalAgent'),

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string]$RuntimeIdentifier = 'win-x64',

    [Parameter()]
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$uiProject = Join-Path $repositoryRoot 'src\OD.MyLocalAgent.UI\OD.MyLocalAgent.UI.csproj'
$runnerProject = Join-Path $repositoryRoot 'src\OD.MyLocalAgent.Runner\OD.MyLocalAgent.Runner.csproj'
$appSettingsPath = Join-Path $repositoryRoot 'appsettings.json'
$resolvedDestination = [System.IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($DestinationPath))
$destinationRoot = [System.IO.Path]::GetPathRoot($resolvedDestination)

if ([string]::IsNullOrWhiteSpace($destinationRoot) -or
    $resolvedDestination.TrimEnd('\', '/') -eq $destinationRoot.TrimEnd('\', '/')) {
    throw 'DestinationPath must identify a dedicated application directory, not a drive root.'
}

if ($resolvedDestination.TrimEnd('\', '/') -eq $repositoryRoot.TrimEnd('\', '/')) {
    throw 'DestinationPath must not be the repository root.'
}

$destinationParent = Split-Path -Parent $resolvedDestination
New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null

$operationId = [Guid]::NewGuid().ToString('N')
$stagingRoot = Join-Path $destinationParent ".OD.MyLocalAgent-publish-$operationId"
$deploymentOutput = Join-Path $stagingRoot 'OD.MyLocalAgent'
$uiOutput = Join-Path $deploymentOutput 'UI'
$runnerOutput = Join-Path $deploymentOutput 'Runner'
$backupPath = Join-Path $destinationParent ".OD.MyLocalAgent-backup-$operationId"

function Invoke-ProjectPublish {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectPath,

        [Parameter(Mandatory)]
        [string]$OutputPath
    )

    $arguments = @(
        'publish',
        $ProjectPath,
        '-c',
        'Release',
        '--runtime',
        $RuntimeIdentifier,
        '--self-contained',
        'true',
        '-m:1',
        '--nologo',
        '--output',
        $OutputPath
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for '$ProjectPath' with exit code $LASTEXITCODE."
    }
}

try {
    Invoke-ProjectPublish -ProjectPath $uiProject -OutputPath $uiOutput
    Invoke-ProjectPublish -ProjectPath $runnerProject -OutputPath $runnerOutput

    foreach ($outputPath in @($uiOutput, $runnerOutput)) {
        $publishedSettingsPath = Join-Path $outputPath 'appsettings.json'
        Copy-Item -LiteralPath $appSettingsPath -Destination $publishedSettingsPath -Force
    }

    $uiPath = Join-Path $uiOutput 'OD.MyLocalAgent.UI.exe'
    $runnerPath = Join-Path $runnerOutput 'OD.MyLocalAgent.Runner.exe'
    if (-not (Test-Path -LiteralPath $uiPath -PathType Leaf)) {
        throw "The published UI executable was not produced at '$uiPath'."
    }

    if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) {
        throw "The published runner executable was not produced at '$runnerPath'."
    }

    $uiWindowsBase = Get-Item -LiteralPath (Join-Path $uiOutput 'WindowsBase.dll')
    if ($uiWindowsBase.Length -lt 1MB) {
        throw 'The UI deployment did not preserve the self-contained WPF WindowsBase assembly.'
    }

    if (-not (Test-Path -LiteralPath (Join-Path $uiOutput 'coreclr.dll') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $runnerOutput 'coreclr.dll') -PathType Leaf)) {
        throw 'One or both deployments are missing the self-contained .NET runtime.'
    }

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

    if (Test-Path -LiteralPath $backupPath -PathType Container) {
        Remove-Item -LiteralPath $backupPath -Recurse -Force
    }

    $publishedUiPath = Join-Path $resolvedDestination 'UI\OD.MyLocalAgent.UI.exe'
    $publishedRunnerPath = Join-Path $resolvedDestination 'Runner\OD.MyLocalAgent.Runner.exe'
    Write-Host ''
    Write-Host "Published OD.MyLocalAgent to: $resolvedDestination"
    Write-Host "Deployment mode: self-contained ($RuntimeIdentifier)"
    Write-Host "UI path: $publishedUiPath"
    Write-Host "Resolved runner path: $publishedRunnerPath"
    Write-Host 'Settings -> Scheduler:RunnerPath = ""'
    Write-Host 'The empty value auto-resolves the adjacent runner or the standard UI/Runner layout.'
}
finally {
    if (Test-Path -LiteralPath $stagingRoot -PathType Container) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
