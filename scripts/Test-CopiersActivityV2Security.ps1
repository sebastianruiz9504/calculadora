#requires -Version 7.4
# Pure local tests: load function definitions only, replace every Dataverse call.
$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot 'Provision-CopiersActivityV2Security.ps1'
$parseErrors=$null
$ast=[System.Management.Automation.Language.Parser]::ParseFile($path,[ref]$null,[ref]$parseErrors)
if ($parseErrors.Count) { throw ($parseErrors.Message -join '; ') }
foreach ($definition in $ast.FindAll({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst]},$false)) {
    Invoke-Expression $definition.Extent.Text
}
$applicationUserId='app-user'
$businessUnitId='business-unit'
$policy=@(Get-ActivityPrivilegePolicy)
$fieldPolicy=(& python (Join-Path $PSScriptRoot 'provision_copiers_activity_v2.py') --security-policy | Out-String) | ConvertFrom-Json -AsHashtable
if ($LASTEXITCODE -ne 0) { throw 'Local metadata template could not be read.' }
$script:scenario='valid'
$script:reads=0
function Invoke-ActivityDv([string]$Method,[string]$Path,$Body=$null) {
    if ($Method -ne 'GET') { throw 'A test attempted a mutation.' }
    $script:reads++
    if ($Path.StartsWith('systemusers(app-user)?')) { return @{applicationid='ebe37e5d-c246-4310-a7aa-a6e6686bc90e';isdisabled=$false;_businessunitid_value=$businessUnitId} }
    if ($Path -like 'systemusers(app-user)/systemuserroles_association*') {
        $items=@(@{roleid='runtime-role';name='Copiers MTO V2 App Runtime'})
        if ($script:scenario -eq 'extra-role') { $items+=@{roleid='admin-role';name='System Administrator'} }
        return @{value=$items}
    }
    if ($Path -like 'roles(runtime-role)/systemuserroles_association*' -or $Path -like 'fieldsecurityprofiles(profile)/systemuserprofiles_association*') { return @{value=@(@{systemuserid=$applicationUserId})} }
    if ($Path -like 'roles(runtime-role)/teamroles_association*') { return @{value=@()} }
    if ($Path -like 'RetrieveRolePrivilegesRole*') { return @{RolePrivileges=@(@{PrivilegeId='old-privilege';PrivilegeName='prvReadUser';Depth='Global';BusinessUnitId=$businessUnitId})} }
    if ($Path.StartsWith('fieldsecurityprofiles?')) { return @{value=@(@{fieldsecurityprofileid='profile';name='Copiers MTO V2 App Fields'})} }
    if ($Path -like 'systemusers(app-user)/systemuserprofiles_association*') { return @{value=@(@{fieldsecurityprofileid='profile'})} }
    if ($Path -like 'fieldsecurityprofiles(profile)/teamprofiles_association*') { return @{value=$(if($script:scenario -eq 'profile-team'){@(@{teamid='unapproved'})}else{@()})} }
    if ($Path -like 'fieldsecurityprofiles(profile)/applicationuserprofile*') { return @{value=$(if($script:scenario -eq 'profile-app'){@(@{applicationuserid='unapproved'})}else{@()})} }
    if ($Path -like 'fieldpermissions?*') { return @{value=@(@{fieldpermissionid='old-field';entityname='dtc_copiersmtov2';attributelogicalname='dtc_latitude';canread=4;cancreate=4;canupdate=4})} }
    if ($Path -like 'EntityDefinitions*') {
        $table=$Path.Split("'")[1]
        $items=@($fieldPolicy[$table] | ForEach-Object {@{LogicalName=$_;IsSecured=$true}})
        if ($script:scenario -eq 'extra-field') { $items+=@{LogicalName='dtc_unapproved';IsSecured=$true} }
        return @{value=$items}
    }
    throw "Unexpected test read: $Path"
}
$script:passed=0
function Check([string]$Name,[scriptblock]$Body) { & $Body; $script:passed++; Write-Output "PASS $Name" }
function MustFail([scriptblock]$Body,[string]$Message) {
    try { & $Body | Out-Null } catch { if ($_.Exception.Message -notmatch $Message) { throw }; return }
    throw "Expected rejection: $Message"
}
Check 'minimal new privilege policy excludes Delete/admin/Share and unnecessary AppendTo' {
    if ($policy.Count -ne 24 -or @($policy | Where-Object Name -match '^prv(Delete|Share)').Count) { throw 'Policy exceeds scope.' }
    if (@($policy | Where-Object Name -like 'prvAssign*').Count -ne 2) { throw 'Assign must be limited to technician-owned business records.' }
    if (@($policy | Where-Object Name -eq 'prvAppendTodtc_CopiersActivityEvidenceV2').Count) { throw 'Unnecessary evidence AppendTo.' }
}
Check 'valid isolated principal prepares exact new fields without mutation' {
    $snapshot=Get-ActivitySecuritySnapshot $policy $fieldPolicy
    if ($snapshot.MissingFields.Count -ne 27 -or $snapshot.Permissions.Count -ne 1) { throw 'Incorrect missing field plan.' }
}
foreach ($case in @('extra-role','profile-team','profile-app','extra-field')) {
    Check "reject $case before grants" {
        $script:scenario=$case
        MustFail { Get-ActivitySecuritySnapshot $policy $fieldPolicy } 'isolated|drift|no admin'
        $script:scenario='valid'
    }
}
Check 'existing target privilege wrong depth is not escalated or broadened' {
    MustFail { Assert-ActivityPriorPrivileges @(@{PrivilegeId='id';PrivilegeName=$policy[0].Name;Depth='Global'}) $policy } 'depth differs'
}
Check 'existing delete privilege fails closed instead of being silently removed' {
    MustFail { Assert-ActivityPriorPrivileges @(@{PrivilegeId='id';PrivilegeName='prvDeletecr07a_Equipo';Depth='Global'}) $policy } 'administrative/delete/share'
}
Check 'preservation covers previous privilege depths and field permission values' {
    $before=Get-ActivitySecuritySnapshot $policy $fieldPolicy
    $after=Get-ActivitySecuritySnapshot $policy $fieldPolicy
    Assert-ActivityBaselinePreserved $before $after
    $after.Privileges[0].Depth='Basic'
    MustFail { Assert-ActivityBaselinePreserved $before $after } 'privilege or depth changed'
    $after=Get-ActivitySecuritySnapshot $policy $fieldPolicy
    $after.Permissions[0].canread=0
    MustFail { Assert-ActivityBaselinePreserved $before $after } 'field permission changed'
}
Check 'all snapshot validation is before the grant block' {
    $source=Get-Content -LiteralPath $path -Raw
    $preflight=$source.LastIndexOf('$before=Get-ActivitySecuritySnapshot')
    $grant=$source.IndexOf('if ($Apply) {')
    if ($preflight -lt 0 -or $preflight -ge $grant -or $source -match 'RemovePrivilegesRole|ReplacePrivilegesRole|self-elevate') { throw 'Unsafe write ordering/fallback.' }
}
[ordered]@{Passed=$script:passed;Failed=0;DataverseMutations=0;MockReads=$script:reads}|ConvertTo-Json
