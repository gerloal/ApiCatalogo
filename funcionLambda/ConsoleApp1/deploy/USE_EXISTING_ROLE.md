# Usar Rol IAM Existente - Guía Rápida

Si ya tienes un rol IAM que usas para tus funciones Lambda, puedes reutilizarlo en lugar de crear uno nuevo.

## ?? Paso 1: Identificar tu rol existente

```powershell
# Listar roles IAM
aws iam list-roles --query 'Roles[?contains(RoleName, `Lambda`) || contains(RoleName, `App`)].RoleName' --output table

# O buscar por nombre específico
aws iam get-role --role-name TuRolExistente
```

## ? Paso 2: Añadir permisos necesarios

### Opción A: Añadir política inline (recomendado)

```powershell
# Variables
$roleName = "TuRolExistente"  # ? Cambiar por tu rol
$region = "eu-west-1"
$accountId = (aws sts get-caller-identity --query Account --output text)
$env = "dev"

# Crear documento de política
$policyDoc = @"
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "OrderExportDynamoDB",
      "Effect": "Allow",
      "Action": [
        "dynamodb:PutItem",
        "dynamodb:GetItem",
        "dynamodb:UpdateItem",
        "dynamodb:Query"
      ],
      "Resource": "arn:aws:dynamodb:${region}:${accountId}:table/OrderExportJobs-${env}"
    },
    {
      "Sid": "OrderExportS3",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject"
      ],
      "Resource": "arn:aws:s3:::order-exports-${accountId}-${env}/*"
    },
    {
      "Sid": "OrderExportSQS",
      "Effect": "Allow",
      "Action": [
        "sqs:SendMessage",
        "sqs:ReceiveMessage",
        "sqs:DeleteMessage",
        "sqs:GetQueueAttributes"
      ],
      "Resource": [
        "arn:aws:sqs:${region}:${accountId}:order-export-queue-${env}",
        "arn:aws:sqs:${region}:${accountId}:order-export-dlq-${env}"
      ]
    },
    {
      "Sid": "OrderExportSecrets",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": "arn:aws:secretsmanager:${region}:${accountId}:secret:*/prod/order-export-*"
    }
  ]
}
"@

# Añadir política al rol
aws iam put-role-policy `
    --role-name $roleName `
    --policy-name "OrderExportPolicy" `
    --policy-document $policyDoc

Write-Host "? Permisos añadidos al rol: $roleName"

# Obtener ARN del rol
$roleArn = aws iam get-role --role-name $roleName --query 'Role.Arn' --output text
Write-Host "Role ARN: $roleArn"
```

### Opción B: Adjuntar política AWS gestionada + política personalizada

```powershell
# Si tu rol no tiene permisos básicos de Lambda
aws iam attach-role-policy `
    --role-name $roleName `
    --policy-arn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"

# Luego añadir la política inline como en Opción A
```

## ? Paso 3: Verificar permisos

```powershell
# Ver todas las políticas del rol
aws iam list-role-policies --role-name $roleName

# Ver detalle de la política OrderExportPolicy
aws iam get-role-policy --role-name $roleName --policy-name OrderExportPolicy
```

## ?? Paso 4: Desplegar con el rol existente

### Modificar script create-aws-resources.ps1

Si ya tienes el rol, puedes **saltar la creación del rol IAM**:

```powershell
# En create-aws-resources.ps1, comentar la sección 5 (Crear Role IAM)
# O simplemente no ejecutar esa parte

# Ejecutar solo las secciones 1-4:
# 1. DynamoDB
# 2. S3
# 3. SQS DLQ
# 4. SQS Queue
```

### Desplegar Lambdas con rol existente

```powershell
# Obtener ARN del rol
$roleArn = aws iam get-role --role-name TuRolExistente --query 'Role.Arn' --output text

# Desplegar
cd deploy
.\deploy-lambdas.ps1 -Environment dev -Region eu-west-1 -RoleArn $roleArn
```

## ?? Ejemplo Completo

```powershell
# 1. Configurar variables
$roleName = "AppRunnerLambdaRole"  # ? TU ROL EXISTENTE
$env = "dev"
$region = "eu-west-1"

# 2. Obtener Account ID
$accountId = (aws sts get-caller-identity --query Account --output text)
Write-Host "Account ID: $accountId"

