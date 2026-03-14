# Script rápido para actualizar SOLO los handlers (sin redesplegar código)
# Uso: .\update-handlers-only.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

Write-Host "==========================================="
Write-Host "?? Actualizando Handlers (solo configuración)"
Write-Host "Region: $Region"
Write-Host "Environment: $Environment"
Write-Host "==========================================="

# Handlers correctos usando ProcessFileOnQueue
$handlers = @{
    "OrderExportAPI-$Environment" = "ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler"
    "OrderExportWorker-$Environment" = "ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler"
}

foreach ($function in $handlers.GetEnumerator()) {
    $functionName = $function.Key
    $handler = $function.Value
    
    Write-Host "`n?? $functionName"
    Write-Host "   Handler: $handler"
    
    try {
        aws lambda update-function-configuration `
            --function-name $functionName `
            --handler $handler `
            --region $Region `
            --no-cli-pager | Out-Null
        
        Write-Host "   ? Actualizado"
    }
    catch {
        Write-Host "   ? Error: $_"
    }
}

Write-Host "`n==========================================="
Write-Host "? Handlers actualizados"
Write-Host "==========================================="

Write-Host "`n?? Verificando configuración..."
foreach ($function in $handlers.GetEnumerator()) {
    $config = aws lambda get-function-configuration `
        --function-name $function.Key `
        --region $Region `
        --query '{Handler: Handler, Runtime: Runtime}' `
        --output json 2>$null | ConvertFrom-Json
    
    if ($config) {
        Write-Host "`n$($function.Key):"
        Write-Host "   Handler: $($config.Handler)"
        Write-Host "   Runtime: $($config.Runtime)"
        
        if ($config.Handler -eq $function.Value) {
            Write-Host "   ? Correcto"
        } else {
            Write-Host "   ? Incorrecto (esperado: $($function.Value))"
        }
    }
}

Write-Host "`n?? Ahora puedes probar la API:"
Write-Host "   .\test-order-export-api.ps1"
