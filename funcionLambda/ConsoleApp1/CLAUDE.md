# CLAUDE.md — funcionLambda / ConsoleApp1

## Propósito del Proyecto

Función AWS Lambda en .NET 8 que gestiona dos operaciones asíncronas para vendedores de Amazon:

1. **FEED_CATALOG** — Una tienda sube un fichero CSV con precios/stock → se registra un job en SQS → la Lambda procesa la actualización del catálogo vía Amazon SP-API → avisa al usuario por email.
2. **EXPORT_ORDERS** — Un cliente solicita una exportación de pedidos de Amazon → se crea un job asíncrono → la Lambda consulta la SP-API y genera ficheros CSV → devuelve URLs de descarga temporales.

---

## Arquitectura General

```
Cliente HTTP
    ↓
API Gateway → LambdaServices (REST handler)
    ├─ Valida API Key (X-Api-Key) y parámetros
    ├─ Crea job en DynamoDB (PENDING)
    └─ Manda mensaje a SQS
            ↓
        SQS Queue
            ↓
    OrderExportWorker / CatalogoService (SQS handler)
        ├─ Actualiza estado → RUNNING
        ├─ Obtiene credenciales SP-API de Secrets Manager
        ├─ Llama a Amazon SP-API (FikaAmazonAPI)
        ├─ Genera CSV → sube a S3
        ├─ Actualiza estado → DONE / FAILED (DynamoDB)
        └─ Notifica por email (SES)
```

---

## Stack Tecnológico

- **.NET 8** / `linux-arm64`
- **AWS Lambda** — 2 funciones: API REST y procesador SQS
- **AWS API Gateway** — endpoints REST
- **AWS SQS** — cola de jobs (+ DLQ con 3 reintentos)
- **AWS DynamoDB** — tracking de jobs (pk: `TENANT#{tenantId}`, sk: `JOB#{jobId}`)
- **AWS S3** — almacenamiento de CSVs exportados (cifrado AES256, lifecycle 7 días)
- **AWS Secrets Manager** — credenciales SP-API por tenant
- **AWS SES** — notificaciones por email
- **FikaAmazonAPI** — librería para Amazon Selling Partner API
- **AWS SAM** (`template.yaml`) — infraestructura como código

---

## Ficheros Clave

| Fichero | Responsabilidad |
|---|---|
| `LambdaServices.cs` | Handler REST (POST /exports/orders, GET /exports/orders/{id}) |
| `OrderExportWorker.cs` | Handler SQS para exportación de pedidos |
| `CatalogoService.cs` | Orquestador multi-operación (FEED_CATALOG, EXPORT_ORDERS) |
| `AmazonServices.cs` | Envío de feeds de precios/stock a SP-API y notificación por email |
| `SecretManagerService.cs` | Recupera credenciales SP-API de Secrets Manager |
| `Services/ExportJobService.cs` | CRUD de jobs en DynamoDB + envío a SQS |
| `Services/OrderExportService.cs` | Lógica principal: SP-API → CSV → S3 → URLs prefirmadas |
| `Models/` | DTOs: request, response, queue message, job status, order data |
| `template.yaml` | SAM/CloudFormation: Lambda, SQS, DynamoDB, S3, API Gateway |

---

## Variables de Entorno

| Variable | Función | Usado en |
|---|---|---|
| `DYNAMODB_TABLE` | Tabla de tracking de jobs | Ambas funciones |
| `SQS_QUEUE_URL` | URL de la cola SQS | LambdaServices |
| `S3_BUCKET` | Bucket para CSVs exportados | OrderExportWorker |
| `API_KEY` | Clave de autenticación (opcional en dev) | LambdaServices |

---

## Multi-tenancy

- Cada tenant tiene sus propias credenciales SP-API en Secrets Manager: `/catalog-api/{env}/tenants/{tenantId}/spapi`
- Los datos en DynamoDB y S3 se aíslan por `tenantId`
- El tenant `sportandem` usa formato CSV de ancho fijo en lugar de delimitado por comas

---

## Estados del Job

```
PENDING → RUNNING → DONE   (con URLs prefirmadas en DynamoDB)
                  → FAILED (con errorMessage)
```

---

## Flujo SQS

- **Visibilidad**: 900 s (15 min, igual que el timeout de Lambda)
- **Reintentos**: 3 veces antes de mover a DLQ
- **DLQ**: Retención 14 días; alarma CloudWatch cuando llegan mensajes
- **Batch size**: 1 mensaje por invocación

---

## S3 — Estructura de Ficheros

```
exports/
└── {tenantId}/
    ├── {jobId}_headers.csv   ← cabeceras de pedidos
    └── {jobId}_lines.csv     ← líneas de pedidos
```

URLs prefirmadas con validez de 7 días. Bucket completamente privado.

---

## Handlers de Lambda

```
API:     ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler
Worker:  ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler
```

---

## Despliegue

```bash
dotnet publish -c Release -r linux-arm64 --self-contained false
cd bin/Release/net8.0/linux-arm64/publish && zip -r ../../../../../function.zip .
sam deploy --template-file template.yaml --stack-name order-export-api-{env}
```

Ver `deploy/DEPLOYMENT_GUIDE.md` y `QUICKSTART.md` para guías completas.
