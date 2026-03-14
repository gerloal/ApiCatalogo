# Script para diagnosticar y corregir problemas con el Worker Lambda
# Uso: .\diagnose-worker.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

Write-Host "==========================================="
Write-Host "?? Diagnóstico del Worker Lambda"
Write-Host "==========================================="

$AccountId = aws sts get-caller-identity --query Account --output text
$QueueUrl = "https://sqs.$Region.amazonaws.com/$AccountId/order-export-$Environment"
$FunctionName = "OrderExportWorker-$Environment"

Write-Host "`n?? Configuración:"
Write-Host "   Queue URL: $QueueUrl"
Write-Host "   Function: $FunctionName"
Write-Host "   Region: $Region"

# 1. Verificar estado de la cola SQS
Write-Host "`n1?? Verificando cola SQS..."
$queueAttrs = aws sqs get-queue-attributes `
    --queue-url $QueueUrl `
    --attribute-names All `
    --region $Region `
    --output json | ConvertFrom-Json

$visibilityTimeout = [int]$queueAttrs.Attributes.VisibilityTimeout
$messagesAvailable = [int]$queueAttrs.Attributes.ApproximateNumberOfMessages
$messagesInFlight = [int]$queueAttrs.Attributes.ApproximateNumberOfMessagesNotVisible
$messagesDelayed = [int]$queueAttrs.Attributes.ApproximateNumberOfMessagesDelayed

Write-Host "   VisibilityTimeout: $visibilityTimeout segundos ($([math]::Round($visibilityTimeout/60, 1)) minutos)"
Write-Host "   Mensajes disponibles: $messagesAvailable"
Write-Host "   Mensajes en proceso: $messagesInFlight" -ForegroundColor $(if ($messagesInFlight -gt 0) { "Yellow" } else { "Green" })
Write-Host "   Mensajes retrasados: $messagesDelayed"

