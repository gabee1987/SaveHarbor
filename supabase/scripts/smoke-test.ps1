<#
.SYNOPSIS
  End-to-end check of the LOCAL SaveHarbor Cloud stack through its real HTTP APIs.

.DESCRIPTION
  Signs in the two seed users with e-mail codes read from Mailpit, uploads an archive to storage, publishes it as a
  version, races two lock requests at the same instant several times (exactly one may win each round), heartbeats,
  downloads and verifies the archive as the other user, and finally removes the world again.

  Local only: it reads sign-in codes from Mailpit, which exists only in the local stack. Never point it at the hosted
  project. Requires `npm run db:start` (or `npx supabase start`) first.

.EXAMPLE
  pwsh supabase/scripts/smoke-test.ps1
#>
[CmdletBinding()]
param(
    [string]$ApiUrl = 'http://127.0.0.1:54321',
    [string]$MailpitUrl = 'http://127.0.0.1:54324',
    [string]$PublishableKey = '',
    [int]$RaceRounds = 10
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($ApiUrl -notmatch '^http://(127\.0\.0\.1|localhost)(:\d+)?$') {
    throw 'This script only runs against the local stack.'
}

$GroupId = '11111111-1111-4111-8111-111111111111'   # TEST_GROUP from seed.sql
$WorldId = 'TEST_WORLD_SMOKE'
$EmailA = 'test-a@example.invalid'
$EmailB = 'test-b@example.invalid'
$script:Failures = 0

function Write-Check([bool]$Passed, [string]$Name) {
    if ($Passed) { Write-Host "  PASS  $Name" -ForegroundColor Green }
    else { Write-Host "  FAIL  $Name" -ForegroundColor Red; $script:Failures++ }
}

if (-not $PublishableKey) {
    # Resolved from the script's own location: a PowerShell profile may change the current folder at startup.
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $cli = Join-Path $repoRoot 'node_modules/supabase/dist/supabase.js'
    if (-not (Test-Path $cli)) {
        throw "The pinned Supabase CLI is missing. Run 'npm install' in $repoRoot first."
    }
    $keyLine = & node $cli status -o env --workdir $repoRoot 2>$null | Where-Object { $_ -match '^PUBLISHABLE_KEY=' } | Select-Object -First 1
    if (-not $keyLine) {
        throw "Could not read the local publishable key. Start the stack with 'npm run db:start', or pass -PublishableKey."
    }
    $PublishableKey = ($keyLine -replace '^PUBLISHABLE_KEY=', '').Trim('"')
}

$http = [System.Net.Http.HttpClient]::new()

function Send-Json([string]$Method, [string]$Path, $Body, [string]$Token) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$ApiUrl$Path")
    $request.Headers.Add('apikey', $PublishableKey)
    if ($Token) { $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token) }
    if ($null -ne $Body) {
        $request.Content = [System.Net.Http.StringContent]::new(($Body | ConvertTo-Json -Depth 10 -Compress), [Text.Encoding]::UTF8, 'application/json')
    }
    return $http.SendAsync($request)
}

function Read-Response($Task) {
    $response = $Task.GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $text; Json = $(if ($text) { try { $text | ConvertFrom-Json } catch { $null } }) }
}

function Invoke-Json([string]$Method, [string]$Path, $Body, [string]$Token) {
    Read-Response (Send-Json $Method $Path $Body $Token)
}

function Get-SignInToken([string]$Email) {
    Invoke-RestMethod -Method Delete -Uri "$MailpitUrl/api/v1/messages" | Out-Null
    $otp = Invoke-Json 'POST' '/auth/v1/otp' @{ email = $Email; create_user = $false }
    if ($otp.Status -ne 200) { throw "Requesting a code for $Email failed ($($otp.Status)): $($otp.Body)" }

    $code = $null
    for ($i = 0; $i -lt 20 -and -not $code; $i++) {
        Start-Sleep -Milliseconds 250
        $search = Invoke-RestMethod -Uri "$MailpitUrl/api/v1/search?query=$([uri]::EscapeDataString("to:$Email"))"
        if ($search.messages.Count -gt 0) {
            $message = Invoke-RestMethod -Uri "$MailpitUrl/api/v1/message/$($search.messages[0].ID)"
            if ($message.Text -match '\b(\d{6})\b') { $code = $Matches[1] }
        }
    }
    if (-not $code) { throw "No sign-in code arrived in Mailpit for $Email." }

    $verify = Invoke-Json 'POST' '/auth/v1/verify' @{ type = 'email'; email = $Email; token = $code }
    if ($verify.Status -ne 200) { throw "Verifying the code for $Email failed ($($verify.Status)): $($verify.Body)" }
    return $verify.Json.access_token
}

