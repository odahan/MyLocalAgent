<#
.SYNOPSIS
Builds a read-only JSON inventory for the intelligent Downloads sorting workflow.

.DESCRIPTION
Lists eligible files at the root of the supplied directory, lists immediate destination
folders, computes SHA-256 hashes, and finds certain or metadata-based duplicate references
below the root. The script never creates, modifies, moves, or deletes a file.

.PARAMETER RootPath
Absolute path of the Downloads directory to inspect.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string] $RootPath
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

function Test-IgnoredAttributes {
    <#
    .SYNOPSIS
    Indicates whether a filesystem entry must be excluded from the workflow inventory.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.FileSystemInfo] $Entry
    )

    $ignored = [System.IO.FileAttributes]::Hidden -bor [System.IO.FileAttributes]::System
    return (($Entry.Attributes -band $ignored) -ne 0)
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

$root = [System.IO.Path]::GetFullPath($RootPath).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)

if (-not [System.IO.Path]::IsPathRooted($root)) {
    throw 'RootPath must be absolute.'
}

if (-not [System.IO.Directory]::Exists($root)) {
    throw "Root directory '$root' does not exist."
}

$rootInfo = Get-Item -LiteralPath $root -Force
if (($rootInfo.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Root directory '$root' cannot be a reparse point."
}

$rootFiles = @(
    Get-ChildItem -LiteralPath $root -File -Force |
        Where-Object {
            -not (Test-IgnoredAttributes -Entry $_) -and
            $_.Name -notmatch '(?i)^Sort-.*\.md$'
        } |
        Sort-Object -Property FullName
)

$destinationFolders = @(
    Get-ChildItem -LiteralPath $root -Directory -Force |
        Where-Object {
            -not (Test-IgnoredAttributes -Entry $_) -and
            ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0 -and
            $_.Name -ne '.my-local-agent'
        } |
        Sort-Object -Property Name |
        ForEach-Object {
            [ordered]@{
                name = $_.Name
                path = $_.FullName
            }
        }
)

$candidateSizes = @{}
foreach ($file in $rootFiles) {
    $candidateSizes[[string] $file.Length] = $true
}

$descendantFiles = @(
    if ($rootFiles.Count -gt 0) {
        Get-ChildItem -LiteralPath $root -File -Recurse -Force |
            Where-Object {
                $_.DirectoryName -ne $root -and
                -not (Test-IgnoredAttributes -Entry $_) -and
                ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0 -and
                $candidateSizes.ContainsKey([string] $_.Length)
            } |
            Sort-Object -Property FullName
    }
)

$hashCache = @{}
function Get-CachedSha256 {
    <#
    .SYNOPSIS
    Computes a SHA-256 hash once per canonical path during the inventory run.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string] $LiteralPath
    )

    if (-not $hashCache.ContainsKey($LiteralPath)) {
        $hashCache[$LiteralPath] = Get-Sha256 -LiteralPath $LiteralPath
    }

    return $hashCache[$LiteralPath]
}

$candidates = @(
    foreach ($file in $rootFiles) {
        $extension = $file.Extension.ToLowerInvariant()
        $normalizedStem = Get-NormalizedCopyStem -Stem $file.BaseName
        $hash = Get-CachedSha256 -LiteralPath $file.FullName
        $references = @(
            foreach ($other in $descendantFiles) {
                if ($other.Length -ne $file.Length) {
                    continue
                }

                $otherHash = Get-CachedSha256 -LiteralPath $other.FullName
                $hashMatches = $hash -eq $otherHash
                $extensionMatches = $extension -eq $other.Extension.ToLowerInvariant()
                $otherNormalizedStem = Get-NormalizedCopyStem -Stem $other.BaseName
                $nameMatches =
                    $file.BaseName.Equals($other.BaseName, [System.StringComparison]::OrdinalIgnoreCase) -or
                    $normalizedStem.Equals($otherNormalizedStem, [System.StringComparison]::OrdinalIgnoreCase)

                if ($hashMatches -or ($extensionMatches -and $nameMatches)) {
                    [ordered]@{
                        path = $other.FullName
                        status = if ($hashMatches) { 'Certain' } else { 'Potential' }
                        hash = $otherHash
                        size = $other.Length
                        extension = $other.Extension.ToLowerInvariant()
                    }
                }
            }
        )

        $duplicateStatus = 'None'
        if (@($references | Where-Object { $_.status -eq 'Certain' }).Count -gt 0) {
            $duplicateStatus = 'Certain'
        }
        elseif ($references.Count -gt 0) {
            $duplicateStatus = 'Potential'
        }

        [ordered]@{
            source = $file.FullName
            name = $file.Name
            baseName = $file.BaseName
            extension = $extension
            size = $file.Length
            lastModifiedUtc = $file.LastWriteTimeUtc.ToString('o')
            hashAlgorithm = 'SHA-256'
            hash = $hash
            duplicateStatus = $duplicateStatus
            duplicateReferences = $references
        }
    }
)

$inventory = [ordered]@{
    version = 1
    generatedAt = [System.DateTimeOffset]::Now.ToString('o')
    rootPath = $root
    exclusionPattern = '^Sort-.*\.md$'
    destinationFolders = $destinationFolders
    candidates = $candidates
    counts = [ordered]@{
        candidates = $candidates.Count
        destinationFolders = $destinationFolders.Count
        certainDuplicates = @($candidates | Where-Object { $_.duplicateStatus -eq 'Certain' }).Count
        potentialDuplicates = @($candidates | Where-Object { $_.duplicateStatus -eq 'Potential' }).Count
    }
}

$inventory | ConvertTo-Json -Depth 8 -Compress
