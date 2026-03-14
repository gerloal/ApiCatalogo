# ?? Guía de Primera Prueba - Order Export API

## ?? Prerequisitos

Antes de probar, asegúrate de tener:

- ? Funciones Lambda desplegadas
- ? API Gateway configurado
- ? Secret configurado en Secrets Manager para Sportandem
- ? API Endpoint URL (de API Gateway)

## ?? Información de Prueba

```
Tenant ID: Sportandem
API Key: 9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy
```

## ?? Paso 1: Obtener el API Endpoint

Primero necesitas el endpoint de tu API Gateway:

```powershell
# Buscar el API ID
aws apigateway get-rest-apis --region eu-west-1 --query 'items[?name==`OrderExportAPI-dev`].{id:id,name:name}' --output table

# El endpoint será:
# https://{api-id}.execute-api.eu-west-1.amazonaws.com/dev
```

O desde la consola AWS:
1. Ve a **API Gateway** ? **APIs**
2. Selecciona `OrderExportAPI-dev`
3. En el menú izquierdo: **Stages** ? **dev**
4. Copia el **Invoke URL**

## ?? Paso 2: Verificar Secret de Sportandem

Verifica que existe el secret para Sportandem:

```powershell
aws secretsmanager describe-secret `
    --secret-id "Sportandem/prod/order-export" `
    --region eu-west-1
```

**Si no existe**, créalo con las credenciales de Amazon SP-API:

```powershell
aws secretsmanager create-secret `
    --name "Sportandem/prod/order-export" `
    --description "Amazon SP-API credentials for Sportandem" `
    --secret-string '{
        "ClientId": "amzn1.application-oa2-client.YOUR_CLIENT_ID",
        "ClientSecret": "YOUR_CLIENT_SECRET",
        "RefreshToken": "Atzr|YOUR_REFRESH_TOKEN",
        "MarketPlaceID": "A1RKKUPIHCS9HS",
        "RoleArn": "arn:aws:iam::YOUR_ACCOUNT:role/SPAPIRole",
        "SellerId": "YOUR_SELLER_ID",
        "TenantId": "Sportandem"
    }' `
    --region eu-west-1
```

## ?? Paso 3: Ejecutar Prueba Completa

### Opción A: Script Automatizado (Recomendado)

```powershell
cd deploy

# Actualizar el endpoint en quick-test.ps1
# Edita la línea:
# $ApiEndpoint = "https://xxxxx.execute-api.eu-west-1.amazonaws.com/dev"

# Ejecutar
.\quick-test.ps1
```

### Opción B: Script con Monitoreo

```powershell
cd deploy

# Ejecutar con monitoreo automático
.\test-order-export-api.ps1 `
    -ApiEndpoint "https://xxxxx.execute-api.eu-west-1.amazonaws.com/dev" `
    -TenantId "Sportandem" `
    -ApiKey "9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy" `
    -DaysBack 30
```

### Opción C: Manualmente con PowerShell

```powershell
# 1. Configuración
$ApiEndpoint = "https://xxxxx.execute-api.eu-west-1.amazonaws.com/dev"
$TenantId = "Sportandem"
$ApiKey = "9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy"

# 2. Crear job
$body = @{
    tenantId = $TenantId
    startDate = (Get-Date).AddDays(-30).ToString("yyyy-MM-ddTHH:mm:ssZ")
    endDate = (Get-Date).ToString("yyyy-MM-ddTHH:mm:ssZ")
    format = "CSV"
} | ConvertTo-Json

$headers = @{
    "Content-Type" = "application/json"
    "X-Api-Key" = $ApiKey
}

$response = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders" `
    -Method Post -Body $body -Headers $headers

Write-Host "Job creado: $($response.jobId)"
$jobId = $response.jobId

# 3. Verificar estado (repetir cada 10 segundos)
$status = Invoke-RestMethod -Uri "$ApiEndpoint/exports/orders/${jobId}?tenantId=$TenantId" `
    -Method Get -Headers @{ "X-Api-Key" = $ApiKey }

$status | Format-List

# 4. Cuando status = DONE, descargar archivos
Invoke-WebRequest -Uri $status.headersPresignedUrl -OutFile "headers.csv"
Invoke-WebRequest -Uri $status.linesPresignedUrl -OutFile "lines.csv"
```

## ?? Respuestas Esperadas

