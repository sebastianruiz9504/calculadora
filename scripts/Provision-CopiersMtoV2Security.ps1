[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$ConfirmApply = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$EnvironmentUrl = "https://orgc79ca19c.crm2.dynamics.com"
$EnvironmentName = "Digital Tech Copiers (default)"
$SolutionName = "CopiersMtoFirmadoV2"
$ApplicationUserId = "6b07e603-7ca2-f111-aaad-70a8a5a95cf5"
$ApplicationId = "ebe37e5d-c246-4310-a7aa-a6e6686bc90e"
$ApplicationObjectId = "7fad5476-7051-43a8-802d-d935717ba2f2"
$RootBusinessUnitId = "ffcb9773-c5f3-ee11-a1fe-6045bd3b5b1d"
$RoleName = "Copiers MTO V2 App Runtime"
$FieldSecurityProfileName = "Copiers MTO V2 App Fields"
$RequiredConfirmation = "APPLY-COPIERS-MTO-V2-SECURITY:$ApplicationUserId@$EnvironmentUrl"
$DataverseContext = "app=dataverse-skills/1.11.3;skill=dv-security;agent=codex"

# This is the complete approved role. Additions require a new review.
$PrivilegePolicy = @(
    [pscustomobject]@{ Name = "prvReadOrganization"; Depth = "Global" },
    [pscustomobject]@{ Name = "prvReadBusinessUnit"; Depth = "Global" },
    [pscustomobject]@{ Name = "prvReadUser"; Depth = "Global" },

    [pscustomobject]@{ Name = "prvCreatedtc_CopiersMtoV2"; Depth = "Basic" },
    [pscustomobject]@{ Name = "prvReaddtc_CopiersMtoV2"; Depth = "Basic" },
    [pscustomobject]@{ Name = "prvWritedtc_CopiersMtoV2"; Depth = "Basic" },
    [pscustomobject]@{ Name = "prvAppenddtc_CopiersMtoV2"; Depth = "Basic" },
    [pscustomobject]@{ Name = "prvAppendTodtc_CopiersMtoV2"; Depth = "Basic" },

    [pscustomobject]@{ Name = "prvCreatedtc_CopiersMtoEvidenciaV2"; Depth = "Basic" },
    [pscustomobject]@{ Name = "prvReaddtc_CopiersMtoEvidenciaV2"; Depth = "Basic" },
    [pscustomobject]@{ Name = "prvWritedtc_CopiersMtoEvidenciaV2"; Depth = "Basic" },
    [pscustomobject]@{ Name = "prvAppenddtc_CopiersMtoEvidenciaV2"; Depth = "Basic" },

    [pscustomobject]@{ Name = "prvReadcr07a_Cliente"; Depth = "Global" },
    [pscustomobject]@{ Name = "prvAppendTocr07a_Cliente"; Depth = "Global" },
    [pscustomobject]@{ Name = "prvReadcr07a_Equipo"; Depth = "Global" },
    [pscustomobject]@{ Name = "prvAppendTocr07a_Equipo"; Depth = "Global" }
)

# Dataverse adds these nine platform privileges to every newly-created role.
# They are not part of the approved runtime policy. The apply path may replace
# this exact, unassigned bootstrap set once; any other drift still fails closed.
$PlatformDefaultNewRolePrivileges = @(
    "prvCreateSharePointData",
    "prvReadPluginAssembly",
    "prvReadPluginType",
    "prvReadSdkMessage",
    "prvReadSdkMessageProcessingStep",
    "prvReadSdkMessageProcessingStepImage",
    "prvReadSharePointData",
    "prvReadSharePointDocument",
    "prvWriteSharePointData"
)

# Full create/read/update access is restricted to the dedicated application user
# and only to the V2 fields that are explicitly secured by the schema contract.
$SecuredFieldPolicy = [ordered]@{
    "dtc_copiersmtov2" = @(
        "dtc_technicianemailsnapshot",
        "dtc_clientemailsnapshot",
        "dtc_answersjson",
        "dtc_serviceaddressinternal",
        "dtc_internalnotes",
        "dtc_signername",
        "dtc_signerrole",
        "dtc_latitude",
        "dtc_longitude",
        "dtc_accuracymeters",
        "dtc_locationcapturedatutc",
        "dtc_locationsource",
        "dtc_signaturesha256",
        "dtc_signatureevidencekey",
        "dtc_finalizationfingerprint",
        "dtc_finalizationleasekey",
        "dtc_emailoutboxkey",
        "dtc_emailtosnapshot",
        "dtc_emailsubjectsnapshot",
        "dtc_emailhtmlbodysnapshot",
        "dtc_providerdraftid",
        "dtc_internetmessageid",
        "dtc_lasterrorsafemessage"
    )
    "dtc_copiersmtoevidenciav2" = @(
        "dtc_filecontent",
        "dtc_originalfilename",
        "dtc_sha256",
        "dtc_securityprovider"
    )
}

function Normalize-Url {
    param([Parameter(Mandatory)][string]$Value)
    return $Value.Trim().TrimEnd('/').ToLowerInvariant()
}

function Remove-Ansi {
    param([Parameter(Mandatory)][string]$Value)
    return [regex]::Replace($Value, "`e\[[0-?]*[ -/]*[@-~]", "")
}

function Invoke-DataverseApi {
    param(
        [Parameter(Mandatory)][ValidateSet("GET", "POST", "PATCH")][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body = $null,
        [switch]$SolutionAware
    )

    $requestPath = if ($Path.StartsWith('/')) {
        $Path
    }
    else {
        "/api/data/v9.2/$Path"
    }
    $cliArguments = @(
        "api", "request",
        "--target", "dataverse",
        "--method", $Method,
        "--path", $requestPath,
        "--environment", $EnvironmentUrl,
        "--context", $DataverseContext
    )
    if ($null -ne $Body) {
        $jsonBody = $Body | ConvertTo-Json -Depth 20 -Compress
        $cliArguments += @("--body", $jsonBody)
    }
    if ($SolutionAware) {
        $cliArguments += @("--header", "MSCRM.SolutionName:$SolutionName")
    }

    $rawOutput = (& dataverse @cliArguments 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    $cleanOutput = Remove-Ansi -Value $rawOutput
    if ($exitCode -ne 0) {
        throw "dataverse $Method $requestPath failed (exit $exitCode): $cleanOutput"
    }

    $jsonStart = $cleanOutput.IndexOf('{')
    $jsonEnd = $cleanOutput.LastIndexOf('}')
    if ($jsonStart -lt 0 -or $jsonEnd -lt $jsonStart) {
        return $null
    }
    $payload = $cleanOutput.Substring($jsonStart, $jsonEnd - $jsonStart + 1) |
        ConvertFrom-Json -Depth 30
    if ($null -ne $payload.PSObject.Properties["error"]) {
        throw "Dataverse returned an error: $($payload.error | ConvertTo-Json -Compress)"
    }
    return $payload
}

function Get-DataverseRows {
    param([Parameter(Mandatory)][string]$Path)
    $response = Invoke-DataverseApi -Method GET -Path $Path
    if ($null -eq $response -or $null -eq $response.PSObject.Properties["value"]) {
        throw "Expected a Dataverse row collection for $Path."
    }
    return @($response.value)
}

function Get-OneOrNone {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Rows,
        [Parameter(Mandatory)][string]$Description
    )
    if ($Rows.Count -gt 1) {
        throw "Multiple $Description records were found; refusing an ambiguous security change."
    }
    if ($Rows.Count -eq 0) {
        return $null
    }
    return $Rows[0]
}

function Assert-ToolingAndEnvironment {
    if ($null -eq (Get-Command dataverse -ErrorAction SilentlyContinue)) {
        throw "The first-party Dataverse CLI is not installed."
    }
    if ($null -eq (Get-Command pac -ErrorAction SilentlyContinue)) {
        throw "The Power Platform CLI is not installed."
    }

    $dataverseWho = Remove-Ansi -Value (& dataverse auth who 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or
        $dataverseWho -notmatch [regex]::Escape($EnvironmentUrl) -or
        $dataverseWho -notmatch [regex]::Escape($EnvironmentName)) {
        throw "The active Dataverse CLI profile is not the pinned environment $EnvironmentName ($EnvironmentUrl)."
    }

    $pacWho = Remove-Ansi -Value (& pac org who 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or
        $pacWho -notmatch [regex]::Escape($EnvironmentUrl) -or
        $pacWho -notmatch [regex]::Escape($EnvironmentName)) {
        throw "The active PAC profile is not the pinned environment $EnvironmentName ($EnvironmentUrl)."
    }
}

function Assert-StaticPolicy {
    if ($PrivilegePolicy.Count -ne 16) {
        throw "The approved role must contain exactly 16 privileges."
    }

    $uniqueNames = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $PrivilegePolicy) {
        if (-not $uniqueNames.Add([string]$item.Name)) {
            throw "Duplicate privilege in policy: $($item.Name)."
        }
        if ($item.Name -match '^prv(Delete|Assign|Share)') {
            throw "Forbidden privilege in policy: $($item.Name)."
        }
        if ($item.Depth -notin @("Basic", "Global")) {
            throw "Unsupported privilege depth for $($item.Name): $($item.Depth)."
        }
    }

    $platformDefaults = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $PlatformDefaultNewRolePrivileges) {
        if (-not $platformDefaults.Add([string]$name)) {
            throw "Duplicate platform-default privilege: $name."
        }
        if ($uniqueNames.Contains([string]$name)) {
            throw "Platform-default privilege must not be approved implicitly: $name."
        }
    }
    if ($platformDefaults.Count -ne 9) {
        throw "The pinned Dataverse new-role bootstrap set must contain exactly 9 privileges."
    }

    $fieldKeys = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $SecuredFieldPolicy.GetEnumerator()) {
        foreach ($fieldName in $entry.Value) {
            $key = "$($entry.Key).$fieldName"
            if (-not $fieldKeys.Add($key)) {
                throw "Duplicate secured field in policy: $key."
            }
        }
    }
    if ($fieldKeys.Count -ne 27) {
        throw "The approved field-security profile must contain exactly 27 V2 fields."
    }
}

