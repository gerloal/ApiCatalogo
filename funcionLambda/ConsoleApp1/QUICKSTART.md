# Quick Start - Deploy Order Export API

## ?? Prerequisitos

- AWS CLI configurado
- SAM CLI instalado
- .NET 8 SDK instalado
- Permisos AWS adecuados

## ?? Deployment Rápido

### 1. Compilar y empaquetar

```bash
# Desde el directorio del proyecto
cd "C:\Users\germa\source\repos\ApiCatalogo\funcionLambda\ConsoleApp1"

# Limpiar y compilar
dotnet clean
dotnet build -c Release

# Publicar para Lambda
dotnet publish -c Release -r linux-arm64 --self-contained false

# Crear paquete ZIP
cd bin/Release/net8.0/linux-arm64/publish
zip -r ../../../../../../lambda-package.zip .
cd ../../../../../..
```

### 2. Deploy con SAM

```bash
# Primera vez (crea el stack)
sam deploy \
  --template-file template.yaml \
  --stack-name order-export-api-dev \
  --parameter-overrides Environment=dev ApiKey="" \
  --capabilities CAPABILITY_IAM \
  --region eu-west-1

# Actualizaciones posteriores (solo código)
sam build
sam deploy
```

### 3. Configurar Secrets Manager

Para cada tenant, crear un secret:

```bash
aws secretsmanager create-secret \
  --name "tenant-123/prod/order-export" \
  --description "Amazon SP-API credentials for tenant-123" \
  --secret-string '{
    "ClientId": "amzn1.application-oa2-client.xxxxx",
    "ClientSecret": "xxxxx",
    "RefreshToken": "Atzr|xxxxx",
    "MarketPlaceID": "A1RKKUPIHCS9HS",
    "RoleArn": "arn:aws:iam::123456789012:role/SPAPIRole",
    "SellerId": "A1XXXXX",
    "AccessKey": "",
    "SecretKey": "",
    "TenantId": "tenant-123"
  }' \
  --region eu-west-1
```

### 4. Obtener endpoint de la API

```bash
# Obtener el endpoint de API Gateway
aws cloudformation describe-stacks \
  --stack-name order-export-api-dev \
  --query 'Stacks[0].Outputs[?OutputKey==`ApiEndpoint`].OutputValue' \
  --output text \
  --region eu-west-1
```

### 5. Probar la API

```bash
# Configurar variables
export API_ENDPOINT="https://xxxxx.execute-api.eu-west-1.amazonaws.com/dev"
export TENANT_ID="tenant-123"

# Crear un export job
curl -X POST "${API_ENDPOINT}/exports/orders" \
  -H "Content-Type: application/json" \
  -d '{
    "tenantId": "'"${TENANT_ID}"'",
    "startDate": "2024-01-01T00:00:00Z",
    "endDate": "2024-01-31T23:59:59Z",
    "format": "CSV"
  }'

# Verificar estado (reemplazar {jobId} con el ID devuelto)
curl -X GET "${API_ENDPOINT}/exports/orders/{jobId}?tenantId=${TENANT_ID}"
```

## ?? Configuración Producción

### 1. Activar API Key

```bash
# Actualizar stack con API Key
sam deploy \
  --parameter-overrides Environment=prod ApiKey="your-secure-api-key-here"
```

### 2. Configurar alarmas SNS

```bash
# Crear tópico SNS para alarmas
aws sns create-topic --name order-export-alarms

# Suscribirse al tópico
aws sns subscribe \
  --topic-arn arn:aws:sns:eu-west-1:123456789012:order-export-alarms \
  --protocol email \
  --notification-endpoint your-email@example.com
```

### 3. Ajustar configuración

Editar `template.yaml`:
- Timeout: 900 segundos (ajustar según necesidad)
- MemorySize: 1024 MB para worker (ajustar según volumen)
- S3 Lifecycle: 7 días (ajustar según retención requerida)

## ?? Verificar Deployment

### CloudWatch Logs
```bash
# Logs de API
aws logs tail /aws/lambda/OrderExportAPI-dev --follow

# Logs de Worker
aws logs tail /aws/lambda/OrderExportWorker-dev --follow
```

### DynamoDB
```bash
# Listar items
aws dynamodb scan \
  --table-name OrderExportJobs-dev \
  --max-items 10
```

### SQS
```bash
# Ver mensajes en cola
aws sqs get-queue-attributes \
  --queue-url https://sqs.eu-west-1.amazonaws.com/123456789012/order-export-queue-dev \
  --attribute-names All
```

### S3
```bash
# Listar archivos
aws s3 ls s3://order-exports-123456789012-dev/exports/ --recursive
```

## ?? Actualizar solo código Lambda

```bash
# Compilar
dotnet publish -c Release -r linux-arm64 --self-contained false

# Crear ZIP
cd bin/Release/net8.0/linux-arm64/publish
zip -r lambda-update.zip .

# Actualizar LambdaServices
aws lambda update-function-code \
  --function-name OrderExportAPI-dev \
  --zip-file fileb://lambda-update.zip \
  --region eu-west-1

# Actualizar OrderExportWorker
aws lambda update-function-code \
  --function-name OrderExportWorker-dev \
  --zip-file fileb://lambda-update.zip \
  --region eu-west-1
```

## ?? Limpiar recursos

```bash
# Eliminar stack (cuidado: borra todo)
sam delete --stack-name order-export-api-dev --region eu-west-1

# Eliminar secrets
aws secretsmanager delete-secret \
  --secret-id "tenant-123/prod/order-export" \
  --force-delete-without-recovery \
  --region eu-west-1

# Vaciar y eliminar bucket S3 (si no se eliminó automáticamente)
aws s3 rm s3://order-exports-123456789012-dev --recursive
aws s3 rb s3://order-exports-123456789012-dev
```

## ?? Troubleshooting

### Error: "Function not found"
```bash
# Verificar que las funciones existen
aws lambda list-functions --region eu-west-1 | grep OrderExport
```

### Error: "Access Denied" en S3
```bash
# Verificar permisos del rol Lambda
aws iam get-role-policy \
  --role-name order-export-api-dev-OrderExportWorkerRole-xxx \
  --policy-name OrderExportWorkerRolePolicy
```

### Error: "Secret not found"
```bash
# Verificar que el secret existe
aws secretsmanager describe-secret \
  --secret-id "tenant-123/prod/order-export" \
  --region eu-west-1
```

### Mensajes en DLQ
```bash
# Ver mensajes en DLQ
aws sqs receive-message \
  --queue-url https://sqs.eu-west-1.amazonaws.com/123456789012/order-export-dlq-dev \
  --max-number-of-messages 10
```

## ?? Referencias

- [README_OrderExport.md](./README_OrderExport.md) - Documentación completa
- [EXAMPLES_OrderExport.md](./EXAMPLES_OrderExport.md) - Ejemplos de uso
- [IMPLEMENTATION_SUMMARY.md](./IMPLEMENTATION_SUMMARY.md) - Resumen de implementación
- [template.yaml](./template.yaml) - CloudFormation template

## ?? Tips

1. **Desarrollo local**: Usa SAM Local para testing:
   ```bash
   sam local invoke OrderExportAPI -e test-event.json
   ```

2. **Logs en tiempo real**: 
   ```bash
   sam logs -n OrderExportWorker --tail
   ```

3. **Testing de API Gateway local**:
   ```bash
   sam local start-api
   ```

4. **Variables de entorno**: Edita `template.yaml` para configuración personalizada

5. **Costos**: Monitorea con AWS Cost Explorer, especialmente S3 y Lambda invocations
