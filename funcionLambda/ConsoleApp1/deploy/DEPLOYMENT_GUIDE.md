# Guía de Despliegue - Order Export API

## 📋 Prerequisitos

1. **AWS CLI** configurado con credenciales apropiadas
2. **.NET 8 SDK** instalado
3. **Permisos AWS** necesarios para crear recursos

## 🚀 Despliegue Paso a Paso

### Paso 1: Crear Recursos AWS

Desde PowerShell en el directorio `deploy/`:

```powershell
# Navegar al directorio deploy
cd deploy

# Ejecutar script de creación de recursos
.\create-aws-resources.ps1 -Environment dev -Region eu-west-1
```

Esto creará:
- ✅ Tabla DynamoDB: `OrderExportJobs-dev`
- ✅ Bucket S3: `order-exports-{accountId}-dev`
- ✅ Cola SQS: `order-export-queue-dev`
- ✅ DLQ: `order-export-dlq-dev`
- ✅ Role IAM: `OrderExportLambdaRole-dev`

**Salida esperada:**
```
✅ Recursos creados exitosamente!

📋 Resumen de recursos:
  - DynamoDB: OrderExportJobs-dev
  - S3 Bucket: order-exports-123456789-dev
  - SQS Queue: order-export-queue-dev
  ...
```

### Paso 2: Desplegar Funciones Lambda

```powershell
# Obtener el ARN del rol creado (o usar uno existente)
$roleArn = "arn:aws:iam::123456789:role/OrderExportLambdaRole-dev"
# O si usas un rol existente:
# $roleArn = "arn:aws:iam::123456789:role/TuRolExistente"

# Desplegar lambdas
.\deploy-lambdas.ps1 -Environment dev -Region eu-west-1 -RoleArn $roleArn
```

**Esto hará:**
1. Compilar el proyecto .NET
2. Publicar para linux-arm64
3. Crear paquete ZIP
4. Crear/actualizar función `OrderExportAPI-dev`
5. Crear/actualizar función `OrderExportWorker-dev`
6. Configurar SQS trigger

**Salida esperada:**
```
✅ Despliegue completado exitosamente!

📋 Funciones Lambda desplegadas:
  - OrderExportAPI-dev
    ARN: arn:aws:lambda:eu-west-1:123456789:function:OrderExportAPI-dev
  - OrderExportWorker-dev
    ARN: arn:aws:lambda:eu-west-1:123456789:function:OrderExportWorker-dev
```

### Paso 3: Configurar API Gateway

#### Opción A: Manual (AWS Console)

1. **Ir a API Gateway** en AWS Console
2. **Crear API REST**
   - Nombre: `OrderExportAPI-dev`
   - Tipo: REST API
3. **Crear recursos:**
   ```
   /
   └── exports
       └── orders
           └── {jobId}
   ```
4. **Configurar métodos:**

   **POST en `/exports/orders`:**
   - Integration type: Lambda Function
   - Lambda Function: `OrderExportAPI-dev`
   - Use Lambda Proxy integration: ✅ Sí
   
   **GET en `/exports/orders/{jobId}`:**
   - Integration type: Lambda Function
   - Lambda Function: `OrderExportAPI-dev`
   - Use Lambda Proxy integration: ✅ Sí

5. **Habilitar CORS:**
   - Seleccionar cada recurso → Actions → Enable CORS
   - Configurar:
     - Access-Control-Allow-Origin: `*` (o tu dominio)
     - Access-Control-Allow-Headers: `Content-Type,X-Api-Key,X-Tenant-Id`
     - Access-Control-Allow-Methods: `GET,POST,OPTIONS`

6. **Deploy API:**
   - Actions → Deploy API
   - Stage name: `dev`
   - Copiar el **Invoke URL**

#### Opción B: AWS CLI