function Assert-SolutionAndApplicationUser {
    $solutionFilter = [uri]::EscapeDataString("uniquename eq '$SolutionName'")
    $solution = Get-OneOrNone -Rows @(
        Get-DataverseRows -Path "solutions?%24select=solutionid,uniquename,ismanaged&%24filter=$solutionFilter"
    ) -Description "solution $SolutionName"
    if ($null -eq $solution -or [bool]$solution.ismanaged) {
        throw "The unmanaged solution $SolutionName is missing or incompatible."
    }

    $businessUnits = Get-DataverseRows -Path (
        "businessunits?%24select=businessunitid,name&%24filter=" +
        [uri]::EscapeDataString("_parentbusinessunitid_value eq null"))
    $rootBusinessUnit = Get-OneOrNone -Rows @($businessUnits) -Description "root business unit"
    if ($null -eq $rootBusinessUnit -or
        [string]$rootBusinessUnit.businessunitid -ne $RootBusinessUnitId) {
        throw "The root business unit does not match the pinned environment."
    }

    $applicationUser = Invoke-DataverseApi -Method GET -Path (
        "systemusers($ApplicationUserId)?%24select=" +
        "systemuserid,fullname,applicationid,azureactivedirectoryobjectid,accessmode,isdisabled,_businessunitid_value")
    if ($null -eq $applicationUser -or
        [string]$applicationUser.systemuserid -ne $ApplicationUserId -or
        [string]$applicationUser.applicationid -ne $ApplicationId -or
        [string]$applicationUser.azureactivedirectoryobjectid -ne $ApplicationObjectId -or
        [int]$applicationUser.accessmode -ne 4 -or
        [bool]$applicationUser.isdisabled -or
        [string]$applicationUser._businessunitid_value -ne $RootBusinessUnitId) {
        throw "Application user $ApplicationUserId does not match the pinned non-interactive identity."
    }

    # This identity was created through the classic systemuser application-user
    # path. It must not simultaneously exist in the newer applicationuser table,
    # otherwise field-profile association would be ambiguous.
    $applicationUserComponentFilter = [uri]::EscapeDataString(
        "applicationid eq $ApplicationId")
    $applicationUserComponents = @(Get-DataverseRows -Path (
        "applicationusers?%24select=applicationuserid,applicationid,_businessunitid_value" +
        "&%24filter=$applicationUserComponentFilter"))
    if ($applicationUserComponents.Count -ne 0) {
        throw (
            "Application identity $ApplicationId also exists in the applicationuser table; " +
            "refusing an ambiguous field-security-profile association.")
    }
    return $applicationUser
}

