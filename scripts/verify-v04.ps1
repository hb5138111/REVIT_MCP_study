# End-to-end release gate; blocked runtime never deploys or reports success.
[CmdletBinding()]
param([switch]$Deploy)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$reportRoot = Join-Path $repo ('test-artifacts\verification-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $reportRoot | Out-Null
$report = [ordered]@{ TestRunId=[Guid]::NewGuid().ToString(); Timestamp=[DateTimeOffset]::UtcNow; RevitVersion='2026'; BuildHash=$null; FixtureVersion='coordination-1'; GateA='NOT_RUN'; GateB='NOT_RUN'; Build='NOT_RUN'; QAQC='NOT_RUN'; GateC='NOT_RUN'; GateD='N/A'; Deployment='NOT_RUN'; Status='SOURCE_FAILURE'; Evidence=@() }
function Invoke-Gate([string]$Name,[scriptblock]$Action) {
    & $Action *> (Join-Path $reportRoot "$Name.log")
    if ($LASTEXITCODE -ne 0) { throw "$Name exited $LASTEXITCODE" }
}
try {
    Invoke-Gate 'inventory' { & pwsh -NoProfile -File scripts/run-productization-audit.ps1 }
    Invoke-Gate 'contracts' { node scripts/test-workflow-contracts.cjs $reportRoot }; $report.GateA='PASS'
    Invoke-Gate 'logic' { dotnet run --project tests/WorkflowLogic -- $reportRoot }; $report.GateB='PASS'
    Invoke-Gate 'build' { dotnet build -c Release.R26 .\MCP\RevitMCP.csproj }; $report.Build='PASS'
    $report.BuildHash=(Get-FileHash 'MCP/bin/Release.R26/RevitMCP.dll' -Algorithm SHA256).Hash
    Invoke-Gate 'qaqc' { & pwsh -NoProfile -File scripts/verify-qaqc.ps1 -Version 2026 }; $report.QAQC='PASS'
    $launchOutput = & pwsh -NoProfile -File scripts/run-revit-selftest.ps1
    $launchExit = $LASTEXITCODE
    $report.Evidence += ($launchOutput -join "`n")
    if ($launchExit -ne 0) { $report.GateC='RUNTIME_TEST_BLOCKED'; $report.Status='RUNTIME_TEST_BLOCKED' }
    else {
        $runDirectory = ($launchOutput | Where-Object { $_ -match '^STARTED ' } | Select-Object -Last 1) -replace '^STARTED \d+ ',''
        $runtimePath = Join-Path $runDirectory 'runtime.json'
        $deadline = [DateTime]::UtcNow.AddMinutes(3)
        while (-not (Test-Path $runtimePath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Seconds 2 }
        if (-not (Test-Path $runtimePath)) { $report.GateC='RUNTIME_TEST_BLOCKED'; $report.Status='RUNTIME_TEST_BLOCKED' }
        else {
            $runtime = Get-Content $runtimePath -Raw | ConvertFrom-Json
            $report.Evidence += $runtimePath
            if ($runtime.BuildHash -ne $report.BuildHash -or $runtime.GateC -ne 'PASS' -or $runtime.Failed -ne 0) { throw 'Runtime hash/assertion gate failed' }
            $report.GateC='PASS'
            $ownedProcessId = [int](($launchOutput | Where-Object { $_ -match '^STARTED ' } | Select-Object -Last 1) -split ' ')[1]
            $ownedProcess = Get-Process -Id $ownedProcessId -ErrorAction SilentlyContinue
            if ($ownedProcess) {
                # Runner closes all documents before writing its report; request a normal exit.
                [void]$ownedProcess.CloseMainWindow()
                if (-not $ownedProcess.WaitForExit(10000)) {
                    $report.Status='RUNTIME_TEST_BLOCKED'
                    throw 'Completed fixture process did not exit normally; deployment held.'
                }
            }
            if ($Deploy) {
                Invoke-Gate 'deploy' { & pwsh -NoProfile -File scripts/install-addon.ps1 -Version 2026 -NonInteractive }
                $installed = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2026\RevitMCP\RevitMCP.dll'
                if ((Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash -ne $report.BuildHash) { throw 'Deployment hash mismatch' }
                $report.Deployment='PASS'; $report.Status='READY_FOR_OPTIONAL_UAT'
            }
            else { $report.Deployment='EXISTING_HASH_PASS'; $report.Status='READY_FOR_OPTIONAL_UAT'; $report.Evidence += 'Runtime executed the already-registered assembly with matching build hash.' }
        }
    }
}
catch { $report.Evidence += $_.Exception.Message }
finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $reportRoot 'report.json')
    "# v0.4 Verification`n`nStatus: $($report.Status)`n`n" + (($report.GetEnumerator() | ForEach-Object { "- $($_.Key): $($_.Value)" }) -join "`n") | Set-Content (Join-Path $reportRoot 'report.md')
    Write-Output "$($report.Status) $reportRoot"
}
if ($report.Status -ne 'READY_FOR_OPTIONAL_UAT') { exit 2 }
