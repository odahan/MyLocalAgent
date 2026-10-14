[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$TaskPath,
    [Parameter(Mandatory)] [string]$DiscoveryCachePath,
    [Parameter(Mandatory)] [string]$RunnerPath,
    [string]$SourceId = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$originalTaskText = Get-Content -LiteralPath $TaskPath -Raw
$task = $originalTaskText | ConvertFrom-Json -AsHashtable
$discoveryJson = Get-Content -LiteralPath $DiscoveryCachePath -Raw
$discovery = $discoveryJson | ConvertFrom-Json -AsHashtable
if (-not $discovery.candidates.Count) { throw 'The saved discovery has no candidates.' }
$resumeIds = @('ensureSundayProposalMemory', 'prepareBlogSources', 'writeBlogArticle', 'validateBlogArticle', 'export', 'storeBlogProposal')
$task.workflow = @($task.workflow | Where-Object { $_.id -in $resumeIds })
$ensure = $task.workflow | Where-Object id -EQ 'ensureSundayProposalMemory'
$ensure.next = 'prepareBlogSources'
$prepare = $task.workflow | Where-Object id -EQ 'prepareBlogSources'
$prepare.args.discoveryJson = $discoveryJson
$prepare.args.sourceId = $SourceId
$resolvedTaskPath = [IO.Path]::GetFullPath($TaskPath)
try {
    [IO.File]::WriteAllText($resolvedTaskPath, ($task | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    if ([IO.Path]::GetExtension($RunnerPath) -eq '.dll') {
        & dotnet $RunnerPath run --task $task.id
    }
    else {
        & $RunnerPath run --task $task.id
    }
    if ($LASTEXITCODE -ne 0) { throw "Editorial drafting recovery failed with exit code $LASTEXITCODE." }
}
finally {
    [IO.File]::WriteAllText($resolvedTaskPath, $originalTaskText, [Text.UTF8Encoding]::new($false))
}
