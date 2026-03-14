# Recursos AWS Necesarios - Resumen Ejecutivo

## ?? Tabla de Recursos

| # | Recurso | Nombre | Propósito | ¿Nuevo? | Costo Estimado |
|---|---------|--------|-----------|---------|----------------|
| 1 | **DynamoDB Table** | `OrderExportJobs-{env}` | Almacenar estado de jobs | ? SÍ | ~$0.25/mes (1M reads) |
| 2 | **S3 Bucket** | `order-exports-{accountId}-{env}` | Almacenar CSVs | ? SÍ | ~$0.023/GB/mes |
| 3 | **SQS Queue** | `order-export-queue-{env}` | Cola de trabajos | ? SÍ | ~$0.40/1M requests |
| 4 | **SQS DLQ** | `order-export-dlq-{env}` | Mensajes fallidos | ? SÍ | ~$0.40/1M requests |
| 5 | **Lambda API** | `OrderExportAPI-{env}` | API REST handler | ? SÍ | ~$0.20/1M requests |
| 6 | **Lambda Worker** | `OrderExportWorker-{env}` | Procesador SQS | ? SÍ | Variable (por ejecución) |
| 7 | **API Gateway** | `OrderExportAPI-{env}` | REST API | ? SÍ | ~$3.50/1M requests |
| 8 | **Secrets Manager** | `{tenant}/prod/order-export` | Credenciales SP-API | ?? CONFIG | $0.40/secret/mes |
| 9 | **IAM Role** | `OrderExportLambdaRole-{env}` (o existente) | Permisos Lambda | ?? OPCIONAL | Gratis |
| 10 | **CloudWatch Logs** | `/aws/lambda/*` | Logs | ?? AUTO | ~$0.50/GB |

**Costo mensual estimado (bajo uso):** ~$5-10 USD

---

## ??? Arquitectura de Recursos

```
???????????????????????????????????????????????????????????????????
?                          CLIENTE                                 ?
?                     (Aplicación Externa)                         ?
???????????????????????????????????????????????????????????????????
                            ? HTTPS
                            ?
???????????????????????????????????????????????????????????????????
?                      API GATEWAY REST                            ?
?                  OrderExportAPI-{env}                            ?
?                                                                   ?
?  Endpoints:                                                       ?
?  - POST /exports/orders                                          ?
?  - GET /exports/orders/{jobId}                                   ?
???????????????????????????????????????????????????????????????????
                            ?
                            ?
???????????????????????????????????????????????????????????????????
?                  LAMBDA: OrderExportAPI                          ?
?                  (API Handler - 512MB)                           ?
?                                                                   ?
?  Responsabilidades:                                               ?
?  - Validar autenticación                                         ?
?  - Validar input                                                 ?
?  - Generar jobId                                                 ?
?  - Guardar en DynamoDB ? PENDING                                 ?
?  - Enviar mensaje a SQS                                          ?
?  - Response 202 Accepted                                         ?
???????????????????????????????????????????????????????????????????
            ?                       ?
            ? write                 ? send
            ?                       ?
   ???????????????????    ????????????????????????
   ?   DynamoDB      ?    ?    SQS Queue         ?
   ? OrderExportJobs ?    ? order-export-queue   ?
   ?                 ?    ?                      ?
   ? PK: TENANT#id   ?    ? Message:             ?
   ? SK: JOB#id      ?    ? - tenantId           ?
   ?                 ?    ? - jobId              ?
   ? Attributes:     ?    ? - dates              ?
   ? - status        ?    ?                      ?
   ? - totalOrders   ?    ? DLQ:                 ?
   ? - URLs          ?    ? order-export-dlq     ?
   ???????????????????    ????????????????????????
            ?                         ? trigger
            ?                         ?
            ? update                  ?
            ?              ????????????????????????
            ?              ?  LAMBDA: Worker      ?
            ?              ?  OrderExportWorker   ?
            ?              ?  (1024MB, 15min)     ?
            ?              ?                      ?
            ?              ?  Proceso:            ?
            ?              ?  1. Update RUNNING   ?
            ????????????????  2. Get credentials  ?
                           ?  3. Export orders    ?
                           ?  4. Generate CSVs    ?
                           ?  5. Upload to S3     ?
                           ?  6. Update DONE      ?
                           ????????????????????????
                              ?          ?
                       read   ?          ? write
                              ?          ?
                   ??????????????????????????????
                   ?   Secrets Manager          ?
                   ?                            ?
                   ? {tenant}/prod/order-export ?
                   ? - ClientId                 ?
                   ? - ClientSecret             ?
                   ? - RefreshToken             ?
                   ? - MarketPlaceID            ?
                   ??????????????????????????????
                                        
                                        ?
                                        ?
                              ????????????????????
                              ?   S3 Bucket      ?
                              ? order-exports    ?
                              ?                  ?
                              ? exports/         ?
                              ?   tenant-123/    ?
                              ?     {job}_*.csv  ?
                              ?                  ?
                              ? Lifecycle: 7d    ?
                              ????????????????????
```

---

## ?? Permisos IAM Necesarios

