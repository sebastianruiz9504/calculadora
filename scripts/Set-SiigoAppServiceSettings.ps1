[CmdletBinding()]
param(
    [string]$SubscriptionId = "7018b9b6-5dfc-4d91-bc4d-5f29f27553bd",
    [string]$ResourceGroupName = "DigitalTechAppAI",
    [string]$WebAppName = "calculadoradt",
    [string]$UserSecretsId = "a3ba7dab-4c2e-4c55-afdc-bd56661e9ee4"
)

$ErrorActionPreference = "Stop"

$secretsPath = Join-Path $env:APPDATA "Microsoft\UserSecrets\$UserSecretsId\secrets.json"
if (-not (Test-Path -LiteralPath $secretsPath)) {
    throw "No encontre el archivo de User Secrets: $secretsPath"
}

$secrets = Get-Content -Raw -LiteralPath $secretsPath | ConvertFrom-Json
$username = [string]$secrets.'Siigo:Username'
$accessKey = [string]$secrets.'Siigo:AccessKey'

if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($accessKey)) {
    throw "Falta Siigo:Username o Siigo:AccessKey en User Secrets."
}

az account set --subscription $SubscriptionId
az webapp config appsettings set `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --settings `
        "Siigo__Username=$username" `
        "Siigo__AccessKey=$accessKey" `
        "Siigo__BaseUrl=https://api.siigo.com" `
        "Siigo__PartnerId=CotizadorInterno" `
    --output none

$registeredSettings = az webapp config appsettings list `
    --resource-group $ResourceGroupName `
    --name $WebAppName `
    --query "[?starts_with(name, 'Siigo__')].name" `
    -o tsv

Write-Host "App Settings de Siigo registradas en ${WebAppName}:"
$registeredSettings | Sort-Object | ForEach-Object { Write-Host " - $_" }