Write-Host 'Public metadata'
$meta = Invoke-Json 'GET' '/rest/v1/app_meta?select=key,value' $null $null
Write-Check ($meta.Status -eq 200 -and ($meta.Json | Where-Object key -eq 'min_supported_app_version')) 'anon reads min_supported_app_version'
$denied = Invoke-Json 'GET' '/rest/v1/worlds?select=id' $null $null
Write-Check ($denied.Status -in 401, 403) 'anon cannot read worlds'

Write-Host 'Sign-in with e-mail codes'
$tokenA = Get-SignInToken $EmailA
$tokenB = Get-SignInToken $EmailB
Write-Check ([bool]$tokenA -and [bool]$tokenB) 'both seed users signed in with a code from Mailpit'

Write-Host 'Upload and publish'
$archive = Join-Path ([IO.Path]::GetTempPath()) "saveharbor-smoke-$([guid]::NewGuid().ToString('N')).zip"
$source = "$archive.txt"
Set-Content -Path $source -Value 'TEST_WORLD_SMOKE placeholder save data'
Compress-Archive -Path $source -DestinationPath $archive -Force
$sha = (Get-FileHash -Path $archive -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $archive).Length
$versionId = "smoke_$(Get-Date -Format yyyyMMdd_HHmmss)_v1"
$objectPath = "$GroupId/windrose/$WorldId/$versionId.zip"

$upload = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$ApiUrl/storage/v1/object/saves/$objectPath")
$upload.Headers.Add('apikey', $PublishableKey)
$upload.Headers.Add('x-upsert', 'false')
$upload.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)
$upload.Content = [System.Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($archive))
$upload.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/zip')
$uploadResult = Read-Response ($http.SendAsync($upload))
Write-Check ($uploadResult.Status -eq 200) "archive uploaded to storage ($size bytes)"

$outsidePath = "00000000-0000-4000-8000-000000000000/windrose/$WorldId/$versionId.zip"
$outside = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$ApiUrl/storage/v1/object/saves/$outsidePath")
$outside.Headers.Add('apikey', $PublishableKey)
$outside.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenA)
$outside.Content = [System.Net.Http.ByteArrayContent]::new([byte[]](1, 2, 3))
$outside.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('application/zip')
Write-Check ((Read-Response ($http.SendAsync($outside))).Status -ge 400) 'upload outside the own group is refused'

$publish = Invoke-Json 'POST' '/rest/v1/rpc/publish_world_version' @{
    p_group = $GroupId; p_game = 'windrose'; p_world_id = $WorldId; p_world_name = 'Smoke Test World'; p_expected_latest = 0
    p_version = @{ version_id = $versionId; archive_path = $objectPath; archive_sha256 = $sha; archive_size_bytes = $size; uploader_device = 'TEST_DEVICE_A' }
} $tokenA
Write-Check ($publish.Status -eq 200 -and $publish.Json.version_number -eq 1) 'published as version 1'
$worldUuid = $publish.Json.world_uuid

$conflict = Invoke-Json 'POST' '/rest/v1/rpc/publish_world_version' @{
    p_group = $GroupId; p_game = 'windrose'; p_world_id = $WorldId; p_world_name = 'Smoke Test World'; p_expected_latest = 0
    p_version = @{ version_id = "$versionId-again"; archive_path = $objectPath; archive_sha256 = $sha; archive_size_bytes = $size }
} $tokenB
Write-Check ($conflict.Status -eq 400 -and $conflict.Json.message -eq 'version_conflict') 'a stale publish returns version_conflict over HTTP'

