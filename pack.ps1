#Requires -Version 7.0
<#
.SYNOPSIS
    Builds, tests, and packs the readmd .NET tool, optionally pushing it to nuget.org.

.DESCRIPTION
    Restores and builds the solution in Release, runs the test suite, then packs
    src/Readmd.Cli (the `readmd` global/local .NET tool) into ./artifacts/nupkg.

    With -Push, the resulting .nupkg is published to nuget.org. The API key is taken
    from -ApiKey if provided, otherwise from the NUGET_API_KEY environment variable.

.PARAMETER Push
    Publish the packed .nupkg to nuget.org after a successful pack.

.PARAMETER ApiKey
    The nuget.org API key to use when pushing. Falls back to $env:NUGET_API_KEY.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER SkipTests
    Skip running the test suite (not recommended before a publish).

.EXAMPLE
    ./pack.ps1
    Build, test, and pack locally (no publish).

.EXAMPLE
    ./pack.ps1 -Push
    Build, test, pack, and push using NUGET_API_KEY.

.EXAMPLE
    ./pack.ps1 -Push -ApiKey "oy2..."
    Build, test, pack, and push using an explicit key.
#>
[CmdletBinding()]
param(
    [switch]$Push,
    [string]$ApiKey,
    [string]$Configuration = 'Release',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root      = $PSScriptRoot
$solution  = Join-Path $root 'Readmd.slnx'
$cliProj   = Join-Path $root 'src/Readmd.Cli/Readmd.Cli.csproj'
$testProj  = Join-Path $root 'tests/Readmd.Tests/Readmd.Tests.csproj'
$outDir    = Join-Path $root 'artifacts/nupkg'
$source    = 'https://api.nuget.org/v3/index.json'

function Invoke-Step {
    param([string]$Name, [scriptblock]$Action)
    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Name (exit code $LASTEXITCODE)"
    }
}

# 1) Build
Invoke-Step "Restoring & building ($Configuration)" {
    dotnet build $solution --configuration $Configuration --nologo
}

# 2) Test
if ($SkipTests) {
    Write-Host "==> Skipping tests (-SkipTests)" -ForegroundColor Yellow
} else {
    Invoke-Step "Running tests" {
        dotnet test $testProj --configuration $Configuration --no-build --nologo
    }
}

# 3) Pack
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
Invoke-Step "Packing the readmd tool" {
    dotnet pack $cliProj --configuration $Configuration --no-build --output $outDir --nologo
}

$package = Get-ChildItem -Path $outDir -Filter '*.nupkg' |
    Where-Object { $_.Name -notlike '*.symbols.nupkg' } |
    Select-Object -First 1
if (-not $package) { throw "No .nupkg produced in $outDir" }
Write-Host "    Packed: $($package.FullName)" -ForegroundColor Green

# 4) Push (optional)
if ($Push) {
    if (-not $ApiKey) { $ApiKey = $env:NUGET_API_KEY }
    if (-not $ApiKey) {
        throw "Pushing requires an API key. Pass -ApiKey or set the NUGET_API_KEY environment variable."
    }
    Invoke-Step "Pushing $($package.Name) to nuget.org" {
        dotnet nuget push $package.FullName --api-key $ApiKey --source $source --skip-duplicate
    }
    Write-Host "Published $($package.Name) to nuget.org." -ForegroundColor Green
} else {
    Write-Host "Pack complete. Re-run with -Push to publish to nuget.org." -ForegroundColor Green
}
