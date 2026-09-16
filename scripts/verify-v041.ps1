# v0.4.1 source, runtime and release gates. Runtime staging is reversible; final deployment is explicit.
[CmdletBinding()]
param([switch]$Runtime,[switch]$Deploy,[string]$RuntimeEvidence)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Set-Location $repo
$out=Join-Path $repo 'test-artifacts/v041'
New-Item -ItemType Directory -Path $out -Force|Out-Null
function Gate($name,[scriptblock]$action){ & $action *> (Join-Path $out "$name.log");if($LASTEXITCODE -ne 0){throw "$name failed; inspect test-artifacts/v041/$name.log"} }
Gate 'build' {dotnet build -c Release.R26 MCP/RevitMCP.csproj}
Gate 'audit' {& pwsh -NoProfile -File scripts/run-productization-audit.ps1}
Gate 'contracts' {node scripts/test-workflow-contracts.cjs $out}
Gate 'logic' {dotnet run --project tests/WorkflowLogic -- $out}
Gate 'workflow-state' {dotnet run --project tests/CoordinationWorkflow -- $out}
Gate 'qaqc' {& pwsh -NoProfile -File scripts/verify-qaqc.ps1 -Version 2026}
Copy-Item -LiteralPath (Join-Path $out 'qaqc.log') -Destination (Join-Path $out 'qaqc-final-source.log') -Force
if($Runtime){
    $lines=& pwsh -NoProfile -File scripts/test-reversible-gate-c.ps1
    $lines|Write-Output
    $line=$lines|Where-Object{$_ -match '^GATE_C_PASS_ROLLBACK_PASS '}|Select-Object -Last 1
    if(-not $line){throw 'Runtime or rollback incomplete; inspect reversible report. Do not deploy.'}
    $RuntimeEvidence=$line -replace '^GATE_C_PASS_ROLLBACK_PASS ',''
}
if(-not $RuntimeEvidence){throw 'Source checks complete. Gate C/C3 require -Runtime or explicit matching -RuntimeEvidence; no deployment performed.'}
node scripts/complete-productization-audit.cjs
if($LASTEXITCODE -ne 0){throw 'Matrix enrichment failed'}
node scripts/write-v041-report.cjs $RuntimeEvidence
if($LASTEXITCODE -ne 0){throw 'Report generation failed'}
$report=Get-Content docs/productization/v041-report.json -Raw|ConvertFrom-Json
if(@($report.Gates.PSObject.Properties|Where-Object{$_.Value.Status -ne 'PASS'}).Count){throw 'A required release gate is not PASS'}
if($Deploy){
    & pwsh -NoProfile -File scripts/publish-v041.ps1 -ReversibleDirectory $RuntimeEvidence
    if($LASTEXITCODE -ne 0){throw 'Formal deployment failed; inspect deployment receipt and rollback'}
    node scripts/write-v041-report.cjs $RuntimeEvidence test-artifacts/v041/deployment.json
    if($LASTEXITCODE -ne 0){throw 'Final report failed'}
}
