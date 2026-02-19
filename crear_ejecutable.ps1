param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = "src\AuditHistoryExtractorPro.UI\AuditHistoryExtractorPro.UI.csproj",

    [Parameter(Mandatory = $false)]
    [int]$Port = 5188
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n=== $Message ===" -ForegroundColor Cyan
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $root) {
    $root = Get-Location
}

$projectFullPath = Join-Path $root $ProjectPath
if (-not (Test-Path $projectFullPath)) {
    throw "No se encontró el proyecto: $projectFullPath"
}

$projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectFullPath)
$buildsDir = Join-Path $root "Builds"
$publishTempDir = Join-Path $root "publish-temp\$projectName"

Write-Step "Limpieza de compilaciones previas"
dotnet clean $projectFullPath -c Release
if ($LASTEXITCODE -ne 0) {
    throw "dotnet clean falló con código $LASTEXITCODE"
}

if (Test-Path $publishTempDir) {
    Remove-Item -Path $publishTempDir -Recurse -Force
}

Write-Step "Publicación optimizada (Release, win-x64, self-contained, single-file)"
dotnet publish $projectFullPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -o $publishTempDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falló con código $LASTEXITCODE"
}

$exe = Get-ChildItem -Path $publishTempDir -Filter "*.exe" -File | Select-Object -First 1
if (-not $exe) {
    throw "No se encontró un archivo .exe en: $publishTempDir"
}

Write-Step "Organizando ejecutable en carpeta Builds"
if (-not (Test-Path $buildsDir)) {
    New-Item -Path $buildsDir -ItemType Directory | Out-Null
}

$destinationExe = Join-Path $buildsDir $exe.Name
if (Test-Path $destinationExe) {
    $processName = [System.IO.Path]::GetFileNameWithoutExtension($destinationExe)
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300

    try {
        Remove-Item $destinationExe -Force
    }
    catch {
        throw "No se pudo reemplazar $destinationExe porque está en uso. Cierra la app y vuelve a ejecutar el script."
    }
}

Move-Item -Path $exe.FullName -Destination $destinationExe -Force

$launcherBatPath = Join-Path $buildsDir "iniciar_ui_publicada.bat"
$launcherBatContent = @"
@echo off
setlocal enableextensions
title Audit History Extractor Pro - Published
color 0A

cd /d "%~dp0"

set "PORT=$Port"
set "ASPNETCORE_URLS=http://localhost:%PORT%"

echo [INFO] Iniciando Audit History Extractor Pro publicado...
echo [INFO] URL: http://localhost:%PORT%/
echo.

start "" "http://localhost:%PORT%/"
"$($exe.Name)"

echo.
echo [INFO] La aplicacion se detuvo.
pause
"@

Set-Content -Path $launcherBatPath -Value $launcherBatContent -Encoding ASCII

if (Test-Path $publishTempDir) {
    Remove-Item -Path $publishTempDir -Recurse -Force
}

Write-Host "`nEjecutable generado correctamente:" -ForegroundColor Green
Write-Host "  $destinationExe" -ForegroundColor Green
Write-Host "Lanzador BAT generado:" -ForegroundColor Green
Write-Host "  $launcherBatPath" -ForegroundColor Green

Write-Step "Abriendo carpeta Builds"
Start-Process explorer.exe $buildsDir