### Response al crear job (202 Accepted):
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "PENDING",
  "message": "Export job created successfully. Use GET /exports/orders/{jobId} to check status."
}
```

### Response al verificar estado (200 OK):

**PENDING**:
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "Sportandem",
  "status": "PENDING",
  "totalOrders": 0,
  "totalLines": 0,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:30:00Z"
}
```

**RUNNING**:
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "Sportandem",
  "status": "RUNNING",
  "totalOrders": 0,
  "totalLines": 0,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:31:15Z"
}
```

**DONE** (con URLs):
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "Sportandem",
  "status": "DONE",
  "totalOrders": 150,
  "totalLines": 320,
  "headersPresignedUrl": "https://s3.amazonaws.com/...",
  "linesPresignedUrl": "https://s3.amazonaws.com/...",
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:35:30Z"
}
```

## ?? Troubleshooting

### Error 401 Unauthorized
```
Causa: API Key incorrecta
Solución: Verifica que estás usando: 9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy
```

### Error 400 Bad Request
```
Causa: Datos inválidos en el request
Solución: Verifica formato de fechas (ISO 8601) y tenantId
```

### Error 404 Not Found
```
Causa: Endpoint incorrecto
Solución: Verifica el API Endpoint URL
```

### Job en FAILED
```
Ver logs de CloudWatch:
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1

Causas comunes:
- Secret no encontrado en Secrets Manager
- Credenciales SP-API inválidas
- Sin pedidos en el rango de fechas
```

### Job tarda mucho en PENDING
```
Verificar:
1. SQS tiene mensajes
2. Lambda Worker está activa
3. Event source mapping está enabled

aws lambda list-event-source-mappings \
  --function-name OrderExportWorker-dev \
  --region eu-west-1
```

## ?? Monitoreo en Tiempo Real

### Ver logs de API
```powershell
aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1
```

### Ver logs de Worker
```powershell
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1
```

### Ver mensajes en SQS
```powershell
$accountId = (aws sts get-caller-identity --query Account --output text)
aws sqs receive-message `
    --queue-url "https://sqs.eu-west-1.amazonaws.com/$accountId/order-export-dev" `
    --region eu-west-1
```

### Ver jobs en DynamoDB
```powershell
aws dynamodb scan `
    --table-name OrderExportJobs-dev `
    --filter-expression "tenantId = :tid" `
    --expression-attribute-values '{":tid":{"S":"Sportandem"}}' `
    --region eu-west-1
```

## ? Verificación Final

Una vez que el job esté DONE:

1. ? Verifica que se crearon 2 archivos CSV
2. ? Abre headers.csv y verifica la estructura
3. ? Abre lines.csv y verifica los datos
4. ? Confirma que las URLs expiran después de 7 días

## ?? Ejemplo Completo de Salida Exitosa

```
=========================================
Probando Order Export API
API Endpoint: https://abc123.execute-api.eu-west-1.amazonaws.com/dev
Tenant: Sportandem
=========================================

Fechas de exportación:
  Start: 2024-12-01T00:00:00Z
  End:   2024-12-31T23:59:59Z

1. Creando job de exportación...
? Job creado exitosamente!
   Job ID: 550e8400-e29b-41d4-a716-446655440000
   Status: PENDING
   Message: Export job created successfully...

2. Monitoreando estado del job...
   [Intento 1] Status: PENDING
   [Intento 2] Status: RUNNING
   [Intento 3] Status: RUNNING
   [Intento 4] Status: DONE

? Exportación completada!

Resultados:
  - Total Pedidos: 150
  - Total Líneas: 320
  - Creado: 2024-01-15T10:30:00Z
  - Actualizado: 2024-01-15T10:35:30Z

3. Descargando archivos CSV...
   Descargando headers...
   ? Headers guardado: C:\...\exports\Sportandem_headers_20240115_103530.csv
   Descargando lines...
   ? Lines guardado: C:\...\exports\Sportandem_lines_20240115_103530.csv

=========================================
? Prueba completada exitosamente!
=========================================
```

## ?? Próximos Pasos

Una vez que la primera prueba funcione:

1. Probar con diferentes rangos de fechas
2. Probar con otros tenants
3. Implementar manejo de errores en tu aplicación
4. Configurar notificaciones cuando los exports completen
5. Automatizar exports periódicos

¿Necesitas ayuda con algún paso específico?
