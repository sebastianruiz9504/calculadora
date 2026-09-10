#requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BeforeSolutionZip,[switch]$Apply)
$ErrorActionPreference='Stop'
# Remove only the four unexpected grants introduced in this release, never a
# baseline privilege. Microsoft exposes the singular bound RemovePrivilegeRole.
# https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/removeprivilegerole
$path=Join-Path $PSScriptRoot 'Provision-CopiersActivityV2Security.ps1'
$errors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$null,[ref]$errors)
if ($errors.Count) { throw 'Security helper syntax is invalid.' }
foreach ($definition in $ast.FindAll({param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst]},$false)) {
    Invoke-Expression $definition.Extent.Text
}
$environmentUrl='https://orgc79ca19c.crm2.dynamics.com'
$applicationUserId='6b07e603-7ca2-f111-aaad-70a8a5a95cf5'
$businessUnitId='ffcb9773-c5f3-ee11-a1fe-6045bd3b5b1d'
$solution='CopiersMtoFirmadoV2'
$roleId='815ea1ef-8aa2-f111-aaad-70a8a5a95cf5'
$unexpected=@{
    prvReadSharePointDocument='d71fc8d0-99bc-430e-abd7-d95c64f11e9c'
    prvReadSharePointData='fecbd29c-df64-4ede-a611-47226b402c22'
    prvWriteSharePointData='cfdd12cf-090b-4599-8302-771962d2350a'
    prvCreateSharePointData='5eb85025-363b-46ea-a77e-ce24159cd231'
}
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $BeforeSolutionZip).Path)
try {
    $entry=$zip.GetEntry('customizations.xml')
    if (-not $entry) { throw 'Missing baseline customization export.' }
    $reader=[IO.StreamReader]::new($entry.Open())
    try { [xml]$baseline=$reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $zip.Dispose() }
$baselineRoles=@($baseline.SelectNodes('//Role') | Where-Object { $_.GetAttribute('id').Trim('{}') -eq $roleId -and $_.GetAttribute('name') -eq 'Copiers MTO V2 App Runtime' })
if ($baselineRoles.Count -ne 1) { throw 'The baseline does not identify the approved runtime role.' }
$expected=@{}
foreach ($p in $baselineRoles[0].RolePrivileges.RolePrivilege) { $expected[$p.GetAttribute('name')]=$p.GetAttribute('level') }
if ($expected.Count -ne 16 -or @($unexpected.Keys | Where-Object { $expected.ContainsKey($_) }).Count) { throw 'Unexpected baseline; no pre-existing permission may be removed.' }
$policy=@(Get-ActivityPrivilegePolicy)
foreach ($p in $policy) {
    if ($expected.ContainsKey($p.Name) -and $expected[$p.Name] -ne $p.Depth) { throw 'Conflicting approved depth.' }
    $expected[$p.Name]=$p.Depth
}
if ($expected.Count -ne 40) { throw 'Expected exactly the baseline plus the 24 approved privileges.' }
$fieldPolicy=(& python (Join-Path $PSScriptRoot 'provision_copiers_activity_v2.py') --security-policy | Out-String) | ConvertFrom-Json -AsHashtable
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the approved local field policy.' }
$before=Get-ActivitySecuritySnapshot $policy $fieldPolicy
if ($before.RoleId -ne $roleId -or $before.MissingFields.Count) { throw 'Runtime identity or protected-field setup changed.' }
function Assert-ScopedPrivileges($Privileges,[bool]$AllowUnexpected) {
    foreach ($name in $expected.Keys) {
        if (@($Privileges | Where-Object { $_.PrivilegeName -eq $name -and $_.Depth -eq $expected[$name] }).Count -ne 1) { throw "Required privilege missing or depth changed: $name" }
    }
    foreach ($p in $Privileges) {
        if ($expected.ContainsKey($p.PrivilegeName)) { continue }
        if (-not $AllowUnexpected -or -not $unexpected.ContainsKey($p.PrivilegeName) -or $unexpected[$p.PrivilegeName] -ne $p.PrivilegeId -or $p.Depth -ne 'Global') { throw "Unrecognized extra privilege: $($p.PrivilegeName)" }
    }
}
Assert-ScopedPrivileges $before.Privileges $true
$remove=@($before.Privileges | Where-Object { $unexpected.ContainsKey($_.PrivilegeName) })
if ($Apply) {
    foreach ($p in $remove) {
        $current=@((Invoke-ActivityDv GET "RetrieveRolePrivilegesRole(RoleId=$roleId)").RolePrivileges)
        Assert-ScopedPrivileges $current $true
        if (@($current | Where-Object PrivilegeId -eq $p.PrivilegeId).Count -eq 1) {
            [void](Invoke-ActivityDv POST "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{Privilege=@{'@odata.type'='Microsoft.Dynamics.CRM.privilege';privilegeid=$p.PrivilegeId}})
        }
    }
    $after=Get-ActivitySecuritySnapshot $policy $fieldPolicy
    Assert-ScopedPrivileges $after.Privileges $false
    $retained=@($before.Privileges | Where-Object { $expected.ContainsKey($_.PrivilegeName) })
    Assert-ActivityBaselinePreserved @{RoleId=$before.RoleId;ProfileId=$before.ProfileId;Privileges=$retained;Permissions=$before.Permissions} $after
}
[ordered]@{Mode=if($Apply){'Applied'}else{'Plan'};RoleId=$roleId;Removed=@($remove.PrivilegeName);RetainedPrivilegeCount=40;BaselinePrivileges=16;ApprovedAddedPrivileges=24;Ready=($Apply -or -not $remove.Count);ProtectedFieldsPreserved=$true;BusinessRecordsModified=$false}|ConvertTo-Json
