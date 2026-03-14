# Script de prueba para Order Export API
# Uso: .\test-order-export-api.ps1 -ApiEndpoint "https://xxx.execute-api.eu-west-1.amazonaws.com/dev"

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiEndpoint="https://q0wnv840ik.execute-api.eu-west-1.amazonaws.com/dev",
    
    [Parameter(Mandatory=$false)]
    [string]$TenantId = "Sportandem",
    
    [Parameter(Mandatory=$false)]
    [string]$ApiKey = "9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy",
    
    [Parameter(Mandatory=$false)]
    [int]$DaysBack = 30
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================="
Write-Host "Probando Order Export API"
Write-Host "API Endpoint: $ApiEndpoint"
Write-Host "Tenant: $TenantId"
Write-Host "==========================================="

# Calcular fechas
$endDate = Get-Date
$startDate = $endDate.AddDays(-$DaysBack)

# Formatear a ISO 8601
$startDateStr = $startDate.ToString("yyyy-MM-ddTHH:mm:ssZ")
$endDateStr = $endDate.ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "`nFechas de exportación:"
Write-Host "  Start: $startDateStr"
Write-Host "  End:   $endDateStr"

# 1. Crear job de exportación
Write-Host "`n1. Creando job de exportación..."

$body = @{
    TenantId = $TenantId
    StartDate = $startDateStr
    EndDate = $endDateStr
    Format = "CSV"
} | ConvertTo-Json

$headers = @{
    "Content-Type" = "application/json"
    "X-Api-Key" = $ApiKey
}

try {
    $createResponse = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $body `
        -Headers $headers `
        -ContentType "application/json"
    
    Write-Host "✅ Job creado exitosamente!"
    Write-Host "   Job ID: $($createResponse.jobId)"
    Write-Host "   Status: $($createResponse.status)"
    Write-Host "   Message: $($createResponse.message)"
    
    $jobId = $createResponse.jobId
    
} catch {
    Write-Host "❌ Error creando job:"
    Write-Host "   URL llamada: $ApiEndpoint/exports/orders"
    Write-Host "   Método: POST"
    Write-Host ""
    Write-Host "   Status Code: $($_.Exception.Response.StatusCode.value__)"
    Write-Host "   Status Description: $($_.Exception.Response.StatusDescription)"
    Write-Host "   Error Message: $($_.Exception.Message)"
    Write-Host ""
    
    # Mostrar headers enviados
    Write-Host "   Headers enviados:"
    $headers.GetEnumerator() | ForEach-Object {
        if ($_.Key -match "Key|Token|Auth") {
            Write-Host "      $($_.Key): ******"
        } else {
            Write-Host "      $($_.Key): $($_.Value)"
        }
    }
    Write-Host ""
    
    # Mostrar body enviado
    Write-Host "   Body enviado:"
    Write-Host "      $body"
    Write-Host ""
    
    if ($_.ErrorDetails.Message) {
        Write-Host "   Detalles del error de la API:"
        try {
            $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
            $errorDetails | Format-List
        } catch {
            Write-Host "      $($_.ErrorDetails.Message)"
        }
    }
    
    # Sugerencias basadas en el código de error
    Write-Host "   💡 Posibles causas:"
    switch ($_.Exception.Response.StatusCode.value__) {
        401 { Write-Host "      - Token de autenticación inválido o faltante" 
              Write-Host "      - Ejecuta .\diagnose-api-gateway.ps1 para ver qué tipo de auth requiere" }
        403 { Write-Host "      - API Key inválida o no configurada correctamente"
              Write-Host "      - Verifica que el API Key esté asociado al Usage Plan" }
        404 { Write-Host "      - Endpoint no existe o ruta incorrecta"
              Write-Host "      - Verifica: $ApiEndpoint/exports/orders" }
        500 { Write-Host "      - Error interno en Lambda"
              Write-Host "      - Ver logs: aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1" }
    }
    
    exit 1
}

