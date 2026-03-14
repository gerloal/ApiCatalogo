# Variables de Entorno para Funciones Lambda

## ?? Resumen por Función

### 1. **OrderExportAPI-dev** (API Gateway Handler)

| Variable | Requerida | Valor Ejemplo | Descripción |
|----------|-----------|---------------|-------------|
| `DYNAMODB_TABLE` | ? Sí | `OrderExportJobs-dev` | Nombre de la tabla DynamoDB para tracking de jobs |
| `SQS_QUEUE_URL` | ? Sí | `https://sqs.eu-west-1.amazonaws.com/340663646958/order-export-dev` | URL completa de la cola SQS |
| `API_KEY` | ?? Opcional | `mi-clave-secreta-123` | API Key para autenticación (vacío = acepta todas las peticiones) |

**Configuración actual en deploy-lambdas.ps1:**
```powershell
Variables={
    DYNAMODB_TABLE=OrderExportJobs-$Environment,
    SQS_QUEUE_URL=$QueueUrl,
    API_KEY=
}
```

---

### 2. **OrderExportWorker-dev** (SQS Worker)

| Variable | Requerida | Valor Ejemplo | Descripción |
|----------|-----------|---------------|-------------|
| `DYNAMODB_TABLE` | ? Sí | `OrderExportJobs-dev` | Nombre de la tabla DynamoDB para tracking de jobs |
| `S3_BUCKET` | ? Sí | `order-exports-340663646958-dev` | Nombre del bucket S3 para almacenar archivos CSV |

**Configuración actual en deploy-lambdas.ps1:**
```powershell
Variables={
    DYNAMODB_TABLE=OrderExportJobs-$Environment,
    S3_BUCKET=order-exports-$AccountId-$Environment
}
```

---

## ?? Valores Calculados Automáticamente

El script `deploy-lambdas.ps1` calcula automáticamente estos valores:

```powershell
# Account ID de AWS
$AccountId = aws sts get-caller-identity --query Account --output text

# URL de la cola SQS
$QueueUrl = "https://sqs.$Region.amazonaws.com/$AccountId/order-export-$Environment"
```

---

## ?? Ejemplo para Environment = "dev" y Account = "340663646958"

### OrderExportAPI-dev
```bash
DYNAMODB_TABLE=OrderExportJobs-dev
SQS_QUEUE_URL=https://sqs.eu-west-1.amazonaws.com/340663646958/order-export-dev
API_KEY=                  # Vacío = acepta todas las peticiones (solo desarrollo)
```

### OrderExportWorker-dev
```bash
DYNAMODB_TABLE=OrderExportJobs-dev
S3_BUCKET=order-exports-340663646958-dev
```

---

## ?? Cómo Configurarlas

### Opción 1: Automático (Recomendado)
El script `deploy-lambdas.ps1` ya las configura automáticamente:

```powershell
.\deploy-lambdas.ps1 -RoleArn "arn:aws:iam::340663646958:role/LambdaExecutionRole"
```

### Opción 2: Manual desde AWS CLI

#### Para OrderExportAPI-dev:
```bash
aws lambda update-function-configuration \
    --function-name OrderExportAPI-dev \
    --environment "Variables={
        DYNAMODB_TABLE=OrderExportJobs-dev,
        SQS_QUEUE_URL=https://sqs.eu-west-1.amazonaws.com/340663646958/order-export-dev,
        API_KEY=
    }" \
    --region eu-west-1
```

#### Para OrderExportWorker-dev:
```bash
aws lambda update-function-configuration \
    --function-name OrderExportWorker-dev \
    --environment "Variables={
        DYNAMODB_TABLE=OrderExportJobs-dev,
        S3_BUCKET=order-exports-340663646958-dev
    }" \
    --region eu-west-1
```

### Opción 3: Desde la Consola de AWS

1. Ve a **AWS Lambda** ? Tu función
2. Tab **Configuration** ? **Environment variables**
3. Click **Edit**
4. Agrega las variables según la tabla arriba
5. **Save**

---

## ?? API_KEY - Consideraciones de Seguridad

