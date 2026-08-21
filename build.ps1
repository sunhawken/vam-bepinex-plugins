param(
    [string]$VaMDir = $env:VAM_DIR,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($VaMDir)) {
    $VaMDir = 'T:\New folder'
}

dotnet build (Join-Path $repoRoot 'VaM.BepInEx.Plugins.sln') `
    --configuration $Configuration `
    --nologo `
    --verbosity quiet `
    "-p:VaMDir=$VaMDir"

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

Write-Host "Build verified. DLLs are under $repoRoot\artifacts\$Configuration" -ForegroundColor Green
