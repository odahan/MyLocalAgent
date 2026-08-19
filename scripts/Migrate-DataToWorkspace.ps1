[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter()]
    [string]$WorkspacePath,

    [Parameter()]
    [string]$UserDataPath = (Join-Path $env:LOCALAPPDATA 'OD.MyLocalAgent'),

    [Parameter()]
    [string]$SamplesPath,

    [Parameter()]
    [string]$BackupPath,

    [Parameter()]
    [switch]$SkipRuntimeChecks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$baseSettingsPath = Join-Path $repositoryRoot 'appsettings.json'
$resolvedUserDataPath = [System.IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($UserDataPath))
$userSettingsPath = Join-Path $resolvedUserDataPath 'settings.json'

function Get-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-NestedValue {
    param(
        [Parameter()][object]$Object,
        [Parameter(Mandatory)][string[]]$Names
    )

    $current = $Object
    foreach ($name in $Names) {
        if ($null -eq $current) {
            return $null
        }

        $property = $current.PSObject.Properties[$name]
        if ($null -eq $property) {
            return $null
        }

        $current = $property.Value
    }

    return $current
}

$baseSettings = Get-JsonFile -Path $baseSettingsPath
$userSettings = Get-JsonFile -Path $userSettingsPath
if ([string]::IsNullOrWhiteSpace($WorkspacePath)) {
    $WorkspacePath = Get-NestedValue -Object $userSettings -Names @('FileSystem', 'RootPath')
}

if ([string]::IsNullOrWhiteSpace($WorkspacePath)) {
    $WorkspacePath = Get-NestedValue -Object $baseSettings -Names @('FileSystem', 'RootPath')
}

if ([string]::IsNullOrWhiteSpace($WorkspacePath)) {
    throw 'The workspace path was not supplied and could not be resolved from settings.'
}

if ([string]::IsNullOrWhiteSpace($SamplesPath)) {
    $SamplesPath = Join-Path $repositoryRoot 'Docs\samples'
}