function Get-PrivilegeCatalog {
    $filter = ($PrivilegePolicy | ForEach-Object {
        "name eq '$($_.Name.Replace("'", "''"))'"
    }) -join " or "
    $rows = Get-DataverseRows -Path (
        "privileges?%24select=privilegeid,name&%24filter=" +
        [uri]::EscapeDataString($filter))
    $catalog = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $rows) {
        $catalog[[string]$row.name] = $row
    }
    $missing = @($PrivilegePolicy | Where-Object { -not $catalog.ContainsKey($_.Name) } |
        ForEach-Object Name)
    if ($missing.Count -gt 0) {
        throw "Dataverse did not expose approved privileges: $($missing -join ', ')."
    }
    if ($catalog.Count -ne $PrivilegePolicy.Count) {
        throw "The resolved privilege catalog is ambiguous."
    }
    return ,$catalog
}

function Get-RoleRecord {
    $filter = [uri]::EscapeDataString(
        "name eq '$($RoleName.Replace("'", "''"))' and _businessunitid_value eq $RootBusinessUnitId")
    return Get-OneOrNone -Rows @(
        Get-DataverseRows -Path "roles?%24select=roleid,name,_businessunitid_value&%24filter=$filter"
    ) -Description "security role $RoleName"
}

function Get-FieldSecurityProfileRecord {
    $filter = [uri]::EscapeDataString(
        "name eq '$($FieldSecurityProfileName.Replace("'", "''"))'")
    return Get-OneOrNone -Rows @(
        Get-DataverseRows -Path (
            "fieldsecurityprofiles?%24select=fieldsecurityprofileid,name,description&%24filter=$filter")
    ) -Description "field-security profile $FieldSecurityProfileName"
}

function Get-ExpectedFieldKeys {
    $keys = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $SecuredFieldPolicy.GetEnumerator()) {
        foreach ($fieldName in $entry.Value) {
            [void]$keys.Add("$($entry.Key).$fieldName")
        }
    }
    return ,$keys
}

function Assert-LiveSecuredFields {
    $expected = Get-ExpectedFieldKeys
    $actual = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $SecuredFieldPolicy.GetEnumerator()) {
        $rows = Get-DataverseRows -Path (
            "EntityDefinitions(LogicalName='$($entry.Key)')/Attributes" +
            "?%24select=LogicalName,IsSecured&%24filter=IsSecured%20eq%20true")
        foreach ($row in $rows) {
            [void]$actual.Add("$($entry.Key).$($row.LogicalName)")
        }
    }

    $missing = @($expected | Where-Object { -not $actual.Contains($_) } | Sort-Object)
    $unexpected = @($actual | Where-Object { -not $expected.Contains($_) } | Sort-Object)
    if ($missing.Count -gt 0 -or $unexpected.Count -gt 0) {
        throw (
            "Secured-field metadata drift. Missing=[$($missing -join ', ')]; " +
            "unexpected=[$($unexpected -join ', ')].")
    }
}

