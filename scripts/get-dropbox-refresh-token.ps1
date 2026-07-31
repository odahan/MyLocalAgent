[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

function ConvertTo-Base64Url {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    return [Convert]::ToBase64String($Bytes).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

$appKey = Read-Host "Dropbox app key"
if ([string]::IsNullOrWhiteSpace($appKey)) {
    throw "The Dropbox app key is required."
}

$verifierBytes = [byte[]]::new(64)
$randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomNumberGenerator.GetBytes($verifierBytes)
}
finally {
    $randomNumberGenerator.Dispose()
}

$codeVerifier = ConvertTo-Base64Url -Bytes $verifierBytes
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $challengeBytes = $sha256.ComputeHash(
        [Text.Encoding]::ASCII.GetBytes($codeVerifier))
}
finally {
    $sha256.Dispose()
}

$codeChallenge = ConvertTo-Base64Url -Bytes $challengeBytes

$authorizeParameters = [ordered]@{
    client_id             = $appKey.Trim()
    response_type         = "code"
    token_access_type     = "offline"
    code_challenge_method = "S256"
    code_challenge        = $codeChallenge
}
$authorizeQuery = ($authorizeParameters.GetEnumerator() | ForEach-Object {
        "{0}={1}" -f
            [Uri]::EscapeDataString($_.Key),
            [Uri]::EscapeDataString([string]$_.Value)
    }) -join "&"
$authorizeUri = "https://www.dropbox.com/oauth2/authorize?$authorizeQuery"

Write-Host ""
Write-Host "Opening Dropbox authorization in your browser..."
Start-Process $authorizeUri

$authorizationCode = Read-Host "Paste the authorization code shown by Dropbox"
if ([string]::IsNullOrWhiteSpace($authorizationCode)) {
    throw "The Dropbox authorization code is required."
}

$tokenResponse = Invoke-RestMethod `
    -Method Post `
    -Uri "https://api.dropboxapi.com/oauth2/token" `
    -ContentType "application/x-www-form-urlencoded" `
    -Body @{
        code          = $authorizationCode.Trim()
        grant_type    = "authorization_code"
        client_id     = $appKey.Trim()
        code_verifier = $codeVerifier
    }

if ([string]::IsNullOrWhiteSpace($tokenResponse.refresh_token)) {
    throw "Dropbox did not return a refresh token."
}

Set-Clipboard -Value $tokenResponse.refresh_token
Write-Host ""
Write-Host "The Dropbox refresh token is now in the clipboard."
Write-Host "Paste it directly into MyLocalAgent; it has not been written to disk."
