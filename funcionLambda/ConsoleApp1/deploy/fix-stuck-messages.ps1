# Script para liberar mensajes bloqueados en SQS
# Uso: .\fix-stuck-messages.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

Write-Host "==========================================="
Write-Host "?? Liberando Mensajes Bloqueados en SQS"
Write-Host "==========================================="

$AccountId = aws sts get-caller-identity --query Account --output text
$QueueUrl = "https://sqs.$Region.amazonaws.com/$AccountId/order-export-$Environment"

Write-Host "`n?? Cola SQS:"
Write-Host "   URL: $QueueUrl"

# Verificar estado actual
Write-Host "`n1?? Verificando estado actual..."
$queueAttrs = aws sqs get-queue-attributes `
    --queue-url $QueueUrl `
    --attribute-names ApproximateNumberOfMessages,ApproximateNumberOfMessagesNotVisible,VisibilityTimeout `
    --region $Region `
    --output json | ConvertFrom-Json

$messagesAvailable = [int]$queueAttrs.Attributes.ApproximateNumberOfMessages
$messagesInFlight = [int]$queueAttrs.Attributes.ApproximateNumberOfMessagesNotVisible
$visibilityTimeout = [int]$queueAttrs.Attributes.VisibilityTimeout

Write-Host "   Mensajes disponibles: $messagesAvailable"
Write-Host "   Mensajes bloqueados: $messagesInFlight" -ForegroundColor Yellow
Write-Host "   VisibilityTimeout actual: $visibilityTimeout segundos"

if ($messagesInFlight -eq 0) {
    Write-Host "`n? No hay mensajes bloqueados" -ForegroundColor Green
    exit 0
}

# Opciones para liberar mensajes
Write-Host "`n2?? Opciones para liberar los mensajes bloqueados:"
Write-Host "   1. REDUCIR VisibilityTimeout temporalmente (mensajes se liberan en ~1 minuto)"
Write-Host "   2. PURGAR la cola (elimina TODOS los mensajes, incluyendo los bloqueados)"
Write-Host "   3. ESPERAR $([math]::Round($visibilityTimeout/60, 0)) minutos a que expiren naturalmente"
Write-Host "   4. CANCELAR"

$choice = Read-Host "`nSelecciona una opción (1-4)"

switch ($choice) {
    "1" {
        Write-Host "`n?? Reduciendo VisibilityTimeout temporalmente..."
        
        # Guardar timeout original
        Write-Host "   Timeout original: $visibilityTimeout segundos"
        
        # Reducir a 30 segundos temporalmente
        Write-Host "   Reduciendo a 30 segundos..."
        aws sqs set-queue-attributes `
            --queue-url $QueueUrl `
            --attributes "VisibilityTimeout=30" `
            --region $Region
        
        Write-Host "   ? Timeout reducido a 30 segundos"
        Write-Host "`n   ? Esperando 35 segundos para que los mensajes se liberen..."
        Start-Sleep -Seconds 35
        
        # Verificar si se liberaron
        $newAttrs = aws sqs get-queue-attributes `
            --queue-url $QueueUrl `
            --attribute-names ApproximateNumberOfMessages,ApproximateNumberOfMessagesNotVisible `
            --region $Region `
            --output json | ConvertFrom-Json
        
        $newAvailable = [int]$newAttrs.Attributes.ApproximateNumberOfMessages
        $newInFlight = [int]$newAttrs.Attributes.ApproximateNumberOfMessagesNotVisible
        
        Write-Host "`n   ?? Estado después de reducir timeout:"
        Write-Host "      Mensajes disponibles: $newAvailable" -ForegroundColor Green
        Write-Host "      Mensajes bloqueados: $newInFlight"
        
        # Restaurar timeout original
        Write-Host "`n   ?? Restaurando VisibilityTimeout original..."
        aws sqs set-queue-attributes `
            --queue-url $QueueUrl `
            --attributes "VisibilityTimeout=$visibilityTimeout" `
            --region $Region
        
        Write-Host "   ? Timeout restaurado a $visibilityTimeout segundos"
        
        if ($newAvailable -gt 0) {
            Write-Host "`n   ? Mensajes liberados! El Worker debería procesarlos ahora." -ForegroundColor Green
        } else {
            Write-Host "`n   ??  Los mensajes aún no se liberaron. Espera unos minutos más." -ForegroundColor Yellow
        }
    }
    
    "2" {
        Write-Host "`n??  ADVERTENCIA: Esto eliminará TODOS los mensajes de la cola" -ForegroundColor Red
        Write-Host "   Esto incluye:"
        Write-Host "   - $messagesInFlight mensajes bloqueados"
        Write-Host "   - $messagesAvailable mensajes disponibles"
        Write-Host "`n   ¿Estás seguro? (escribe 'PURGAR' para confirmar)"
        
        $confirmation = Read-Host
        
        if ($confirmation -eq "PURGAR") {
            Write-Host "`n   ???  Purgando cola..."
            aws sqs purge-queue --queue-url $QueueUrl --region $Region
            
            Write-Host "   ? Cola purgada exitosamente" -ForegroundColor Green
            Write-Host "`n   ?? Ahora debes crear nuevos jobs de exportación:"
            Write-Host "      .\test-order-export-detailed.ps1"
        } else {
            Write-Host "`n   ? Operación cancelada"
        }
    }
    
    "3" {
        $waitMinutes = [math]::Round($visibilityTimeout/60, 0)
        Write-Host "`n   ? Los mensajes se liberarán automáticamente en ~$waitMinutes minutos"
        Write-Host "   Puedes cerrar este script y volver más tarde"
    }
    
    "4" {
        Write-Host "`n   ? Operación cancelada"
    }
    
    default {
        Write-Host "`n   ? Opción inválida"
    }
}

Write-Host "`n==========================================="
Write-Host "? Proceso completado"
Write-Host "==========================================="

Write-Host "`n?? Próximos pasos:"
Write-Host "   1. Verificar que los mensajes se procesaron:"
Write-Host "      aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region $Region"
Write-Host "`n   2. Ver estado de la cola:"
Write-Host "      aws sqs get-queue-attributes --queue-url $QueueUrl --attribute-names All --region $Region"
Write-Host "`n   3. Verificar jobs en DynamoDB:"
Write-Host "      aws dynamodb scan --table-name OrderExportJobs-dev --region $Region"
