# Script para forzar la creación del log group invocando la Lambda
# Uso: .\force-lambda-execution.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionName = "OrderExportWorker-dev"
)

Write-Host "==========================================="
Write-Host "?? Forzando Ejecución de Lambda"
Write-Host "==========================================="

Write-Host "`nFunción: $FunctionName"
Write-Host "Región: $Region"

# Crear evento de test mínimo
$testEvent = @{
    Records = @(
        @{
            messageId = "force-log-creation"
            receiptHandle = "test"
            body = @{
                JobId = "test-job"
                TenantId = "TestTenant"
                StartDate = "2024-01-01T00:00:00Z"
                EndDate = "2024-01-31T23:59:59Z"
            } | ConvertTo-Json -Compress
            attributes = @{
                ApproximateReceiveCount = "1"
            }
            messageAttributes = @{}
            md5OfBody = "test"
            eventSource = "aws:sqs"
            eventSourceARN = "arn:aws:sqs:${Region}:test:test"
            awsRegion = $Region
        }
    )
} | ConvertTo-Json -Depth 10

# Guardar evento
$eventFile = "force-execution-event.json"
$testEvent | Out-File -FilePath $eventFile -Encoding UTF8

Write-Host "`n?? Invocando función Lambda..."
Write-Host "   (Esta invocación creará el log group automáticamente)"

try {
    $result = aws lambda invoke `
        --function-name $FunctionName `
        --cli-binary-format raw-in-base64-out `
        --payload file://$eventFile `
        --region $Region `
        response-force.json 2>&1
    
    Write-Host "`n? Función invocada (probablemente falló, pero eso es esperado)"
    Write-Host "   El objetivo era crear el log group, no ejecutar exitosamente"
    
    # Verificar si el log group se creó
    Start-Sleep -Seconds 2
    
    $logGroupName = "/aws/lambda/$FunctionName"
    $exists = aws logs describe-log-groups `
        --log-group-name-prefix $logGroupName `
        --region $Region `
        --output json 2>$null | ConvertFrom-Json
    
    if ($exists.logGroups -and $exists.logGroups.Count -gt 0) {
        Write-Host "`n? Log group creado exitosamente:" -ForegroundColor Green
        Write-Host "   $logGroupName"
        
        # Mostrar primeros logs
        Write-Host "`n?? Primeros logs:"
        aws logs tail $logGroupName --since 1m --region $Region
    } else {
        Write-Host "`n??  Log group aún no existe" -ForegroundColor Yellow
        Write-Host "   Puede tardar unos segundos, intenta de nuevo"
    }
    
} catch {
    Write-Host "`n??  Error al invocar (esto es normal si falta el secret)" -ForegroundColor Yellow
    Write-Host "   $_"
}

# Limpiar
if (Test-Path $eventFile) { Remove-Item $eventFile }
if (Test-Path response-force.json) { Remove-Item response-force.json }

Write-Host "`n==========================================="
Write-Host "? Proceso completado"
Write-Host "==========================================="

Write-Host "`n?? Para verificar logs:"
Write-Host "   aws logs tail /aws/lambda/$FunctionName --follow --region $Region"
