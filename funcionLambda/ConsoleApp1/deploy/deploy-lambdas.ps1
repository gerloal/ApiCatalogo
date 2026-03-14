# Script PowerShell para desplegar funciones Lambda
# Uso: .\deploy-lambdas.ps1 -Environment dev -Region eu-west-1 -RoleArn "arn:aws:iam::123456789:role/YourRole"

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$true)]
    [string]$RoleArn
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================="
Write-Host "Desplegando funciones Lambda"
Write-Host "Environment: $Environment"
Write-Host "Region: $Region"
Write-Host "Role ARN: $RoleArn"
Write-Host "==========================================="

# Obtener Account ID
$AccountId = (aws sts get-caller-identity --query Account --output text)
$QueueUrl = "https://sqs.$Region.amazonaws.com/$AccountId/order-export-$Environment"

# Rutas - CORREGIDAS
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir  # Directorio del proyecto (un nivel arriba de deploy/)
$PublishDir = Join-Path $ProjectDir "publish"
$PackagePath = Join-Path $ProjectDir "lambda-package.zip"

Write-Host "`nDirectorios:"
Write-Host "  Script: $ScriptDir"
Write-Host "  Project: $ProjectDir"
Write-Host "  Publish: $PublishDir"
Write-Host "  Package: $PackagePath"

# 1. Compilar proyecto
Write-Host "`n1. Compilando proyecto .NET..."
Set-Location $ProjectDir

dotnet clean
if ($LASTEXITCODE -ne 0) { throw "Error en dotnet clean" }

dotnet restore
if ($LASTEXITCODE -ne 0) { throw "Error en dotnet restore" }

dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw "Error en dotnet build" }

# 2. Publicar para Lambda
Write-Host "`n2. Publicando para Lambda (linux-arm64)..."
dotnet publish -c Release -r linux-arm64 --self-contained false -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "Error en dotnet publish" }

# Verificar que publish existe
if (-not (Test-Path $PublishDir)) {
    throw "Error: El directorio publish no fue creado en: $PublishDir"
}

Write-Host "✅ Directorio publish creado correctamente en: $PublishDir"

# 3. Crear paquete ZIP
Write-Host "`n3. Creando paquete ZIP..."
if (Test-Path $PackagePath) {
    Remove-Item $PackagePath -Force
}

# Comprimir usando .NET
Add-Type -Assembly System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PublishDir, $PackagePath)

$PackageSize = (Get-Item $PackagePath).Length / 1MB
Write-Host "✅ Paquete creado: $PackagePath ($([math]::Round($PackageSize, 2)) MB)"

# 4. Desplegar Lambda: OrderExportAPI
Write-Host "`n4. Desplegando Lambda: OrderExportAPI-$Environment..."

$ApiFunctionName = "OrderExportAPI-$Environment"
$FunctionExists = $false

try {
    aws lambda get-function --function-name $ApiFunctionName --region $Region --no-cli-pager | Out-Null
    $FunctionExists = $true
} catch {
    $FunctionExists = $false
}

if ($FunctionExists) {
    Write-Host "Actualizando función existente..."
    aws lambda update-function-code `
        --function-name $ApiFunctionName `
        --zip-file "fileb://$PackagePath" `
        --region $Region `
        --no-cli-pager
    
    if ($LASTEXITCODE -ne 0) { throw "Error actualizando código de $ApiFunctionName" }
    
    # Esperar a que esté lista
    Write-Host "Esperando a que la función esté lista..."
    aws lambda wait function-updated --function-name $ApiFunctionName --region $Region
    
    # Actualizar configuración
    $EnvVars = "Variables={DYNAMODB_TABLE=OrderExportJobs-$Environment,SQS_QUEUE_URL=$QueueUrl,API_KEY=}"
    aws lambda update-function-configuration `
        --function-name $ApiFunctionName `
        --timeout 30 `
        --memory-size 512 `
        --environment $EnvVars `
        --region $Region `
        --no-cli-pager
} else {
    Write-Host "Creando nueva función..."
    $EnvVars = "Variables={DYNAMODB_TABLE=OrderExportJobs-$Environment,SQS_QUEUE_URL=$QueueUrl,API_KEY=}"
    aws lambda create-function `
        --function-name $ApiFunctionName `
        --runtime dotnet8 `
        --role $RoleArn `
        --handler "ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler" `
        --zip-file "fileb://$PackagePath" `
        --timeout 30 `
        --memory-size 512 `
        --architectures arm64 `
        --environment $EnvVars `
        --tags Environment=$Environment,Application=OrderExport `
        --region $Region `
        --no-cli-pager
    
    if ($LASTEXITCODE -ne 0) { throw "Error creando $ApiFunctionName" }
}

