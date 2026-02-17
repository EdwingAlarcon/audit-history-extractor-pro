# Build script for Audit History Extractor Pro
# PowerShell

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release',
    
    [Parameter(Mandatory=$false)]
    [switch]$Clean,
    
    [Parameter(Mandatory=$false)]
    [switch]$Test,
    
    [Parameter(Mandatory=$false)]
    [switch]$Publish
)

$ErrorActionPreference = "Stop"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Audit History Extractor Pro - Build" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Variables
$SolutionFile = "AuditHistoryExtractorPro.sln"
$PublishDir = "publish"
$TestResultsDir = "TestResults"

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning solution..." -ForegroundColor Yellow
    dotnet clean $SolutionFile --configuration $Configuration
    
    if (Test-Path $PublishDir) {
        Remove-Item -Path $PublishDir -Recurse -Force
    }
    
    if (Test-Path $TestResultsDir) {
        Remove-Item -Path $TestResultsDir -Recurse -Force
    }
    
    Write-Host "Clean completed." -ForegroundColor Green
    Write-Host ""
}

# Restore dependencies
Write-Host "Restoring dependencies..." -ForegroundColor Yellow
dotnet restore $SolutionFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "Restore failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Restore completed." -ForegroundColor Green
Write-Host ""

# Build solution
Write-Host "Building solution ($Configuration)..." -ForegroundColor Yellow
dotnet build $SolutionFile --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build completed successfully." -ForegroundColor Green
Write-Host ""

# Run tests if requested
if ($Test) {
    Write-Host "Running tests..." -ForegroundColor Yellow
    dotnet test $SolutionFile --configuration $Configuration --no-build --logger "trx;LogFileName=test-results.trx" --results-directory $TestResultsDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Tests completed successfully." -ForegroundColor Green
    Write-Host ""
}

# Publish if requested
if ($Publish) {
    Write-Host "Publishing applications..." -ForegroundColor Yellow
    
    # Create publish directory
    if (-not (Test-Path $PublishDir)) {
        New-Item -Path $PublishDir -ItemType Directory | Out-Null
    }
    
    # Publish CLI
    Write-Host "  Publishing CLI..." -ForegroundColor Cyan
    $CLIPublishPath = Join-Path $PublishDir "CLI"
    
    # Windows x64
    dotnet publish "src\AuditHistoryExtractorPro.CLI\AuditHistoryExtractorPro.CLI.csproj" `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output (Join-Path $CLIPublishPath "win-x64") `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    
    # Linux x64
    dotnet publish "src\AuditHistoryExtractorPro.CLI\AuditHistoryExtractorPro.CLI.csproj" `
        --configuration $Configuration `
        --runtime linux-x64 `
        --self-contained true `
        --output (Join-Path $CLIPublishPath "linux-x64") `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    
    # macOS x64
    dotnet publish "src\AuditHistoryExtractorPro.CLI\AuditHistoryExtractorPro.CLI.csproj" `
        --configuration $Configuration `
        --runtime osx-x64 `
        --self-contained true `
        --output (Join-Path $CLIPublishPath "osx-x64") `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    
    Write-Host "  CLI published successfully." -ForegroundColor Green
    
    # Publish UI
    Write-Host "  Publishing UI..." -ForegroundColor Cyan
    $UIPublishPath = Join-Path $PublishDir "UI"
    
    dotnet publish "src\AuditHistoryExtractorPro.UI\AuditHistoryExtractorPro.UI.csproj" `
        --configuration $Configuration `
        --output $UIPublishPath
    
    Write-Host "  UI published successfully." -ForegroundColor Green
    Write-Host ""
    
    # Copy configuration files
    Write-Host "Copying configuration files..." -ForegroundColor Yellow
    Copy-Item "config.example.yaml" -Destination (Join-Path $CLIPublishPath "win-x64\config.example.yaml")
    Copy-Item "config.example.yaml" -Destination (Join-Path $CLIPublishPath "linux-x64\config.example.yaml")
    Copy-Item "config.example.yaml" -Destination (Join-Path $CLIPublishPath "osx-x64\config.example.yaml")
    Copy-Item "config.example.yaml" -Destination (Join-Path $UIPublishPath "config.example.yaml")
    Write-Host "Configuration files copied." -ForegroundColor Green
    Write-Host ""
    
    # Create archives
    Write-Host "Creating distribution archives..." -ForegroundColor Yellow
    
    Compress-Archive -Path (Join-Path $CLIPublishPath "win-x64\*") `
        -DestinationPath (Join-Path $PublishDir "AuditExtractor-CLI-win-x64.zip") `
        -Force
    
    Compress-Archive -Path (Join-Path $CLIPublishPath "linux-x64\*") `
        -DestinationPath (Join-Path $PublishDir "AuditExtractor-CLI-linux-x64.zip") `
        -Force
    
    Compress-Archive -Path (Join-Path $CLIPublishPath "osx-x64\*") `
        -DestinationPath (Join-Path $PublishDir "AuditExtractor-CLI-osx-x64.zip") `
        -Force
    
    Compress-Archive -Path (Join-Path $UIPublishPath "*") `
        -DestinationPath (Join-Path $PublishDir "AuditExtractor-UI.zip") `
        -Force
    
    Write-Host "Archives created successfully." -ForegroundColor Green
    Write-Host ""
}

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Build script completed!" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Cyan

if ($Publish) {
    Write-Host ""
    Write-Host "Published files are in: $PublishDir" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Distribution archives:" -ForegroundColor Cyan
    Get-ChildItem -Path $PublishDir -Filter "*.zip" | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  - $($_.Name) ($size MB)" -ForegroundColor White
    }
}
