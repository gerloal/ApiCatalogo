# Lambda Functions - Order Export API

Este proyecto contiene dos funciones Lambda para el sistema de exportación de pedidos de Amazon:

## Funciones Lambda

### 1. LambdaServices (API Handler)
**Handler**: `ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler`

Esta función maneja las peticiones HTTP a través de API Gateway:

#### Endpoints:

**POST /exports/orders**
- Crea un nuevo job de exportación
- Body (JSON):
```json
{
  "tenantId": "tenant-123",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-01-31T23:59:59Z",
  "format": "CSV"
}
```
- Headers:
  - `X-Api-Key`: API key para autenticación (opcional en desarrollo)
  
- Response (202 Accepted):
```json
{
  "jobId": "uuid-del-job",
  "status": "PENDING",
  "message": "Export job created successfully. Use GET /exports/orders/{jobId} to check status."
}
```

**GET /exports/orders/{jobId}**
- Obtiene el estado de un job de exportación
- Query params o headers:
  - `tenantId` (query param) o `X-Tenant-Id` (header): ID del tenant
  - `X-Api-Key` (header): API key para autenticación
  
- Response (200 OK):
```json
{
  "jobId": "uuid-del-job",
  "tenantId": "tenant-123",
  "status": "DONE",
  "totalOrders": 150,
  "totalLines": 320,
  "headersPresignedUrl": "https://s3.amazonaws.com/...",
  "linesPresignedUrl": "https://s3.amazonaws.com/...",
  "errorMessage": null,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:35:00Z"
}
```

Estados posibles:
- `PENDING`: Job creado, esperando procesamiento
- `RUNNING`: Job en proceso
- `DONE`: Job completado, URLs disponibles
- `FAILED`: Job fallido, ver errorMessage

### 2. OrderExportWorker (SQS Worker)
**Handler**: `ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler`

Esta función procesa mensajes SQS para exportar pedidos:
- Lee mensajes de SQS
- Obtiene credenciales de Amazon SP-API desde Secrets Manager
- Exporta pedidos usando Amazon SP-API
- Genera archivos CSV
- Sube archivos a S3
- Genera URLs pre-firmadas
- Actualiza estado del job en DynamoDB

## Variables de Entorno

### LambdaServices:
```
DYNAMODB_TABLE=OrderExportJobs
SQS_QUEUE_URL=https://sqs.eu-west-1.amazonaws.com/123456789/order-export-queue
API_KEY=your-api-key-here (opcional)
```

### OrderExportWorker:
```
DYNAMODB_TABLE=OrderExportJobs
S3_BUCKET=order-exports-bucket
```

## Estructura DynamoDB

**Tabla**: OrderExportJobs

- **pk** (String, Partition Key): `TENANT#{tenantId}`
- **sk** (String, Sort Key): `JOB#{jobId}`
- **tenantId** (String): ID del tenant
- **jobId** (String): UUID del job
- **status** (String): PENDING | RUNNING | DONE | FAILED
- **startDate** (String): Fecha inicio ISO 8601
- **endDate** (String): Fecha fin ISO 8601
- **format** (String): CSV
- **createdAt** (String): Timestamp ISO 8601
- **updatedAt** (String): Timestamp ISO 8601
- **totalOrders** (Number): Total de pedidos exportados
- **totalLines** (Number): Total de líneas exportadas
- **headersFileKey** (String): S3 key del archivo headers
- **linesFileKey** (String): S3 key del archivo lines
- **headersUrl** (String): URL pre-firmada para headers
- **linesUrl** (String): URL pre-firmada para lines
- **errorMessage** (String): Mensaje de error si falla

## Estructura S3

Los archivos se guardan con la siguiente estructura:
```
s3://bucket/exports/{tenantId}/{jobId}_headers.csv
s3://bucket/exports/{tenantId}/{jobId}_lines.csv
```

## Estructura SQS Message