$ApiFunctionArn = aws lambda get-function `
    --function-name $ApiFunctionName `
    --region $Region `
    --query 'Configuration.FunctionArn' `
    --output text

Write-Host "✅ $ApiFunctionName desplegada: $ApiFunctionArn"

# 5. Desplegar Lambda: OrderExportWorker
Write-Host "`n5. Desplegando Lambda: OrderExportWorker-$Environment..."

$WorkerFunctionName = "OrderExportWorker-$Environment"
$FunctionExists = $false

try {
    aws lambda get-function --function-name $WorkerFunctionName --region $Region --no-cli-pager | Out-Null
    $FunctionExists = $true
} catch {
    $FunctionExists = $false
}

if ($FunctionExists) {
    Write-Host "Actualizando función existente..."
    aws lambda update-function-code `
        --function-name $WorkerFunctionName `
        --zip-file "fileb://$PackagePath" `
        --region $Region `
        --no-cli-pager
    
    if ($LASTEXITCODE -ne 0) { throw "Error actualizando código de $WorkerFunctionName" }
    
    # Esperar a que esté lista
    Write-Host "Esperando a que la función esté lista..."
    aws lambda wait function-updated --function-name $WorkerFunctionName --region $Region
    
    # Actualizar configuración
    $EnvVars = "Variables={DYNAMODB_TABLE=OrderExportJobs-$Environment,S3_BUCKET=order-exports-$AccountId-$Environment}"
    aws lambda update-function-configuration `
        --function-name $WorkerFunctionName `
        --timeout 900 `
        --memory-size 1024 `
        --environment $EnvVars `
        --region $Region `
        --no-cli-pager
} else {
    Write-Host "Creando nueva función..."
    $EnvVars = "Variables={DYNAMODB_TABLE=OrderExportJobs-$Environment,S3_BUCKET=order-exports-$AccountId-$Environment}"
    aws lambda create-function `
        --function-name $WorkerFunctionName `
        --runtime dotnet8 `
        --role $RoleArn `
        --handler "ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler" `
        --zip-file "fileb://$PackagePath" `
        --timeout 900 `
        --memory-size 1024 `
        --architectures arm64 `
        --environment $EnvVars `
        --tags Environment=$Environment,Application=OrderExport `
        --region $Region `
        --no-cli-pager
    
    if ($LASTEXITCODE -ne 0) { throw "Error creando $WorkerFunctionName" }
}

$WorkerFunctionArn = aws lambda get-function `
    --function-name $WorkerFunctionName `
    --region $Region `
    --query 'Configuration.FunctionArn' `
    --output text

Write-Host "✅ $WorkerFunctionName desplegada: $WorkerFunctionArn"

# 6. Configurar SQS trigger
Write-Host "`n6. Configurando SQS trigger para OrderExportWorker..."

$QueueArn = "arn:aws:sqs:${Region}:${AccountId}:order-export-$Environment"

# Verificar si ya existe
$ExistingMappings = aws lambda list-event-source-mappings `
    --function-name $WorkerFunctionName `
    --region $Region `
    --query "EventSourceMappings[?EventSourceArn=='$QueueArn'].UUID" `
    --output text

