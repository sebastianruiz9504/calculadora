#requires -Version 7.4
[CmdletBinding()]
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$environmentUrl = 'https://orgc79ca19c.crm2.dynamics.com'
$applicationUserId = '6b07e603-7ca2-f111-aaad-70a8a5a95cf5'
$businessUnitId = 'ffcb9773-c5f3-ee11-a1fe-6045bd3b5b1d'
$solution = 'CopiersMtoFirmadoV2'

function Invoke-ActivityDv([string]$Method, [string]$Path, $Body = $null) {
    if ($Method -notin @('GET','POST')) { throw 'Only reads and additive privilege/field grants are permitted.' }
    $arguments = @('api','request','--target','dataverse','--method',$Method,'--path',('/api/data/v9.2/'+$Path),
        '--environment',$environmentUrl,'--context','app=dataverse-skills/1.11.3;skill=dv-security;agent=codex')
    if ($null -ne $Body) { $arguments += @('--body',($Body | ConvertTo-Json -Depth 30 -Compress),'--header',('MSCRM.SolutionUniqueName:'+$solution)) }
    $raw = (& dataverse @arguments 2>&1 | Out-String)
    $exitCode=$LASTEXITCODE
    $raw = [regex]::Replace($raw,"`e\[[0-?]*[ -/]*[@-~]",'')
    $start=$raw.IndexOf('{'); $end=$raw.LastIndexOf('}')
    $value=if ($start -ge 0 -and $end -ge $start) { $raw.Substring($start,$end-$start+1) | ConvertFrom-Json -Depth 40 } else { $null }
    if ($exitCode -ne 0 -or $value.error) { throw "Managed Dataverse operation failed: $Method $Path $raw" }
    if ($Method -eq 'GET' -and $null -eq $value) { throw "Missing read response: $Path" }
    if ($value.'@odata.nextLink' -or $value.nextLink) { throw "Paged security response requires reconciliation: $Path" }
    return $value
}

function Get-ActivityPrivilegePolicy {
    $policy=@()
    foreach ($schema in @('dtc_CopiersActivityV2','dtc_CopiersActivityEvidenceV2')) {
        foreach ($operation in @('Create','Read','Write','Append')) { $policy += @{Name="prv$operation$schema";Depth='Basic'} }
    }
    $policy += @{Name='prvAppendTodtc_CopiersActivityV2';Depth='Basic'}
    # Business rows are owned by the technician, not the runtime app. Global
    # Read/Write/Assign permit read-back, file upload and the explicit owner.
    foreach ($schema in @('cr07a_movimientosequipos','cr07a_Entrega')) {
        foreach ($operation in @('Create','Read','Write','Append','Assign')) { $policy += @{Name="prv$operation$schema";Depth='Global'} }
    }
    foreach ($operation in @('Read','Write','AppendTo')) { $policy += @{Name="prv${operation}cr07a_Suministro";Depth='Global'} }
    foreach ($operation in @('Write','Append')) { $policy += @{Name="prv${operation}cr07a_Equipo";Depth='Global'} }
    return $policy
}

function Get-ActivityFieldPolicy {
    # Generated from the same frozen metadata template, without network I/O.
    $raw = & python (Join-Path $PSScriptRoot 'provision_copiers_activity_v2.py') --security-policy
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the local approved field contract.' }
    $policy = ($raw -join "`n") | ConvertFrom-Json -AsHashtable
    if ((($policy.Keys | Sort-Object) -join ',') -cne 'dtc_copiersactivityevidencev2,dtc_copiersactivityv2') { throw 'Unexpected local field contract.' }
    return $policy
}

function Assert-ActivityPriorPrivileges($Privileges, $Policy) {
    $ids=@{}
    foreach ($p in $Privileges) {
        if (-not $p.PrivilegeId -or -not $p.PrivilegeName -or $ids.ContainsKey([string]$p.PrivilegeId)) { throw 'Incomplete or duplicate role privilege snapshot.' }
        $ids[[string]$p.PrivilegeId]=$true
        $expected=@($Policy | Where-Object Name -eq $p.PrivilegeName)
        if ($expected.Count -and $p.Depth -ne $expected[0].Depth) { throw "Existing privilege depth differs; no escalation or replacement allowed: $($p.PrivilegeName)" }
        if ($p.PrivilegeName -match '^prv(Delete|Share)' -or $p.PrivilegeName -match '^prv(ActOnBehalfOfAnotherUser|PublishCustomization|ImportCustomization|ExportCustomization|CreateRole|WriteRole|CreatePrivilege|WritePrivilege)$') {
            throw "Unexpected administrative/delete/share privilege: $($p.PrivilegeName)"
        }
        if ($p.PrivilegeName -match '^prvAssign' -and -not $expected.Count) { throw "Unapproved assignment privilege: $($p.PrivilegeName)" }
    }
}

