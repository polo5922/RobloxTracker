# Construit l'installateur : publie l'app (autonome, fichier unique) puis génère le .msi.
# Usage : .\build-installer.ps1 [-Version 1.2.3]
# Sans -Version, un numéro croissant est tiré de la date (1.AA.JJJHQ) pour que chaque build soit bien une mise à jour.
param([string]$Version)

if (-not $Version) {
    $now = Get-Date
    $Version = "1.{0}.{1}" -f ($now.Year % 100), ($now.DayOfYear * 100 + $now.Hour * 4 + [math]::Floor($now.Minute / 15))
}

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publish = Join-Path $root "publish"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish "$root\src\RobloxTracker.App\RobloxTracker.App.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish a échoué" }

dotnet build "$root\installer\RobloxTracker.Installer.wixproj" -c Release -p:AppVersion=$Version -o "$root\dist"
if ($LASTEXITCODE -ne 0) { throw "La génération du .msi a échoué" }

Write-Host "Installateur prêt : $root\dist\RobloxTracker-Setup.msi"
