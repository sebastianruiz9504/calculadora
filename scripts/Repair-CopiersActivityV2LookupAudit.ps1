#requires -Version 7.4
[CmdletBinding()]
param([switch]$Apply,[switch]$SelfTest)
$ErrorActionPreference='Stop'
if ($Apply -and $SelfTest) { throw 'Self-test cannot apply metadata changes.' }
$arguments=@('-u',(Join-Path $PSScriptRoot 'repair_copiers_activity_v2_lookup_audit.py'))
if ($Apply) { $arguments+='--apply' }
if ($SelfTest) { $arguments+='--self-test' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Scoped lookup audit normalization failed; no automatic retry or broader fallback.' }
