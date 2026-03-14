# Script para crear el secret de Sportandem en el formato correcto
# Uso: .\create-sportandem-secret.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$TenantId = "Sportandem",
    
    [Parameter(Mandatory=$true)]
    [string]$ClientId,
    
    [Parameter(Mandatory=$true)]
    [string]$ClientSecret,
    
    [Parameter(Mandatory=$true)]
    [string]$RefreshToken,
    
    [Parameter(Mandatory=$false)]
    [string]$MarketPlaceID = "A1RKKUPIHCS9HS",
    
    [Parameter(Mandatory=$true)]
    [string]$RoleArn,
    
    [Parameter(Mandatory=$true)]
    [string]$SellerId
)

Write-Host "==========================================="
Write-Host "Creating Sportandem SP-API Secret"
Write-Host "==========================================="

# Formato: <TenantId>/prod/order-export
$secretName = "$TenantId/prod/order-export"

Write-Host "`nSecret Name: $secretName"
Write-Host "Region: $Region"

# Crear JSON del secret
$secretValue = @{
    ClientId = $ClientId
    ClientSecret = $ClientSecret
    RefreshToken = $RefreshToken
    MarketPlaceID = $MarketPlaceID
    RoleArn = $RoleArn
    SellerId = $SellerId
    TenantId = $TenantId
} | ConvertTo-Json

Write-Host "`nSecret Value (masked):"
Write-Host "  ClientId: $($ClientId.Substring(0, [Math]::Min(10, $ClientId.Length)))..."
Write-Host "  MarketPlaceID: $MarketPlaceID"
Write-Host "  SellerId: $($SellerId.Substring(0, [Math]::Min(10, $SellerId.Length)))..."
Write-Host "  RoleArn: $RoleArn"

# Verificar si el secret ya existe
Write-Host "`nChecking if secret already exists..."

try {
    $existingSecret = aws secretsmanager describe-secret `
        --secret-id $secretName `
        --region $Region `
        --output json 2>$null | ConvertFrom-Json
    
    if ($existingSecret) {
        Write-Host "??  Secret already exists!" -ForegroundColor Yellow
        Write-Host "   ARN: $($existingSecret.ARN)"
        Write-Host "   Last Modified: $($existingSecret.LastChangedDate)"
        
        $update = Read-Host "`nDo you want to update it? (y/N)"
        
        if ($update -eq "y" -or $update -eq "Y") {
            Write-Host "`nUpdating secret..."
            
            aws secretsmanager update-secret `
                --secret-id $secretName `
                --secret-string $secretValue `
                --region $Region
            
            Write-Host "? Secret updated successfully!"
        } else {
            Write-Host "Skipping update"
            exit 0
        }
    }
} catch {
    # Secret doesn't exist, create it
    Write-Host "Secret doesn't exist, creating new one..."
    
    aws secretsmanager create-secret `
        --name $secretName `
        --description "Amazon SP-API credentials for $TenantId" `
        --secret-string $secretValue `
        --region $Region
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? Secret created successfully!"
    } else {
        Write-Host "? Failed to create secret" -ForegroundColor Red
        exit 1
    }
}

Write-Host "`n==========================================="
Write-Host "? Secret Configuration Complete"
Write-Host "==========================================="

Write-Host "`n?? Next steps:"
Write-Host "   1. Verify secret:"
Write-Host "      aws secretsmanager get-secret-value --secret-id $secretName --region $Region"
Write-Host ""
Write-Host "   2. Test Order Export API:"
Write-Host "      .\test-order-export-detailed.ps1"