```json
{
  "tenantId": "tenant-123",
  "jobId": "uuid-del-job",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-01-31T23:59:59Z",
  "format": "CSV",
  "operation": "EXPORT_ORDERS"
}
```

## Secrets Manager

Las credenciales de Amazon SP-API se deben guardar en Secrets Manager con el formato:
```
Secret Name: {tenantId}/prod/order-export

Secret Value (JSON):
{
  "AccessKey": "...",
  "SecretKey": "...",
  "RoleArn": "arn:aws:iam::...",
  "ClientId": "...",
  "ClientSecret": "...",
  "RefreshToken": "...",
  "MarketPlaceID": "...",
  "SellerId": "..."
}
```

## Deployment

### Compilar:
```bash
dotnet build -c Release
```

### Publicar:
```bash
dotnet publish -c Release -r linux-arm64 --self-contained false
```

### Crear ZIP para Lambda:
```bash
cd bin/Release/net8.0/linux-arm64/publish
zip -r ../../../../../lambda-package.zip .
```

### Deploy con AWS CLI:

#### LambdaServices (API):
```bash
aws lambda update-function-code \
  --function-name OrderExportAPI \
  --zip-file fileb://lambda-package.zip \
  --region eu-west-1
```

#### OrderExportWorker (SQS):
```bash
aws lambda update-function-code \
  --function-name OrderExportWorker \
  --zip-file fileb://lambda-package.zip \
  --region eu-west-1
```

## Configuración API Gateway

1. Crear API REST en API Gateway
2. Crear recursos:
   - `/exports`
   - `/exports/orders`
   - `/exports/orders/{jobId}`
3. Configurar métodos:
   - POST en `/exports/orders` → Lambda: OrderExportAPI
   - GET en `/exports/orders/{jobId}` → Lambda: OrderExportAPI
4. Deploy a stage (dev, prod, etc.)

## Permisos IAM

### LambdaServices necesita:
- `dynamodb:PutItem` en tabla OrderExportJobs
- `dynamodb:GetItem` en tabla OrderExportJobs
- `sqs:SendMessage` en cola order-export-queue
- CloudWatch Logs (automático)

### OrderExportWorker necesita:
- `dynamodb:UpdateItem` en tabla OrderExportJobs
- `s3:PutObject` en bucket order-exports
- `s3:GetObject` en bucket order-exports (para generar URLs)
- `secretsmanager:GetSecretValue` en secrets de tenants
- `sqs:ReceiveMessage` en cola order-export-queue
- `sqs:DeleteMessage` en cola order-export-queue
- CloudWatch Logs (automático)

## Testing Local

Ejemplo de petición POST:
```bash
curl -X POST https://your-api-id.execute-api.eu-west-1.amazonaws.com/prod/exports/orders \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-api-key" \
  -d '{
    "tenantId": "tenant-123",
    "startDate": "2024-01-01T00:00:00Z",
    "endDate": "2024-01-31T23:59:59Z",
    "format": "CSV"
  }'
```

Ejemplo de petición GET:
```bash
curl -X GET "https://your-api-id.execute-api.eu-west-1.amazonaws.com/prod/exports/orders/{jobId}?tenantId=tenant-123" \
  -H "X-Api-Key: your-api-key"
```

## Validaciones

- TenantId requerido
- Fechas requeridas y válidas
- StartDate < EndDate
- EndDate no puede ser futuro
- Rango máximo: 30 días
- Solo formato CSV soportado
- API Key requerida (configurable)

## Monitoreo

Métricas importantes en CloudWatch:
- Duración de Lambda
- Errores de Lambda
- Mensajes en SQS (ApproximateNumberOfMessages)
- Jobs por estado en DynamoDB
- Tamaño de archivos en S3

Alarmas recomendadas:
- Lambda con muchos errores
- SQS con mensajes antiguos (DLQ)
- Jobs en estado RUNNING por mucho tiempo
