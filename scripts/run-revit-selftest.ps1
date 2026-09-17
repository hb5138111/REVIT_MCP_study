# Launch fixtures only after verifying the real registered assembly.
# Revit 2026 ignores APPDATA overrides for add-in discovery (tested locally).
# This launcher does not deploy: initial staging needs a separately approved host.
[CmdletBinding()]
param(
    [string]$RevitExe = 'C:\Program Files\Autodesk\Revit 2026\Revit.exe',
    [string]$ProjectTemplate = 'C:\ProgramData\Autodesk\RVT 2026\Templates\Default_M_ENU.rte',
    [string]$FamilyTemplate = 'C:\ProgramData\Autodesk\RVT 2026\Family Templates\English\Metric Generic Model.rft',
    [switch]$CadOnly,
    [switch]$EarthworkWorkflowOnly,
    [switch]$DrawingWorkflowOnly,
    [switch]$DrawingJourneyOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$runId = 'revit-selftest-' + [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $repo "test-artifacts\$runId"
New-Item -ItemType Directory -Path $runRoot | Out-Null
$build = Join-Path $repo 'MCP\bin\Release.R26'
$hash = (Get-FileHash -LiteralPath (Join-Path $build 'RevitMCP.dll') -Algorithm SHA256).Hash
$registeredRoot = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Autodesk\Revit\Addins\2026'
$registeredDll = Join-Path $registeredRoot 'RevitMCP\RevitMCP.dll'
if (-not (Test-Path -LiteralPath $registeredDll) -or (Get-FileHash -LiteralPath $registeredDll -Algorithm SHA256).Hash -ne $hash) {
    @{ TestRunId=$runId; Timestamp=[DateTimeOffset]::UtcNow; GateC='RUNTIME_TEST_BLOCKED'; BuildHash=$hash; Reason='Registered assembly differs; isolated APPDATA add-in discovery is unsupported. No deployment performed.' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'launch.json')
    Write-Output "RUNTIME_TEST_BLOCKED $runRoot"
    exit 2
}
if (Get-Process Revit -ErrorAction SilentlyContinue) {
    @{ TestRunId=$runId; GateC='BLOCKED_BY_ACTIVE_REVIT'; BuildHash=$hash } | ConvertTo-Json | Set-Content (Join-Path $runRoot 'launch.json')
    Write-Output "BLOCKED_BY_ACTIVE_REVIT $runRoot"
    exit 2
}
foreach ($inputPath in @($RevitExe,$ProjectTemplate,$FamilyTemplate)) { if (-not (Test-Path -LiteralPath $inputPath)) { throw "Missing fixture prerequisite: $inputPath" } }
# Verify the actual canonical manifest rather than relying on a redirected profile.
[xml]$registeredManifest = Get-Content -LiteralPath (Join-Path $registeredRoot 'RevitMCP.addin') -Raw
if ($registeredManifest.RevitAddIns.AddIn.FullClassName -ne 'RevitMCP.Application' -or
    $registeredManifest.RevitAddIns.AddIn.Assembly -ne 'RevitMCP\RevitMCP.dll') { throw 'Registered manifest does not match canonical application.' }
@{ ProjectTemplate=$ProjectTemplate; BaseProjectTemplate='C:\ProgramData\Autodesk\RVT 2026\Templates\Default_M_ENU.rte'; FamilyTemplate=$FamilyTemplate; ExpectedBuildHash=$hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'request.json')
$process = Start-Process -FilePath $RevitExe -ArgumentList '/nosplash','/language','ENU' -WindowStyle Hidden -PassThru -Environment @{
    REVIT_MCP_SELFTEST_DIR=$runRoot
    REVIT_MCP_SELFTEST_CAD_ONLY=([string][bool]$CadOnly)
    REVIT_MCP_SELFTEST_EARTHWORK_WORKFLOW=([string][bool]$EarthworkWorkflowOnly)
    REVIT_MCP_SELFTEST_DRAWING_JOURNEY=([string][bool]$DrawingJourneyOnly)
    REVIT_MCP_SELFTEST_DRAWING_WORKFLOW=([string][bool]$DrawingWorkflowOnly)
}
@{ TestRunId=$runId; Timestamp=[DateTimeOffset]::UtcNow; ProcessId=$process.Id; BuildHash=$hash; GateC='RUNNING'; OutputDirectory=$runRoot } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'launch.json')
Write-Output "STARTED $($process.Id) $runRoot"
