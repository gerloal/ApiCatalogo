# Script para actualizar el visibility timeout de la cola SQS
# Uso: .\fix-sqs-timeout.ps1 -Environment dev -Region eu-west-1

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1"
)

Write-Host "==========================================="
Write-Host "Actualizando SQS Queue Visibility Timeout"
Write-Host "Environment: $Environment"
Write-Host "Region: $Region"
Write-Host "==========================================="

# Obtener Account ID
$AccountId = (aws sts get-caller-identity --query Account --output text)
$QueueName = "order-export-$Environment"
$QueueUrl = "https://sqs.$Region.amazonaws.com/$AccountId/$QueueName"

Write-Host "`nQueue URL: $QueueUrl"

# Lambda Worker timeout es 900 segundos (15 minutos)
# Visibility timeout debe ser al menos 6x mayor = 5400 segundos (90 minutos)
$VisibilityTimeout = 5400

Write-Host "`nActualizando Visibility Timeout a $VisibilityTimeout segundos (90 minutos)..."

try {
    aws sqs set-queue-attributes `
        --queue-url $QueueUrl `
        --attributes "VisibilityTimeout=$VisibilityTimeout" `
        --region $Region `
        --no-cli-pager
    
    Write-Host "? Visibility Timeout actualizado exitosamente"
    
    # Verificar
    Write-Host "`nVerificando configuración..."
    $attributes = aws sqs get-queue-attributes `
        --queue-url $QueueUrl `
        --attribute-names All `
        --region $Region `
        --query 'Attributes' `
        --output json | ConvertFrom-Json
    
    Write-Host "`nConfiguración actual de la cola:"
    Write-Host "  - Visibility Timeout: $($attributes.VisibilityTimeout) segundos"
    Write-Host "  - Message Retention: $($attributes.MessageRetentionPeriod) segundos"
    Write-Host "  - Max Receives: $($attributes.RedrivePolicy | ConvertFrom-Json | Select-Object -ExpandProperty maxReceiveCount)"
    
} catch {
    Write-Host "? Error actualizando visibility timeout: $_"
    exit 1
}

Write-Host "`n==========================================="
Write-Host "? Actualización completada"
Write-Host "==========================================="
Write-Host "`nAhora puedes ejecutar deploy-lambdas.ps1 sin errores"
