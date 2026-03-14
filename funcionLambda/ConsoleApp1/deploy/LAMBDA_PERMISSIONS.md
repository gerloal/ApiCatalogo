# ? Permisos de Lambda Role Configurados

## ?? Problema Solucionado

**El Worker Lambda no generaba logs en CloudWatch** porque el rol `OrderExportLambdaRole-dev` **no tenía permisos para escribir logs**.

### Error Original
```
El grupo de registros /aws/lambda/OrderExportWorker-dev no existe
```

### Causa Raíz
El rol no tenía las políticas necesarias para:
- ? Crear log groups
- ? Crear log streams  
- ? Escribir eventos de log

## ? Solución Aplicada

Se adjuntaron dos políticas AWS Managed al rol:

### 1. AWSLambdaBasicExecutionRole ?

**Permisos incluidos:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
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

**Comando ejecutado:**
```bash
aws iam attach-role-policy \
    --role-name OrderExportLambdaRole-dev \
    --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole
```

### 2. AWSLambdaSQSQueueExecutionRole ?

**Permisos incluidos:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "sqs:ReceiveMessage",
        "sqs:DeleteMessage",
        "sqs:GetQueueAttributes",
        "logs:CreateLogGroup",
        "logs:CreateLogStream",
        "logs:PutLogEvents"
      ],
      "Resource": "*"
    }
  ]
}
```

**Comando ejecutado:**
```bash
aws iam attach-role-policy \
    --role-name OrderExportLambdaRole-dev \
    --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaSQSQueueExecutionRole
```

## ?? Políticas Actuales del Role

### Managed Policies (Adjuntas)

| Política | Propósito | Status |
|----------|-----------|--------|
| **AWSLambdaBasicExecutionRole** | Permisos básicos de CloudWatch Logs | ? Adjuntada |
| **AWSLambdaSQSQueueExecutionRole** | Permisos de SQS + CloudWatch Logs | ? Adjuntada |
| **OrderProcessingPolicy** | Política custom para DynamoDB, S3 | ? Existente |
| **AmazonCloudDirectoryFullAccess** | (heredada, revisar si es necesaria) | ?? Revisar |

### Inline Policies

| Política | Propósito | Status |
|----------|-----------|--------|
| **SecretsAmazon** | Permisos para leer secrets de SP-API | ? Configurada |

## ? Verificación

### Test de Logs

```powershell
# Invocar Lambda para generar logs
.\force-lambda-execution.ps1

# Ver logs en tiempo real
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1
```

**Resultado esperado:**
```
2026-01-14T15:13:18.356Z INIT_START Runtime Version: dotnet:8.v71
2026-01-14T15:13:18.720Z START RequestId: xxx
2026-01-14T15:13:18.911Z info Processing 1 messages
2026-01-14T15:13:18.956Z info Processing message: {...}
2026-01-14T15:13:18.961Z info Processing export job xxx for tenant TestTenant
```

### Logs Generados ?

Los logs ahora se están generando correctamente:
- ? Log group creado: `/aws/lambda/OrderExportWorker-dev`
- ? Log streams generándose en cada ejecución
- ? Eventos de log visibles en CloudWatch

## ?? Permisos Completos del Role

### CloudWatch Logs
- ? `logs:CreateLogGroup`
- ? `logs:CreateLogStream`
- ? `logs:PutLogEvents`

### SQS
- ? `sqs:ReceiveMessage`
- ? `sqs:DeleteMessage`
- ? `sqs:GetQueueAttributes`

### DynamoDB (desde OrderProcessingPolicy)
- ? `dynamodb:GetItem`
- ? `dynamodb:PutItem`
- ? `dynamodb:UpdateItem`
- ? `dynamodb:Query`
- ? `dynamodb:Scan`

### S3 (desde OrderProcessingPolicy)
- ? `s3:PutObject`
- ? `s3:GetObject`
- ? `s3:ListBucket`

### Secrets Manager (desde SecretsAmazon inline policy)
- ? `secretsmanager:GetSecretValue`
- ? `secretsmanager:DescribeSecret`
- ? `secretsmanager:ListSecretVersionIds`

## ?? Comandos de Verificación

### Ver todas las políticas del role

```bash
# Managed policies
aws iam list-attached-role-policies \
    --role-name OrderExportLambdaRole-dev \
    --output table