Write-Host "Lock race ($RaceRounds rounds, both requests sent at the same instant)"
$cleanRounds = 0
for ($round = 1; $round -le $RaceRounds; $round++) {
    $lockA = [guid]::NewGuid().ToString(); $lockB = [guid]::NewGuid().ToString()
    $argsFor = { param($lock, $device) @{ p_world = $worldUuid; p_lock_id = $lock; p_device_id = $device; p_device_label = 'TEST_DEVICE'; p_based_on_number = 1; p_based_on_id = $versionId; p_ttl_seconds = 120 } }
    $taskA = Send-Json 'POST' '/rest/v1/rpc/acquire_session_lock' (& $argsFor $lockA 'da000000-0000-4000-8000-000000000001') $tokenA
    $taskB = Send-Json 'POST' '/rest/v1/rpc/acquire_session_lock' (& $argsFor $lockB 'db000000-0000-4000-8000-000000000001') $tokenB
    $resultA = Read-Response $taskA; $resultB = Read-Response $taskB
    $winsA = $resultA.Json.lock_id -eq $lockA; $winsB = $resultB.Json.lock_id -eq $lockB
    $sameHolder = $resultA.Json.lock_id -eq $resultB.Json.lock_id
    if (($winsA -xor $winsB) -and $sameHolder) { $cleanRounds++ }
    $winnerToken = if ($winsA) { $tokenA } else { $tokenB }
    $winnerLock = if ($winsA) { $lockA } else { $lockB }
    if ($round -eq $RaceRounds) {
        $beat = Invoke-Json 'POST' '/rest/v1/rpc/heartbeat_session_lock' @{ p_world = $worldUuid; p_lock_id = $winnerLock; p_ttl_seconds = 120 } $winnerToken
        Write-Check ($beat.Status -eq 200) 'the winner can heartbeat its lock'
        $snapshot = Invoke-Json 'POST' '/rest/v1/rpc/get_group_snapshot' @{ p_group = $GroupId; p_game = 'windrose' } $tokenA
        $smokeWorld = $snapshot.Json.worlds | Where-Object world_id -eq $WorldId
        Write-Check ($smokeWorld.lock.lock_id -eq $winnerLock -and $smokeWorld.latest.version_number -eq 1) 'snapshot shows the latest version and the live lock'
    }
    $release = Invoke-Json 'POST' '/rest/v1/rpc/release_session_lock' @{ p_world = $worldUuid; p_lock_id = $winnerLock } $winnerToken
    if ($release.Body -ne 'true') { Write-Check $false "round $round release" }
}
Write-Check ($cleanRounds -eq $RaceRounds) "exactly one winner in $cleanRounds of $RaceRounds rounds"

Write-Host 'Download and verify as the other player'
$downloadPath = "$archive.download"
$download = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$ApiUrl/storage/v1/object/authenticated/saves/$objectPath")
$download.Headers.Add('apikey', $PublishableKey)
$download.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $tokenB)
$downloadResponse = $http.SendAsync($download).GetAwaiter().GetResult()
[IO.File]::WriteAllBytes($downloadPath, $downloadResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
$downloadSha = (Get-FileHash -Path $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Check ([int]$downloadResponse.StatusCode -eq 200 -and $downloadSha -eq $sha) 'B downloads the archive and the SHA-256 matches'

Write-Host 'Clean up'
$deleteObjects = Invoke-Json 'DELETE' '/storage/v1/object/saves' @{ prefixes = @($objectPath) } $tokenA
Write-Check ($deleteObjects.Status -eq 200) 'archive deleted from storage'
$remove = Invoke-Json 'POST' '/rest/v1/rpc/remove_world' @{ p_world = $worldUuid } $tokenA
Write-Check ($remove.Status -in 200, 204) 'world removed'
Remove-Item -Path $archive, $source, $downloadPath -ErrorAction SilentlyContinue

Write-Host ''
if ($script:Failures -gt 0) { Write-Host "$script:Failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All smoke checks passed.' -ForegroundColor Green
