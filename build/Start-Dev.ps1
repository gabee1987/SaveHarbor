<#
.SYNOPSIS
Starts SaveHarbor against the local Supabase stack, with its own app data folder.

.DESCRIPTION
Sets SAVEHARBOR_ENVIRONMENT=Development and the local stack's publishable key for this run only (nothing is written
to the machine's or user's environment), then runs the app. Development reads appsettings.Development.json and keeps
its data in %LOCALAPPDATA%\SaveHarbor-Development, so the real sign-in, sync state and backups are never touched.
Sign-in codes arrive in Mailpit: http://127.0.0.1:54324

Start the stack first: npm run db:start
#>
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent

Push-Location $repoRoot
try {
    $status = npx supabase status -o env 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'The local Supabase stack is not running. Start it with: npm run db:start'
    }

    $keyLine = $status | Where-Object { $_ -match '^PUBLISHABLE_KEY=' } | Select-Object -First 1
    $key = if ($keyLine) { ($keyLine -replace '^PUBLISHABLE_KEY=', '').Trim('"') } else { '' }
    if ([string]::IsNullOrWhiteSpace($key)) {
        throw 'The local Supabase stack did not report a publishable key.'
    }

    $env:SAVEHARBOR_ENVIRONMENT = 'Development'
    $env:SAVEHARBOR__CloudProvider__Supabase__PublishableKey = $key

    $runArgs = @('run', '--project', 'SaveHarbor.App')
    if ($NoBuild) {
        $runArgs += '--no-build'
    }

    dotnet @runArgs
}
finally {
    Remove-Item Env:SAVEHARBOR_ENVIRONMENT, Env:SAVEHARBOR__CloudProvider__Supabase__PublishableKey -ErrorAction SilentlyContinue
    Pop-Location
}