function Read-SecurityState {
    $role = Get-RoleRecord
    $profile = Get-FieldSecurityProfileRecord
    $expectedPrivileges = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $PrivilegePolicy) {
        $expectedPrivileges[$item.Name] = $item.Depth
    }

    $actualPrivileges = @()
    $roleUsers = @()
    $roleTeams = @()
    if ($null -ne $role) {
        $roleId = [string]$role.roleid
        $privilegeResponse = Invoke-DataverseApi -Method GET -Path (
            "RetrieveRolePrivilegesRole(RoleId=$roleId)")
        $actualPrivileges = @($privilegeResponse.RolePrivileges)
        $roleUsers = @(Get-DataverseRows -Path (
            "roles($roleId)/systemuserroles_association?%24select=systemuserid,fullname"))
        $roleTeams = @(Get-DataverseRows -Path (
            "roles($roleId)/teamroles_association?%24select=teamid,name"))
    }

    $actualByName = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($privilege in $actualPrivileges) {
        $actualByName[[string]$privilege.PrivilegeName] = $privilege
    }
    $missingPrivileges = @($PrivilegePolicy |
        Where-Object { -not $actualByName.ContainsKey($_.Name) } |
        ForEach-Object Name)
    $unexpectedPrivileges = @($actualPrivileges |
        Where-Object { -not $expectedPrivileges.ContainsKey([string]$_.PrivilegeName) } |
        ForEach-Object PrivilegeName | Sort-Object -Unique)
    $wrongDepthPrivileges = @($actualPrivileges | Where-Object {
        $expectedPrivileges.ContainsKey([string]$_.PrivilegeName) -and
        $expectedPrivileges[[string]$_.PrivilegeName] -ne [string]$_.Depth
    } | ForEach-Object { "$($_.PrivilegeName):$($_.Depth)" })
    $forbiddenPrivileges = @($actualPrivileges | Where-Object {
        [string]$_.PrivilegeName -match '^prv(Delete|Assign|Share)'
    } | ForEach-Object PrivilegeName | Sort-Object -Unique)

    $applicationUserRoles = @(Get-DataverseRows -Path (
        "systemusers($ApplicationUserId)/systemuserroles_association?%24select=roleid,name"))
    $otherApplicationUserRoles = @($applicationUserRoles | Where-Object {
        $null -eq $role -or [string]$_.roleid -ne [string]$role.roleid
    })
    $otherRoleUsers = @($roleUsers | Where-Object {
        [string]$_.systemuserid -ne $ApplicationUserId
    })

    $fieldPermissions = @()
    $profileUsers = @()
    $profileTeams = @()
    $profileApplicationUsers = @()
    if ($null -ne $profile) {
        $profileId = [string]$profile.fieldsecurityprofileid
        $fieldPermissions = @(Get-DataverseRows -Path (
            "fieldpermissions?%24select=fieldpermissionid,entityname,attributelogicalname," +
            "cancreate,canread,canupdate&%24filter=" +
            [uri]::EscapeDataString("_fieldsecurityprofileid_value eq $profileId")))
        $profileUsers = @(Get-DataverseRows -Path (
            "fieldsecurityprofiles($profileId)/systemuserprofiles_association" +
            "?%24select=systemuserid,fullname"))
        $profileTeams = @(Get-DataverseRows -Path (
            "fieldsecurityprofiles($profileId)/teamprofiles_association?%24select=teamid,name"))
        $profileApplicationUsers = @(Get-DataverseRows -Path (
            "fieldsecurityprofiles($profileId)/applicationuserprofile" +
            "?%24select=applicationuserid,applicationid"))
    }

    $expectedFields = Get-ExpectedFieldKeys
    $actualFieldMap = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($permission in $fieldPermissions) {
        $key = "$($permission.entityname).$($permission.attributelogicalname)"
        if ($actualFieldMap.ContainsKey($key)) {
            throw "Duplicate field permission in profile: $key."
        }
        $actualFieldMap[$key] = $permission
    }
    $missingFieldPermissions = @($expectedFields |
        Where-Object { -not $actualFieldMap.ContainsKey($_) } | Sort-Object)
    $unexpectedFieldPermissions = @($actualFieldMap.Keys |
        Where-Object { -not $expectedFields.Contains($_) } | Sort-Object)
    $wrongFieldPermissions = @($actualFieldMap.GetEnumerator() | Where-Object {
        [int]$_.Value.cancreate -ne 4 -or
        [int]$_.Value.canread -ne 4 -or
        [int]$_.Value.canupdate -ne 4
    } | ForEach-Object Key | Sort-Object)

    $applicationUserProfiles = @(Get-DataverseRows -Path (
        "systemusers($ApplicationUserId)/systemuserprofiles_association" +
        "?%24select=fieldsecurityprofileid,name"))
    $otherApplicationUserProfiles = @($applicationUserProfiles | Where-Object {
        $null -eq $profile -or
        [string]$_.fieldsecurityprofileid -ne [string]$profile.fieldsecurityprofileid
    })
    $otherProfileUsers = @($profileUsers | Where-Object {
        [string]$_.systemuserid -ne $ApplicationUserId
    })

    $unsafe = @()
    if ($unexpectedPrivileges.Count -gt 0) { $unsafe += "unexpected role privileges" }
    if ($wrongDepthPrivileges.Count -gt 0) { $unsafe += "wrong role privilege depths" }
    if ($forbiddenPrivileges.Count -gt 0) { $unsafe += "Delete/Assign/Share privilege detected" }
    if ($otherApplicationUserRoles.Count -gt 0) { $unsafe += "application user has another role" }
    if ($otherRoleUsers.Count -gt 0 -or $roleTeams.Count -gt 0) { $unsafe += "role assigned to another principal" }
    if ($unexpectedFieldPermissions.Count -gt 0) { $unsafe += "unexpected field permissions" }
    if ($wrongFieldPermissions.Count -gt 0) { $unsafe += "wrong field permission values" }
    if ($otherApplicationUserProfiles.Count -gt 0) { $unsafe += "application user has another field profile" }
    if ($otherProfileUsers.Count -gt 0 -or $profileTeams.Count -gt 0 -or
        $profileApplicationUsers.Count -gt 0) {
        $unsafe += "field profile assigned to another principal"
    }

    $roleAssigned = $null -ne $role -and @($applicationUserRoles | Where-Object {
        [string]$_.roleid -eq [string]$role.roleid
    }).Count -eq 1
    $profileAssigned = $null -ne $profile -and @($applicationUserProfiles | Where-Object {
        [string]$_.fieldsecurityprofileid -eq [string]$profile.fieldsecurityprofileid
    }).Count -eq 1

    $requiredChanges = @()
    if ($null -eq $role) { $requiredChanges += "create role $RoleName" }
    if ($missingPrivileges.Count -gt 0) {
        $requiredChanges += "add $($missingPrivileges.Count) approved role privileges"
    }
    if (-not $roleAssigned) { $requiredChanges += "assign role to application user" }
    if ($null -eq $profile) {
        $requiredChanges += "create field-security profile $FieldSecurityProfileName"
    }
    if ($missingFieldPermissions.Count -gt 0) {
        $requiredChanges += "create $($missingFieldPermissions.Count) approved field permissions"
    }
    if (-not $profileAssigned) {
        $requiredChanges += "assign field-security profile to application user"
    }

    $ready = $unsafe.Count -eq 0 -and $requiredChanges.Count -eq 0 -and
        $actualPrivileges.Count -eq $PrivilegePolicy.Count -and
        $fieldPermissions.Count -eq $expectedFields.Count -and
        $applicationUserRoles.Count -eq 1 -and
        $applicationUserProfiles.Count -eq 1 -and
        $roleUsers.Count -eq 1 -and $roleTeams.Count -eq 0 -and
        $profileUsers.Count -eq 1 -and $profileTeams.Count -eq 0 -and
        $profileApplicationUsers.Count -eq 0

    return [pscustomobject]@{
        Ready = $ready
        Unsafe = @($unsafe)
        RequiredChanges = @($requiredChanges)
        Role = $role
        Profile = $profile
        ActualPrivileges = @($actualPrivileges)
        MissingPrivileges = @($missingPrivileges)
        UnexpectedPrivileges = @($unexpectedPrivileges)
        WrongDepthPrivileges = @($wrongDepthPrivileges)
        ForbiddenPrivileges = @($forbiddenPrivileges)
        MissingFieldPermissions = @($missingFieldPermissions)
        UnexpectedFieldPermissions = @($unexpectedFieldPermissions)
        WrongFieldPermissions = @($wrongFieldPermissions)
        RoleAssigned = $roleAssigned
        ProfileAssigned = $profileAssigned
        ApplicationUserRoleCount = $applicationUserRoles.Count
        ApplicationUserProfileCount = $applicationUserProfiles.Count
        RolePrincipalCount = $roleUsers.Count + $roleTeams.Count
        ProfilePrincipalCount = $profileUsers.Count + $profileTeams.Count + $profileApplicationUsers.Count
    }
}