### En Desarrollo (API_KEY vacío)
- ?? Acepta **todas las peticiones** sin validación
- Solo usar en desarrollo/testing
- El código muestra un warning en los logs

```csharp
if (string.IsNullOrEmpty(envApiKey))
{
    context.Logger.LogLine("Warning: No API_KEY configured, accepting all requests");
    return true;
}
```

### En Producción (API_KEY configurado)
- ? Requiere header `X-Api-Key` en todas las peticiones
- Usar un valor seguro y compartirlo solo con clientes autorizados

**Ejemplo con API Key:**
```bash
aws lambda update-function-configuration \
    --function-name OrderExportAPI-dev \
    --environment "Variables={
        DYNAMODB_TABLE=OrderExportJobs-dev,
        SQS_QUEUE_URL=https://sqs.eu-west-1.amazonaws.com/340663646958/order-export-dev,
        API_KEY=MiClaveSecreta123!@#
    }" \
    --region eu-west-1
```

**Luego en las peticiones:**
```bash
curl -X POST https://tu-api.execute-api.eu-west-1.amazonaws.com/prod/exports/orders \
  -H "X-Api-Key: MiClaveSecreta123!@#" \
  -H "Content-Type: application/json" \
  -d '{...}'
```

---

## ?? Verificar Variables Actuales

```bash
# Ver todas las variables de una función
aws lambda get-function-configuration \
    --function-name OrderExportAPI-dev \
    --region eu-west-1 \
    --query 'Environment.Variables'

# Resultado esperado:
{
    "DYNAMODB_TABLE": "OrderExportJobs-dev",
    "SQS_QUEUE_URL": "https://sqs.eu-west-1.amazonaws.com/340663646958/order-export-dev",
    "API_KEY": ""
}
```

---

## ? Errores Comunes

### Error: "SQS_QUEUE_URL environment variable is required"
**Causa**: No está configurada la variable `SQS_QUEUE_URL` en OrderExportAPI

**Solución**: 
```bash
aws lambda update-function-configuration \
    --function-name OrderExportAPI-dev \
    --environment "Variables={
        DYNAMODB_TABLE=OrderExportJobs-dev,
        SQS_QUEUE_URL=https://sqs.eu-west-1.amazonaws.com/340663646958/order-export-dev,
        API_KEY=
    }" \
    --region eu-west-1
```

### Error: "S3_BUCKET environment variable is required"
**Causa**: No está configurada la variable `S3_BUCKET` en OrderExportWorker

**Solución**:
```bash
aws lambda update-function-configuration \
    --function-name OrderExportWorker-dev \
    --environment "Variables={
        DYNAMODB_TABLE=OrderExportJobs-dev,
        S3_BUCKET=order-exports-340663646958-dev
    }" \
    --region eu-west-1
```

---

## ?? Valores por Defecto en el Código

Si una variable no está configurada:

| Variable | Valor por Defecto | ¿Falla? |
|----------|------------------|---------|
| `DYNAMODB_TABLE` | `"OrderExportJobs"` | ? No (usa default) |
| `SQS_QUEUE_URL` | - | ? Sí (exception) |
| `S3_BUCKET` | - | ? Sí (exception) |
| `API_KEY` | `""` (vacío) | ? No (acepta todas las peticiones) |

---

## ?? Script de Verificación

Crea un script para verificar las variables:

```powershell
# verify-env-vars.ps1
param(
    [string]$Environment = "dev",
    [string]$Region = "eu-west-1"
)

$functions = @("OrderExportAPI-$Environment", "OrderExportWorker-$Environment")

foreach ($func in $functions) {
    Write-Host "`n=========================================="
    Write-Host "Función: $func"
    Write-Host "==========================================`n"
    
    $vars = aws lambda get-function-configuration `
        --function-name $func `
        --region $Region `
        --query 'Environment.Variables' `
        --output json | ConvertFrom-Json
    
    $vars.PSObject.Properties | ForEach-Object {
        $value = if ($_.Value) { $_.Value } else { "(vacío)" }
        Write-Host "  $($_.Name) = $value"
    }
}
```

Ejecuta:
```powershell
.\verify-env-vars.ps1
```
