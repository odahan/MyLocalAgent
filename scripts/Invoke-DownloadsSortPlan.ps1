<#
.SYNOPSIS
Validates and executes an AI-generated Downloads sorting plan.

.DESCRIPTION
Treats the plan as untrusted input. Every source, hash, action, duplicate reference, and
destination is checked immediately before a non-overwriting move. Invalid or missing decisions
leave files in place and are recorded in the JSON execution log.

.PARAMETER RootPath
Absolute path of the Downloads directory to organize.

.PARAMETER PlanPath
Absolute path of the JSON plan produced by the workflow AI step.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $RootPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $PlanPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NormalizedCopyStem {
    <#
    .SYNOPSIS
    Removes a trailing French "copie" marker for deterministic name comparison.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string] $Stem
    )

    return ($Stem -replace '(?i)[\s_-]+copie(?:[\s_-]*\d+)?$', '')
}

function Get-Sha256 {
    <#
    .SYNOPSIS
    Returns an uppercase SHA-256 hash for a file.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string] $LiteralPath
    )

    return (Get-FileHash -LiteralPath $LiteralPath -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Test-IsImmediateChildFile {
    <#
    .SYNOPSIS
    Verifies that a path identifies a file directly below the workflow root.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $parent = [System.IO.Path]::GetDirectoryName($fullPath)
    return $parent.Equals($Root, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsDescendantPath {
    <#
    .SYNOPSIS
    Verifies that a canonical path remains strictly below the workflow root.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [Parameter(Mandatory = $true)]
        [string] $Root
    )

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $prefix = $Root.TrimEnd($separator, [System.IO.Path]::AltDirectorySeparatorChar) + $separator
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-AvailableDestinationPath {
    <#
    .SYNOPSIS
    Returns a non-existing destination path using _2, _3, and subsequent suffixes.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string] $DirectoryPath,

        [Parameter(Mandatory = $true)]
        [string] $FileName
    )

    $candidate = Join-Path -Path $DirectoryPath -ChildPath $FileName
    if (-not [System.IO.File]::Exists($candidate) -and -not [System.IO.Directory]::Exists($candidate)) {
        return $candidate
    }

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($FileName)
    $extension = [System.IO.Path]::GetExtension($FileName)
    for ($index = 2; $index -lt [int]::MaxValue; $index++) {
        $candidate = Join-Path -Path $DirectoryPath -ChildPath ("{0}_{1}{2}" -f $stem, $index, $extension)
        if (-not [System.IO.File]::Exists($candidate) -and -not [System.IO.Directory]::Exists($candidate)) {
            return $candidate
        }
    }

    throw "No available destination name could be generated for '$FileName'."
}

function Test-DuplicateReference {
    <#
    .SYNOPSIS
    Validates a duplicate reference using hash equality or the configured metadata rule.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $Source,

        [Parameter(Mandatory = $true)]
        [System.IO.FileInfo] $Reference
    )

    if ($Source.Length -ne $Reference.Length) {
        return $false
    }

    if ((Get-Sha256 -LiteralPath $Source.FullName) -eq (Get-Sha256 -LiteralPath $Reference.FullName)) {
        return $true
    }

    if (-not $Source.Extension.Equals($Reference.Extension, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $sourceStem = Get-NormalizedCopyStem -Stem $Source.BaseName
    $referenceStem = Get-NormalizedCopyStem -Stem $Reference.BaseName
    return $sourceStem.Equals($referenceStem, [System.StringComparison]::OrdinalIgnoreCase)
}

function Add-ExecutionError {
    <#
    .SYNOPSIS
    Adds a sanitized validation or execution error to the result collection.
    #>
    param(
        [AllowNull()]
        [string] $Source,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    $script:errors.Add([ordered]@{
        source = $Source
        message = $Message
    })
}

$root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$planFile = [System.IO.Path]::GetFullPath($PlanPath)

if (-not [System.IO.Path]::IsPathRooted($root) -or -not [System.IO.Path]::IsPathRooted($planFile)) {
    throw 'RootPath and PlanPath must be absolute.'
}

if (-not [System.IO.Directory]::Exists($root)) {
    throw "Root directory '$root' does not exist."
}

if (-not (Test-IsDescendantPath -Path $planFile -Root $root)) {
    throw 'PlanPath must remain below RootPath.'
}

if (-not [System.IO.File]::Exists($planFile)) {
    throw "Plan file '$planFile' does not exist."
}

$rootInfo = Get-Item -LiteralPath $root -Force
if (($rootInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Root directory '$root' cannot be a reparse point."
}

try {
    $plan = Get-Content -LiteralPath $planFile -Raw -Encoding UTF8 | ConvertFrom-Json
}
catch {
    throw "The plan is not valid JSON: $($_.Exception.Message)"
}

if ($null -eq $plan.version -or [int] $plan.version -ne 1) {
    throw 'The plan must have version 1.'
}

if ($null -eq $plan.items) {
    throw "The plan must contain an 'items' array."
}

$reviewFolderName = '00_A_VERIFIER'
$duplicateFolderName = '90_DOUBLONS_POTENTIELS'
$reviewFolder = Join-Path -Path $root -ChildPath $reviewFolderName
$duplicateFolder = Join-Path -Path $root -ChildPath $duplicateFolderName
[System.IO.Directory]::CreateDirectory($reviewFolder) | Out-Null
[System.IO.Directory]::CreateDirectory($duplicateFolder) | Out-Null

$allowedFolders = @{}
Get-ChildItem -LiteralPath $root -Directory -Force |
    Where-Object {
        ($_.Attributes -band ([System.IO.FileAttributes]::Hidden -bor [System.IO.FileAttributes]::System)) -eq 0 -and
        ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0 -and
        $_.Name -ne '.my-local-agent'
    } |
    ForEach-Object {
        $allowedFolders[$_.Name.ToLowerInvariant()] = $_.FullName
    }

$initialCandidates = @(
    Get-ChildItem -LiteralPath $root -File -Force |
        Where-Object {
            ($_.Attributes -band ([System.IO.FileAttributes]::Hidden -bor [System.IO.FileAttributes]::System)) -eq 0 -and
            $_.Name -notmatch '(?i)^Sort-.*\.md$'
        }
)

$initialByPath = @{}
foreach ($candidate in $initialCandidates) {
    $initialByPath[$candidate.FullName.ToLowerInvariant()] = $candidate.FullName
}

$moved = [System.Collections.Generic.List[object]]::new()
$duplicates = [System.Collections.Generic.List[object]]::new()
$review = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[object]]::new()
$handledSources = @{}
$declaredSources = @{}

foreach ($item in @($plan.items)) {
    $sourceText = if ($null -eq $item.source) { $null } else { [string] $item.source }

    try {
        if ([string]::IsNullOrWhiteSpace($sourceText)) {
            throw 'An item has no source path.'
        }

        $source = [System.IO.Path]::GetFullPath($sourceText)
        $sourceKey = $source.ToLowerInvariant()
        if ($declaredSources.ContainsKey($sourceKey)) {
            throw 'The plan contains the same source more than once.'
        }
        $declaredSources[$sourceKey] = $true

        if (-not (Test-IsImmediateChildFile -Path $source -Root $root)) {
            throw 'The source is not a file directly below RootPath.'
        }

        if (-not $initialByPath.ContainsKey($sourceKey)) {
            throw 'The source was not an eligible root candidate when execution started.'
        }

        if ([System.IO.Path]::GetFileName($source) -match '(?i)^Sort-.*\.md$') {
            throw 'Sort report files are excluded.'
        }

        if (-not [System.IO.File]::Exists($source)) {
            throw 'The source file no longer exists.'
        }

        $expectedHash = if ($null -eq $item.expectedHash) { '' } else { ([string] $item.expectedHash).ToUpperInvariant() }
        if ($expectedHash -notmatch '^[0-9A-F]{64}$') {
            throw 'expectedHash is not a valid SHA-256 value.'
        }

        $actualHash = Get-Sha256 -LiteralPath $source
        if ($actualHash -ne $expectedHash) {
            throw 'The source hash changed after inventory; the file was left in place.'
        }

        $action = if ($null -eq $item.action) { '' } else { [string] $item.action }
        if ($action -notin @('Move', 'Duplicate', 'Review')) {
            throw "Unsupported action '$action'."
        }

        $destinationFolderName = if ($null -eq $item.destinationFolder) { '' } else { [string] $item.destinationFolder }
        if ([string]::IsNullOrWhiteSpace($destinationFolderName) -or
            $destinationFolderName -ne [System.IO.Path]::GetFileName($destinationFolderName) -or
            $destinationFolderName -in @('.', '..')) {
            throw 'destinationFolder must be one immediate folder name.'
        }

        if ($action -eq 'Duplicate' -and $destinationFolderName -ne $duplicateFolderName) {
            throw "Duplicate actions must target '$duplicateFolderName'."
        }

        if ($action -eq 'Review' -and $destinationFolderName -ne $reviewFolderName) {
            throw "Review actions must target '$reviewFolderName'."
        }

        if ($action -eq 'Move' -and $destinationFolderName -in @($reviewFolderName, $duplicateFolderName)) {
            throw 'Move actions cannot target a reserved workflow folder.'
        }

        $folderKey = $destinationFolderName.ToLowerInvariant()
        if (-not $allowedFolders.ContainsKey($folderKey)) {
            throw "Destination folder '$destinationFolderName' is not an allowed immediate folder."
        }

        $referencePath = $null
        if ($action -eq 'Duplicate') {
            if ($null -eq $item.referencePath -or [string]::IsNullOrWhiteSpace([string] $item.referencePath)) {
                throw 'Duplicate actions require referencePath.'
            }

            $referencePath = [System.IO.Path]::GetFullPath([string] $item.referencePath)
            if (-not (Test-IsDescendantPath -Path $referencePath -Root $root) -or
                (Test-IsImmediateChildFile -Path $referencePath -Root $root)) {
                throw 'referencePath must identify a descendant file below an immediate folder.'
            }

            if (-not [System.IO.File]::Exists($referencePath)) {
                throw 'The duplicate reference file does not exist.'
            }

            $sourceInfo = Get-Item -LiteralPath $source -Force
            $referenceInfo = Get-Item -LiteralPath $referencePath -Force
            if (-not (Test-DuplicateReference -Source $sourceInfo -Reference $referenceInfo)) {
                throw 'The duplicate reference does not satisfy the configured duplicate rules.'
            }
        }

        $destinationDirectory = [string] $allowedFolders[$folderKey]
        $destination = Get-AvailableDestinationPath -DirectoryPath $destinationDirectory -FileName ([System.IO.Path]::GetFileName($source))
        Move-Item -LiteralPath $source -Destination $destination -ErrorAction Stop

        $entry = [ordered]@{
            source = $source
            destination = $destination
            reason = if ($null -eq $item.reason) { '' } else { [string] $item.reason }
            referencePath = $referencePath
        }

        switch ($action) {
            'Duplicate' { $duplicates.Add($entry) }
            'Review' { $review.Add($entry) }
            default { $moved.Add($entry) }
        }

        $handledSources[$sourceKey] = $true
    }
    catch {
        Add-ExecutionError -Source $sourceText -Message $_.Exception.Message
    }
}

$nonTreated = [System.Collections.Generic.List[object]]::new()
foreach ($candidate in $initialCandidates) {
    $candidateKey = $candidate.FullName.ToLowerInvariant()
    if (-not $handledSources.ContainsKey($candidateKey)) {
        $nonTreated.Add([ordered]@{
            source = $candidate.FullName
            reason = if ($declaredSources.ContainsKey($candidateKey)) {
                'The proposed action failed validation or execution.'
            }
            else {
                'No plan item was supplied for this candidate.'
            }
        })
    }
}

$result = [ordered]@{
    version = 1
    executedAt = [System.DateTimeOffset]::Now.ToString('o')
    rootPath = $root
    planPath = $planFile
    moved = $moved
    duplicates = $duplicates
    review = $review
    nonTreated = $nonTreated
    errors = $errors
    counts = [ordered]@{
        moved = $moved.Count
        duplicates = $duplicates.Count
        review = $review.Count
        nonTreated = $nonTreated.Count
        errors = $errors.Count
    }
}

$result | ConvertTo-Json -Depth 8 -Compress