function Assert-StateSafeToApply {
    param([Parameter(Mandatory)][pscustomobject]$State)
    if ($State.Unsafe.Count -gt 0) {
        throw "Security drift requires manual review: $($State.Unsafe -join '; ')."
    }
}

function Test-ExactPlatformDefaultBootstrapState {
    param([Parameter(Mandatory)][pscustomobject]$State)

    if ($null -eq $State.Role -or $null -ne $State.Profile -or
        $State.ApplicationUserRoleCount -ne 0 -or
        $State.ApplicationUserProfileCount -ne 0 -or
        $State.RolePrincipalCount -ne 0 -or
        $State.ProfilePrincipalCount -ne 0 -or
        $State.MissingPrivileges.Count -ne $PrivilegePolicy.Count -or
        $State.WrongDepthPrivileges.Count -ne 0 -or
        $State.ForbiddenPrivileges.Count -ne 0 -or
        $State.UnexpectedFieldPermissions.Count -ne 0 -or
        $State.WrongFieldPermissions.Count -ne 0 -or
        $State.Unsafe.Count -ne 1 -or
        $State.Unsafe[0] -ne "unexpected role privileges") {
        return $false
    }

    $expected = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $PlatformDefaultNewRolePrivileges) {
        [void]$expected.Add([string]$name)
    }
    $actual = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $State.UnexpectedPrivileges) {
        [void]$actual.Add([string]$name)
    }

    return $actual.Count -eq $expected.Count -and $actual.IsSubsetOf($expected)
}