function Get-ActivitySecuritySnapshot($Policy,$FieldPolicy) {
    $user=Invoke-ActivityDv GET "systemusers($applicationUserId)?%24select=systemuserid,applicationid,isdisabled,_businessunitid_value"
    if ($user.applicationid -ne 'ebe37e5d-c246-4310-a7aa-a6e6686bc90e' -or $user.isdisabled -or $user._businessunitid_value -ne $businessUnitId) { throw 'Unexpected runtime application user.' }
    $roles=@((Invoke-ActivityDv GET "systemusers($applicationUserId)/systemuserroles_association?%24select=roleid,name").value)
    if ($roles.Count -ne 1 -or $roles[0].name -ne 'Copiers MTO V2 App Runtime') { throw 'Expected only the existing assigned isolated runtime role; no admin/extra role is permitted.' }
    $roleId=$roles[0].roleid
    $members=@((Invoke-ActivityDv GET "roles($roleId)/systemuserroles_association?%24select=systemuserid").value)
    $teams=@((Invoke-ActivityDv GET "roles($roleId)/teamroles_association?%24select=teamid").value)
    if ($members.Count -ne 1 -or $members[0].systemuserid -ne $applicationUserId -or $teams.Count -ne 0) { throw 'Runtime role has unexpected members.' }
    $privileges=@((Invoke-ActivityDv GET "RetrieveRolePrivilegesRole(RoleId=$roleId)").RolePrivileges)
    Assert-ActivityPriorPrivileges $privileges $Policy
    $profiles=@((Invoke-ActivityDv GET "fieldsecurityprofiles?%24select=fieldsecurityprofileid,name&%24filter=name eq 'Copiers MTO V2 App Fields'").value)
    if ($profiles.Count -ne 1) { throw 'Expected existing isolated field profile.' }
    $profileId=$profiles[0].fieldsecurityprofileid
    $assigned=@((Invoke-ActivityDv GET "systemusers($applicationUserId)/systemuserprofiles_association?%24select=fieldsecurityprofileid").value)
    $profileUsers=@((Invoke-ActivityDv GET "fieldsecurityprofiles($profileId)/systemuserprofiles_association?%24select=systemuserid").value)
    $profileTeams=@((Invoke-ActivityDv GET "fieldsecurityprofiles($profileId)/teamprofiles_association?%24select=teamid").value)
    $profileApps=@((Invoke-ActivityDv GET "fieldsecurityprofiles($profileId)/applicationuserprofile?%24select=applicationuserid").value)
    if ($assigned.Count -ne 1 -or $assigned[0].fieldsecurityprofileid -ne $profileId -or $profileUsers.Count -ne 1 -or $profileUsers[0].systemuserid -ne $applicationUserId -or $profileTeams.Count -ne 0 -or $profileApps.Count -ne 0) { throw 'Field profile is not isolated to the approved application user.' }
    $permissions=@((Invoke-ActivityDv GET "fieldpermissions?%24select=fieldpermissionid,entityname,attributelogicalname,canread,cancreate,canupdate&%24filter=_fieldsecurityprofileid_value eq $profileId").value)
    $permissionMap=@{}
    foreach ($p in $permissions) {
        $key="$($p.entityname).$($p.attributelogicalname)"
        if ($permissionMap.ContainsKey($key)) { throw "Duplicate field permission: $key" }
        $permissionMap[$key]=$p
    }
    $missing=@()
    foreach ($table in $FieldPolicy.Keys) {
        $fields=@((Invoke-ActivityDv GET "EntityDefinitions(LogicalName='$table')/Attributes?%24select=LogicalName,IsSecured&%24filter=IsSecured eq true").value)
        $expected=@($FieldPolicy[$table] | Sort-Object)
        $actual=@($fields.LogicalName | Sort-Object)
        if (@(Compare-Object $expected $actual -CaseSensitive).Count) { throw "Secured field metadata drift: $table" }
        foreach ($name in $expected) {
            $key="$table.$name"
            if ($permissionMap.ContainsKey($key)) {
                $p=$permissionMap[$key]
                if ($p.canread -ne 4 -or $p.cancreate -ne 4 -or $p.canupdate -ne 4) { throw "Unexpected existing field permission: $key" }
            } else { $missing += @{entityname=$table;attributelogicalname=$name;canread=4;cancreate=4;canupdate=4;'fieldsecurityprofileid@odata.bind'="/fieldsecurityprofiles($profileId)"} }
        }
    }
    foreach ($p in $permissions | Where-Object { $FieldPolicy.ContainsKey($_.entityname) }) {
        if ($p.attributelogicalname -cnotin $FieldPolicy[$p.entityname]) { throw 'Unexpected existing activity field grant.' }
    }
    return @{RoleId=$roleId;ProfileId=$profileId;Privileges=$privileges;Permissions=$permissions;MissingFields=$missing}
}

