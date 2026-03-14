# Script para corregir el handler de las funciones Lambda
# Uso: .\fix-lambda-handler.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

Write-Host "==========================================="
Write-Host "?? Corrigiendo Handler de Lambda Functions"
Write-Host "==========================================="

# Función para actualizar handler
function Update-LambdaHandler {
    param(
        [string]$FunctionName,
        [string]$Handler
    )
    
    Write-Host "`n?? Actualizando: $FunctionName"
    Write-Host "   Handler: $Handler"
    
    try {
        # Verificar si la función existe
        $exists = aws lambda get-function `
            --function-name $FunctionName `
            --region $Region 2>$null
        
        if (-not $exists) {
            Write-Host "   ??  Función no existe, saltando..."
            return $null
        }
        
        # Actualizar handler
        aws lambda update-function-configuration `
            --function-name $FunctionName `
            --handler $Handler `
            --region $Region `
            --output json | Out-Null
        
        Write-Host "   ? Handler actualizado correctamente"
        return $true
    }
    catch {
        Write-Host "   ? Error: $_"
        return $false
    }
}

# Definir handlers correctos (usando ProcessFileOnQueue como AssemblyName)
$functions = @{
    "OrderExportAPI-$Environment" = "ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler"
    "OrderExportWorker-$Environment" = "ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler"
    "UnifiedSQSHandler" = "ProcessFileOnQueue::FuncionLambda.UnifiedSQSHandler::FunctionHandler"
}

$successCount = 0
$failCount = 0
$skippedCount = 0

foreach ($func in $functions.GetEnumerator()) {
    $result = Update-LambdaHandler -FunctionName $func.Key -Handler $func.Value
    
    if ($result -eq $true) {
        $successCount++
    }
    elseif ($result -eq $false) {
        $failCount++
    }
    else {
        $skippedCount++
    }
}

Write-Host "`n==========================================="
Write-Host "?? Resumen:"
Write-Host "   ? Actualizadas: $successCount"
Write-Host "   ? Fallidas: $failCount"
Write-Host "   ??  Saltadas: $skippedCount"
Write-Host "==========================================="

# Verificar configuración final
Write-Host "`n?? Verificando configuración actual..."
foreach ($func in $functions.GetEnumerator()) {
    try {
        $config = aws lambda get-function-configuration `
            --function-name $func.Key `
            --region $Region `
            --query '{Handler: Handler, Runtime: Runtime, Timeout: Timeout}' `
            --output json 2>$null | ConvertFrom-Json
        
        if ($config) {
            Write-Host "`n$($func.Key):"
            Write-Host "   Handler: $($config.Handler)"
            Write-Host "   Runtime: $($config.Runtime)"
            Write-Host "   Timeout: $($config.Timeout)s"
        }
    }
    catch {
        # Silenciar errores de funciones que no existen
    }
}

if ($failCount -eq 0) {
    Write-Host "`n?? Todos los handlers actualizados correctamente"
    Write-Host "`n?? Ahora puedes probar la API:"
    Write-Host "   .\test-order-export-api.ps1"
}
else {
    Write-Host "`n??  Algunas funciones fallaron. Verifica los logs arriba."
}
