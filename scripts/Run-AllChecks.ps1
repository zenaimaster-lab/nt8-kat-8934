# Run-AllChecks.ps1 — one-shot verification: docs sync, xunit suite, net48 compile gate.
# Exit 0 only when all pass. Usage:  pwsh scripts/Run-AllChecks.ps1

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host '=== 0/3: docs sync ==='
pwsh (Join-Path $repoRoot 'scripts\Check-DocsSync.ps1')
$docsOk = ($LASTEXITCODE -eq 0)

Write-Host '=== 1/3: xunit suite ==='
dotnet test (Join-Path $repoRoot 'tests\Kat34Scalper.Tests') --nologo --verbosity quiet
$testsOk = ($LASTEXITCODE -eq 0)

Write-Host '=== 2/3: CompileCheck (net48 gate) ==='
dotnet build (Join-Path $repoRoot 'tools\CompileCheck') --nologo --verbosity quiet
$gateOk = ($LASTEXITCODE -eq 0)

if ($docsOk -and $testsOk -and $gateOk) {
    Write-Host 'ALL CHECKS GREEN.'
    exit 0
}

if (-not $docsOk)  { Write-Host 'FAILED: docs sync' }
if (-not $testsOk) { Write-Host 'FAILED: xunit suite' }
if (-not $gateOk)  { Write-Host 'FAILED: compile gate' }
exit 1
