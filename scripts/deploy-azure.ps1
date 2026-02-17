# Deploy to Azure script
# PowerShell

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroup,
    
    [Parameter(Mandatory=$true)]
    [string]$Location,
    
    [Parameter(Mandatory=$true)]
    [string]$AppName,
    
    [Parameter(Mandatory=$false)]
    [string]$KeyVaultName = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$CreateResources
)

$ErrorActionPreference = "Stop"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "Azure Deployment Script" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Verify Azure CLI is installed
try {
    az --version | Out-Null
}
catch {
    Write-Host "Azure CLI is not installed or not in PATH!" -ForegroundColor Red
    Write-Host "Install from: https://docs.microsoft.com/cli/azure/install-azure-cli" -ForegroundColor Yellow
    exit 1
}

# Login to Azure
Write-Host "Logging in to Azure..." -ForegroundColor Yellow
az login
Write-Host ""

# Create resources if requested
if ($CreateResources) {
    Write-Host "Creating Azure resources..." -ForegroundColor Yellow
    Write-Host ""
    
    # Create Resource Group
    Write-Host "Creating Resource Group: $ResourceGroup" -ForegroundColor Cyan
    az group create --name $ResourceGroup --location $Location
    Write-Host ""
    
    # Create App Service Plan
    $AppServicePlan = "$AppName-plan"
    Write-Host "Creating App Service Plan: $AppServicePlan" -ForegroundColor Cyan
    az appservice plan create `
        --name $AppServicePlan `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku B1 `
        --is-linux
    Write-Host ""
    
    # Create Web App for UI
    $WebAppName = "$AppName-ui"
    Write-Host "Creating Web App: $WebAppName" -ForegroundColor Cyan
    az webapp create `
        --name $WebAppName `
        --resource-group $ResourceGroup `
        --plan $AppServicePlan `
        --runtime "DOTNET:8.0"
    Write-Host ""
    
    # Enable Managed Identity
    Write-Host "Enabling Managed Identity for Web App..." -ForegroundColor Cyan
    az webapp identity assign `
        --name $WebAppName `
        --resource-group $ResourceGroup
    Write-Host ""
    
    # Create Storage Account for exports
    $StorageAccount = $AppName.ToLower() -replace '[^a-z0-9]', ''
    $StorageAccount = $StorageAccount.Substring(0, [Math]::Min(24, $StorageAccount.Length))
    
    Write-Host "Creating Storage Account: $StorageAccount" -ForegroundColor Cyan
    az storage account create `
        --name $StorageAccount `
        --resource-group $ResourceGroup `
        --location $Location `
        --sku Standard_LRS
    Write-Host ""
    
    # Create Blob Container
    Write-Host "Creating Blob Container: audit-exports" -ForegroundColor Cyan
    $StorageKey = (az storage account keys list --account-name $StorageAccount --resource-group $ResourceGroup --query "[0].value" -o tsv)
    az storage container create `
        --name "audit-exports" `
        --account-name $StorageAccount `
        --account-key $StorageKey
    Write-Host ""
    
    # Create Key Vault if specified
    if ($KeyVaultName) {
        Write-Host "Creating Key Vault: $KeyVaultName" -ForegroundColor Cyan
        az keyvault create `
            --name $KeyVaultName `
            --resource-group $ResourceGroup `
            --location $Location
        Write-Host ""
        
        # Get Web App Managed Identity
        $PrincipalId = (az webapp identity show --name $WebAppName --resource-group $ResourceGroup --query principalId -o tsv)
        
        # Grant access to Key Vault
        Write-Host "Granting Key Vault access to Web App..." -ForegroundColor Cyan
        az keyvault set-policy `
            --name $KeyVaultName `
            --object-id $PrincipalId `
            --secret-permissions get list
        Write-Host ""
    }
    
    # Create Application Insights
    $AppInsights = "$AppName-insights"
    Write-Host "Creating Application Insights: $AppInsights" -ForegroundColor Cyan
    az monitor app-insights component create `
        --app $AppInsights `
        --location $Location `
        --resource-group $ResourceGroup `
        --application-type web
    Write-Host ""
    
    # Get App Insights Instrumentation Key
    $InstrumentationKey = (az monitor app-insights component show --app $AppInsights --resource-group $ResourceGroup --query instrumentationKey -o tsv)
    
    # Configure App Insights for Web App
    Write-Host "Configuring Application Insights..." -ForegroundColor Cyan
    az webapp config appsettings set `
        --name $WebAppName `
        --resource-group $ResourceGroup `
        --settings APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=$InstrumentationKey"
    Write-Host ""
    
    Write-Host "Resources created successfully!" -ForegroundColor Green
    Write-Host ""
}

# Build and publish application
Write-Host "Building application..." -ForegroundColor Yellow
.\scripts\build.ps1 -Configuration Release -Publish
Write-Host ""

# Deploy UI to Azure Web App
$WebAppName = "$AppName-ui"
$PublishPath = "publish\UI"

Write-Host "Deploying UI to Azure Web App: $WebAppName" -ForegroundColor Yellow
az webapp deploy `
    --resource-group $ResourceGroup `
    --name $WebAppName `
    --src-path "$PublishPath.zip" `
    --type zip

if ($LASTEXITCODE -eq 0) {
    Write-Host "Deployment completed successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Get Web App URL
    $WebAppUrl = "https://$WebAppName.azurewebsites.net"
    Write-Host "Application URL: $WebAppUrl" -ForegroundColor Cyan
    Write-Host ""
    
    # Display resource information
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Deployment Information" -ForegroundColor Cyan
    Write-Host "==================================" -ForegroundColor Cyan
    Write-Host "Resource Group: $ResourceGroup" -ForegroundColor White
    Write-Host "Location: $Location" -ForegroundColor White
    Write-Host "Web App: $WebAppName" -ForegroundColor White
    Write-Host "URL: $WebAppUrl" -ForegroundColor White
    
    if ($KeyVaultName) {
        Write-Host "Key Vault: $KeyVaultName" -ForegroundColor White
    }
    
    Write-Host "==================================" -ForegroundColor Cyan
}
else {
    Write-Host "Deployment failed!" -ForegroundColor Red
    exit 1
}
