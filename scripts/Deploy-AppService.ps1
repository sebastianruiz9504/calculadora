[CmdletBinding()]
param(
    [string]$SubscriptionId = "7018b9b6-5dfc-4d91-bc4d-5f29f27553bd",
    [string]$ResourceGroupName = "DigitalTechAppAI",
    [string]$WebAppName = "calculadoradt",
    [string]$ProjectPath = "$PSScriptRoot\..\CotizadorInterno.Web.csproj",
    [string]$Configuration = "Release",
    [string]$VerifyPath = "/PrimaLegal",
    [int]$TimeoutSeconds = 900,
    [switch]$NoRestore,
    [switch]$SkipVerify,
    [switch]$KeepPackage
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet no esta disponible en PATH."
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI no esta disponible en PATH."
}

$resolvedProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("cotizador-appservice-" + [Guid]::NewGuid().ToString("N"))
$publishDir = Join-Path $workRoot "publish"
$zipPath = Join-Path $workRoot "$WebAppName.zip"

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

try {
    if (-not $NoRestore) {
        Write-Host "Restaurando paquetes..."
        dotnet restore $resolvedProjectPath
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore fallo con codigo $LASTEXITCODE."
        }
    }

    Write-Host "Publicando $resolvedProjectPath ($Configuration)..."
    dotnet publish $resolvedProjectPath `
        --configuration $Configuration `
        --no-restore `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish fallo con codigo $LASTEXITCODE. No se desplegara un paquete incompleto."
    }

    Write-Host "Empaquetando artefacto..."
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force

    Write-Host "Seleccionando suscripcion $SubscriptionId..."
    az account set --subscription $SubscriptionId
    if ($LASTEXITCODE -ne 0) {
        throw "No fue posible seleccionar la suscripcion $SubscriptionId."
    }

    Write-Host "Desplegando $WebAppName en $ResourceGroupName..."
    az webapp deploy `
        --resource-group $ResourceGroupName `
        --name $WebAppName `
        --src-path $zipPath `
        --type zip `
        --restart true `
        --timeout $TimeoutSeconds `
        --output json
    if ($LASTEXITCODE -ne 0) {
        throw "Azure rechazo el despliegue de $WebAppName con codigo $LASTEXITCODE."
    }

    if (-not $SkipVerify) {
        $defaultHostName = az webapp show `
            --resource-group $ResourceGroupName `
            --name $WebAppName `
            --query "defaultHostName" `
            -o tsv
        if ($LASTEXITCODE -ne 0) {
            throw "No fue posible consultar el hostname del App Service $WebAppName."
        }

        if ([string]::IsNullOrWhiteSpace($defaultHostName)) {
            throw "No fue posible resolver el hostname del App Service $WebAppName."
        }

        $verifyUrl = "https://$defaultHostName$VerifyPath"
        Write-Host "Verificando $verifyUrl..."
        $lastStatus = 0
        for ($attempt = 1; $attempt -le 12; $attempt++) {
            try {
                $response = Invoke-WebRequest -Uri $verifyUrl -MaximumRedirection 0 -TimeoutSec 30 -ErrorAction Stop
                $lastStatus = [int]$response.StatusCode
            }
            catch {
                if ($_.Exception.Response) {
                    $lastStatus = [int]$_.Exception.Response.StatusCode
                }
                else {
                    $lastStatus = 0
                }
            }

            if ($lastStatus -ge 200 -and $lastStatus -lt 400) {
                Write-Host "Verificacion OK: $verifyUrl respondio HTTP $lastStatus."
                break
            }

            if ($attempt -eq 12) {
                throw "La verificacion no fue exitosa. Ultimo estado HTTP: $lastStatus."
            }

            Start-Sleep -Seconds 10
        }
    }

    Write-Host "Despliegue completado."
}
finally {
    if ($KeepPackage) {
        Write-Host "Paquete conservado en $zipPath"
    }
    elseif (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
