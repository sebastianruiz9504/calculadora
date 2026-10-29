param([ValidateSet('Preview','Capture')][string]$Mode='Preview',[int]$Year=2026,[string]$RevisePeriod='')
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
$start.ArgumentList.Add($(if($Mode -eq 'Preview'){'--month-close-dry-run'}else{'--month-close-capture'}))
foreach($setting in $settings){$start.Environment[$setting.name]=$setting.value}
$start.Environment['MonthClose__Year']=[string]$Year
$start.Environment['Logging__LogLevel__Default']='Warning'
$start.Environment['Logging__LogLevel__System.Net.Http.HttpClient']='Warning'
if($RevisePeriod){$start.Environment['MonthClose__RevisePeriod']=$RevisePeriod}
$process=[System.Diagnostics.Process]::Start($start)
$process.WaitForExit()
if($process.ExitCode -ne 0){throw "La operación de cierre terminó con código $($process.ExitCode)."}
