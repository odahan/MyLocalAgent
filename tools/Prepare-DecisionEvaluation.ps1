<#
.SYNOPSIS
Validates the paired pilot corpus and prepares reproducible disabled workflow inputs offline.
.DESCRIPTION
Never calls a model, imports tasks or changes application settings. Labels require independent
review before phase C. Generated files are inputs for explicitly authorized workflow tests.
#>
[CmdletBinding()]
param(
    [string]$FixturePath,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if (-not $FixturePath) { $FixturePath = Join-Path $PSScriptRoot '../Docs/evaluation/decision-readiness-pairs.v1.json' }
$resolvedFixture = (Resolve-Path -LiteralPath $FixturePath).Path
$corpus = Get-Content -LiteralPath $resolvedFixture -Raw -Encoding utf8 | ConvertFrom-Json
if ($corpus.schemaVersion -ne 1 -or $corpus.pairs.Count -lt 30) { throw 'Expected schema version 1 and at least 30 paired cases.' }
$optionIds = @($corpus.options.PSObject.Properties.Name)
if ($optionIds.Count -lt 2 -or $optionIds.Count -gt 24) { throw 'Expected 2-24 options.' }
if (@($corpus.pairs.id | Select-Object -Unique).Count -ne $corpus.pairs.Count) { throw 'Pair identifiers must be unique.' }
if (@($corpus.pairs | Where-Object split -EQ 'held-out').Count -lt 10) { throw 'Reserve at least ten held-out pairs before tuning.' }
foreach ($pair in $corpus.pairs) {
    if ($pair.split -notin @('development', 'held-out') -or $pair.expected -notin $optionIds) { throw "Invalid split or reference label: $($pair.id)." }
    foreach ($language in @('fr', 'en')) {
        if ([string]::IsNullOrWhiteSpace($pair.$language)) { throw "Missing $language text: $($pair.id)." }
    }
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
try { $fixtureHash = [BitConverter]::ToString($sha256.ComputeHash([System.IO.File]::ReadAllBytes($resolvedFixture))).Replace('-', '') }
finally { $sha256.Dispose() }
$plan = [ordered]@{
    schemaVersion = 1
    fixtureVersion = $corpus.fixtureVersion
    fixtureSha256 = $fixtureHash
    labelReviewStatus = $corpus.labelReviewStatus
    question = $corpus.question
    options = $corpus.options
    pairCount = $corpus.pairs.Count
    caseCount = 2 * $corpus.pairs.Count
    executionPerformed = $false
    cases = @()
}

if ($OutputDirectory) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $resolvedOutput) {
        if (@(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -gt 0) { throw 'Use an empty output directory to preserve previous runs.' }
    }
    else { $null = New-Item -ItemType Directory -Path $resolvedOutput }
}

foreach ($pair in $corpus.pairs) {
    foreach ($language in @('fr', 'en')) {
        $caseId = "$($pair.id)-$language"
        $plan.cases += [ordered]@{
            id = $caseId; pairId = $pair.id; language = $language; split = $pair.split
            expected = $pair.expected; category = $pair.category; source = $pair.$language
        }
        if ($OutputDirectory) {
            $task = [ordered]@{
                id = "decision-pilot-$caseId"; name = "Decision pilot $caseId"
                schedule = '0 9 * * *'; prompt = ''; enabled = $false
                errorPolicy = 'StopOnError'; tools = @('decision')
                workflow = @([ordered]@{
                    step = 'decision.evaluate'; id = 'evaluate'; next = '@end'
                    args = [ordered]@{ source = $pair.$language; question = $corpus.question; options = $corpus.options }
                })
            }
            $task | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $resolvedOutput "$caseId.json") -Encoding utf8
        }
    }
}

if ($OutputDirectory) {
    $plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'manifest.json') -Encoding utf8
    Write-Output "Prepared $($plan.caseCount) disabled inputs in $resolvedOutput. No model execution. Label review: $($plan.labelReviewStatus)."
}
else { $plan | ConvertTo-Json -Depth 12 }
