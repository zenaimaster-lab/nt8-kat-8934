# Check-DocsSync.ps1 — verify VERSION parity across Kat34Scalper.cs / README.md / DIARY.md / AGENTS.md
# Usage: pwsh scripts/Check-DocsSync.ps1
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

function Get-VersionFromCs {
	$cs = Get-Content (Join-Path $repoRoot 'Kat34Scalper.cs') -Raw
	if ($cs -match 'VERSION\s*=\s*"(?<v>[0-9]+\.[0-9]+)"') { return $Matches['v'] }
	throw "VERSION not found in Kat34Scalper.cs"
}
function Get-VersionFromReadme {
	$md = Get-Content (Join-Path $repoRoot 'README.md') -Raw
	if ($md -match 'Current Version.*v(?<v>[0-9]+\.[0-9]+)') { return $Matches['v'] }
	throw "Version not found in README.md"
}
function Get-VersionFromDiary {
	$md = Get-Content (Join-Path $repoRoot 'DIARY.md') -Raw
	# latest ### [vX.XX] entry
	if ($md -match '### \[v(?<v>[0-9]+\.[0-9]+)\]') { return $Matches['v'] }
	throw "Version not found in DIARY.md"
}
function Get-VersionFromAgents {
	$md = Get-Content (Join-Path $repoRoot 'AGENTS.md') -Raw
	if ($md -match 'Current:\s*v(?<v>[0-9]+\.[0-9]+)') { return $Matches['v'] }
	throw "Version not found in AGENTS.md"
}

$cs = Get-VersionFromCs
$rm = Get-VersionFromReadme
$di = Get-VersionFromDiary
$ag = Get-VersionFromAgents

Write-Host "Kat34Scalper.cs: $cs"
Write-Host "README.md:       $rm"
Write-Host "DIARY.md:        $di"
Write-Host "AGENTS.md:       $ag"

$ok = ($cs -eq $rm) -and ($cs -eq $di) -and ($cs -eq $ag)
if ($ok) {
	Write-Host "DocsSync OK — all v$cs" -ForegroundColor Green
	exit 0
} else {
	Write-Host "DocsSync FAIL — versions diverge!" -ForegroundColor Red
	exit 1
}
