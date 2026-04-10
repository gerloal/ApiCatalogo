# Script para recompilar y redesplegar después del cambio de AssemblyName
# Uso: .\rebuild-and-deploy.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================="
Write-Host "🔨 Rebuild & Redeploy Lambda"
Write-Host "==========================================="

# Obtener Account ID y Role ARN
$AccountId = aws sts get-caller-identity --query Account --output text
$RoleArn = "arn:aws:iam::${AccountId}:role/OrderExportLambdaRole-dev"

Write-Host "`n📋 Configuración:"
Write-Host "   Account: $AccountId"
Write-Host "   Region: $Region"
Write-Host "   Environment: $Environment"
Write-Host "   Role: $RoleArn"

# Directorios
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $ScriptDir

Write-Host "`n1️⃣ Limpiando builds anteriores..."
Set-Location $ProjectDir
dotnet clean
if ($LASTEXITCODE -ne 0) { throw "Error en dotnet clean" }

Write-Host "`n2️⃣ Compilando proyecto..."
dotnet build -c Release
if ($LASTEXITCODE -ne 0) { throw "Error en dotnet build" }

# Verificar que el DLL tiene el nombre correcto
# El RuntimeIdentifier=linux-arm64 hace que el DLL se genere en bin\Release\net8.0\linux-arm64\
$dllPath = Join-Path $ProjectDir "bin\Release\net8.0\linux-arm64\ProcessFileOnQueue.dll"
if (Test-Path $dllPath) {
    Write-Host "   ✅ ProcessFileOnQueue.dll generado correctamente"
    Write-Host "      Ubicación: bin\Release\net8.0\linux-arm64\ProcessFileOnQueue.dll"
} else {
    Write-Host "   ❌ Error: ProcessFileOnQueue.dll no encontrado en: $dllPath"
    Write-Host "   Buscando en otros directorios..."
    
    # Buscar el DLL en todas las carpetas posibles
    $possiblePaths = @(
        "bin\Release\net8.0\ProcessFileOnQueue.dll",
        "bin\Release\net8.0\linux-arm64\ProcessFileOnQueue.dll",
        "bin\Release\net8.0\linux-x64\ProcessFileOnQueue.dll"
    )
    
    foreach ($path in $possiblePaths) {
        $fullPath = Join-Path $ProjectDir $path
        if (Test-Path $fullPath) {
            Write-Host "   ✅ Encontrado en: $path"
            $dllPath = $fullPath
            break
        }
    }
    
    if (-not (Test-Path $dllPath)) {
        Write-Host "   Archivos .dll en bin\Release:"
        Get-ChildItem (Join-Path $ProjectDir "bin\Release") -Filter "*.dll" -Recurse | ForEach-Object { 
            Write-Host "      - $($_.FullName.Replace($ProjectDir, '.'))" 
        }
        throw "El assembly no tiene el nombre correcto"
    }
}

Write-Host "`n3️⃣ Desplegando funciones Lambda (OrderExportAPI + OrderExportWorker)..."
Set-Location $ScriptDir
.\deploy-lambdas.ps1 -RoleArn $RoleArn -Environment $Environment -Region $Region

if ($LASTEXITCODE -ne 0) { throw "Error en deploy-lambdas.ps1" }

# El ZIP ya fue creado por deploy-lambdas.ps1; lo reutilizamos
$PackagePath = Join-Path $ProjectDir "lambda-package.zip"
if (-not (Test-Path $PackagePath)) {
    throw "No se encontró el paquete $PackagePath — deploy-lambdas.ps1 debería haberlo creado"
}

Write-Host "`n3️⃣b Desplegando ProcessFileOnQueue (catalog jobs)..."
aws lambda update-function-code `
    --function-name ProcessFileOnQueue `
    --zip-file "fileb://$PackagePath" `
    --region $Region `
    --no-cli-pager | Out-Null

if ($LASTEXITCODE -ne 0) { throw "Error actualizando ProcessFileOnQueue" }

Write-Host "   Esperando a que ProcessFileOnQueue esté lista..."
aws lambda wait function-updated --function-name ProcessFileOnQueue --region $Region
if ($LASTEXITCODE -ne 0) { throw "Timeout esperando ProcessFileOnQueue" }

Write-Host "   ✅ ProcessFileOnQueue desplegada"

Write-Host "`n4️⃣ Verificando configuración de handlers..."
$functions = @{
    "OrderExportAPI-$Environment"    = "ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler"
    "OrderExportWorker-$Environment" = "ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler"
    "ProcessFileOnQueue"             = "ProcessFileOnQueue::FuncionLambda.CatalogoService::FunctionHandler"
}

foreach ($func in $functions.GetEnumerator()) {
    $config = aws lambda get-function-configuration `
        --function-name $func.Key `
        --region $Region `
        --query '{Handler: Handler, Runtime: Runtime}' `
        --output json 2>$null | ConvertFrom-Json
    
    if ($config) {
        $expectedHandler = $func.Value
        $actualHandler = $config.Handler
        
        if ($actualHandler -eq $expectedHandler) {
            Write-Host "   ✅ $($func.Key): Handler correcto"
        } else {
            Write-Host "   ⚠️  $($func.Key):"
            Write-Host "      Expected: $expectedHandler"
            Write-Host "      Actual: $actualHandler"
            Write-Host "      Corrigiendo..."
            
            aws lambda update-function-configuration `
                --function-name $func.Key `
                --handler $expectedHandler `
                --region $Region `
                --no-cli-pager | Out-Null
            
            Write-Host "      ✅ Handler actualizado"
        }
    }
}

Write-Host "`n==========================================="
Write-Host "✅ Rebuild & Redeploy completado"
Write-Host "==========================================="

Write-Host "`n💡 Ahora puedes probar la API:"
Write-Host "   .\test-order-export-api.ps1"