function Test-OnlyRemovablePlatformDefaultPrivilegeDrift {
    param([Parameter(Mandatory)][pscustomobject]$State)

    if ($null -eq $State.Role -or $null -ne $State.Profile -or
        $State.ApplicationUserRoleCount -ne 0 -or
        $State.ApplicationUserProfileCount -ne 0 -or
        $State.RolePrincipalCount -ne 0 -or
        $State.ProfilePrincipalCount -ne 0 -or
        $State.MissingPrivileges.Count -ne 0 -or
        $State.UnexpectedPrivileges.Count -eq 0 -or
        $State.WrongDepthPrivileges.Count -ne 0 -or
        $State.ForbiddenPrivileges.Count -ne 0 -or
        $State.UnexpectedFieldPermissions.Count -ne 0 -or
        $State.WrongFieldPermissions.Count -ne 0 -or
        $State.Unsafe.Count -ne 1 -or
        $State.Unsafe[0] -ne "unexpected role privileges") {
        return $false
    }

    $allowed = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $PlatformDefaultNewRolePrivileges) {
        [void]$allowed.Add([string]$name)
    }
    $actual = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $State.UnexpectedPrivileges) {
        [void]$actual.Add([string]$name)
    }

    return $actual.Count -eq $State.UnexpectedPrivileges.Count -and
        $actual.IsSubsetOf($allowed)
}

function Invoke-PacRoleAssignment {
    $pacArguments = @(
        "admin", "assign-user",
        "--environment", $EnvironmentUrl,
        "--user", $ApplicationId,
        "--role", $RoleName,
        "--application-user",
        "--business-unit", $RootBusinessUnitId
    )
    $rawOutput = (& pac @pacArguments 2>&1 | Out-String)
    $exitCode = $LASTEXITCODE
    $cleanOutput = Remove-Ansi -Value $rawOutput
    if ($exitCode -ne 0 -or
        $cleanOutput -match '(?im)^\s*Error\s*:' -or
        $cleanOutput -match '(?i)does not exist|not found|failed') {
        throw "PAC role assignment did not succeed: $cleanOutput"
    }
}

