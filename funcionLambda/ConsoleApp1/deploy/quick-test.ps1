# Script simple para crear un job de exportación
# Ejecutar desde PowerShell

# CONFIGURACIÓN - ACTUALIZA ESTOS VALORES
$ApiEndpoint = "https://TU-API-ID.execute-api.eu-west-1.amazonaws.com/dev"  # ? CAMBIAR
$TenantId = "Sportandem"
$ApiKey = "9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy"

# Fechas (últimos 30 días)
$endDate = Get-Date
$startDate = $endDate.AddDays(-30)
$startDateStr = $startDate.ToString("yyyy-MM-ddTHH:mm:ssZ")
$endDateStr = $endDate.ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "Creando job de exportación para $TenantId..."
Write-Host "Rango: $startDateStr a $endDateStr"

# Body de la petición
$body = @{
    tenantId = $TenantId
    startDate = $startDateStr
    endDate = $endDateStr
    format = "CSV"
} | ConvertTo-Json

# Headers
$headers = @{
    "Content-Type" = "application/json"
    "X-Api-Key" = $ApiKey
}

try {
    # Crear job
    $response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $body `
        -Headers $headers
    
    Write-Host "`n? Job creado:"
    $response | Format-List
    
    $jobId = $response.jobId
    
    # Verificar estado
    Write-Host "`nVerificando estado inicial..."
    $status = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders/${jobId}?tenantId=$TenantId" `
        -Method Get `
        -Headers @{ "X-Api-Key" = $ApiKey }
    
    Write-Host "`n?? Estado del job:"
    $status | Format-List
    
    Write-Host "`n?? Para verificar el estado posteriormente:"
    Write-Host "   `$jobId = '$jobId'"
    Write-Host "   `$status = Invoke-RestMethod -Uri '$ApiEndpoint/exports/orders/${jobId}?tenantId=$TenantId' -Method Get -Headers @{ 'X-Api-Key' = '$ApiKey' }"
    Write-Host "   `$status | Format-List"
    
} catch {
    Write-Host "`n? Error:"
    Write-Host $_.Exception.Message
    
    if ($_.ErrorDetails) {
        Write-Host "`nDetalles:"
        $_.ErrorDetails.Message | ConvertFrom-Json | Format-List
    }
}
