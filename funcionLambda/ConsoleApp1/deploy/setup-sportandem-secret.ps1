# Script para verificar y crear el secret de Sportandem
# Uso: .\setup-sportandem-secret.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1"
)

Write-Host "==========================================="
Write-Host "Verificando Secret para Sportandem"
Write-Host "Region: $Region"
Write-Host "==========================================="

$secretName = "Sportandem/prod/order-export"

# Verificar si existe el secret
Write-Host "`nVerificando si existe el secret: $secretName"

try {
    $secretInfo = aws secretsmanager describe-secret `
        --secret-id $secretName `
        --region $Region `
        --output json 2>$null | ConvertFrom-Json
    
    Write-Host "? Secret existe:"
    Write-Host "   ARN: $($secretInfo.ARN)"
    Write-Host "   Última modificación: $($secretInfo.LastChangedDate)"
    Write-Host "   Descripción: $($secretInfo.Description)"
    
    # Mostrar los campos del secret (sin valores)
    Write-Host "`n?? Verificando estructura del secret..."
    $secretValue = aws secretsmanager get-secret-value `
        --secret-id $secretName `
        --region $Region `
        --query 'SecretString' `
        --output text | ConvertFrom-Json
    
    Write-Host "`n? Secret contiene los siguientes campos:"
    $secretValue.PSObject.Properties.Name | ForEach-Object {
        $value = $secretValue.$_
        if ($_ -match "Secret|Token|Key") {
            Write-Host "   - $_: ****** (oculto)"
        } else {
            Write-Host "   - $_: $value"
        }
    }
    
    # Verificar campos requeridos
    $requiredFields = @("ClientId", "ClientSecret", "RefreshToken", "MarketPlaceID", "RoleArn", "SellerId", "TenantId")
    $missingFields = @()
    
    foreach ($field in $requiredFields) {
        if (-not $secretValue.$field) {
            $missingFields += $field
        }
    }
    
    if ($missingFields.Count -gt 0) {
        Write-Host "`n??  Advertencia: Faltan los siguientes campos requeridos:"
        $missingFields | ForEach-Object { Write-Host "   - $_" }
    } else {
        Write-Host "`n? Todos los campos requeridos están presentes"
    }
    
} catch {
    Write-Host "? Secret NO existe"
    Write-Host "`nPara crear el secret, necesitas las credenciales de Amazon SP-API de Sportandem."
    Write-Host "`n?? Comando para crear el secret:"
    Write-Host @"

aws secretsmanager create-secret \`
    --name "$secretName" \`
    --description "Amazon SP-API credentials for Sportandem" \`
    --secret-string '{
        "ClientId": "amzn1.application-oa2-client.TU_CLIENT_ID",
        "ClientSecret": "TU_CLIENT_SECRET",
        "RefreshToken": "Atzr|TU_REFRESH_TOKEN",
        "MarketPlaceID": "A1RKKUPIHCS9HS",
        "RoleArn": "arn:aws:iam::TU_ACCOUNT:role/SPAPIRole",
        "SellerId": "TU_SELLER_ID",
        "TenantId": "Sportandem"
    }' \`
    --region $Region

"@
    
    Write-Host "`n?? Obtén las credenciales desde:"
    Write-Host "   1. Amazon Seller Central ? Settings ? User Permissions ? Developer ? View Credentials"
    Write-Host "   2. O desde tu sistema de gestión de credenciales"
    
    exit 1
}

Write-Host "`n==========================================="
Write-Host "? Verificación completada"
Write-Host "==========================================="
Write-Host "`n?? Ahora puedes ejecutar la prueba:"
Write-Host "   .\test-order-export-api.ps1"