function Apply-SecurityPolicy {
    param(
        [Parameter(Mandatory)][pscustomobject]$InitialState,
        [Parameter(Mandatory)]$PrivilegeCatalog
    )
    if (-not (Test-ExactPlatformDefaultBootstrapState -State $InitialState) -and
        -not (Test-OnlyRemovablePlatformDefaultPrivilegeDrift -State $InitialState)) {
        Assert-StateSafeToApply -State $InitialState
    }

    $role = $InitialState.Role
    if ($null -eq $role) {
        Write-Host "Creating isolated role $RoleName..."
        [void](Invoke-DataverseApi -Method POST -Path "roles" -SolutionAware -Body @{
            name = $RoleName
            "businessunitid@odata.bind" = "/businessunits($RootBusinessUnitId)"
        })
        $role = Get-RoleRecord
        if ($null -eq $role) {
            throw "The new role was not visible in read-back."
        }
    }

    $roleState = Read-SecurityState
    if (Test-ExactPlatformDefaultBootstrapState -State $roleState) {
        $replacementPrivileges = @($PrivilegePolicy | ForEach-Object {
            [ordered]@{
                PrivilegeId = [string]$PrivilegeCatalog[$_.Name].privilegeid
                PrivilegeName = [string]$_.Name
                Depth = [string]$_.Depth
                BusinessUnitId = $RootBusinessUnitId
            }
        })
        Write-Host (
            "Replacing the exact unassigned Dataverse bootstrap privilege set " +
            "with the $($replacementPrivileges.Count)-privilege approved policy...")
        [void](Invoke-DataverseApi -Method POST -Path (
            "roles($($role.roleid))/Microsoft.Dynamics.CRM.ReplacePrivilegesRole") -Body @{
                Privileges = $replacementPrivileges
            })
    }
    elseif (-not (Test-OnlyRemovablePlatformDefaultPrivilegeDrift -State $roleState)) {
        Assert-StateSafeToApply -State $roleState
    }

    $roleState = Read-SecurityState
    if (Test-OnlyRemovablePlatformDefaultPrivilegeDrift -State $roleState) {
        $unexpectedSet = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($name in $roleState.UnexpectedPrivileges) {
            [void]$unexpectedSet.Add([string]$name)
        }
        $unexpectedPrivilegeRows = @($roleState.ActualPrivileges | Where-Object {
            $unexpectedSet.Contains([string]$_.PrivilegeName)
        })
        if ($unexpectedPrivilegeRows.Count -ne $unexpectedSet.Count) {
            throw "The removable platform-default privilege set did not resolve uniquely."
        }
        foreach ($privilege in $unexpectedPrivilegeRows) {
            Write-Host "Removing Dataverse bootstrap privilege $($privilege.PrivilegeName)..."
            [void](Invoke-DataverseApi -Method POST -Path (
                "roles($($role.roleid))/Microsoft.Dynamics.CRM.RemovePrivilegeRole") -Body @{
                    Privilege = @{
                        privilegeid = [string]$privilege.PrivilegeId
                    }
                })
        }
        $roleState = Read-SecurityState
    }
    Assert-StateSafeToApply -State $roleState
    if ($roleState.MissingPrivileges.Count -gt 0) {
        $privileges = @($PrivilegePolicy | Where-Object {
            $_.Name -in $roleState.MissingPrivileges
        } | ForEach-Object {
            [ordered]@{
                PrivilegeId = [string]$PrivilegeCatalog[$_.Name].privilegeid
                PrivilegeName = [string]$_.Name
                Depth = [string]$_.Depth
                BusinessUnitId = $RootBusinessUnitId
            }
        })
        Write-Host "Adding $($privileges.Count) approved privileges to $RoleName..."
        [void](Invoke-DataverseApi -Method POST -Path (
            "roles($($role.roleid))/Microsoft.Dynamics.CRM.AddPrivilegesRole") -Body @{
                Privileges = $privileges
            })
    }

    $roleState = Read-SecurityState
    Assert-StateSafeToApply -State $roleState
    if (-not $roleState.RoleAssigned) {
        Write-Host "Assigning $RoleName only to application user $ApplicationUserId..."
        Invoke-PacRoleAssignment
    }

    $profile = Get-FieldSecurityProfileRecord
    if ($null -eq $profile) {
        Write-Host "Creating isolated field-security profile $FieldSecurityProfileName..."
        [void](Invoke-DataverseApi -Method POST -Path "fieldsecurityprofiles" -SolutionAware -Body @{
            name = $FieldSecurityProfileName
            description = "Full create/read/update access only to the 27 secured Copiers MTO V2 fields for the dedicated application user."
        })
        $profile = Get-FieldSecurityProfileRecord
        if ($null -eq $profile) {
            throw "The new field-security profile was not visible in read-back."
        }
    }

    $profileState = Read-SecurityState
    Assert-StateSafeToApply -State $profileState
    $missingFieldSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($key in $profileState.MissingFieldPermissions) {
        [void]$missingFieldSet.Add($key)
    }
    foreach ($entry in $SecuredFieldPolicy.GetEnumerator()) {
        foreach ($fieldName in $entry.Value) {
            $key = "$($entry.Key).$fieldName"
            if (-not $missingFieldSet.Contains($key)) {
                continue
            }
            Write-Host "Creating field permission $key..."
            [void](Invoke-DataverseApi -Method POST -Path "fieldpermissions" -SolutionAware -Body @{
                attributelogicalname = $fieldName
                entityname = $entry.Key
                cancreate = 4
                canread = 4
                canupdate = 4
                "fieldsecurityprofileid@odata.bind" = "/fieldsecurityprofiles($($profile.fieldsecurityprofileid))"
            })
        }
    }

    $profileState = Read-SecurityState
    Assert-StateSafeToApply -State $profileState
    if (-not $profileState.ProfileAssigned) {
        Write-Host "Assigning the field-security profile only to application user $ApplicationUserId..."
        [void](Invoke-DataverseApi -Method POST -Path (
            "systemusers($ApplicationUserId)/systemuserprofiles_association/`$ref") -Body @{
                "@odata.id" = "$EnvironmentUrl/api/data/v9.2/fieldsecurityprofiles($($profile.fieldsecurityprofileid))"
            })
    }
}

