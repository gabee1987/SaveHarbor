<#
.SYNOPSIS
    Writes the CHANGELOG.md section of one version to a file, for `vpk pack --releaseNotes`.
.DESCRIPTION
    Takes the lines below "## [<Version>]" up to the next "## " heading or the link references at the end of the
    changelog. Fails when the section is missing or empty, so a release never ships without notes.
.EXAMPLE
    ./build/Get-ReleaseNotes.ps1 -Version 2.3.0 -Out artifacts/notes.md
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Out,
    [string] $Changelog = (Join-Path $PSScriptRoot '..' 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Changelog)) {
    throw "Changelog not found: $Changelog"
}

$heading = '^##\s+\[' + [regex]::Escape($Version) + '\](\s|$)'
$section = $null
foreach ($line in Get-Content -LiteralPath $Changelog -Encoding utf8) {
    if ($null -eq $section) {
        if ($line -match $heading) { $section = [System.Collections.Generic.List[string]]::new() }
        continue
    }
    if ($line -match '^##\s' -or $line -match '^\[[^\]]+\]:\s') { break }
    $section.Add($line)
}

if ($null -eq $section) {
    throw "CHANGELOG.md has no '## [$Version]' section. Add it before tagging v$Version."
}

$notes = ($section -join "`n").Trim()
if ($notes.Length -eq 0) {
    throw "The '## [$Version]' section in CHANGELOG.md is empty."
}

$folder = Split-Path -Parent $Out
if ($folder) { New-Item -ItemType Directory -Force -Path $folder | Out-Null }
[System.IO.File]::WriteAllText($Out, $notes + "`n", [System.Text.UTF8Encoding]::new($false))
Write-Host "Release notes for $Version written to $Out ($($section.Count) lines)."
