<#
.SYNOPSIS
    Builds an installable SaveHarbor version on this PC, the way the release workflow does, into a local update feed.
.DESCRIPTION
    Publishes SaveHarbor.App (Release, self-contained, win-x64) with the given version and packs it with vpk into
    artifacts/local/feed. Every version packed into the same feed can update to a later one, so pack an older
    version, install it, then pack a newer one and start the installed app with --update-source on the feed.
    The installer of each version is also kept as artifacts/local/SaveHarbor-Setup-<version>.exe, because every pack
    overwrites the feed's SaveHarborApp-win-Setup.exe. Release notes come from CHANGELOG.md when it has a section
    for the version, otherwise a short "local test build" note is used. Nothing is uploaded.
.EXAMPLE
    ./build/Pack-LocalRelease.ps1 -Version 2.3.0
    & artifacts/local/SaveHarbor-Setup-2.3.0.exe
    ./build/Pack-LocalRelease.ps1 -Version 2.3.1
    & "$env:LOCALAPPDATA\SaveHarborApp\current\SaveHarbor.App.exe" --update-source "$PWD\artifacts\local\feed"
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$local = Join-Path $root 'artifacts/local'
$publish = Join-Path $local "v$Version"
$feed = Join-Path $local 'feed'
$notes = Join-Path $local "notes-$Version.md"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw 'vpk is not installed. Install it with: dotnet tool install -g vpk'
}

New-Item -ItemType Directory -Force $local, $feed | Out-Null
if (Test-Path $publish) {
    Remove-Item -Recurse -Force $publish
}

dotnet publish (Join-Path $root 'SaveHarbor.App') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

try {
    & (Join-Path $PSScriptRoot 'Get-ReleaseNotes.ps1') -Version $Version -Out $notes | Out-Null
} catch {
    Set-Content -LiteralPath $notes -Encoding utf8 -Value "### Local test build`n`n- Built on this PC with build/Pack-LocalRelease.ps1 to try the update."
}

vpk pack --packId SaveHarborApp --packVersion $Version --packDir $publish --mainExe SaveHarbor.App.exe `
    --packTitle SaveHarbor --packAuthors SaveHarbor --releaseNotes $notes `
    --icon (Join-Path $root 'SaveHarbor.App/Resources/saveharbor.ico') --outputDir $feed
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed ($LASTEXITCODE)." }

$setup = Join-Path $local "SaveHarbor-Setup-$Version.exe"
Copy-Item (Join-Path $feed 'SaveHarborApp-win-Setup.exe') $setup -Force
Write-Host "SaveHarbor $Version is in $feed; its installer is $setup."