```powershell
# Crear API
$apiId = aws apigateway create-rest-api --name "OrderExportAPI-dev" --region eu-west-1 --query 'id' --output text

# Obtener root resource
$rootId = aws apigateway get-resources --rest-api-id $apiId --region eu-west-1 --query 'items[?path==`/`].id' --output text

# Crear recursos
$exportsId = aws apigateway create-resource --rest-api-id $apiId --parent-id $rootId --path-part exports --region eu-west-1 --query 'id' --output text
$ordersId = aws apigateway create-resource --rest-api-id $apiId --parent-id $exportsId --path-part orders --region eu-west-1 --query 'id' --output text
$jobIdId = aws apigateway create-resource --rest-api-id $apiId --parent-id $ordersId --path-part '{jobId}' --region eu-west-1 --query 'id' --output text

# Crear método POST
aws apigateway put-method --rest-api-id $apiId --resource-id $ordersId --http-method POST --authorization-type NONE --region eu-west-1

# Integrar con Lambda (sustituir ARN)
$lambdaArn = "arn:aws:lambda:eu-west-1:123456789:function:OrderExportAPI-dev"
aws apigateway put-integration --rest-api-id $apiId --resource-id $ordersId --http-method POST --type AWS_PROXY --integration-http-method POST --uri "arn:aws:apigateway:eu-west-1:lambda:path/2015-03-31/functions/$lambdaArn/invocations" --region eu-west-1

# Dar permiso a API Gateway para invocar Lambda
aws lambda add-permission --function-name OrderExportAPI-dev --statement-id apigateway-post --action lambda:InvokeFunction --principal apigateway.amazonaws.com --source-arn "arn:aws:execute-api:eu-west-1:123456789:$apiId/*/POST/exports/orders" --region eu-west-1

# Repetir para GET...
# (similar al POST pero con GET y el resource jobIdId)

# Deploy
aws apigateway create-deployment --rest-api-id $apiId --stage-name dev --region eu-west-1

# Obtener endpoint
Write-Host "API Endpoint: https://$apiId.execute-api.eu-west-1.amazonaws.com/dev"
```

### Paso 4: Configurar Secrets Manager

Para cada tenant que vaya a usar el sistema:

```powershell
# Crear secret para un tenant
aws secretsmanager create-secret `
    --name "tenant-123/prod/order-export" `
    --description "Amazon SP-API credentials for tenant-123" `
    --secret-string '{
        "ClientId": "amzn1.application-oa2-client.xxxxx",
        "ClientSecret": "xxxxx",
        "RefreshToken": "Atzr|xxxxx",
        "MarketPlaceID": "A1RKKUPIHCS9HS",
        "RoleArn": "arn:aws:iam::123456789:role/SPAPIRole",
        "SellerId": "A1XXXXX",
        "TenantId": "tenant-123"
    }' `
    --region eu-west-1
```

**Nota:** Necesitas obtener estas credenciales desde Amazon Seller Central.

### Paso 5: Probar la API

#### Crear un export job:

```powershell
$apiEndpoint = "https://your-api-id.execute-api.eu-west-1.amazonaws.com/dev"

$body = @{
    tenantId = "tenant-123"
    startDate = "2024-01-01T00:00:00Z"
    endDate = "2024-01-31T23:59:59Z"
    format = "CSV"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "$apiEndpoint/exports/orders" -Method Post -Body $body -ContentType "application/json"
$response

# Guardar jobId
$jobId = $response.jobId
Write-Host "Job ID: $jobId"
```

#### Verificar estado:

```powershell
$status = Invoke-RestMethod -Uri "$apiEndpoint/exports/orders/${jobId}?tenantId=tenant-123" -Method Get
$status

# Cuando status sea "DONE", descargar archivos
if ($status.status -eq "DONE") {
    Invoke-WebRequest -Uri $status.headersPresignedUrl -OutFile "headers.csv"
    Invoke-WebRequest -Uri $status.linesPresignedUrl -OutFile "lines.csv"
    Write-Host "✅ Archivos descargados: headers.csv, lines.csv"
}
```

## 🔧 Usando un Rol Existente

Si prefieres usar un rol IAM existente en lugar de crear uno nuevo:

1. **Añadir permisos al rol existente:**

```powershell
# Nombre del rol existente
$roleName = "TuRolExistente"

# Política a añadir
$policyDocument = @'
{
  "Version": "2012-10-17",
  "Statement": [
    {
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
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject"
      ],
      "Resource": "arn:aws:s3:::order-exports-*/*"
    },
    {
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
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": "arn:aws:secretsmanager:*:*:secret:*/prod/order-export-*"
    }
  ]
}
'@

