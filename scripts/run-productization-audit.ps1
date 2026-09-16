# Development-only semantic inventory. Does not rebuild/deploy the tested add-in.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
Set-Location $repo
New-Item -ItemType Directory -Path test-artifacts/source-audit -Force | Out-Null
dotnet msbuild MCP/RevitMCP.csproj -p:Configuration=Release.R26 -t:ResolveReferences -getItem:ReferencePath > test-artifacts/source-audit/references.json
if($LASTEXITCODE -ne 0){throw 'Cannot resolve R26 references'}
dotnet run --project tests/SourceAudit -- $repo
if($LASTEXITCODE -ne 0){throw 'Source analysis failed'}
node scripts/audit-productization.cjs
if($LASTEXITCODE -ne 0){throw 'Inventory failed'}
node scripts/complete-productization-audit.cjs
if($LASTEXITCODE -ne 0){throw 'Semantic inventory failed'}
node scripts/test-productization-audit.cjs
if($LASTEXITCODE -ne 0){throw 'Audit contract failed'}
