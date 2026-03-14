# Script para crear Log Groups de CloudWatch para Lambda
# Uso: .\create-lambda-log-groups.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

Write-Host "==========================================="
Write-Host "?? Creando Log Groups de CloudWatch"
Write-Host "==========================================="

$functions = @(
    "OrderExportAPI-$Environment",
    "OrderExportWorker-$Environment"
)

foreach ($functionName in $functions) {
    $logGroupName = "/aws/lambda/$functionName"
    
    Write-Host "`n?? Creando log group para: $functionName"
    Write-Host "   Log Group: $logGroupName"
    
    try {
        # Verificar si ya existe
        $exists = aws logs describe-log-groups `
            --log-group-name-prefix $logGroupName `
            --region $Region `
            --output json 2>$null | ConvertFrom-Json
        
        if ($exists.logGroups -and $exists.logGroups.Count -gt 0) {
            Write-Host "   ? Log group ya existe" -ForegroundColor Green
            Write-Host "      Creado: $($exists.logGroups[0].creationTime)"
            Write-Host "      Tamaño: $([math]::Round($exists.logGroups[0].storedBytes / 1KB, 2)) KB"
        } else {
            # Crear el log group
            aws logs create-log-group `
                --log-group-name $logGroupName `
                --region $Region
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "   ? Log group creado exitosamente" -ForegroundColor Green
                
                # Configurar retention de 7 días
                aws logs put-retention-policy `
                    --log-group-name $logGroupName `
                    --retention-in-days 7 `
                    --region $Region
                
                Write-Host "   ?? Retention policy configurado: 7 días"
            } else {
                Write-Host "   ? Error al crear log group" -ForegroundColor Red
            }
        }
        
    } catch {
        Write-Host "   ? Error: $_" -ForegroundColor Red
    }
}

Write-Host "`n==========================================="
Write-Host "? Proceso completado"
Write-Host "==========================================="

Write-Host "`n?? Para ver los logs en tiempo real:"
foreach ($functionName in $functions) {
    Write-Host "   aws logs tail /aws/lambda/$functionName --follow --region $Region"
}
