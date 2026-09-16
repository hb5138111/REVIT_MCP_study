# Source gates; runtime uses the existing reversible fixture runner and full deployment snapshot.
[CmdletBinding()]
param([string]$RuntimeEvidence,[string]$ProjectTemplate,[switch]$Runtime,[switch]$Deploy)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$out='test-artifacts/v05'
New-Item -ItemType Directory -Path $out -Force|Out-Null
function Gate($name,[scriptblock]$action){& $action *> (Join-Path $out "$name.log");if($LASTEXITCODE){throw "$name failed"}}
Gate 'build' {dotnet build -c Release.R26 MCP/RevitMCP.csproj}
Gate 'audit' {pwsh -NoProfile -File scripts/run-productization-audit.ps1}
Gate 'contracts' {node scripts/test-workflow-contracts.cjs $out}
Gate 'logic' {dotnet run --project tests/WorkflowLogic -- $out}
Gate 'workflow-state' {dotnet run --project tests/CoordinationWorkflow -- $out}
Gate 'terrain' {dotnet run --project tests/SiteTerrain -- $out}
Gate 'qaqc' {pwsh -NoProfile -File scripts/verify-qaqc.ps1 -Version 2026}
Gate 'qaqc-source' {pwsh -NoProfile -File scripts/verify-qaqc.ps1 -SkipBuild -SkipDeploy}
if($Runtime){
    if(-not $ProjectTemplate){throw 'Explicit isolated .rte with existing 3D required for coordination regression'}
    $lines=pwsh -NoProfile -File scripts/test-reversible-gate-c.ps1 -ProjectTemplate $ProjectTemplate
    $lines|Write-Output
    $line=$lines|Where-Object{$_ -match '^GATE_C_PASS_ROLLBACK_PASS '}|Select-Object -Last 1
    if(-not $line){throw 'Runtime or rollback incomplete; inspect reversible report'}
    $RuntimeEvidence=$line -replace '^GATE_C_PASS_ROLLBACK_PASS ',''
}
node scripts/write-v05-report.cjs $RuntimeEvidence
if($LASTEXITCODE){throw 'Report failed'}
if($Deploy){
    if(-not $RuntimeEvidence){throw 'No runtime evidence'}
    pwsh -NoProfile -File scripts/publish-v05.ps1 -ReversibleDirectory $RuntimeEvidence
    if($LASTEXITCODE){throw 'Formal deployment failed'}
    node scripts/write-v05-report.cjs $RuntimeEvidence test-artifacts/v05/deployment.json
    if($LASTEXITCODE){throw 'Final report failed'}
}