function New-Result {
    param(
        [Parameter(Mandatory)][pscustomobject]$State,
        [Parameter(Mandatory)][string]$Mode
    )
    return [ordered]@{
        mode = $Mode
        environment = $EnvironmentUrl
        environmentName = $EnvironmentName
        solution = $SolutionName
        applicationUser = [ordered]@{
            systemUserId = $ApplicationUserId
            applicationId = $ApplicationId
            objectId = $ApplicationObjectId
            identityModel = "classic-systemuser"
            applicationUserEntityCount = 0
            fieldProfileAssociation = "systemuserprofiles_association"
            assignedRoleCount = $State.ApplicationUserRoleCount
            assignedFieldProfileCount = $State.ApplicationUserProfileCount
        }
        role = [ordered]@{
            name = $RoleName
            id = if ($null -eq $State.Role) { $null } else { $State.Role.roleid }
            expectedPrivilegeCount = $PrivilegePolicy.Count
            missingPrivileges = $State.MissingPrivileges
            unexpectedPrivileges = $State.UnexpectedPrivileges
            wrongDepthPrivileges = $State.WrongDepthPrivileges
            forbiddenDeleteAssignShare = $State.ForbiddenPrivileges
            assignedOnlyToTarget = $State.RoleAssigned -and $State.RolePrincipalCount -eq 1
        }
        fieldSecurityProfile = [ordered]@{
            name = $FieldSecurityProfileName
            id = if ($null -eq $State.Profile) { $null } else { $State.Profile.fieldsecurityprofileid }
            expectedFieldPermissionCount = 27
            missingFieldPermissions = $State.MissingFieldPermissions
            unexpectedFieldPermissions = $State.UnexpectedFieldPermissions
            wrongFieldPermissions = $State.WrongFieldPermissions
            assignedOnlyToTarget = $State.ProfileAssigned -and $State.ProfilePrincipalCount -eq 1
        }
        safety = [ordered]@{
            zeroDeleteAssignShare = $State.ForbiddenPrivileges.Count -eq 0
            zeroUnexpectedRolePrivileges = $State.UnexpectedPrivileges.Count -eq 0
            zeroUnexpectedFieldPermissions = $State.UnexpectedFieldPermissions.Count -eq 0
            unsafeFindings = $State.Unsafe
        }
        requiredChanges = $State.RequiredChanges
        ready = $State.Ready
    }
}

Assert-StaticPolicy
if ($Apply -and $ConfirmApply -cne $RequiredConfirmation) {
    throw (
        "Apply is blocked. Review the plan, then pass -Apply -ConfirmApply " +
        "'$RequiredConfirmation' exactly.")
}

Assert-ToolingAndEnvironment
[void](Assert-SolutionAndApplicationUser)
Assert-LiveSecuredFields
$privilegeCatalog = Get-PrivilegeCatalog
$state = Read-SecurityState

if (-not $Apply) {
    New-Result -State $state -Mode "plan" | ConvertTo-Json -Depth 20
    Write-Host "Plan only. No Dataverse security change was executed."
    Write-Host "Approved apply token: $RequiredConfirmation"
    if ($state.Unsafe.Count -gt 0) {
        exit 1
    }
    if (-not $state.Ready) {
        exit 2
    }
    exit 0
}

Apply-SecurityPolicy -InitialState $state -PrivilegeCatalog $privilegeCatalog
$finalState = Read-SecurityState
Assert-StateSafeToApply -State $finalState
$result = New-Result -State $finalState -Mode "apply"
$result | ConvertTo-Json -Depth 20
if (-not $finalState.Ready) {
    throw "Read-back did not prove the exact minimum security policy."
}
Write-Host "Applied and independently read back the exact Copiers MTO V2 security policy."