# Añadir política inline
aws iam put-role-policy `
    --role-name $roleName `
    --policy-name "OrderExportPolicy" `
    --policy-document $policyDocument `
    --region eu-west-1
```

2. **Desplegar con el rol existente:**

```powershell
# Obtener ARN del rol
$roleArn = aws iam get-role --role-name $roleName --query 'Role.Arn' --output text

# Desplegar
.\deploy-lambdas.ps1 -Environment dev -Region eu-west-1 -RoleArn $roleArn
```

## 📊 Verificar Despliegue

### Ver logs de Lambda:

```powershell
# Logs de API
aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1

# Logs de Worker
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1
```

### Ver mensajes en SQS:

```powershell
# Ver atributos de la cola
aws sqs get-queue-attributes `
    --queue-url "https://sqs.eu-west-1.amazonaws.com/123456789/order-export-queue-dev" `
    --attribute-names All `
    --region eu-west-1
```

### Ver items en DynamoDB:

```powershell
# Listar jobs
aws dynamodb scan `
    --table-name OrderExportJobs-dev `
    --max-items 10 `
    --region eu-west-1
```

### Ver archivos en S3:

```powershell
# Listar exports
aws s3 ls s3://order-exports-123456789-dev/exports/ --recursive --region eu-west-1
```

## 🧹 Limpiar Recursos (si es necesario)

```powershell
# Eliminar funciones Lambda
aws lambda delete-function --function-name OrderExportAPI-dev --region eu-west-1
aws lambda delete-function --function-name OrderExportWorker-dev --region eu-west-1

# Eliminar API Gateway
aws apigateway delete-rest-api --rest-api-id {api-id} --region eu-west-1

# Vaciar y eliminar bucket S3
aws s3 rm s3://order-exports-123456789-dev --recursive --region eu-west-1
aws s3 rb s3://order-exports-123456789-dev --region eu-west-1

# Eliminar colas SQS
aws sqs delete-queue --queue-url {queue-url} --region eu-west-1
aws sqs delete-queue --queue-url {dlq-url} --region eu-west-1

# Eliminar tabla DynamoDB
aws dynamodb delete-table --table-name OrderExportJobs-dev --region eu-west-1

# Eliminar role IAM (si fue creado por el script)
aws iam delete-role-policy --role-name OrderExportLambdaRole-dev --policy-name OrderExportPolicy
aws iam detach-role-policy --role-name OrderExportLambdaRole-dev --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole
aws iam delete-role --role-name OrderExportLambdaRole-dev
```

## 🐛 Troubleshooting

### Error: "AccessDenied" al crear recursos
- Verificar que tu usuario AWS tiene permisos para crear recursos
- Verificar que AWS CLI está configurado correctamente: `aws sts get-caller-identity`

### Error: "ResourceAlreadyExists"
- Los recursos ya existen, puedes continuar con el siguiente paso

### Lambda no recibe mensajes de SQS
- Verificar que el event source mapping está activo
- Verificar permisos del rol Lambda
- Ver logs de CloudWatch

### Error 500 en API Gateway
- Ver logs de Lambda en CloudWatch
- Verificar que las variables de entorno están configuradas
- Verificar que el Secret Manager tiene el secret del tenant

## 📚 Referencias

- [README_OrderExport.md](../README_OrderExport.md)
- [EXAMPLES_OrderExport.md](../EXAMPLES_OrderExport.md)
- [QUICKSTART.md](../QUICKSTART.md)
