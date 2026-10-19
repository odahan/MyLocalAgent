[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$TaskPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedPath = [IO.Path]::GetFullPath($TaskPath)
$text = Get-Content -LiteralPath $resolvedPath -Raw
$task = $text | ConvertFrom-Json -AsHashtable
if ($task.id -ne 'editorial-watch-daily-db') { throw 'This repair only supports editorial-watch-daily-db.' }
$drafts = @($task.workflow | Where-Object id -EQ 'draftApprovedSubjects')
if ($drafts.Count -ne 1 -or $drafts[0].step -ne 'ai.prompt') { throw 'Expected one daily drafting step.' }
$draft = $drafts[0]
if (-not $draft.ContainsKey('generationOptions')) { $draft.generationOptions = @{} }
$draft.generationOptions.reasoningEnabled = $false
$draft.generationOptions.Remove('reasoningEffort') | Out-Null
$draft.onError = 'Retry'
$draft.retryCount = 2
$draft.retryBackoffSeconds = 2
$task.updatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
$backupPath = $resolvedPath + '.' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '.bak'
$newText = $task | ConvertTo-Json -Depth 100
$null = $newText | ConvertFrom-Json -AsHashtable
if ((Get-Content -LiteralPath $resolvedPath -Raw) -cne $text) { throw 'The task changed during repair; retry after reviewing it.' }
[IO.File]::WriteAllText($backupPath, $text, [Text.UTF8Encoding]::new($false))
$temporaryPath = $resolvedPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporaryPath, $newText, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($temporaryPath, $resolvedPath, $true)
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
}
Write-Output "Updated daily drafting: thinking disabled, two retries. Backup: $backupPath"
