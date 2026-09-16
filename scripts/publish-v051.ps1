# Formal release gate wrapper; deployment itself is performed only by the repository installer.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReversibleDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Set-Location $repo
$run=(Resolve-Path -LiteralPath $ReversibleDirectory).Path
$release=Join-Path $repo 'test-artifacts/v051'
$state=Get-Content docs/productization/v051-report.json -Raw|ConvertFrom-Json
$snapshot=Get-Content (Join-Path $run 'reversible.json') -Raw|ConvertFrom-Json
$base=Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk/Revit/Addins/2026'
$target=Join-Path $base 'RevitMCP'
$build=Join-Path $repo 'MCP/bin/Release.R26'
$buildHash=(Get-FileHash (Join-Path $build 'RevitMCP.dll')).Hash
if((git branch --show-current) -ne 'bim-custom'){throw 'Wrong branch'}
if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Close Revit normally before formal deployment'}
foreach($gate in $state.Gates.PSObject.Properties){if($gate.Value.Status -ne 'PASS'){throw "Gate $($gate.Name) is not PASS"}}
if(-not $state.SourceFingerprintVerified -or $state.BuildSHA256 -ne $buildHash -or $snapshot.BuildSHA256 -ne $buildHash -or $snapshot.GateC -ne 'PASS' -or $snapshot.GateC3 -ne 'PASS' -or $snapshot.Rollback -ne 'PASS'){throw 'Stale or incomplete release evidence'}
$terrain=Get-Content (Join-Path $snapshot.RuntimeDirectory 'terrain-runtime.json') -Raw|ConvertFrom-Json
if($terrain.Status -ne 'PASS' -or $terrain.Failed -ne 0 -or $terrain.Passed -lt 31 -or $terrain.BuildSHA256 -ne $buildHash){throw 'Terrain runtime proof does not match release build'}
$cad=Get-Content (Join-Path $snapshot.RuntimeDirectory 'cad-runtime.json') -Raw|ConvertFrom-Json
if($cad.Status -ne 'PASS' -or $cad.Failed -ne 0 -or $cad.Passed -lt 30 -or $cad.BuildSHA256 -ne $buildHash){throw 'CAD runtime proof does not match release build'}
$backend=Get-Content test-artifacts/source-audit/backend.json -Raw|ConvertFrom-Json
foreach($file in $backend.SourceFiles){if((Get-FileHash -LiteralPath $file.Path).Hash -ne $file.Sha256){throw "Source changed after test: $($file.Path)"}}
function Inventory($directory){@(Get-ChildItem -LiteralPath $directory -Recurse -File|Sort-Object FullName|ForEach-Object{[pscustomobject]@{Path=[IO.Path]::GetRelativePath($directory,$_.FullName);SHA256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}
if((Inventory $target|ConvertTo-Json -Compress) -ne ($snapshot.OriginalFiles|ConvertTo-Json -Compress)){throw 'Current stable deployment differs from verified rollback snapshot'}
if((Inventory (Join-Path $run 'snapshot')|ConvertTo-Json -Compress) -ne ($snapshot.OriginalFiles|ConvertTo-Json -Compress)){throw 'Rollback snapshot damaged'}
if((Get-FileHash (Join-Path $base 'RevitMCP.addin')).Hash -ne $snapshot.ManifestSHA256){throw 'Stable manifest differs from snapshot'}
$receipt=[ordered]@{Kind='FORMAL_V051_RELEASE';Status='PENDING';BuildSHA256=$buildHash;Timestamp=[DateTimeOffset]::UtcNow;Backup=[IO.Path]::GetRelativePath($repo,$run);Rollback='NOT_NEEDED'}
try {
    if(Get-Process Revit -ErrorAction SilentlyContinue){throw 'Revit started before installer'}
    & pwsh -NoProfile -File scripts/install-addon.ps1 -Version 2026 -NonInteractive -KeepBackups -1 *> (Join-Path $release 'formal-installer.log')
    if($LASTEXITCODE -ne 0){throw 'Formal installer failed'}
    $required=@(Get-ChildItem -LiteralPath $build -Filter '*.dll' -File)
    $checks=@($required|ForEach-Object{[pscustomobject]@{File=$_.Name;BuildSHA256=(Get-FileHash $_.FullName).Hash;DeployedSHA256=(Get-FileHash -LiteralPath (Join-Path $target $_.Name)).Hash}})
    if(@($checks|Where-Object{$_.BuildSHA256 -ne $_.DeployedSHA256}).Count){throw 'Required DLL hashes differ'}
    $roots=@($base,(Join-Path $env:ProgramData 'Autodesk/Revit/Addins/2026'))
    $entries=@(foreach($addinRoot in $roots){if(Test-Path $addinRoot){Get-ChildItem -LiteralPath $addinRoot -Filter '*.addin' -Recurse -File|ForEach-Object{
        [xml]$xml=Get-Content -LiteralPath $_.FullName -Raw
        @($xml.RevitAddIns.AddIn)|Where-Object{$_.FullClassName -eq 'RevitMCP.Application' -or $_.Assembly -match 'RevitMCP.dll$'}
    }}})
    if($entries.Count -ne 1 -or $entries[0].Assembly -ne 'RevitMCP\RevitMCP.dll' -or $entries[0].FullClassName -ne 'RevitMCP.Application'){throw 'Manifest integrity failed'}
    $receipt.DeployedSHA256=(Get-FileHash -LiteralPath (Join-Path $target 'RevitMCP.dll')).Hash
    if($receipt.DeployedSHA256 -ne $buildHash){throw 'Main DLL differs from tested build'}
    $receipt.HashMatch=$true;$receipt.RequiredDllSet='PASS';$receipt.RequiredDllCount=$required.Count;$receipt.Files=$checks
    $receipt.ManifestCount=$entries.Count;$receipt.Assembly=[string]$entries[0].Assembly;$receipt.FullClassName=[string]$entries[0].FullClassName
    $receipt.Status='PASS'
}
catch {
    $receipt.Status='FAIL';$receipt.Reason=$_.Exception.Message
    if(Get-Process Revit -ErrorAction SilentlyContinue){$receipt.Rollback='WAITING_FOR_NORMAL_REVIT_EXIT'}
    else {
        & pwsh -NoProfile -File scripts/test-reversible-gate-c.ps1 -RecoveryDirectory $run
        $recovery=Get-Content (Join-Path $run 'reversible.json') -Raw|ConvertFrom-Json
        $receipt.Rollback=$recovery.Rollback
    }
}
finally {$receipt|ConvertTo-Json -Depth 8|Set-Content (Join-Path $release 'deployment.json')}
$receipt|ConvertTo-Json -Depth 8
if($receipt.Status -ne 'PASS'){exit 2}