# 2. Verificar configuración de Lambda
Write-Host "`n2?? Verificando función Lambda..."
$lambdaConfig = aws lambda get-function-configuration `
    --function-name $FunctionName `
    --region $Region `
    --output json | ConvertFrom-Json

$lambdaTimeout = $lambdaConfig.Timeout
$lambdaMemory = $lambdaConfig.MemorySize
$lambdaState = $lambdaConfig.State

Write-Host "   Timeout: $lambdaTimeout segundos ($([math]::Round($lambdaTimeout/60, 1)) minutos)"
Write-Host "   Memory: $lambdaMemory MB"
Write-Host "   State: $lambdaState" -ForegroundColor $(if ($lambdaState -eq "Active") { "Green" } else { "Red" })

# 3. Verificar Event Source Mapping
Write-Host "`n3?? Verificando Event Source Mapping..."
$mappings = aws lambda list-event-source-mappings `
    --function-name $FunctionName `
    --region $Region `
    --output json | ConvertFrom-Json

if ($mappings.EventSourceMappings.Count -eq 0) {
    Write-Host "   ? No hay Event Source Mapping configurado" -ForegroundColor Red
} else {
    $mapping = $mappings.EventSourceMappings[0]
    Write-Host "   UUID: $($mapping.UUID)"
    Write-Host "   State: $($mapping.State)" -ForegroundColor $(if ($mapping.State -eq "Enabled") { "Green" } else { "Red" })
    Write-Host "   BatchSize: $($mapping.BatchSize)"
    
    if ($mapping.LastProcessingResult) {
        Write-Host "   Last Result: $($mapping.LastProcessingResult)" -ForegroundColor Yellow
    }
}

# 4. Verificar logs recientes
Write-Host "`n4?? Verificando logs recientes..."
try {
    $logs = aws logs tail "/aws/lambda/$FunctionName" `
        --since 1h `
        --region $Region 2>$null
    
    if ($logs) {
        Write-Host "   ? Hay logs recientes" -ForegroundColor Green
        Write-Host "`n   Últimas líneas:"
        $logs | Select-Object -Last 5 | ForEach-Object { Write-Host "      $_" }
    } else {
        Write-Host "   ??  No hay logs en la última hora" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ? No se pudieron leer los logs (probablemente el Worker nunca se ejecutó)" -ForegroundColor Red
}

# 5. Análisis de problemas
Write-Host "`n5?? Análisis de problemas..."

$issues = @()

# Verificar VisibilityTimeout vs Lambda Timeout
if ($visibilityTimeout -lt ($lambdaTimeout * 6)) {
    $recommendedTimeout = $lambdaTimeout * 6
    $issues += @{
        Problem = "VisibilityTimeout muy bajo"
        Detail = "Actual: $visibilityTimeout seg, Lambda timeout: $lambdaTimeout seg"
        Recommendation = "Aumentar VisibilityTimeout a $recommendedTimeout segundos (6x Lambda timeout)"
        Severity = "HIGH"
    }
}

# Verificar mensajes en proceso
if ($messagesInFlight -gt 0 -and $messagesAvailable -eq 0) {
    $issues += @{
        Problem = "Mensajes bloqueados en proceso"
        Detail = "$messagesInFlight mensajes están en VisibilityTimeout"
        Recommendation = "Esperar $([math]::Round(($visibilityTimeout - 1800)/60, 0)) minutos o reducir VisibilityTimeout"
        Severity = "MEDIUM"
    }
}

# Verificar si Lambda está activa
if ($lambdaState -ne "Active") {
    $issues += @{
        Problem = "Lambda no está activa"
        Detail = "State: $lambdaState"
        Recommendation = "Esperar a que Lambda termine de actualizarse"
        Severity = "HIGH"
    }
}

# Verificar Event Source Mapping
if ($mappings.EventSourceMappings.Count -eq 0) {
    $issues += @{
        Problem = "No hay Event Source Mapping"
        Detail = "La Lambda no está conectada a SQS"
        Recommendation = "Ejecutar: .\deploy-lambdas.ps1 para configurar el trigger"
        Severity = "CRITICAL"
    }
} elseif ($mapping.State -ne "Enabled") {
    $issues += @{
        Problem = "Event Source Mapping deshabilitado"
        Detail = "State: $($mapping.State)"
        Recommendation = "Habilitar el mapping manualmente"
        Severity = "CRITICAL"
    }
}

if ($issues.Count -eq 0) {
    Write-Host "   ? No se detectaron problemas de configuración" -ForegroundColor Green
} else {
    foreach ($issue in $issues) {
        $color = switch ($issue.Severity) {
            "CRITICAL" { "Red" }
            "HIGH" { "Red" }
            "MEDIUM" { "Yellow" }
            default { "White" }
        }
        
        Write-Host "`n   [$($issue.Severity)] $($issue.Problem)" -ForegroundColor $color
        Write-Host "      Detalle: $($issue.Detail)"
        Write-Host "      Recomendación: $($issue.Recommendation)" -ForegroundColor Cyan
    }
}

# 6. Soluciones automáticas
Write-Host "`n6?? Soluciones disponibles..."

if ($visibilityTimeout -lt ($lambdaTimeout * 6)) {
    Write-Host "`n   ?? Corregir VisibilityTimeout automáticamente?"
    Write-Host "      Presiona 'Y' para aplicar el fix, o cualquier otra tecla para saltar..."
    $response = Read-Host
    
    if ($response -eq 'Y' -or $response -eq 'y') {
        $newTimeout = $lambdaTimeout * 6
        Write-Host "      Actualizando VisibilityTimeout a $newTimeout segundos..."
        
        aws sqs set-queue-attributes `
            --queue-url $QueueUrl `
            --attributes "VisibilityTimeout=$newTimeout" `
            --region $Region
        
        Write-Host "      ? VisibilityTimeout actualizado" -ForegroundColor Green
        
        if ($messagesInFlight -gt 0) {
            Write-Host "`n      ??  Hay mensajes bloqueados. Opciones:"
            Write-Host "         1. Esperar a que expiren ($([math]::Round($visibilityTimeout/60, 0)) minutos)"
            Write-Host "         2. Purgar la cola (elimina todos los mensajes)"
            Write-Host "`n      ¿Purgar la cola? (Y/N)"
            $purgeResponse = Read-Host
            
            if ($purgeResponse -eq 'Y' -or $purgeResponse -eq 'y') {
                Write-Host "      Purgando cola..."
                aws sqs purge-queue --queue-url $QueueUrl --region $Region
                Write-Host "      ? Cola purgada. Vuelve a crear los jobs." -ForegroundColor Green
            }
        }
    }
}

Write-Host "`n==========================================="
Write-Host "? Diagnóstico completado"
Write-Host "==========================================="

Write-Host "`n?? Próximos pasos:"
if ($issues.Count -gt 0) {
    Write-Host "   1. Revisar los problemas detectados arriba"
    Write-Host "   2. Aplicar las recomendaciones"
    Write-Host "   3. Volver a probar: .\test-order-export-detailed.ps1"
} else {
    Write-Host "   1. Verificar manualmente los logs: aws logs tail /aws/lambda/$FunctionName --follow --region $Region"
    Write-Host "   2. Crear un nuevo job de prueba: .\test-order-export-detailed.ps1"
}
