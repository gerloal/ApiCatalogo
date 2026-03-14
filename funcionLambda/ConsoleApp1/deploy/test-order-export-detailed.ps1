# Script de prueba completo con diagnóstico detallado
# Uso: .\test-order-export-detailed.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$ApiEndpoint="https://q0wnv840ik.execute-api.eu-west-1.amazonaws.com/dev",
    
    [Parameter(Mandatory=$false)]
    [string]$TenantId = "Sportandem",
    
    [Parameter(Mandatory=$false)]
    [string]$ApiKey = "9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy",
    
    [Parameter(Mandatory=$false)]
    [int]$DaysBack = 30,
    
    [Parameter(Mandatory=$false)]
    [switch]$UseCamelCase = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================="
Write-Host "?? Test Detallado - Order Export API"
Write-Host "==========================================="
Write-Host "API Endpoint: $ApiEndpoint"
Write-Host "Tenant: $TenantId"
Write-Host "Formato JSON: $(if ($UseCamelCase) { 'camelCase' } else { 'PascalCase' })"
Write-Host "==========================================="

# Calcular fechas
$endDate = Get-Date
$startDate = $endDate.AddDays(-$DaysBack)

# Formatear a ISO 8601
$startDateStr = $startDate.ToString("yyyy-MM-ddTHH:mm:ssZ")
$endDateStr = $endDate.ToString("yyyy-MM-ddTHH:mm:ssZ")

Write-Host "`n?? Fechas de exportación:"
Write-Host "   Start: $startDateStr"
Write-Host "   End:   $endDateStr"

# Preparar body según formato
if ($UseCamelCase) {
    $bodyObject = @{
        tenantId = $TenantId
        startDate = $startDateStr
        endDate = $endDateStr
        format = "CSV"
    }
} else {
    $bodyObject = @{
        TenantId = $TenantId
        StartDate = $startDateStr
        EndDate = $endDateStr
        Format = "CSV"
    }
}

$body = $bodyObject | ConvertTo-Json

Write-Host "`n?? Request Body:"
Write-Host $body

$headers = @{
    "Content-Type" = "application/json"
    "X-Api-Key" = $ApiKey
}

# Test 1: Crear job de exportación
Write-Host "`n" + ("=" * 50)
Write-Host "TEST 1: Crear Job de Exportación"
Write-Host ("=" * 50)

try {
    Write-Host "`n?? Enviando POST a: $ApiEndpoint/exports/orders"
    
    $createResponse = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
        -Method Post `
        -Body $body `
        -Headers $headers `
        -ContentType "application/json" `
        -Verbose
    
    Write-Host "`n? Job creado exitosamente!" -ForegroundColor Green
    Write-Host "   Job ID: $($createResponse.jobId)" -ForegroundColor Cyan
    Write-Host "   Status: $($createResponse.status)" -ForegroundColor Yellow
    Write-Host "   Message: $($createResponse.message)"
    
    $jobId = $createResponse.jobId
    
} catch {
    Write-Host "`n? ERROR al crear job" -ForegroundColor Red
    Write-Host ""
    
    # Información de la petición
    Write-Host "?? Información de la Petición:" -ForegroundColor Yellow
    Write-Host "   URL: $ApiEndpoint/exports/orders"
    Write-Host "   Método: POST"
    Write-Host "   Content-Type: application/json"
    Write-Host ""
    
    # Status Code
    if ($_.Exception.Response) {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $statusDescription = $_.Exception.Response.StatusDescription
        Write-Host "?? Status Code: $statusCode - $statusDescription" -ForegroundColor Red
    }
    Write-Host ""
    
    # Headers enviados
    Write-Host "?? Headers Enviados:" -ForegroundColor Yellow
    $headers.GetEnumerator() | ForEach-Object {
        if ($_.Key -match "Key|Token|Auth") {
            Write-Host "   $($_.Key): ******"
        } else {
            Write-Host "   $($_.Key): $($_.Value)"
        }
    }
    Write-Host ""
    
    # Body enviado
    Write-Host "?? Body Enviado:" -ForegroundColor Yellow
    Write-Host $body
    Write-Host ""
    
    # Respuesta de error de la API
    if ($_.ErrorDetails.Message) {
        Write-Host "?? Respuesta de Error:" -ForegroundColor Yellow
        try {
            $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
            Write-Host "   Error: $($errorDetails.error)" -ForegroundColor Red
            Write-Host "   Message: $($errorDetails.message)" -ForegroundColor Red
        } catch {
            Write-Host "   $($_.ErrorDetails.Message)"
        }
        Write-Host ""
    }
    
    # Sugerencias
    Write-Host "?? Sugerencias:" -ForegroundColor Cyan
    if ($statusCode -eq 400) {
        Write-Host "   - Verifica que el formato JSON sea correcto"
        Write-Host "   - Prueba con formato PascalCase: .\test-order-export-detailed.ps1"
        Write-Host "   - O con camelCase: .\test-order-export-detailed.ps1 -UseCamelCase"
    } elseif ($statusCode -eq 401) {
        Write-Host "   - Verifica que la API Key sea correcta"
        Write-Host "   - Verifica las variables de entorno de la Lambda"
    } elseif ($statusCode -eq 403) {
        Write-Host "   - Verifica los permisos del role de Lambda"
        Write-Host "   - Verifica la configuración de API Gateway"
    }
    
    exit 1
}

