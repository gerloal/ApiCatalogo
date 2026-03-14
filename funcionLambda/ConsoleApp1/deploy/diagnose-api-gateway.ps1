# Script para diagnosticar configuración de API Gateway
# Uso: .\diagnose-api-gateway.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiId = "q0wnv840ik",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1"
)

Write-Host "==========================================="
Write-Host "Diagnosticando API Gateway"
Write-Host "API ID: $ApiId"
Write-Host "Region: $Region"
Write-Host "==========================================="

# 1. Información básica de la API
Write-Host "`n1. Información de la API:"
try {
    $apiInfo = aws apigateway get-rest-api --rest-api-id $ApiId --region $Region --output json | ConvertFrom-Json
    Write-Host "   Nombre: $($apiInfo.name)"
    Write-Host "   Creada: $($apiInfo.createdDate)"
    Write-Host "   Endpoint: https://$ApiId.execute-api.$Region.amazonaws.com"
} catch {
    Write-Host "   ? Error obteniendo información de la API"
    exit 1
}

# 2. Listar recursos
Write-Host "`n2. Recursos disponibles:"
$resources = aws apigateway get-resources --rest-api-id $ApiId --region $Region --output json | ConvertFrom-Json

foreach ($resource in $resources.items) {
    Write-Host "   Path: $($resource.path)"
    Write-Host "   Resource ID: $($resource.id)"
    
    if ($resource.resourceMethods) {
        $methods = $resource.resourceMethods.PSObject.Properties.Name
        Write-Host "   Métodos: $($methods -join ', ')"
        
        # Ver detalles de cada método
        foreach ($method in $methods) {
            if ($method -ne "OPTIONS") {
                try {
                    $methodDetails = aws apigateway get-method `
                        --rest-api-id $ApiId `
                        --resource-id $resource.id `
                        --http-method $method `
                        --region $Region `
                        --output json | ConvertFrom-Json
                    
                    Write-Host "      [$method]"
                    Write-Host "         API Key Required: $($methodDetails.apiKeyRequired)"
                    Write-Host "         Authorization Type: $($methodDetails.authorizationType)"
                    
                    if ($methodDetails.authorizerId) {
                        Write-Host "         Authorizer ID: $($methodDetails.authorizerId)"
                    }
                    
                    # Verificar integration
                    if ($methodDetails.methodIntegration) {
                        Write-Host "         Integration Type: $($methodDetails.methodIntegration.type)"
                        if ($methodDetails.methodIntegration.uri) {
                            Write-Host "         Integration URI: $($methodDetails.methodIntegration.uri)"
                        }
                    }
                } catch {
                    Write-Host "      [$method] ?? Error obteniendo detalles"
                }
            }
        }
    }
    Write-Host ""
}

# 3. Listar autorizadores
Write-Host "`n3. Autorizadores configurados:"
try {
    $authorizers = aws apigateway get-authorizers --rest-api-id $ApiId --region $Region --output json | ConvertFrom-Json
    
    if ($authorizers.items.Count -eq 0) {
        Write-Host "   No hay autorizadores configurados"
    } else {
        foreach ($auth in $authorizers.items) {
            Write-Host "   - $($auth.name)"
            Write-Host "     Type: $($auth.type)"
            Write-Host "     ID: $($auth.id)"
            if ($auth.authorizerUri) {
                Write-Host "     URI: $($auth.authorizerUri)"
            }
        }
    }
} catch {
    Write-Host "   No hay autorizadores o error al obtener"
}

# 4. Verificar API Keys
Write-Host "`n4. API Keys configuradas:"
try {
    $apiKeys = aws apigateway get-api-keys --region $Region --output json | ConvertFrom-Json
    
    if ($apiKeys.items.Count -eq 0) {
        Write-Host "   No hay API Keys configuradas"
    } else {
        foreach ($key in $apiKeys.items) {
            Write-Host "   - $($key.name)"
            Write-Host "     ID: $($key.id)"
            Write-Host "     Enabled: $($key.enabled)"
            Write-Host "     Created: $($key.createdDate)"
        }
    }
} catch {
    Write-Host "   Error obteniendo API Keys"
}

# 5. Verificar stages
Write-Host "`n5. Stages desplegados:"
try {
    $stages = aws apigateway get-stages --rest-api-id $ApiId --region $Region --output json | ConvertFrom-Json
    
    foreach ($stage in $stages.item) {
        Write-Host "   - Stage: $($stage.stageName)"
        Write-Host "     Deploy ID: $($stage.deploymentId)"
        Write-Host "     Created: $($stage.createdDate)"
        Write-Host "     Last Updated: $($stage.lastUpdatedDate)"
    }
} catch {
    Write-Host "   Error obteniendo stages"
}

# 6. Verificar permisos de Lambda
Write-Host "`n6. Verificando permisos de Lambda para API Gateway:"
try {
    $policy = aws lambda get-policy `
        --function-name OrderExportAPI-dev `
        --region $Region `
        --query 'Policy' `
        --output text 2>$null | ConvertFrom-Json
    
    if ($policy) {
        Write-Host "   ? Lambda tiene permisos configurados"
        $statements = $policy.Statement | Where-Object { $_.Principal.Service -eq "apigateway.amazonaws.com" }
        Write-Host "   Statements de API Gateway: $($statements.Count)"
    }
} catch {
    Write-Host "   ??  No se pudieron verificar permisos de Lambda"
}

Write-Host "`n==========================================="
Write-Host "? Diagnóstico completado"
Write-Host "==========================================="

Write-Host "`n?? Recomendaciones:"
Write-Host "1. Si 'API Key Required' es true, necesitas enviar el header 'x-api-key'"
Write-Host "2. Si 'Authorization Type' es 'AWS_IAM', necesitas firmar la petición con AWS Signature"
Write-Host "3. Si hay un Authorizer configurado, necesitas enviar el token apropiado"
Write-Host "4. Si 'Authorization Type' es 'NONE', no se requiere autenticación"
