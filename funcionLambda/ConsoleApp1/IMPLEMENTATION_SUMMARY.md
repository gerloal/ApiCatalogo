# Resumen de Implementación - Sistema de Exportación de Pedidos

## ? Componentes Implementados

### 1. **Modelos de Datos** (`Models/`)
- ? `ExportOrdersRequest.cs` - Request body para crear job
- ? `ExportOrdersResponse.cs` - Response al crear job
- ? `ExportOrdersQueueMessage.cs` - Mensaje para SQS
- ? `JobStatusResponse.cs` - Response con estado del job
- ? `OrderHeader.cs` - Modelo para cabeceras de pedidos
- ? `OrderLine.cs` - Modelo para líneas de pedidos
- ? `OrderExportResult.cs` - Resultado de la exportación

### 2. **Servicios** (`Services/`)
- ? `ExportJobService.cs` - Gestión de jobs en DynamoDB y SQS
  - Crear job
  - Obtener estado
  - Actualizar estado a RUNNING
  
- ? `OrderExportService.cs` - Exportación de pedidos desde Amazon SP-API
  - Obtener pedidos y líneas
  - Generar CSV
  - Subir a S3
  - Generar URLs pre-firmadas
  - Actualizar DynamoDB con resultados

### 3. **Funciones Lambda**

#### **LambdaServices.cs** - API Handler
**Handler**: `ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler`

- ? Endpoint POST `/exports/orders` - Crear job
  - Validación de autenticación (API Key)
  - Validación de input
  - Creación de jobId
  - Guardado en DynamoDB (estado PENDING)
  - Envío de mensaje a SQS
  - Response 202 Accepted con jobId

- ? Endpoint GET `/exports/orders/{jobId}` - Obtener estado
  - Validación de autenticación
  - Consulta a DynamoDB
  - Response con estado actual y URLs si está DONE

#### **OrderExportWorker.cs** - SQS Processor
**Handler**: `ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler`

- ? Procesa mensajes SQS
- ? Actualiza estado a RUNNING
- ? Obtiene credenciales de Secrets Manager
- ? Se conecta a Amazon SP-API
- ? Exporta pedidos usando `OrderExportService`
- ? Manejo de errores y actualización de estado FAILED

### 4. **Infraestructura**

- ? `template.yaml` - SAM/CloudFormation template
  - DynamoDB table
  - S3 bucket con lifecycle policies
  - SQS queue con DLQ
  - Lambda functions con permisos IAM
  - API Gateway REST API
  - CloudWatch Alarms

### 5. **Documentación**

- ? `README_OrderExport.md` - Documentación completa
  - Descripción de funciones Lambda
  - Variables de entorno
  - Estructura de datos
  - Instrucciones de deployment
  - Configuración de permisos

- ? `EXAMPLES_OrderExport.md` - Ejemplos de uso
  - Ejemplos de peticiones HTTP
  - Formato de responses
  - Scripts Bash y Python
  - Errores comunes
  - Mejores prácticas

### 6. **Dependencias**

- ? Agregado `Amazon.Lambda.APIGatewayEvents` al proyecto

## ?? Flujo de Trabajo

```
???????????????????????????????????????????????????????????????????
?                         CLIENTE API                              ?
???????????????????????????????????????????????????????????????????
                            ?
                            ?
                  POST /exports/orders
                            ?
???????????????????????????????????????????????????????????????????
?                    LambdaServices (API)                          ?
?  1. Valida autenticación (API Key)                               ?
?  2. Valida input (fechas, tenant, etc.)                          ?
?  3. Genera jobId (UUID)                                          ?
?  4. Guarda en DynamoDB (PENDING)                                 ?
?  5. Envía mensaje a SQS                                          ?
?  6. Response 202 Accepted + jobId                                ?
???????????????????????????????????????????????????????????????????
                            ?
                            ?
                       SQS Queue
                            ?
???????????????????????????????????????????????????????????????????
?               OrderExportWorker (SQS Processor)                  ?
?  1. Lee mensaje de SQS                                           ?
?  2. Actualiza DynamoDB ? RUNNING                                 ?
?  3. Obtiene credenciales SP-API (Secrets Manager)                ?
?  4. Conecta a Amazon SP-API                                      ?
?  5. Exporta pedidos (OrderExportService)                         ?
?     - Obtiene pedidos por rango de fechas                        ?
?     - Obtiene líneas de cada pedido                              ?
?     - Genera CSV (headers + lines)                               ?
?     - Sube a S3                                                  ?
?     - Genera URLs pre-firmadas (7 días)                          ?
?  6. Actualiza DynamoDB ? DONE (con URLs)                         ?
???????????????????????????????????????????????????????????????????
                            ?
                            ?
                  GET /exports/orders/{jobId}
                            ?
???????????????????????????????????????????????????????????????????
?                    LambdaServices (API)                          ?
?  1. Valida autenticación                                         ?
?  2. Consulta DynamoDB                                            ?
?  3. Response con estado actual                                   ?
?     - PENDING / RUNNING / DONE / FAILED                          ?
?     - Si DONE: incluye URLs pre-firmadas                         ?
???????????????????????????????????????????????????????????????????
                            ?
                            ?
                      CLIENTE API
              Descarga CSVs desde URLs S3
```