# 3. Añadir permisos para Order Export
$policyDoc = @"
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "OrderExportDynamoDB",
      "Effect": "Allow",
      "Action": [
        "dynamodb:PutItem",
        "dynamodb:GetItem",
        "dynamodb:UpdateItem",
        "dynamodb:Query"
      ],
      "Resource": "arn:aws:dynamodb:${region}:${accountId}:table/OrderExportJobs-${env}"
    },
    {
      "Sid": "OrderExportS3",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject"
      ],
      "Resource": "arn:aws:s3:::order-exports-${accountId}-${env}/*"
    },
    {
      "Sid": "OrderExportSQS",
      "Effect": "Allow",
      "Action": [
        "sqs:SendMessage",
        "sqs:ReceiveMessage",
        "sqs:DeleteMessage",
        "sqs:GetQueueAttributes"
      ],
      "Resource": [
        "arn:aws:sqs:${region}:${accountId}:order-export-queue-${env}",
        "arn:aws:sqs:${region}:${accountId}:order-export-dlq-${env}"
      ]
    },
    {
      "Sid": "OrderExportSecrets",
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": "arn:aws:secretsmanager:${region}:${accountId}:secret:*/prod/order-export-*"
    }
  ]
}
"@

aws iam put-role-policy `
    --role-name $roleName `
    --policy-name "OrderExportPolicy" `
    --policy-document $policyDoc

Write-Host "? Permisos añadidos"

# 4. Obtener ARN
$roleArn = aws iam get-role --role-name $roleName --query 'Role.Arn' --output text
Write-Host "Role ARN: $roleArn"

# 5. Crear recursos AWS (sin crear nuevo rol)
cd deploy
.\create-aws-resources.ps1 -Environment $env -Region $region

# 6. Desplegar Lambdas
.\deploy-lambdas.ps1 -Environment $env -Region $region -RoleArn $roleArn

Write-Host "`n? Despliegue completado usando rol existente: $roleName"
```

## ?? Principio de Menor Privilegio

Si quieres ser más restrictivo, puedes limitar los permisos solo a recursos específicos:

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "OrderExportDynamoDBSpecific",
      "Effect": "Allow",
      "Action": [
        "dynamodb:PutItem",
        "dynamodb:GetItem"
      ],
      "Resource": "arn:aws:dynamodb:eu-west-1:123456789:table/OrderExportJobs-dev",
      "Condition": {
        "StringEquals": {
          "aws:RequestedRegion": "eu-west-1"
        }
      }
    },
    {
      "Sid": "OrderExportS3Specific",
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject"
      ],
      "Resource": "arn:aws:s3:::order-exports-123456789-dev/exports/*",
      "Condition": {
        "StringEquals": {
          "s3:x-amz-server-side-encryption": "AES256"
        }
      }
    }
  ]
}
```

## ?? Verificar que funciona

```powershell
# Ver permisos efectivos del rol
aws iam simulate-principal-policy `
    --policy-source-arn "arn:aws:iam::${accountId}:role/$roleName" `
    --action-names `
        dynamodb:PutItem `
        s3:PutObject `
        sqs:SendMessage `
        secretsmanager:GetSecretValue `
    --resource-arns `
        "arn:aws:dynamodb:${region}:${accountId}:table/OrderExportJobs-${env}" `
        "arn:aws:s3:::order-exports-${accountId}-${env}/*" `
        "arn:aws:sqs:${region}:${accountId}:order-export-queue-${env}" `
        "arn:aws:secretsmanager:${region}:${accountId}:secret:tenant-123/prod/order-export-XXXXXX"

# Debería mostrar "allowed" para todas las acciones
```

## ??? Remover permisos (si necesitas)

```powershell
# Eliminar la política OrderExportPolicy del rol
aws iam delete-role-policy --role-name $roleName --policy-name OrderExportPolicy

Write-Host "? Política OrderExportPolicy eliminada de $roleName"
```

## ?? Tips

1. **Nombra las políticas claramente**: Usa nombres descriptivos como `OrderExportPolicy` para facilitar mantenimiento

2. **Documenta los cambios**: Guarda una copia de las políticas que añades

3. **Usa variables de entorno**: Parametriza el environment (dev/prod) para facilitar multi-environment

4. **Revisa permisos existentes**: Antes de añadir, verifica que no estés duplicando permisos

5. **Considera política gestionada**: Si vas a usar esto en múltiples roles, crea una política gestionada customer-managed

## ?? Referencias

- [AWS IAM Best Practices](https://docs.aws.amazon.com/IAM/latest/UserGuide/best-practices.html)
- [Lambda Execution Role](https://docs.aws.amazon.com/lambda/latest/dg/lambda-intro-execution-role.html)
- [DEPLOYMENT_GUIDE.md](./DEPLOYMENT_GUIDE.md)
