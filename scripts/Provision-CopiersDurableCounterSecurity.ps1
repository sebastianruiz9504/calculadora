#requires -Version 7.4
[CmdletBinding()]
param([switch]$Apply, [switch]$ReconcileAutoAddedSharePointPrivileges)
$ErrorActionPreference='Stop'
$environmentUrl='https://orgc79ca19c.crm2.dynamics.com/'
$appUser='6b07e603-7ca2-f111-aaad-70a8a5a95cf5'
$expectedRole='815ea1ef-8aa2-f111-aaad-70a8a5a95cf5'
function Invoke-CounterSecurity([string]$Method,[string]$Path,$Body=$null) {
    if($Method -notin @('GET','POST')){throw 'Only reads/additive grants allowed.'}
    $arguments=@('api','request','--target','dataverse','--method',$Method,'--path',('/api/data/v9.2/'+$Path),
        '--environment',$environmentUrl,'--context','app=dataverse-skills/1.11.3;skill=dv-security;agent=codex')
    if($null -ne $Body){$arguments+=@('--body',($Body|ConvertTo-Json -Depth 12 -Compress),'--header','MSCRM.SolutionUniqueName:CopiersMtoFirmadoV2')}
    $raw=(& dataverse @arguments 2>&1|Out-String);$code=$LASTEXITCODE
    $start=$raw.IndexOf('{');$end=$raw.LastIndexOf('}')
    $result=if($start -ge 0){$raw.Substring($start,$end-$start+1)|ConvertFrom-Json -Depth 30}else{$null}
    if($code -ne 0 -or $result.error -or ($Method -eq 'GET' -and $null -eq $result)){throw "Security operation failed: $Method $Path"}
    if($result.'@odata.nextLink'){throw 'Paged security result requires review.'}
    return $result
}
$user=Invoke-CounterSecurity GET "systemusers($appUser)?%24select=applicationid,isdisabled,_businessunitid_value"
if($user.applicationid -ne 'ebe37e5d-c246-4310-a7aa-a6e6686bc90e' -or $user.isdisabled){throw 'Unexpected runtime identity.'}
$roles=@((Invoke-CounterSecurity GET "systemusers($appUser)/systemuserroles_association?%24select=roleid,name").value)
if($roles.Count -ne 1 -or $roles[0].roleid -ne $expectedRole -or $roles[0].name -ne 'Copiers MTO V2 App Runtime'){throw 'Runtime role changed.'}
$members=@((Invoke-CounterSecurity GET "roles($expectedRole)/systemuserroles_association?%24select=systemuserid").value)
$teams=@((Invoke-CounterSecurity GET "roles($expectedRole)/teamroles_association?%24select=teamid").value)
if($members.Count -ne 1 -or $members[0].systemuserid -ne $appUser -or $teams.Count){throw 'Role is not isolated to the approved application.'}
$before=@((Invoke-CounterSecurity GET "RetrieveRolePrivilegesRole(RoleId=$expectedRole)").RolePrivileges)
$autoAdded=@{prvReadSharePointDocument='d71fc8d0-99bc-430e-abd7-d95c64f11e9c';prvReadSharePointData='fecbd29c-df64-4ede-a611-47226b402c22';prvWriteSharePointData='cfdd12cf-090b-4599-8302-771962d2350a';prvCreateSharePointData='5eb85025-363b-46ea-a77e-ce24159cd231'}
if($ReconcileAutoAddedSharePointPrivileges){
    # Recovery of this deployment's verified 40 -> 47 automatic expansion only.
    # Never reconcile an unknown baseline or another application's role.
    if(!$Apply -or $before.Count -ne 47){throw 'Expected the observed 47-privilege expansion.'}
    foreach($p in $before|Where-Object {$autoAdded.ContainsKey($_.PrivilegeName)}){
        if($autoAdded[$p.PrivilegeName] -ne $p.PrivilegeId -or $p.Depth -ne 'Global'){throw 'Unexpected automatic privilege identity.'}
    }
    $before=@($before|Where-Object {-not $autoAdded.ContainsKey($_.PrivilegeName)})
}
$policy=@{prvReadcr07a_Contadores='Global';prvCreatecr07a_Contadores='Basic';prvAppendcr07a_Contadores='Basic'}
$filter=[Uri]::EscapeDataString(($policy.Keys|ForEach-Object{"name eq '$_'"}) -join ' or ')
$catalog=@((Invoke-CounterSecurity GET "privileges?%24select=privilegeid,name&%24filter=$filter").value)
if($catalog.Count -ne 3){throw 'Counter privilege catalog mismatch.'}
$missing=@(foreach($p in $catalog){
    $old=@($before|Where-Object PrivilegeId -eq $p.privilegeid)
    if($old.Count -and $old[0].Depth -ne $policy[$p.name]){throw 'Existing privilege depth differs.'}
    if(!$old.Count){@{PrivilegeId=$p.privilegeid;PrivilegeName=$p.name;Depth=$policy[$p.name];BusinessUnitId=$user._businessunitid_value}}
})
if($Apply -and $missing.Count){[void](Invoke-CounterSecurity POST "roles($expectedRole)/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{Privileges=$missing})}
$after=@((Invoke-CounterSecurity GET "RetrieveRolePrivilegesRole(RoleId=$expectedRole)").RolePrivileges)
if($Apply){
    foreach($p in @($after|Where-Object {$_.PrivilegeId -notin $before.PrivilegeId -and $_.PrivilegeId -notin $missing.PrivilegeId})){
        if(!$autoAdded.ContainsKey($p.PrivilegeName) -or $autoAdded[$p.PrivilegeName] -ne $p.PrivilegeId -or $p.Depth -ne 'Global'){throw 'Unknown added privilege; no removal performed.'}
        [void](Invoke-CounterSecurity POST "roles($expectedRole)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{Privilege=@{'@odata.type'='Microsoft.Dynamics.CRM.privilege';privilegeid=$p.PrivilegeId}})
    }
    $after=@((Invoke-CounterSecurity GET "RetrieveRolePrivilegesRole(RoleId=$expectedRole)").RolePrivileges)
}
foreach($p in $before){if(@($after|Where-Object {$_.PrivilegeId -eq $p.PrivilegeId -and $_.Depth -eq $p.Depth}).Count -ne 1){throw 'Existing privilege changed.'}}
if($Apply){
    if($after.Count -ne ($before.Count+$missing.Count)){throw 'Unexpected extra privilege.'}
    foreach($p in $catalog){if(@($after|Where-Object {$_.PrivilegeId -eq $p.privilegeid -and $_.Depth -eq $policy[$p.name]}).Count -ne 1){throw 'Read-back failed.'}}
}
[ordered]@{Applied=[bool]$Apply;Environment=$environmentUrl;RoleId=$expectedRole;Before=$before.Count;After=$after.Count;Added=if($Apply){$missing.Count}else{0};Required=$missing.Count;Preserved=$true;Policy=$policy}|ConvertTo-Json -Depth 5