function Assert-ActivityBaselinePreserved($Before,$After) {
    if ($Before.RoleId -ne $After.RoleId -or $Before.ProfileId -ne $After.ProfileId) { throw 'Runtime security identity changed.' }
    foreach ($p in $Before.Privileges) {
        $same=@($After.Privileges | Where-Object {$_.PrivilegeId -eq $p.PrivilegeId -and $_.Depth -eq $p.Depth -and $_.BusinessUnitId -eq $p.BusinessUnitId})
        if ($same.Count -ne 1) { throw "An existing privilege or depth changed: $($p.PrivilegeName)" }
    }
    foreach ($p in $Before.Permissions) {
        $same=@($After.Permissions | Where-Object {$_.fieldpermissionid -eq $p.fieldpermissionid -and $_.entityname -eq $p.entityname -and $_.attributelogicalname -eq $p.attributelogicalname -and $_.canread -eq $p.canread -and $_.cancreate -eq $p.cancreate -and $_.canupdate -eq $p.canupdate})
        if ($same.Count -ne 1) { throw 'An existing field permission changed.' }
    }
}

$policy=@(Get-ActivityPrivilegePolicy)
$fieldPolicy=Get-ActivityFieldPolicy
$before=Get-ActivitySecuritySnapshot $policy $fieldPolicy
$filter=[Uri]::EscapeDataString(($policy | ForEach-Object { "name eq '$($_.Name)'" }) -join ' or ')
$catalog=@((Invoke-ActivityDv GET "privileges?%24select=privilegeid,name&%24filter=$filter").value)
if ($catalog.Count -ne $policy.Count) { throw 'Expected privilege metadata is missing or ambiguous.' }
foreach ($p in $policy) {
    if (@($catalog | Where-Object name -eq $p.Name).Count -ne 1) { throw "Expected privilege is missing or ambiguous: $($p.Name)" }
}
$required=@(foreach ($p in $policy) {
    $privilege=$catalog | Where-Object name -eq $p.Name
    if ($privilege.privilegeid -notin $before.Privileges.PrivilegeId) { @{PrivilegeId=$privilege.privilegeid;PrivilegeName=$privilege.name;Depth=$p.Depth;BusinessUnitId=$businessUnitId} }
})
# All identity, membership, depth and field-contract checks above precede writes.
if ($Apply) {
    if ($required.Count) { [void](Invoke-ActivityDv POST "roles($($before.RoleId))/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{Privileges=$required}) }
    foreach ($field in $before.MissingFields) { [void](Invoke-ActivityDv POST fieldpermissions $field) }
    $after=Get-ActivitySecuritySnapshot $policy $fieldPolicy
    Assert-ActivityBaselinePreserved $before $after
    foreach ($p in $policy) {
        $id=($catalog | Where-Object name -eq $p.Name).privilegeid
        if (@($after.Privileges | Where-Object {$_.PrivilegeId -eq $id -and $_.Depth -eq $p.Depth}).Count -ne 1) { throw "Privilege read-back failed: $($p.Name)" }
    }
    if ($after.MissingFields.Count) { throw 'Field permission read-back is incomplete.' }
}
[ordered]@{Mode=if($Apply){'Applied'}else{'Plan'};Environment=$environmentUrl;ApplicationUser=$applicationUserId;AddedPrivileges=$required.Count;MissingFieldPermissions=$before.MissingFields.Count;SecuredFields=@($fieldPolicy.Values | ForEach-Object {$_}).Count;NoDeletePrivileges=$true;NoAdminGrant=$true;ExistingPrivilegesPreserved=$true;Ready=($Apply -or ($required.Count -eq 0 -and $before.MissingFields.Count -eq 0))} | ConvertTo-Json