# Test 2: Consultar estado del job
if ($jobId) {
    Write-Host "`n" + ("=" * 50)
    Write-Host "TEST 2: Consultar Estado del Job"
    Write-Host ("=" * 50)
    
    Write-Host "`n? Esperando 2 segundos antes de consultar..."
    Start-Sleep -Seconds 2
    
    try {
        Write-Host "`n?? Enviando GET a: $ApiEndpoint/exports/orders/$jobId"
        
        # Agregar X-Tenant-Id al header
        $getHeaders = $headers.Clone()
        $getHeaders["X-Tenant-Id"] = $TenantId
        
        $statusResponse = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders/$jobId" `
            -Method Get `
            -Headers $getHeaders
        
        Write-Host "`n? Estado obtenido exitosamente!" -ForegroundColor Green
        Write-Host "   Job ID: $($statusResponse.jobId)" -ForegroundColor Cyan
        Write-Host "   Tenant ID: $($statusResponse.tenantId)"
        Write-Host "   Status: $($statusResponse.status)" -ForegroundColor Yellow
        Write-Host "   Created: $($statusResponse.createdAt)"
        
        if ($statusResponse.completedAt) {
            Write-Host "   Completed: $($statusResponse.completedAt)" -ForegroundColor Green
        }
        
        if ($statusResponse.s3Key) {
            Write-Host "   S3 Key: $($statusResponse.s3Key)" -ForegroundColor Cyan
        }
        
        if ($statusResponse.errorMessage) {
            Write-Host "   Error: $($statusResponse.errorMessage)" -ForegroundColor Red
        }
        
    } catch {
        Write-Host "`n? ERROR al consultar estado" -ForegroundColor Red
        Write-Host "   $($_.Exception.Message)"
        
        if ($_.ErrorDetails.Message) {
            try {
                $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
                Write-Host "   Error: $($errorDetails.error)" -ForegroundColor Red
                Write-Host "   Message: $($errorDetails.message)" -ForegroundColor Red
            } catch {
                Write-Host "   $($_.ErrorDetails.Message)"
            }
        }
    }
}

Write-Host "`n" + ("=" * 50)
Write-Host "? Tests Completados"
Write-Host ("=" * 50)

Write-Host "`n?? Próximos pasos:"
Write-Host "   1. Verificar logs de CloudWatch:"
Write-Host "      aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1"
Write-Host ""
Write-Host "   2. Verificar mensajes en SQS:"
Write-Host "      aws sqs receive-message --queue-url <QUEUE_URL> --region eu-west-1"
Write-Host ""
Write-Host "   3. Verificar tabla DynamoDB:"
Write-Host "      aws dynamodb scan --table-name OrderExportJobs-dev --region eu-west-1"
