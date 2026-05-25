$ErrorActionPreference = "Stop"

$solutionName = "SaveHarbor"
$projectName = "SaveHarbor.App"

function Invoke-CommandStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ""
    Write-Host "==> $Description" -ForegroundColor Cyan

    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Description"
    }
}

Write-Host "=== SaveHarbor Bootstrap ===" -ForegroundColor Cyan

if (Test-Path ".\$solutionName.sln") {
    throw "A SaveHarbor solution already exists in this folder."
}

Invoke-CommandStep "Checking .NET SDK" {
    dotnet --list-sdks
}

Invoke-CommandStep "Creating solution" {
    dotnet new sln -n $solutionName
}

Invoke-CommandStep "Creating WPF project" {
    dotnet new wpf -n $projectName -f net8.0
}

$projectFile = ".\$projectName\$projectName.csproj"

Invoke-CommandStep "Adding project to solution" {
    dotnet sln add $projectFile
}

$packages = @(
    "CommunityToolkit.Mvvm",
    "Serilog",
    "Serilog.Sinks.File",
    "Microsoft.Extensions.Configuration",
    "Microsoft.Extensions.Configuration.Json",
    "Microsoft.Extensions.DependencyInjection",
    "Microsoft.Extensions.Hosting"
)

foreach ($package in $packages) {
    Invoke-CommandStep "Installing package: $package" {
        dotnet add $projectFile package $package
    }
}

Invoke-CommandStep "Creating .gitignore" {
    dotnet new gitignore
}

$folders = @(
    "Application",
    "Domain",
    "Infrastructure",
    "ViewModels",
    "Views",
    "Services",
    "Models",
    "Configuration",
    "Logs"
)

foreach ($folder in $folders) {
    New-Item -ItemType Directory -Path ".\$projectName\$folder" -Force | Out-Null
}

if (-not (Test-Path ".\.git")) {
    Invoke-CommandStep "Initializing git repository" {
        git init
    }
}

Invoke-CommandStep "Building solution" {
    dotnet build
}

Invoke-CommandStep "Creating initial git commit" {
    git add .
    git commit -m "Initial SaveHarbor bootstrap"
}

Write-Host ""
Write-Host "=== Bootstrap completed successfully ===" -ForegroundColor Green
Write-Host "Open project: code ."
Write-Host "Run app: dotnet run --project .\$projectName"