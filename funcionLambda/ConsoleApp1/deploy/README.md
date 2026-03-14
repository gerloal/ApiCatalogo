# ?? Índice de Documentación - Order Export API

## ?? Inicio Rápido

1. **[DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md)** - **EMPEZAR AQUÍ**
   - Guía paso a paso para desplegar todo el sistema
   - Incluye comandos PowerShell listos para usar
   - Troubleshooting común

## ?? Scripts de Despliegue

### Scripts PowerShell (Windows)
- **[create-aws-resources.ps1](./create-aws-resources.ps1)** - Crear toda la infraestructura AWS
- **[deploy-lambdas.ps1](./deploy-lambdas.ps1)** - Desplegar funciones Lambda

### Scripts Bash (Linux/Mac)
- **[create-aws-resources.sh](./create-aws-resources.sh)** - Crear toda la infraestructura AWS
- **[deploy-lambdas.sh](./deploy-lambdas.sh)** - Desplegar funciones Lambda

## ?? Documentación Técnica

### Información de Recursos
- **[RESOURCES_SUMMARY.md](./RESOURCES_SUMMARY.md)**
  - Lista completa de recursos AWS necesarios
  - Diagrama de arquitectura
  - Estimación de costos
  - Checklist de despliegue

### Guías Especializadas
- **[USE_EXISTING_ROLE.md](./USE_EXISTING_ROLE.md)**
  - Cómo usar un rol IAM existente
  - Permisos necesarios a añadir
  - Ejemplos completos

## ?? Documentación del Proyecto

### En el directorio raíz (`../`)
- **[README_OrderExport.md](../README_OrderExport.md)**
  - Documentación completa del sistema
  - Descripción de funciones Lambda
  - Variables de entorno
  - Configuración

- **[EXAMPLES_OrderExport.md](../EXAMPLES_OrderExport.md)**
  - Ejemplos de uso de la API
  - Scripts curl, bash, Python
  - Formato de archivos CSV
  - Errores comunes

- **[IMPLEMENTATION_SUMMARY.md](../IMPLEMENTATION_SUMMARY.md)**
  - Resumen técnico de la implementación
  - Flujo de trabajo
  - Componentes implementados

- **[QUICKSTART.md](../QUICKSTART.md)**
  - Guía rápida de deployment
  - Comandos esenciales
  - Testing básico

- **[template.yaml](../template.yaml)**
  - CloudFormation/SAM template
  - Definición de infraestructura como código

## ??? Recursos AWS Necesarios

| Recurso | Archivo de Creación | Descripción |
|---------|---------------------|-------------|
| DynamoDB Table | `create-aws-resources.*` | Almacena estado de jobs |
| S3 Bucket | `create-aws-resources.*` | Almacena archivos CSV |
| SQS Queue | `create-aws-resources.*` | Cola de trabajos |
| SQS DLQ | `create-aws-resources.*` | Dead Letter Queue |
| Lambda API | `deploy-lambdas.*` | API REST handler |
| Lambda Worker | `deploy-lambdas.*` | Procesador SQS |
| API Gateway | Manual / `template.yaml` | REST API endpoints |
| Secrets Manager | Manual | Credenciales SP-API |
| IAM Role | `create-aws-resources.*` o existente | Permisos |

## ?? Flujo de Despliegue Recomendado

### Opción 1: Con nuevo rol IAM
```powershell
# Paso 1: Crear toda la infraestructura
cd deploy
.\create-aws-resources.ps1 -Environment dev -Region eu-west-1

# Paso 2: Desplegar funciones Lambda
$roleArn = "arn:aws:iam::123456789:role/OrderExportLambdaRole-dev"
.\deploy-lambdas.ps1 -Environment dev -Region eu-west-1 -RoleArn $roleArn

# Paso 3: Configurar API Gateway (manual o con SAM)
# Paso 4: Crear secrets en Secrets Manager
# Paso 5: Probar la API
```

### Opción 2: Con rol IAM existente
```powershell
# Paso 1: Añadir permisos al rol existente
# Ver: USE_EXISTING_ROLE.md

# Paso 2: Crear infraestructura (excepto IAM role)
cd deploy
.\create-aws-resources.ps1 -Environment dev -Region eu-west-1

# Paso 3: Desplegar con rol existente
$roleArn = "arn:aws:iam::123456789:role/TuRolExistente"
.\deploy-lambdas.ps1 -Environment dev -Region eu-west-1 -RoleArn $roleArn

# Paso 4-5: Igual que Opción 1
```

