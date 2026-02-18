param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectPath = "src\AuditHistoryExtractorPro.UI\AuditHistoryExtractorPro.UI.csproj"
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
    Remove-Item $destinationExe -Force
}

Move-Item -Path $exe.FullName -Destination $destinationExe -Force

if (Test-Path $publishTempDir) {
    Remove-Item -Path $publishTempDir -Recurse -Force
}

Write-Host "`nEjecutable generado correctamente:" -ForegroundColor Green
Write-Host "  $destinationExe" -ForegroundColor Green

Write-Step "Abriendo carpeta Builds"
Start-Process explorer.exe $buildsDir
