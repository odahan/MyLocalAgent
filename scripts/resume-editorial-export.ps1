[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$TaskPath,
    [Parameter(Mandatory)] [string]$ContextPath,
    [Parameter(Mandatory)] [string]$RunnerPath,
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$originalTaskText = Get-Content -LiteralPath $TaskPath -Raw
$task = $originalTaskText | ConvertFrom-Json -AsHashtable
$context = Get-Content -LiteralPath $ContextPath -Raw | ConvertFrom-Json -AsHashtable
$article = $context.steps.writeBlogArticle
$source = $context.steps.prepareBlogSources
if (-not $article.markdown -or -not $source.canonicalUrl) { throw 'No completed draft and validated source are available for recovery.' }
$validator = $task.workflow | Where-Object id -EQ 'validateBlogArticle'
$export = $task.workflow | Where-Object id -EQ 'export'
$store = $task.workflow | Where-Object id -EQ 'storeBlogProposal'
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) { $export.args.path = $OutputPath }
$validator.args.articleJson = $article | ConvertTo-Json -Depth 100 -Compress
$validator.args.sourceUrl = $source.canonicalUrl
$store.args.sourceUrl = $source.canonicalUrl
$task.workflow = @($validator, $export, $store)
$recoveryPath = [IO.Path]::GetFullPath($TaskPath)
try {
    [IO.File]::WriteAllText($recoveryPath, ($task | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    if ([IO.Path]::GetExtension($RunnerPath) -eq '.dll') {
        & dotnet $RunnerPath run --task $task.id
        $runnerExitCode = $LASTEXITCODE
    }
    else {
        $runnerProcess = Start-Process -FilePath $RunnerPath -ArgumentList @('run', '--task', $task.id) -WindowStyle Hidden -Wait -PassThru
        $runnerExitCode = $runnerProcess.ExitCode
    }
    if ($runnerExitCode -ne 0) { throw "Editorial export recovery failed with exit code $runnerExitCode." }
}
finally {
    [IO.File]::WriteAllText($recoveryPath, $originalTaskText, [Text.UTF8Encoding]::new($false))
}