### Opción 3: Con SAM/CloudFormation
```bash
# Usar template.yaml
sam build
sam deploy --guided
```

## ?? Arquitectura del Sistema

```
Cliente ? API Gateway ? Lambda API ? DynamoDB
                                   ? SQS ? Lambda Worker ? S3
                                                        ? Secrets Manager
```

## ?? Información Importante

### Variables de Entorno - Lambda API
```
DYNAMODB_TABLE=OrderExportJobs-{env}
SQS_QUEUE_URL=https://sqs.{region}.amazonaws.com/{account}/order-export-queue-{env}
API_KEY=optional-api-key
```

### Variables de Entorno - Lambda Worker
```
DYNAMODB_TABLE=OrderExportJobs-{env}
S3_BUCKET=order-exports-{account}-{env}
```

### Secrets Manager Format
```json
{
  "ClientId": "amzn1.application-oa2-client.xxxxx",
  "ClientSecret": "xxxxx",
  "RefreshToken": "Atzr|xxxxx",
  "MarketPlaceID": "A1RKKUPIHCS9HS",
  "RoleArn": "arn:aws:iam::123456789:role/SPAPIRole",
  "SellerId": "A1XXXXX",
  "TenantId": "tenant-123"
}
```

## ?? Testing

### Crear un export job
```powershell
$endpoint = "https://{api-id}.execute-api.eu-west-1.amazonaws.com/dev"
$body = @{
    tenantId = "tenant-123"
    startDate = "2024-01-01T00:00:00Z"
    endDate = "2024-01-31T23:59:59Z"
    format = "CSV"
} | ConvertTo-Json

Invoke-RestMethod -Uri "$endpoint/exports/orders" -Method Post -Body $body -ContentType "application/json"
```

### Verificar estado
```powershell
Invoke-RestMethod -Uri "$endpoint/exports/orders/{jobId}?tenantId=tenant-123" -Method Get
```

## ?? Monitoreo

### Ver logs
```powershell
# API
aws logs tail /aws/lambda/OrderExportAPI-dev --follow

# Worker
aws logs tail /aws/lambda/OrderExportWorker-dev --follow
```

### Ver métricas
- CloudWatch ? Lambda ? Metrics
- CloudWatch ? DynamoDB ? Metrics
- CloudWatch ? SQS ? Metrics

## ?? Troubleshooting

Ver **[DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md)** sección "Troubleshooting"

Problemas comunes:
- AccessDenied ? Verificar permisos IAM
- ResourceNotFound ? Verificar que recursos están creados
- Lambda timeout ? Aumentar timeout o memory
- 500 error ? Ver logs de CloudWatch

## ?? Costos Estimados

| Uso | Costo Mensual |
|-----|---------------|
| Bajo (100 exports/mes) | ~$3 USD |
| Medio (1000 exports/mes) | ~$25 USD |
| Alto (10000 exports/mes) | ~$200 USD |

Ver detalles en **[RESOURCES_SUMMARY.md](./RESOURCES_SUMMARY.md)**

## ?? Actualizar Código

```powershell
# Solo actualizar código de Lambda (sin cambiar infraestructura)
cd deploy
.\deploy-lambdas.ps1 -Environment dev -Region eu-west-1 -RoleArn {your-role-arn}
```

## ??? Limpiar Recursos

Ver **[DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md)** sección "Limpiar Recursos"

## ?? Soporte

1. Revisar documentación relevante
2. Ver logs en CloudWatch
3. Verificar permisos IAM
4. Revisar configuración de variables de entorno
5. Consultar ejemplos en EXAMPLES_OrderExport.md

## ? Características

- ? API REST asíncrona
- ? Multi-tenant
- ? URLs pre-firmadas temporales (7 días)
- ? Resiliente (DLQ, retries)
- ? Escalable (SQS + Lambda)
- ? Auditable (DynamoDB)
- ? Seguro (Secrets Manager, IAM)

## ?? Notas Adicionales

- Los archivos CSV en S3 se eliminan automáticamente después de 7 días
- Las URLs pre-firmadas expiran después de 7 días
- Lambda Worker timeout: 15 minutos
- Lambda API timeout: 30 segundos
- SQS visibility timeout: 15 minutos
- SQS message retention: 4 días
- DLQ message retention: 14 días

---

**Última actualización:** $(Get-Date)

**Versión:** 1.0.0