## ?? Estados del Job

1. **PENDING** - Job creado, esperando procesamiento
2. **RUNNING** - Job en proceso de exportación
3. **DONE** - Job completado exitosamente, URLs disponibles
4. **FAILED** - Job falló, ver errorMessage

## ?? Archivos CSV Generados

### headers_{jobId}.csv
Información de cabecera de cada pedido:
- AmazonOrderId
- PurchaseDate
- OrderStatus
- OrderTotal
- BuyerEmail
- ShippingAddress
- Etc.

### lines_{jobId}.csv
Líneas de productos de cada pedido:
- AmazonOrderId
- OrderItemId
- ASIN
- SellerSKU
- Title
- QuantityOrdered
- ItemPrice
- Etc.

## ?? Seguridad

- ? Autenticación via API Key (configurable)
- ? Validación de input
- ? Credenciales en Secrets Manager
- ? URLs pre-firmadas con expiración (7 días)
- ? Encriptación S3 (AES256)
- ? Bucket privado (no público)
- ? IAM roles con mínimos privilegios

## ?? Configuración Requerida

### Variables de Entorno - LambdaServices
```
DYNAMODB_TABLE=OrderExportJobs
SQS_QUEUE_URL=https://sqs.eu-west-1.amazonaws.com/.../order-export-queue
API_KEY=optional-api-key
```

### Variables de Entorno - OrderExportWorker
```
DYNAMODB_TABLE=OrderExportJobs
S3_BUCKET=order-exports-bucket
```

### Secrets Manager
Cada tenant debe tener un secret:
```
Nombre: {tenantId}/prod/order-export
Contenido: SpApiSecret (JSON con credenciales)
```

### DynamoDB
```
Tabla: OrderExportJobs
Partition Key: pk (String)
Sort Key: sk (String)
```

## ?? Validaciones Implementadas

- ? TenantId requerido
- ? Fechas requeridas y válidas
- ? StartDate < EndDate
- ? EndDate no puede ser futuro
- ? Rango máximo: 30 días
- ? Solo formato CSV soportado
- ? API Key (opcional en desarrollo)

## ?? Próximos Pasos para Deployment

1. **Compilar proyecto**
   ```bash
   dotnet build -c Release
   dotnet publish -c Release -r linux-arm64 --self-contained false
   ```

2. **Crear ZIP**
   ```bash
   cd bin/Release/net8.0/linux-arm64/publish
   zip -r lambda-package.zip .
   ```

3. **Deploy con SAM**
   ```bash
   sam deploy --template-file template.yaml --stack-name order-export-api \
     --parameter-overrides Environment=prod ApiKey=your-key \
     --capabilities CAPABILITY_IAM
   ```

4. **Configurar Secrets Manager**
   - Crear secrets para cada tenant
   - Formato: `{tenantId}/prod/order-export`

5. **Testing**
   - POST para crear job
   - GET para verificar estado
   - Validar archivos CSV descargados

## ?? Monitoreo

CloudWatch Alarms configurados:
- ? Errores en LambdaServices
- ? Errores en OrderExportWorker
- ? Mensajes en DLQ

Métricas a monitorear:
- Duración de Lambda
- Número de jobs por estado
- Tamaño de archivos generados
- Mensajes en SQS

## ? Características

- ? **Asíncrono**: Response inmediato 202, procesamiento en background
- ? **Escalable**: SQS + Lambda pueden procesar múltiples jobs en paralelo
- ? **Resiliente**: DLQ para mensajes fallidos, retry automático
- ? **Idempotente**: Validaciones de estado en DynamoDB
- ? **Auditable**: Timestamps de creación y actualización
- ? **Multi-tenant**: Soporte para múltiples tenants
- ? **URLs temporales**: Pre-signed URLs con expiración
- ? **Lifecycle**: Archivos S3 se borran automáticamente después de 7 días

## ?? Beneficios

1. **Sin polling del cliente**: El cliente solo necesita 2 llamadas (POST + GET)
2. **Escalabilidad**: Múltiples exports pueden ejecutarse en paralelo
3. **Resiliencia**: Fallos no bloquean otros jobs
4. **Auditoría**: Historial completo en DynamoDB
5. **Seguridad**: URLs temporales, no exponen datos permanentemente
6. **Costo-efectivo**: Pay-per-use, S3 lifecycle elimina archivos antiguos