# 2. Monitorear estado del job
Write-Host "`n2. Monitoreando estado del job..."
Write-Host "   Esperando a que complete (esto puede tardar varios minutos)..."

$maxAttempts = 60  # 60 intentos = 10 minutos
$attempt = 0
$completed = $false

$statusHeaders = @{
    "X-Api-Key" = $ApiKey
}

while (-not $completed -and $attempt -lt $maxAttempts) {
    $attempt++
    Start-Sleep -Seconds 10
    
    try {
        $statusResponse = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders/${jobId}?tenantId=$TenantId" `
            -Method Get `
            -Headers $statusHeaders
        
        $status = $statusResponse.status
        
        Write-Host "   [Intento $attempt] Status: $status"
        
        if ($status -eq "DONE") {
            $completed = $true
            
            Write-Host "`n✅ Exportación completada!"
            Write-Host "`nResultados:"
            Write-Host "  - Total Pedidos: $($statusResponse.totalOrders)"
            Write-Host "  - Total Líneas: $($statusResponse.totalLines)"
            Write-Host "  - Creado: $($statusResponse.createdAt)"
            Write-Host "  - Actualizado: $($statusResponse.updatedAt)"
            
            # 3. Descargar archivos CSV
            Write-Host "`n3. Descargando archivos CSV..."
            
            $outputDir = Join-Path $PSScriptRoot "exports"
            if (-not (Test-Path $outputDir)) {
                New-Item -Path $outputDir -ItemType Directory | Out-Null
            }
            
            $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
            $headersFile = Join-Path $outputDir "${TenantId}_headers_${timestamp}.csv"
            $linesFile = Join-Path $outputDir "${TenantId}_lines_${timestamp}.csv"
            
            # Descargar headers
            if ($statusResponse.headersPresignedUrl) {
                Write-Host "   Descargando headers..."
                Invoke-WebRequest -Uri $statusResponse.headersPresignedUrl -OutFile $headersFile
                Write-Host "   ✅ Headers guardado: $headersFile"
            } else {
                Write-Host "   ⚠️  No hay URL de headers disponible"
            }
            
            # Descargar lines
            if ($statusResponse.linesPresignedUrl) {
                Write-Host "   Descargando lines..."
                Invoke-WebRequest -Uri $statusResponse.linesPresignedUrl -OutFile $linesFile
                Write-Host "   ✅ Lines guardado: $linesFile"
            } else {
                Write-Host "   ⚠️  No hay URL de lines disponible"
            }
            
            # Mostrar preview de los archivos
            if (Test-Path $headersFile) {
                Write-Host "`n📄 Preview de Headers (primeras 5 líneas):"
                Get-Content $headersFile -First 5 | ForEach-Object { Write-Host "   $_" }
            }
            
            if (Test-Path $linesFile) {
                Write-Host "`n📄 Preview de Lines (primeras 5 líneas):"
                Get-Content $linesFile -First 5 | ForEach-Object { Write-Host "   $_" }
            }
            
        } elseif ($status -eq "FAILED") {
            Write-Host "`n❌ Exportación falló!"
            Write-Host "   Error: $($statusResponse.errorMessage)"
            exit 1
            
        } elseif ($status -eq "PENDING" -or $status -eq "RUNNING") {
            # Continuar esperando
        } else {
            Write-Host "   ⚠️  Estado desconocido: $status"
        }
        
    } catch {
        Write-Host "   ⚠️  Error verificando estado: $($_.Exception.Message)"
    }
}

if (-not $completed) {
    Write-Host "`n⚠️  Timeout esperando completar el job"
    Write-Host "   El job sigue en proceso, puedes verificar manualmente:"
    Write-Host "   Job ID: $jobId"
    Write-Host "   URL: $ApiEndpoint/exports/orders/${jobId}?tenantId=$TenantId"
    exit 1
}

Write-Host "`n==========================================="
Write-Host "✅ Prueba completada exitosamente!"
Write-Host "==========================================="
