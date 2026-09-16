# Reversible Gate C only. Never commits or performs a permanent release.
[CmdletBinding()]
param([string]$RecoveryDirectory,[string]$ProjectTemplate)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$base=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2026'
$deployment=Join-Path $base 'RevitMCP'
$manifest=Join-Path $base 'RevitMCP.addin'
$worker=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'RevitMCP\ezdxf_worker.py'
function Inventory($dir) {
    @(Get-ChildItem -LiteralPath $dir -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($dir,$_.FullName);SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
    })
}
function ManifestInfo {
    $entries=@(Get-ChildItem -LiteralPath $base -Filter '*.addin' -Recurse -File | ForEach-Object {
        [xml]$xml=Get-Content -LiteralPath $_.FullName -Raw
        @($xml.RevitAddIns.AddIn) | Where-Object { $_.FullClassName -eq 'RevitMCP.Application' -or $_.Assembly -match 'RevitMCP.dll$' }
    })
    [pscustomobject]@{Count=$entries.Count;Assembly=($entries.Assembly -join ',');FullClassName=($entries.FullClassName -join ',')}
}
function SaveReport {
    $report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $run 'reversible.json')
    '# Reversible Gate C' + "`n`n" + (($report.GetEnumerator() | ForEach-Object { '- '+$_.Key+': '+($_.Value | ConvertTo-Json -Compress -Depth 10) }) -join "`n") | Set-Content -LiteralPath (Join-Path $run 'reversible.md')
}
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'BLOCKED_BY_ACTIVE_REVIT: close Revit normally before retrying.'}
if($ProjectTemplate -and -not $RecoveryDirectory){
    $ProjectTemplate=(Resolve-Path -LiteralPath $ProjectTemplate).Path
    if([IO.Path]::GetExtension($ProjectTemplate) -ne '.rte'){throw 'Only an explicitly selected test .rte template is allowed'}
}
if($RecoveryDirectory){
    $run=(Resolve-Path -LiteralPath $RecoveryDirectory).Path
    $report=Get-Content (Join-Path $run 'reversible.json') -Raw | ConvertFrom-Json -AsHashtable
}else{
    $run=Join-Path $repo ('test-artifacts\reversible-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run | Out-Null
    $info=ManifestInfo
    if($info.Count -ne 1 -or $info.Assembly -ne 'RevitMCP\RevitMCP.dll' -or $info.FullClassName -ne 'RevitMCP.Application'){throw 'Canonical manifest preflight failed'}
    $report=[ordered]@{Snapshot='PENDING';OriginalSHA256=(Get-FileHash -LiteralPath (Join-Path $deployment 'RevitMCP.dll')).Hash;OriginalFiles=(Inventory $deployment);OriginalDirectories=@(Get-ChildItem -LiteralPath $deployment -Directory -Recurse | ForEach-Object {[IO.Path]::GetRelativePath($deployment,$_.FullName)});ManifestBefore=$info;ManifestSHA256=(Get-FileHash -LiteralPath $manifest).Hash;WorkerExisted=(Test-Path -LiteralPath $worker);BuildSHA256=(Get-FileHash (Join-Path $repo 'MCP\bin\Release.R26\RevitMCP.dll')).Hash;TemporaryHashMatch=$false;GateC='SKIPPED';FixtureVersion='coordination-2';GateC3='SKIPPED';RuntimeFailures=@();Rollback='NOT_NEEDED';Status='PREFLIGHT'}
    Copy-Item -LiteralPath $deployment -Destination (Join-Path $run 'snapshot') -Recurse
    Copy-Item -LiteralPath $manifest -Destination (Join-Path $run 'RevitMCP.addin')
    if($report.WorkerExisted){Copy-Item -LiteralPath $worker -Destination (Join-Path $run 'ezdxf_worker.py');$report.WorkerSHA256=(Get-FileHash -LiteralPath $worker).Hash}
    if((Inventory (Join-Path $run 'snapshot') | ConvertTo-Json -Compress) -ne ($report.OriginalFiles | ConvertTo-Json -Compress)){throw 'Snapshot hash verification failed'}
    if((Get-FileHash (Join-Path $run 'RevitMCP.addin')).Hash -ne $report.ManifestSHA256){throw 'Manifest snapshot mismatch'}
    $report.Snapshot='PASS';SaveReport
}
try {
    if(-not $RecoveryDirectory){
        if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Revit started before replacement'}
        $report.Rollback='PENDING';$report.Status='TEMPORARY_LOAD';SaveReport
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'install-addon.ps1') -Version 2026 -NonInteractive -KeepBackups -1 *> (Join-Path $run 'installer.log')
        if($LASTEXITCODE -ne 0){throw 'Temporary installer failed'}
        $report.TemporarySHA256=(Get-FileHash -LiteralPath (Join-Path $deployment 'RevitMCP.dll')).Hash
        $report.TemporaryHashMatch=$report.BuildSHA256 -eq $report.TemporarySHA256
        if(-not $report.TemporaryHashMatch){throw 'Temporary build hash mismatch'}
        SaveReport
        $templateArguments=@()
        if($ProjectTemplate){$templateArguments=@('-ProjectTemplate',$ProjectTemplate)}
        $launch=& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'run-revit-selftest.ps1') @templateArguments
        if($LASTEXITCODE -ne 0){throw ($launch -join "`n")}
        $line=$launch | Where-Object {$_ -match '^STARTED '} | Select-Object -Last 1
        $report.RuntimeDirectory=$line -replace '^STARTED \d+ ',''
        $report.ProcessId=[int](($line -split ' ')[1]);SaveReport
        Write-Output "RUNNING $run"
        $deadline=[DateTime]::UtcNow.AddMinutes(5)
        while((Get-Process -Id $report.ProcessId -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Seconds 2}
        $runtime=Join-Path $report.RuntimeDirectory 'runtime.json'
        if(Test-Path -LiteralPath $runtime){
            $result=Get-Content -LiteralPath $runtime -Raw | ConvertFrom-Json
            $report.GateC=$result.GateC;$report.Assertions=$result.Assertions;$report.Passed=$result.Passed;$report.Failed=$result.Failed
            if($result.BuildHash -ne $report.BuildSHA256){$report.GateC='FAIL';throw 'Runtime loaded hash mismatch'}
            $workflowPath=Join-Path $report.RuntimeDirectory 'workflow-runtime.json'
            if(Test-Path -LiteralPath $workflowPath){
                $workflow=Get-Content -LiteralPath $workflowPath -Raw | ConvertFrom-Json
                $report.GateC3=$workflow.GateC3;$report.WorkflowAssertions=$workflow.Assertions
                if($workflow.BuildHash -ne $report.BuildSHA256){$report.GateC3='FAIL';throw 'Workflow runtime loaded hash mismatch'}
            }else{throw 'Workflow runtime report missing'}
        }else{throw 'Runtime report missing; inspect journal / startup-error.txt'}
    }
}catch{$report.RuntimeFailures+= $_.Exception.Message}
finally{
    if(Get-Process Revit -ErrorAction SilentlyContinue){
        $report.Rollback='WAITING_FOR_NORMAL_REVIT_EXIT';$report.Status='ROLLBACK_PENDING';SaveReport
        Write-Output "WAITING_FOR_NORMAL_REVIT_EXIT $run"
    }else{
        try{
            # Resolve and validate the exact authorized target before any file removals.
            if((Resolve-Path -LiteralPath $deployment).Path -ne $deployment){throw 'Deployment resolved path mismatch'}
            $snapshot=Join-Path $run 'snapshot'
            if((Inventory $snapshot | ConvertTo-Json -Compress) -ne ($report.OriginalFiles | ConvertTo-Json -Compress)){throw 'Recovery snapshot integrity failure'}
            foreach($file in Get-ChildItem -LiteralPath $deployment -Recurse -File){
                $relative=[IO.Path]::GetRelativePath($deployment,$file.FullName)
                if($relative -notin $report.OriginalFiles.Path){Remove-Item -LiteralPath $file.FullName -Force}
            }
            foreach($file in $report.OriginalFiles){
                $target=Join-Path $deployment $file.Path
                New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
                Copy-Item -LiteralPath (Join-Path $snapshot $file.Path) -Destination $target -Force
            }
            foreach($dir in Get-ChildItem -LiteralPath $deployment -Directory -Recurse | Sort-Object {$_.FullName.Length} -Descending){
                if([IO.Path]::GetRelativePath($deployment,$dir.FullName) -notin $report.OriginalDirectories){Remove-Item -LiteralPath $dir.FullName -ErrorAction Stop}
            }
            Copy-Item -LiteralPath (Join-Path $run 'RevitMCP.addin') -Destination $manifest -Force
            if($report.WorkerExisted){Copy-Item -LiteralPath (Join-Path $run 'ezdxf_worker.py') -Destination $worker -Force}
            elseif(Test-Path -LiteralPath $worker){Remove-Item -LiteralPath $worker -Force}
            $report.RestoredSHA256=(Get-FileHash -LiteralPath (Join-Path $deployment 'RevitMCP.dll')).Hash
            $report.ManifestAfter=ManifestInfo
            $report.FileSetMatch=(Inventory $deployment | ConvertTo-Json -Compress) -eq ($report.OriginalFiles | ConvertTo-Json -Compress)
            $workerMatch=if($report.WorkerExisted){(Get-FileHash -LiteralPath $worker).Hash -eq $report.WorkerSHA256}else{-not(Test-Path -LiteralPath $worker)}
            if(-not $report.FileSetMatch -or $report.RestoredSHA256 -ne $report.OriginalSHA256 -or (Get-FileHash -LiteralPath $manifest).Hash -ne $report.ManifestSHA256 -or $report.ManifestAfter.Count -ne 1 -or $report.ManifestAfter.Assembly -ne 'RevitMCP\RevitMCP.dll' -or $report.ManifestAfter.FullClassName -ne 'RevitMCP.Application' -or -not $workerMatch){throw 'Rollback verification mismatch'}
            $report.Rollback='PASS';$report.Status=if($report.GateC -eq 'PASS'){'GATE_C_PASS_ROLLBACK_PASS'}else{'GATE_C_INCOMPLETE_ROLLBACK_PASS'}
        }catch{$report.Rollback='FAIL';$report.Status='ROLLBACK_FAILURE';$report.RuntimeFailures+=$_.Exception.Message}
        SaveReport;Write-Output "$($report.Status) $run"
    }
}
