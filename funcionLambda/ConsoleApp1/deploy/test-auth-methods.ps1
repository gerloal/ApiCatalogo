# Script para probar diferentes configuraciones de autenticación
# Uso: .\test-auth-methods.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiEndpoint = "https://q0wnv840ik.execute-api.eu-west-1.amazonaws.com/dev",
    
    [Parameter(Mandatory=$false)]
    [string]$ApiKey = "9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy"
)

Write-Host "==========================================="
Write-Host "Probando diferentes métodos de autenticación"
Write-Host "API Endpoint: $ApiEndpoint"
Write-Host "==========================================="

$testBody = @{
    tenantId = "Sportandem"
    startDate = (Get-Date).AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ")
    endDate = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
    format = "CSV"
} | ConvertTo-Json

# Test 1: Sin autenticación
Write-Host "`n1. Probando sin autenticación..."
try {
    $response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $testBody `
        -ContentType "application/json" `
        -ErrorAction Stop
    Write-Host "   ? Funciona sin autenticación!"
    exit 0
} catch {
    Write-Host "   ? Falla: $($_.Exception.Response.StatusCode.value__) - $($_.Exception.Response.StatusDescription)"
}

# Test 2: Con X-Api-Key (minúsculas con guiones)
Write-Host "`n2. Probando con x-api-key (minúsculas)..."
try {
    $headers = @{
        "x-api-key" = $ApiKey
    }
    $response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $testBody `
        -Headers $headers `
        -ContentType "application/json" `
        -ErrorAction Stop
    Write-Host "   ? Funciona con x-api-key!"
    Write-Host "   Header correcto: x-api-key: $ApiKey"
    exit 0
} catch {
    Write-Host "   ? Falla: $($_.Exception.Response.StatusCode.value__) - $($_.Exception.Response.StatusDescription)"
}

# Test 3: Con X-API-KEY (mayúsculas)
Write-Host "`n3. Probando con X-API-KEY (mayúsculas)..."
try {
    $headers = @{
        "X-API-KEY" = $ApiKey
    }
    $response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $testBody `
        -Headers $headers `
        -ContentType "application/json" `
        -ErrorAction Stop
    Write-Host "   ? Funciona con X-API-KEY!"
    Write-Host "   Header correcto: X-API-KEY: $ApiKey"
    exit 0
} catch {
    Write-Host "   ? Falla: $($_.Exception.Response.StatusCode.value__) - $($_.Exception.Response.StatusDescription)"
}

# Test 4: Con X-Api-Key (PascalCase)
Write-Host "`n4. Probando con X-Api-Key (PascalCase)..."
try {
    $headers = @{
        "X-Api-Key" = $ApiKey
    }
    $response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $testBody `
        -Headers $headers `
        -ContentType "application/json" `
        -ErrorAction Stop
    Write-Host "   ? Funciona con X-Api-Key!"
    Write-Host "   Header correcto: X-Api-Key: $ApiKey"
    exit 0
} catch {
    Write-Host "   ? Falla: $($_.Exception.Response.StatusCode.value__) - $($_.Exception.Response.StatusDescription)"
}

# Test 5: Con Authorization Bearer
Write-Host "`n5. Probando con Authorization: Bearer..."
try {
    $headers = @{
        "Authorization" = "Bearer $ApiKey"
    }
    $response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $testBody `
        -Headers $headers `
        -ContentType "application/json" `
        -ErrorAction Stop
    Write-Host "   ? Funciona con Authorization Bearer!"
    Write-Host "   Header correcto: Authorization: Bearer $ApiKey"
    exit 0
} catch {
    Write-Host "   ? Falla: $($_.Exception.Response.StatusCode.value__) - $($_.Exception.Response.StatusDescription)"
}

# Test 6: Con Authorization (sin Bearer)
Write-Host "`n6. Probando con Authorization (sin Bearer)..."
try {
    $headers = @{
        "Authorization" = $ApiKey
    }
    $response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $testBody `
        -Headers $headers `
        -ContentType "application/json" `
        -ErrorAction Stop
    Write-Host "   ? Funciona con Authorization!"
    Write-Host "   Header correcto: Authorization: $ApiKey"
    exit 0
} catch {
    Write-Host "   ? Falla: $($_.Exception.Response.StatusCode.value__) - $($_.Exception.Response.StatusDescription)"
}

Write-Host "`n==========================================="
Write-Host "? Ningún método de autenticación funcionó"
Write-Host "==========================================="
Write-Host "`n?? Recomendaciones:"
Write-Host "1. Ejecuta: .\diagnose-api-gateway.ps1"
Write-Host "2. Verifica en AWS Console ? API Gateway ? Method Request"
Write-Host "3. Revisa los logs de Lambda:"
Write-Host "   aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1"
