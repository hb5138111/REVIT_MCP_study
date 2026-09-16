# Compatibility entry: the current Native release must also pass C2/C3.
[CmdletBinding()]
param([switch]$Deploy,[switch]$Runtime,[string]$RuntimeEvidence)
& (Join-Path $PSScriptRoot 'verify-v041.ps1') -Deploy:$Deploy -Runtime:$Runtime -RuntimeEvidence $RuntimeEvidence
