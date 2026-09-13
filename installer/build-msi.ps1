<#
.SYNOPSIS
    Publica PdfSigner para Windows y genera el instalador MSI.

.DESCRIPTION
    Requiere el SDK de .NET 10, los workloads de MAUI y la herramienta WiX 6:

        dotnet workload install maui
        dotnet tool install --global wix --version 6.0.2
        wix extension add --global WixToolset.UI.wixext/6.0.2

    El MSI resultante se instala por usuario, así que no pide permisos de
    administrador, y la aplicación va autocontenida: la máquina de destino no
    necesita tener instalado .NET ni el Windows App SDK.

.EXAMPLE
    .\installer\build-msi.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
$proyecto = Join-Path $repo "src\PdfSigner.App\PdfSigner.App.csproj"
$tfm = "net10.0-windows10.0.19041.0"
$rid = "win-$Arch"

Write-Host "==> Publicando $rid ..." -ForegroundColor Cyan

# Ojo: hay que usar RuntimeIdentifierOverride y NO RuntimeIdentifier. MAUI gestiona el RID
# por su cuenta, y pasarlo directo hace que el SDK busque el runtime pack de Mono para
# Windows, que no existe, con un error NU1102 bastante desorientador.
dotnet publish $proyecto `
    -f $tfm `
    -c Release `
    -p:RuntimeIdentifierOverride=$rid `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    --nologo

if ($LASTEXITCODE -ne 0) { throw "Falló la publicación." }

$publish = Join-Path $repo "src\PdfSigner.App\bin\Release\$tfm\$rid\publish"
if (-not (Test-Path $publish)) { throw "No se encontró la carpeta de publicación: $publish" }

$salida = Join-Path $repo $OutputDir
New-Item -ItemType Directory -Force $salida | Out-Null
$msi = Join-Path $salida "PdfSigner-$Version-$rid.msi"

Write-Host "==> Generando el MSI ..." -ForegroundColor Cyan

wix build (Join-Path $PSScriptRoot "Product.wxs") `
    -d "PublishDir=$publish" `
    -d "Version=$Version" `
    -b $PSScriptRoot `
    -ext WixToolset.UI.wixext `
    -arch $Arch `
    -o $msi

if ($LASTEXITCODE -ne 0) { throw "Falló la generación del MSI." }

$tam = [math]::Round((Get-Item $msi).Length / 1MB, 1)
Write-Host "==> Listo: $msi ($tam MB)" -ForegroundColor Green
