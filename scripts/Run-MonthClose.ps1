param([ValidateSet('Preview','Capture','ReviewReceivables','ApplyReceivables')][string]$Mode='Preview',[ValidateRange(2025,2100)][int]$Year=2026,[string]$RevisePeriod='',[string]$BankEvidencePath='',[string]$SourcePath='',[string]$RevisionPath='tmp/month-close-policy-2025')
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$dotnetPath='C:/Users/SebastianRuiz/AppData/Local/Microsoft/dotnet/dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnetPath)){$dotnetPath=(Get-Command dotnet).Source}
# Retrieve only the integration settings this command needs. Credentials are
# passed in the child environment, never persisted or printed.
$settingsText=& az webapp config appsettings list --subscription 7018b9b6-5dfc-4d91-bc4d-5f29f27553bd --resource-group DigitalTechAppAI --name calculadoradt --query "[?starts_with(name, 'Dataverse__') || starts_with(name, 'Siigo__') || name=='AzureAd__TenantId']" -o json
if($LASTEXITCODE -ne 0){throw 'No fue posible consultar la configuración de integraciones.'}
$settings=($settingsText -join [Environment]::NewLine)|ConvertFrom-Json
$start=[System.Diagnostics.ProcessStartInfo]::new($dotnetPath)
$start.UseShellExecute=$false
$start.WorkingDirectory=(Get-Location).Path
$start.ArgumentList.Add('bin/Release/net10.0/CotizadorInterno.Web.dll')
$commandMode=@{Preview='--month-close-dry-run';Capture='--month-close-capture';ReviewReceivables='--month-close-review-receivables';ApplyReceivables='--month-close-apply-receivables'}
$start.ArgumentList.Add($commandMode[$Mode])
foreach($setting in $settings){$start.Environment[$setting.name]=$setting.value}
$start.Environment['MonthClose__Year']=[string]$Year
$start.Environment['Logging__LogLevel__Default']='Warning'
$start.Environment['Logging__LogLevel__System.Net.Http.HttpClient']='Warning'
if($RevisePeriod){$start.Environment['MonthClose__RevisePeriod']=$RevisePeriod}
if($BankEvidencePath){$start.Environment['MonthClose__BankEvidencePath']=(Resolve-Path -LiteralPath $BankEvidencePath).Path}
if($SourcePath){$start.Environment['MonthClose__SourcePath']=(Resolve-Path -LiteralPath $SourcePath).Path}
$start.Environment['MonthClose__RevisionPath']=$RevisionPath
$process=[System.Diagnostics.Process]::Start($start)
$process.WaitForExit()
if($process.ExitCode -ne 0){throw "La operación de cierre terminó con código $($process.ExitCode)."}