# Inline policies
aws iam list-role-policies \
    --role-name OrderExportLambdaRole-dev \
    --output table
```

### Ver contenido de una política

```bash
# Managed policy (requiere ARN)
aws iam get-policy-version \
    --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole \
    --version-id v4

# Inline policy
aws iam get-role-policy \
    --role-name OrderExportLambdaRole-dev \
    --policy-name SecretsAmazon
```

### Verificar permisos con el script

```powershell
.\verify-lambda-permissions.ps1
```

## ?? Permisos Pendientes/Opcionales

### Actualizar Secret de Secrets Manager

Si necesitas crear/actualizar el secret con formato diferente, agregar:

```json
{
  "Effect": "Allow",
  "Action": [
    "secretsmanager:GetSecretValue"
  ],
  "Resource": "arn:aws:secretsmanager:eu-west-1:340663646958:secret:Sportandem/prod/order-export*"
}
```

**O actualizar la política inline `SecretsAmazon` para incluir ambos formatos:**

```bash
# Ver script: .\update-secrets-policy.ps1
```

## ?? Próximos Pasos

### 1. Crear Secret en Secrets Manager ??

El Worker **aún fallará** cuando intente obtener el secret porque no existe:

```bash
aws secretsmanager create-secret \
    --name "Sportandem/prod/order-export" \
    --secret-string '{
        "ClientId": "amzn1.application-oa2-client.TU_CLIENT_ID",
        "ClientSecret": "TU_CLIENT_SECRET",
        "RefreshToken": "Atzr|TU_REFRESH_TOKEN",
        "MarketPlaceID": "A1RKKUPIHCS9HS",
        "RoleArn": "arn:aws:iam::340663646958:role/SPAPIRole",
        "SellerId": "TU_SELLER_ID",
        "TenantId": "Sportandem"
    }' \
    --region eu-west-1
```

Ver guía completa: `CREATE_SECRET_GUIDE.md`

### 2. Probar Flujo Completo

```powershell
# Terminal 1: Ver logs en tiempo real
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1

# Terminal 2: Crear un job de exportación
.\test-order-export-detailed.ps1
```

### 3. Verificar Archivo en S3

```bash
aws s3 ls s3://order-exports-340663646958-dev/Sportandem/ --recursive
```

## ?? Scripts Relacionados

- `verify-lambda-permissions.ps1` - Verifica permisos del role
- `force-lambda-execution.ps1` - Invoca Lambda para generar logs
- `create-lambda-log-groups.ps1` - Crea log groups manualmente
- `CREATE_SECRET_GUIDE.md` - Guía para crear el secret
- `WORKER_DIAGNOSIS.md` - Diagnóstico completo del Worker

## ? Checklist de Configuración

- [x] **Role existe**
- [x] **Permisos de CloudWatch Logs** (`AWSLambdaBasicExecutionRole`)
- [x] **Permisos de SQS** (`AWSLambdaSQSQueueExecutionRole`)
- [x] **Permisos de DynamoDB** (`OrderProcessingPolicy`)
- [x] **Permisos de S3** (`OrderProcessingPolicy`)
- [x] **Permisos de Secrets Manager** (`SecretsAmazon` inline policy)
- [x] **Log groups creados**
- [x] **Event Source Mapping configurado** (SQS ? Lambda)
- [ ] **Secret creado en Secrets Manager** ?? PENDIENTE
- [ ] **Worker procesa jobs exitosamente** ?? PENDIENTE

---

**Última actualización:** 2026-01-14 15:15  
**Estado:** Permisos configurados ? | Secret pendiente de crear ??