### Rol Lambda (nuevo o existente)

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "DynamoDBAccess",
      "Effect": "Allow",
      "Action": [
        "dynamodb:PutItem",
        "dynamodb:GetItem",
        "dynamodb:UpdateItem",
        "dynamodb:Query"
      ],
      "Resource": "arn:aws:dynamodb:*:*:table/OrderExportJobs-*"
    },
    {
      "Sid": "S3Access",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject"
      ],
      "Resource": "arn:aws:s3:::order-exports-*/*"
    },
    {
      "Sid": "SQSAccess",
      "Effect": "Allow",
      "Action": [
        "sqs:SendMessage",
        "sqs:ReceiveMessage",
        "sqs:DeleteMessage",
        "sqs:GetQueueAttributes"
      ],
      "Resource": [
        "arn:aws:sqs:*:*:order-export-queue-*",
        "arn:aws:sqs:*:*:order-export-dlq-*"
      ]
    },
    {
      "Sid": "SecretsManagerAccess",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": "arn:aws:secretsmanager:*:*:secret:*/prod/order-export-*"
    },
    {
      "Sid": "CloudWatchLogs",
      "Effect": "Allow",
      "Action": [
        "logs:CreateLogGroup",
        "logs:CreateLogStream",
        "logs:PutLogEvents"
      ],
      "Resource": "arn:aws:logs:*:*:*"
    }
  ]
}
```

---

## ?? Checklist de Despliegue

### ? Fase 1: Crear Infraestructura Base
- [ ] Ejecutar `create-aws-resources.ps1`
- [ ] Verificar creación de DynamoDB table
- [ ] Verificar creación de S3 bucket
- [ ] Verificar creación de SQS queues
- [ ] Verificar creación de IAM role (o añadir permisos a rol existente)

### ? Fase 2: Desplegar Funciones Lambda
- [ ] Compilar proyecto .NET
- [ ] Ejecutar `deploy-lambdas.ps1`
- [ ] Verificar `OrderExportAPI-{env}` desplegada
- [ ] Verificar `OrderExportWorker-{env}` desplegada
- [ ] Verificar SQS trigger configurado

### ? Fase 3: Configurar API Gateway
- [ ] Crear REST API
- [ ] Crear recursos `/exports/orders` y `/exports/orders/{jobId}`
- [ ] Configurar método POST en `/exports/orders`
- [ ] Configurar método GET en `/exports/orders/{jobId}`
- [ ] Habilitar CORS
- [ ] Deploy a stage
- [ ] Copiar Invoke URL

### ? Fase 4: Configurar Secrets
- [ ] Para cada tenant, crear secret en Secrets Manager
- [ ] Formato: `{tenantId}/prod/order-export`
- [ ] Incluir todas las credenciales de Amazon SP-API

### ? Fase 5: Testing
- [ ] Probar POST para crear job
- [ ] Verificar job en DynamoDB con estado PENDING
- [ ] Verificar mensaje en SQS
- [ ] Esperar procesamiento (Lambda Worker)
- [ ] Probar GET para obtener estado
- [ ] Cuando DONE, descargar CSVs desde URLs
- [ ] Verificar contenido de CSVs

### ? Fase 6: Monitoreo (Opcional)
- [ ] Configurar CloudWatch Alarms
- [ ] Configurar SNS para notificaciones
- [ ] Configurar dashboards de CloudWatch

---

## ?? Estimación de Costos Detallada

### Escenario: 100 exports/mes (uso bajo)

| Servicio | Uso | Costo Unitario | Costo Mensual |
|----------|-----|----------------|---------------|
| **DynamoDB** | 300K reads, 200K writes | $0.25/M reads, $1.25/M writes | $0.33 |
| **S3** | 5 GB storage, 200 PUTs, 500 GETs | $0.023/GB, $0.005/1K PUT | $0.12 |
| **SQS** | 100 requests | $0.40/M requests | $0.00 |
| **Lambda API** | 200 invocations, 128MB-s | $0.20/M, $0.0000166667/GB-s | $0.01 |
| **Lambda Worker** | 100 invocations, 15min avg | $0.20/M, $0.0000166667/GB-s | $0.50 |
| **API Gateway** | 200 requests | $3.50/M | $0.70 |
| **Secrets Manager** | 1 secret | $0.40/secret | $0.40 |
| **CloudWatch Logs** | 1 GB | $0.50/GB | $0.50 |
| **Data Transfer** | 5 GB out | $0.09/GB (primeros 10TB) | $0.45 |

**TOTAL MENSUAL:** ~$3.00 USD

### Escenario: 1000 exports/mes (uso medio)

**TOTAL MENSUAL:** ~$25 USD

### Escenario: 10000 exports/mes (uso alto)

**TOTAL MENSUAL:** ~$200 USD

---

## ?? Recursos Opcionales

| Recurso | ¿Cuándo crearlo? | Beneficio |
|---------|------------------|-----------|
| **CloudWatch Dashboard** | Producción | Visualización de métricas |
| **SNS Topic** | Producción | Notificaciones de alarmas |
| **VPC Endpoints** | Alta seguridad | Tráfico privado S3/DynamoDB |
| **WAF** | Producción | Protección API Gateway |
| **CloudFront** | Alto tráfico | CDN para API |
| **X-Ray** | Debugging | Trazabilidad distribuida |

---

## ?? Soporte

Para preguntas sobre el despliegue:
1. Ver [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md) para pasos detallados
2. Ver logs en CloudWatch
3. Verificar permisos IAM
4. Revisar configuración de variables de entorno

**Archivos de referencia:**
- `create-aws-resources.ps1` - Crear infraestructura
- `deploy-lambdas.ps1` - Desplegar funciones
- `DEPLOYMENT_GUIDE.md` - Guía paso a paso
- `../README_OrderExport.md` - Documentación completa
