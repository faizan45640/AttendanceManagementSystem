# Quick Deploy Script for AMS to Azure
# Run this script after configuring Azure CLI

Write-Host "🚀 AMS Deployment Script" -ForegroundColor Cyan
Write-Host "=========================" -ForegroundColor Cyan
Write-Host ""

# Configuration
$resourceGroup = "AMS-RG"
$location = "eastus"
$appName = Read-Host "Enter unique app name (e.g., ams-yourname)"
$planName = "AMS-Plan"

Write-Host ""
Write-Host "📦 Configuration:" -ForegroundColor Yellow

Write-Host "   Resource Group: $resourceGroup"
Write-Host "   Location: $location"
Write-Host "   App Name: $appName"
Write-Host "   Plan: $planName (F1 - FREE)"
Write-Host ""

Write-Host "⚠️  CRITICAL: SQL Server Migration Required" -ForegroundColor Red
Write-Host "========================================" -ForegroundColor Red
Write-Host ""
Write-Host "Your app uses SQL Server Express which CANNOT run on Azure!" -ForegroundColor Yellow
Write-Host ""
Write-Host "Before deploying, you MUST:" -ForegroundColor Yellow
Write-Host "  1️⃣  Create SQL Azure database (250MB FREE)" -ForegroundColor Cyan
Write-Host "  2️⃣  Migrate your AMS database" -ForegroundColor Cyan
Write-Host "  3️⃣  Update connection string in Azure" -ForegroundColor Cyan
Write-Host ""
Write-Host "📖 See .deployment-guide.md for step-by-step instructions" -ForegroundColor Green
Write-Host ""

$confirm = Read-Host "Continue with deployment? (y/n)"
if ($confirm -ne "y") {
    Write-Host "❌ Deployment cancelled" -ForegroundColor Red
    exit
}

Write-Host ""
Write-Host "Step 1: Logging into Azure..." -ForegroundColor Green
az login

Write-Host ""
Write-Host "Step 2: Creating Resource Group..." -ForegroundColor Green
az group create --name $resourceGroup --location $location

Write-Host ""
Write-Host "Step 3: Creating App Service Plan (FREE F1)..." -ForegroundColor Green
az appservice plan create --name $planName --resource-group $resourceGroup --sku F1

Write-Host ""
Write-Host "Step 4: Creating Web App..." -ForegroundColor Green
az webapp create --name $appName --resource-group $resourceGroup --plan $planName --runtime "DOTNET:8.0"

Write-Host ""
Write-Host "Step 5: Building application..." -ForegroundColor Green
dotnet publish -c Release -o ./publish

Write-Host ""
Write-Host "Step 6: Creating deployment package..." -ForegroundColor Green
if (Test-Path deploy.zip) {
    Remove-Item deploy.zip
}
Compress-Archive -Path ./publish/* -DestinationPath deploy.zip -Force

Write-Host ""
Write-Host "Step 7: Deploying to Azure..." -ForegroundColor Green
az webapp deployment source config-zip --resource-group $resourceGroup --name $appName --src deploy.zip

Write-Host ""
Write-Host "✅ Deployment Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "🌐 Your app is live at: https://$appName.azurewebsites.net" -ForegroundColor Cyan
Write-Host ""
Write-Host "📊 Next Steps:" -ForegroundColor Yellow
Write-Host "   1. Visit your app URL"
Write-Host "   2. Create admin user"
Write-Host "   3. Test all features"
Write-Host ""
Write-Host "💡 To update your app, run this script again" -ForegroundColor Yellow
Write-Host ""

# Clean up
Write-Host "Cleaning up temporary files..." -ForegroundColor Gray
Remove-Item -Recurse -Force ./publish -ErrorAction SilentlyContinue
Remove-Item deploy.zip -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "🎉 All done!" -ForegroundColor Green
