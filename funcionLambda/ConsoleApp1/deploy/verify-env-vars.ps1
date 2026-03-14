# Script para verificar variables de entorno de las funciones Lambda
# Uso: .\verify-env-vars.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1"
)

Write-Host "==========================================="
Write-Host "?? Verificando Variables de Entorno"
Write-Host "Environment: $Environment"
Write-Host "Region: $Region"
Write-Host "==========================================="

$functions = @("OrderExportAPI-$Environment", "OrderExportWorker-$Environment")

foreach ($func in $functions) {
    Write-Host "`n=========================================="
    Write-Host "?? Función: $func"
    Write-Host "==========================================" 
    
    try {
        $config = aws lambda get-function-configuration `
            --function-name $func `
            --region $Region `
            --output json 2>$null | ConvertFrom-Json
        
        if (-not $config) {
            Write-Host "  ? Función no encontrada"
            continue
        }
        
        $vars = $config.Environment.Variables
        
        if (-not $vars) {
            Write-Host "  ??  No hay variables de entorno configuradas"
            continue
        }
        
        Write-Host "`n  Variables configuradas:"
        $vars.PSObject.Properties | ForEach-Object {
            $name = $_.Name
            $value = $_.Value
            
            # Ocultar valores sensibles
            if ($name -match "KEY|SECRET|PASSWORD|TOKEN") {
                if ([string]::IsNullOrEmpty($value)) {
                    Write-Host "    $name = (vacío)" -ForegroundColor Yellow
                } else {
                    Write-Host "    $name = ****** (oculto)" -ForegroundColor Green
                }
            } else {
                if ([string]::IsNullOrEmpty($value)) {
                    Write-Host "    $name = (vacío)" -ForegroundColor Yellow
                } else {
                    Write-Host "    $name = $value" -ForegroundColor Green
                }
            }
        }
        
        # Validar variables requeridas
        Write-Host "`n  Validación:"
        
        if ($func -like "*OrderExportAPI*") {
            $required = @("DYNAMODB_TABLE", "SQS_QUEUE_URL")
            $optional = @("API_KEY")
        } elseif ($func -like "*OrderExportWorker*") {
            $required = @("DYNAMODB_TABLE", "S3_BUCKET")
            $optional = @()
        }
        
        $missing = @()
        foreach ($req in $required) {
            if (-not $vars.$req) {
                $missing += $req
                Write-Host "    ? Falta variable requerida: $req" -ForegroundColor Red
            } else {
                Write-Host "    ? $req configurada" -ForegroundColor Green
            }
        }
        
        foreach ($opt in $optional) {
            if (-not $vars.$opt -or [string]::IsNullOrEmpty($vars.$opt)) {
                Write-Host "    ??  Variable opcional vacía: $opt (acepta todas las peticiones)" -ForegroundColor Yellow
            } else {
                Write-Host "    ? $opt configurada" -ForegroundColor Green
            }
        }
        
        if ($missing.Count -eq 0) {
            Write-Host "`n  ? Todas las variables requeridas están configuradas" -ForegroundColor Green
        } else {
            Write-Host "`n  ? Faltan variables requeridas" -ForegroundColor Red
        }
        
    } catch {
        Write-Host "  ? Error al obtener configuración: $_" -ForegroundColor Red
    }
}

Write-Host "`n==========================================="
Write-Host "? Verificación completada"
Write-Host "==========================================="

Write-Host "`n?? Para más información, consulta:"
Write-Host "   ENVIRONMENT_VARIABLES.md"