if ($ExistingMappings) {
    Write-Host "Eliminando event source mapping existente..."
    aws lambda delete-event-source-mapping `
        --uuid $ExistingMappings `
        --region $Region `
        --no-cli-pager
    Start-Sleep -Seconds 5
}

# Crear nuevo event source mapping
Write-Host "Creando event source mapping..."
aws lambda create-event-source-mapping `
    --function-name $WorkerFunctionName `
    --event-source-arn $QueueArn `
    --batch-size 1 `
    --enabled `
    --region $Region `
    --no-cli-pager

if ($LASTEXITCODE -ne 0) { 
    Write-Host "❌ Error configurando SQS trigger"
    Write-Host "Posible causa: El visibility timeout de la cola SQS debe ser mayor que el timeout de Lambda"
    Write-Host "Solución: Ejecuta .\fix-sqs-timeout.ps1 y vuelve a intentar"
    throw "Error configurando SQS trigger" 
}

Write-Host "✅ SQS trigger configurado"

# 7. Crear CloudWatch Log Groups
Write-Host "`n7. Creando CloudWatch Log Groups..."

$logGroups = @(
    "/aws/lambda/$ApiFunctionName",
    "/aws/lambda/$WorkerFunctionName"
)

foreach ($logGroupName in $logGroups) {
    try {
        # Verificar si ya existe
        $exists = aws logs describe-log-groups `
            --log-group-name-prefix $logGroupName `
            --region $Region `
            --output json 2>$null | ConvertFrom-Json
        
        if ($exists.logGroups -and $exists.logGroups.Count -gt 0) {
            Write-Host "  ℹ️  Log group ya existe: $logGroupName"
        } else {
            # Crear log group
            aws logs create-log-group `
                --log-group-name $logGroupName `
                --region $Region
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  ✅ Log group creado: $logGroupName"
                
                # Configurar retention de 7 días
                aws logs put-retention-policy `
                    --log-group-name $logGroupName `
                    --retention-in-days 7 `
                    --region $Region
                
                Write-Host "     Retention: 7 días"
            } else {
                Write-Host "  ⚠️  No se pudo crear log group: $logGroupName" -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host "  ⚠️  Error con log group $logGroupName : $_" -ForegroundColor Yellow
    }
}

# 8. Limpiar archivos temporales
Write-Host "`n8. Limpiando archivos temporales..."
if (Test-Path $PublishDir) {
    Remove-Item -Path $PublishDir -Recurse -Force
}
if (Test-Path $PackagePath) {
    Remove-Item -Path $PackagePath -Force
}

Write-Host "`n==========================================="
Write-Host "✅ Despliegue completado exitosamente!"
Write-Host "==========================================="
Write-Host "`n📋 Funciones Lambda desplegadas:"
Write-Host "  - $ApiFunctionName"
Write-Host "    ARN: $ApiFunctionArn"
Write-Host "  - $WorkerFunctionName"
Write-Host "    ARN: $WorkerFunctionArn"
Write-Host "`n📝 Próximos pasos:"
Write-Host "  1. Configurar API Gateway (ver instrucciones abajo)"
Write-Host "  2. Crear secrets en Secrets Manager para cada tenant"
Write-Host "  3. Probar la API"
Write-Host ""

# Guardar información
$OutputFile = Join-Path $ScriptDir "lambda-deployment-$Environment.txt"
@"
Deployment Info
===============
Date: $(Get-Date)
Environment: $Environment
Region: $Region

Lambda Functions:
- API Handler: $ApiFunctionName
  ARN: $ApiFunctionArn
  Handler: ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler
  
- Worker: $WorkerFunctionName
  ARN: $WorkerFunctionArn
  Handler: ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler

API Gateway Configuration Needed:
----------------------------------
1. Create REST API in API Gateway
2. Create resources:
   - /exports
   - /exports/orders
   - /exports/orders/{jobId}
3. Create methods:
   - POST /exports/orders -> Integration: Lambda Function ($ApiFunctionName)
   - GET /exports/orders/{jobId} -> Integration: Lambda Function ($ApiFunctionName)
4. Enable CORS
5. Deploy to stage: $Environment

Example curl commands:
----------------------
# Create export
curl -X POST https://{api-id}.execute-api.$Region.amazonaws.com/$Environment/exports/orders \
  -H "Content-Type: application/json" \
  -d '{"tenantId":"tenant-123","startDate":"2024-01-01T00:00:00Z","endDate":"2024-01-31T23:59:59Z","format":"CSV"}'

# Get status
curl -X GET "https://{api-id}.execute-api.$Region.amazonaws.com/$Environment/exports/orders/{jobId}?tenantId=tenant-123"
"@ | Out-File -FilePath $OutputFile -Encoding UTF8

Write-Host "ℹ️  Información guardada en: $OutputFile"