$resolvedWorkspacePath = [System.IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($WorkspacePath))
$resolvedSamplesPath = [System.IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($SamplesPath))
$workspaceDriveRoot = [System.IO.Path]::GetPathRoot($resolvedWorkspacePath)
if ($resolvedWorkspacePath.TrimEnd('\', '/') -eq $workspaceDriveRoot.TrimEnd('\', '/')) {
    throw 'WorkspacePath must identify a dedicated workspace directory, not a drive root.'
}

$workspaceDataPath = Join-Path $resolvedWorkspacePath '.od-my-local-agent'
if ($workspaceDataPath.StartsWith(
        $resolvedUserDataPath.TrimEnd('\') + '\',
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The workspace application-data directory must not be inside the legacy user-data directory.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($BackupPath)) {
    $BackupPath = Join-Path $workspaceDataPath "migration\backup-$stamp"
}

$resolvedBackupPath = [System.IO.Path]::GetFullPath(
    [Environment]::ExpandEnvironmentVariables($BackupPath))

if (-not $SkipRuntimeChecks -and -not $WhatIfPreference) {
    $runningProcesses = Get-Process -Name @(
        'OD.MyLocalAgent.UI',
        'OD.MyLocalAgent.Runner'
    ) -ErrorAction SilentlyContinue
    if ($null -ne $runningProcesses) {
        $processList = ($runningProcesses | ForEach-Object {
                "$($_.ProcessName) ($($_.Id))"
            }) -join ', '
        throw "OD.MyLocalAgent is still running: $processList"
    }

    $taskFolder = Get-NestedValue -Object $userSettings -Names @('Scheduler', 'TaskFolder')
    if ([string]::IsNullOrWhiteSpace($taskFolder)) {
        $taskFolder = Get-NestedValue -Object $baseSettings -Names @('Scheduler', 'TaskFolder')
    }

    if (-not [string]::IsNullOrWhiteSpace($taskFolder) -and
        $null -ne (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue)) {
        $taskPath = '\' + $taskFolder.Trim('\', '/') + '\'
        $activeTasks = @(Get-ScheduledTask -TaskPath $taskPath -ErrorAction SilentlyContinue |
                Where-Object State -ne 'Disabled')
        if ($activeTasks.Count -gt 0) {
            $taskNames = ($activeTasks | Select-Object -ExpandProperty TaskName) -join ', '
            throw "Scheduled tasks must be disabled before migration: $taskNames"
        }
    }
}

$entries = [System.Collections.Generic.List[object]]::new()

function Add-FileEntry {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$BackupRelativePath,
        [Parameter(Mandatory)][bool]$DeleteSource,
        [Parameter()][bool]$SkipDifferentDestination = $false
    )

    if (Test-Path -LiteralPath $Source -PathType Leaf) {
        $entries.Add([pscustomobject]@{
                Source                  = [System.IO.Path]::GetFullPath($Source)
                Destination             = [System.IO.Path]::GetFullPath($Destination)
                BackupRelativePath      = $BackupRelativePath
                DeleteSource            = $DeleteSource
                SkipDifferentDestination = $SkipDifferentDestination
            })
    }
}

function Add-DirectoryEntries {
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationDirectory,
        [Parameter(Mandatory)][string]$BackupPrefix,
        [Parameter(Mandatory)][bool]$DeleteSource,
        [Parameter()][string]$Filter = '*',
        [Parameter()][bool]$SkipDifferentDestination = $false,
        [Parameter()][string[]]$ExcludeRelativePaths = @()
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        return
    }

    foreach ($file in Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse -Filter $Filter) {
        $relativePath = [System.IO.Path]::GetRelativePath($SourceDirectory, $file.FullName)
        if ($ExcludeRelativePaths -contains $relativePath) {
            continue
        }

        Add-FileEntry `
            -Source $file.FullName `
            -Destination (Join-Path $DestinationDirectory $relativePath) `
            -BackupRelativePath (Join-Path $BackupPrefix $relativePath) `
            -DeleteSource $DeleteSource `
            -SkipDifferentDestination $SkipDifferentDestination
    }
}

$legacyTasksPath = Join-Path $resolvedUserDataPath 'tasks'
$legacyTaskBackupsPath = Join-Path $resolvedUserDataPath 'task-backups'
$legacyLogsPath = Join-Path $resolvedUserDataPath 'logs'
$legacyUserReportsPath = Join-Path $resolvedUserDataPath 'reports'
$legacyWorkspaceReportsPath = Join-Path $resolvedWorkspacePath 'reports'
$tasksDestination = Join-Path $workspaceDataPath 'tasks'
$taskBackupsDestination = Join-Path $tasksDestination 'backups'
$applicationLogsDestination = Join-Path $workspaceDataPath 'logs\application'
$runLogsDestination = Join-Path $workspaceDataPath 'logs\runs'
$dataDestination = Join-Path $workspaceDataPath 'data'
$reportsDestination = Join-Path $workspaceDataPath 'outputs\reports'
$examplesDestination = Join-Path $workspaceDataPath 'examples\tasks'

Add-DirectoryEntries $legacyTasksPath $tasksDestination 'legacy-user-data\tasks' $true
Add-DirectoryEntries $legacyTaskBackupsPath $taskBackupsDestination 'legacy-user-data\task-backups' $true
Add-DirectoryEntries (Join-Path $legacyLogsPath 'runs') $runLogsDestination 'legacy-user-data\logs\runs' $true
Add-DirectoryEntries (Join-Path $legacyLogsPath 'archive') (Join-Path $applicationLogsDestination 'archive') 'legacy-user-data\logs\archive' $true

if (Test-Path -LiteralPath $legacyLogsPath -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $legacyLogsPath -File) {
        $destination = if ($file.Name -eq 'incidents.jsonl') {
            Join-Path $workspaceDataPath 'logs\incidents.jsonl'
        }
        else {
            Join-Path $applicationLogsDestination $file.Name
        }
        Add-FileEntry $file.FullName $destination (Join-Path 'legacy-user-data\logs' $file.Name) $true
    }
}

foreach ($databaseName in @(
        'direct-chat.db',
        'direct-chat.db-journal',
        'direct-chat.db-wal',
        'direct-chat.db-shm')) {
    Add-FileEntry `
        (Join-Path $resolvedUserDataPath $databaseName) `
        (Join-Path $dataDestination $databaseName) `
        (Join-Path 'legacy-user-data' $databaseName) `
        $true
}

Add-DirectoryEntries `
    $legacyUserReportsPath `
    $reportsDestination `
    'legacy-user-data\reports' `
    $true `
    -ExcludeRelativePaths @('index.jsonl')
Add-DirectoryEntries `
    $legacyWorkspaceReportsPath `
    $reportsDestination `
    'legacy-workspace\reports' `
    $true `
    -ExcludeRelativePaths @('index.jsonl')
Add-DirectoryEntries $resolvedSamplesPath $examplesDestination 'samples' $false '*.json' $true

$reportIndexSources = @(
    @(
        [pscustomobject]@{
            Path = Join-Path $legacyUserReportsPath 'index.jsonl'
            BackupRelativePath = 'legacy-user-data\reports\index.jsonl'
        },
        [pscustomobject]@{
            Path = Join-Path $legacyWorkspaceReportsPath 'index.jsonl'
            BackupRelativePath = 'legacy-workspace\reports\index.jsonl'
        }
    ) | Where-Object { Test-Path -LiteralPath $_.Path -PathType Leaf }
)

function Test-ReportIndexNeedsRewrite {
    if ($reportIndexSources.Count -gt 0) {
        return $true
    }

    if (-not (Test-Path -LiteralPath (Join-Path $reportsDestination 'index.jsonl') -PathType Leaf)) {
        return $false
    }

    foreach ($line in Get-Content -LiteralPath (Join-Path $reportsDestination 'index.jsonl')) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $record = $line | ConvertFrom-Json
        $reportPath = [string]$record.reportPath
        if ([string]::IsNullOrWhiteSpace($reportPath) -or
            -not [System.IO.Path]::GetFullPath($reportPath).StartsWith(
                $reportsDestination.TrimEnd('\') + '\',
                [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

$reportIndexNeedsRewrite = Test-ReportIndexNeedsRewrite

$actionableEntries = [System.Collections.Generic.List[object]]::new()
foreach ($entry in $entries) {
    if (Test-Path -LiteralPath $entry.Destination -PathType Leaf) {
        $sourceHash = (Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash
        $destinationHash = (Get-FileHash -LiteralPath $entry.Destination -Algorithm SHA256).Hash
        if ($sourceHash -ne $destinationHash) {
            if ($entry.SkipDifferentDestination) {
                Write-Warning "Keeping existing destination: $($entry.Destination)"
                continue
            }

            throw "Migration conflict: destination already contains a different file: $($entry.Destination)"
        }

        if (-not $entry.DeleteSource) {
            continue
        }
    }

    $actionableEntries.Add($entry)
}

Write-Host ''
Write-Host "Legacy user data : $resolvedUserDataPath"
Write-Host "Workspace        : $resolvedWorkspacePath"
Write-Host "Workspace data   : $workspaceDataPath"
Write-Host "Backup           : $resolvedBackupPath"
$selectedCount = $actionableEntries.Count + $reportIndexSources.Count
if ($reportIndexNeedsRewrite -and $reportIndexSources.Count -eq 0) {
    $selectedCount++
}
Write-Host "Files selected   : $selectedCount"
Write-Host ''
foreach ($entry in $actionableEntries) {
    $operation = if ($entry.DeleteSource) { 'MOVE' } else { 'COPY' }
    Write-Host "$operation  $($entry.Source)"
    Write-Host "   -> $($entry.Destination)"
}
if ($reportIndexNeedsRewrite) {
    foreach ($source in $reportIndexSources) {
        Write-Host "MERGE $($source.Path)"
        Write-Host "   -> $(Join-Path $reportsDestination 'index.jsonl')"
    }

    if ($reportIndexSources.Count -eq 0) {
        Write-Host "REWRITE $(Join-Path $reportsDestination 'index.jsonl')"
    }
}

if (-not $PSCmdlet.ShouldProcess(
        $workspaceDataPath,
        "Migrate $selectedCount file(s) and clean verified sources")) {
    return
}

$copiedCount = 0
$removedCount = 0
foreach ($entry in $actionableEntries) {
    $sourceHash = (Get-FileHash -LiteralPath $entry.Source -Algorithm SHA256).Hash

    if ($entry.DeleteSource) {
        $backupFilePath = Join-Path $resolvedBackupPath $entry.BackupRelativePath
        $backupDirectory = Split-Path -Parent $backupFilePath
        New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
        Copy-Item -LiteralPath $entry.Source -Destination $backupFilePath
        $backupHash = (Get-FileHash -LiteralPath $backupFilePath -Algorithm SHA256).Hash
        if ($backupHash -ne $sourceHash) {
            throw "Backup verification failed for '$($entry.Source)'."
        }
    }

    if (-not (Test-Path -LiteralPath $entry.Destination -PathType Leaf)) {
        $destinationDirectory = Split-Path -Parent $entry.Destination
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        $temporaryPath = $entry.Destination + '.migration-' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            Copy-Item -LiteralPath $entry.Source -Destination $temporaryPath
            $temporaryHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
            if ($temporaryHash -ne $sourceHash) {
                throw "Copy verification failed for '$($entry.Source)'."
            }

            Move-Item -LiteralPath $temporaryPath -Destination $entry.Destination
            $copiedCount++
        }
        finally {
            if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
                Remove-Item -LiteralPath $temporaryPath -Force
            }
        }
    }

    if ($entry.DeleteSource) {
        Remove-Item -LiteralPath $entry.Source -Force
        $removedCount++
    }
}

if ($reportIndexNeedsRewrite) {
    $reportIndexPath = Join-Path $reportsDestination 'index.jsonl'
    $indexInputPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($source in $reportIndexSources) {
        $indexInputPaths.Add($source.Path)
    }
    if (Test-Path -LiteralPath $reportIndexPath -PathType Leaf) {
        $indexInputPaths.Add($reportIndexPath)
    }

    foreach ($source in $reportIndexSources) {
        $sourceHash = (Get-FileHash -LiteralPath $source.Path -Algorithm SHA256).Hash
        $backupFilePath = Join-Path $resolvedBackupPath $source.BackupRelativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $backupFilePath) -Force | Out-Null
        Copy-Item -LiteralPath $source.Path -Destination $backupFilePath
        if ((Get-FileHash -LiteralPath $backupFilePath -Algorithm SHA256).Hash -ne $sourceHash) {
            throw "Backup verification failed for '$($source.Path)'."
        }
    }

    if (Test-Path -LiteralPath $reportIndexPath -PathType Leaf) {
        $existingIndexBackupPath = Join-Path `
            $resolvedBackupPath `
            'workspace-data-existing\outputs\reports\index.jsonl'
        New-Item -ItemType Directory -Path (Split-Path -Parent $existingIndexBackupPath) -Force | Out-Null
        Copy-Item -LiteralPath $reportIndexPath -Destination $existingIndexBackupPath
        $existingHash = (Get-FileHash -LiteralPath $reportIndexPath -Algorithm SHA256).Hash
        if ((Get-FileHash -LiteralPath $existingIndexBackupPath -Algorithm SHA256).Hash -ne $existingHash) {
            throw "Backup verification failed for '$reportIndexPath'."
        }
    }

    $knownReportRoots = @(
        $legacyUserReportsPath,
        $legacyWorkspaceReportsPath,
        $reportsDestination
    )
    $mergedRecords = [System.Collections.Generic.List[object]]::new()
    $seenRecords = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $sequence = 0
    foreach ($indexInputPath in $indexInputPaths) {
        foreach ($line in Get-Content -LiteralPath $indexInputPath) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            $record = $line | ConvertFrom-Json
            $originalReportPath = [System.IO.Path]::GetFullPath([string]$record.reportPath)
            $relativeReportPath = $null
            foreach ($knownRoot in $knownReportRoots) {
                $rootPrefix = $knownRoot.TrimEnd('\') + '\'
                if ($originalReportPath.StartsWith(
                        $rootPrefix,
                        [System.StringComparison]::OrdinalIgnoreCase)) {
                    $relativeReportPath = [System.IO.Path]::GetRelativePath(
                        $knownRoot,
                        $originalReportPath)
                    break
                }
            }

            if ([string]::IsNullOrWhiteSpace($relativeReportPath) -or
                $relativeReportPath.StartsWith('..')) {
                $relativeReportPath = Join-Path `
                    ([string]$record.taskId) `
                    ([System.IO.Path]::GetFileName($originalReportPath))
            }

            $record.reportPath = [System.IO.Path]::GetFullPath(
                (Join-Path $reportsDestination $relativeReportPath))
            $normalizedJson = $record | ConvertTo-Json -Compress -Depth 20
            if ($seenRecords.Add($normalizedJson)) {
                $startedUtc = [System.DateTimeOffset]::Parse(
                    [string]$record.startedUtc,
                    [System.Globalization.CultureInfo]::InvariantCulture)
                $mergedRecords.Add([pscustomobject]@{
                        StartedUtc = $startedUtc
                        Sequence = $sequence++
                        Json = $normalizedJson
                    })
            }
        }
    }

    $mergedLines = @($mergedRecords |
            Sort-Object StartedUtc, Sequence |
            Select-Object -ExpandProperty Json)
    New-Item -ItemType Directory -Path $reportsDestination -Force | Out-Null
    $temporaryIndexPath = $reportIndexPath + '.migration-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [System.IO.File]::WriteAllLines(
            $temporaryIndexPath,
            $mergedLines,
            [System.Text.UTF8Encoding]::new($false))
        $verifiedLineCount = 0
        foreach ($line in Get-Content -LiteralPath $temporaryIndexPath) {
            $verifiedRecord = $line | ConvertFrom-Json
            if (-not (Test-Path -LiteralPath ([string]$verifiedRecord.reportPath) -PathType Leaf)) {
                throw "The merged report index references a missing report: $($verifiedRecord.reportPath)"
            }
            $verifiedLineCount++
        }
        if ($verifiedLineCount -ne $mergedLines.Count) {
            throw 'Report-index verification failed.'
        }

        [System.IO.File]::Move($temporaryIndexPath, $reportIndexPath, $true)
        $copiedCount++
    }
    finally {
        if (Test-Path -LiteralPath $temporaryIndexPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryIndexPath -Force
        }
    }

    foreach ($source in $reportIndexSources) {
        Remove-Item -LiteralPath $source.Path -Force
        $removedCount++
    }
}

$directChatPath = Join-Path $dataDestination 'direct-chat.db'
if (Test-Path -LiteralPath $directChatPath -PathType Leaf) {
    $databaseStream = [System.IO.File]::OpenRead($directChatPath)
    try {
        $headerBytes = [byte[]]::new(16)
        $headerLength = $databaseStream.Read($headerBytes, 0, $headerBytes.Length)
    }
    finally {
        $databaseStream.Dispose()
    }

    $sqliteHeader = [System.Text.Encoding]::ASCII.GetString($headerBytes, 0, $headerLength)
    if ($sqliteHeader -ne "SQLite format 3`0") {
        throw "The migrated Direct Chat database does not have a valid SQLite header. Restore it from '$resolvedBackupPath'."
    }
}

function Remove-EmptyDirectories {
    param([Parameter(Mandatory)][string]$RootDirectory)

    if (-not (Test-Path -LiteralPath $RootDirectory -PathType Container)) {
        return
    }

    $directories = @(Get-ChildItem -LiteralPath $RootDirectory -Directory -Recurse |
            Sort-Object { $_.FullName.Length } -Descending)
    foreach ($directory in $directories) {
        if (@(Get-ChildItem -LiteralPath $directory.FullName -Force).Count -eq 0) {
            Remove-Item -LiteralPath $directory.FullName -Force
        }
    }

    if (@(Get-ChildItem -LiteralPath $RootDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $RootDirectory -Force
    }
}

foreach ($directory in @(
        $legacyTasksPath,
        $legacyTaskBackupsPath,
        $legacyLogsPath,
        $legacyUserReportsPath,
        $legacyWorkspaceReportsPath)) {
    if (Test-Path -LiteralPath $directory -PathType Container) {
        Remove-EmptyDirectories -RootDirectory $directory
    }
}

Write-Host ''
Write-Host 'Migration completed successfully.'
Write-Host "Files copied : $copiedCount"
Write-Host "Sources removed after verification : $removedCount"
Write-Host "Backup retained at : $resolvedBackupPath"
Write-Host 'Keep the backup until the new UI, runner, Direct Chat, reports, and schedules have been verified.'
