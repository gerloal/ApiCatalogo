# Script PowerShell para crear recursos AWS para Order Export API
# Uso: .\create-aws-resources.ps1 -Environment dev -Region eu-west-1

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$AccountId = ""
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================="
Write-Host "Creando recursos para Order Export API"
Write-Host "Environment: $Environment"
Write-Host "Region: $Region"
Write-Host "==========================================="

# Obtener Account ID si no se proporciona
if ([string]::IsNullOrEmpty($AccountId)) {
    $AccountId = (aws sts get-caller-identity --query Account --output text)
    Write-Host "Account ID detectado: $AccountId"
}

# 1. Crear DynamoDB Table
Write-Host "`n1. Creando tabla DynamoDB: OrderExportJobs-$Environment..."
try {
    aws dynamodb create-table `
        --table-name "OrderExportJobs-$Environment" `
        --attribute-definitions AttributeName=pk,AttributeType=S AttributeName=sk,AttributeType=S `
        --key-schema AttributeName=pk,KeyType=HASH AttributeName=sk,KeyType=RANGE `
        --billing-mode PAY_PER_REQUEST `
        --tags Key=Environment,Value=$Environment Key=Application,Value=OrderExport `
        --region $Region `
        --no-cli-pager
    Write-Host "? Tabla DynamoDB creada"
} catch {
    Write-Host "??  Tabla ya existe o error: $_"
}

# 2. Crear S3 Bucket
Write-Host "`n2. Creando bucket S3: order-exports-$AccountId-$Environment..."
$BucketName = "order-exports-$AccountId-$Environment"

try {
    if ($Region -eq "us-east-1") {
        aws s3api create-bucket --bucket $BucketName --region $Region --no-cli-pager
    } else {
        aws s3api create-bucket --bucket $BucketName --region $Region --create-bucket-configuration LocationConstraint=$Region --no-cli-pager
    }
    Write-Host "? Bucket S3 creado"
} catch {
    Write-Host "??  Bucket ya existe o error: $_"
}

# Configurar encriptación
Write-Host "Configurando encriptación..."
$EncryptionConfig = @'
{
    "Rules": [{
        "ApplyServerSideEncryptionByDefault": {
            "SSEAlgorithm": "AES256"
        }
    }]
}
'@
aws s3api put-bucket-encryption --bucket $BucketName --server-side-encryption-configuration $EncryptionConfig --region $Region --no-cli-pager

# Bloquear acceso público
Write-Host "Bloqueando acceso público..."
aws s3api put-public-access-block `
    --bucket $BucketName `
    --public-access-block-configuration BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true `
    --region $Region `
    --no-cli-pager

# Configurar lifecycle
Write-Host "Configurando lifecycle policy..."
$LifecycleConfig = @'
{
    "Rules": [{
        "Id": "DeleteOldExports",
        "Status": "Enabled",
        "Expiration": {
            "Days": 7
        },
        "Prefix": "exports/"
    }]
}
'@
aws s3api put-bucket-lifecycle-configuration --bucket $BucketName --lifecycle-configuration $LifecycleConfig --region $Region --no-cli-pager

# 3. Crear Dead Letter Queue
Write-Host "`n3. Creando Dead Letter Queue: order-export-dlq-$Environment..."
try {
    $DlqUrl = aws sqs create-queue `
        --queue-name "order-export-dlq-$Environment" `
        --attributes MessageRetentionPeriod=1209600 `
        --tags Environment=$Environment,Application=OrderExport `
        --region $Region `
        --query 'QueueUrl' `
        --output text `
        --no-cli-pager
} catch {
    $DlqUrl = aws sqs get-queue-url --queue-name "order-export-dlq-$Environment" --region $Region --query 'QueueUrl' --output text
}

$DlqArn = aws sqs get-queue-attributes `
    --queue-url $DlqUrl `
    --attribute-names QueueArn `
    --region $Region `
    --query 'Attributes.QueueArn' `
    --output text `
    --no-cli-pager

Write-Host "? DLQ ARN: $DlqArn"

# 4. Crear SQS Queue principal
Write-Host "`n4. Creando SQS Queue: order-export-$Environment..."
$RedrivePolicy = "{`"deadLetterTargetArn`":`"$DlqArn`",`"maxReceiveCount`":`"3`"}"

# Lambda Worker timeout es 900 segundos (15 min)
# Visibility timeout debe ser al menos 6x mayor = 5400 segundos (90 min)
$VisibilityTimeout = 5400

try {
    $QueueUrl = aws sqs create-queue `
        --queue-name "order-export-$Environment" `
        --attributes "VisibilityTimeout=$VisibilityTimeout,MessageRetentionPeriod=345600,RedrivePolicy=$RedrivePolicy" `
        --tags Environment=$Environment,Application=OrderExport `
        --region $Region `
        --query 'QueueUrl' `
        --output text `
        --no-cli-pager
    Write-Host "? Queue creada con Visibility Timeout: $VisibilityTimeout segundos (90 minutos)"
    Write-Host "? Queue URL: $QueueUrl"
} catch {
    $QueueUrl = aws sqs get-queue-url --queue-name "order-export-$Environment" --region $Region --query 'QueueUrl' --output text
    Write-Host "??  Queue ya existe: $QueueUrl"
    Write-Host "??  Actualizando Visibility Timeout..."
    
    aws sqs set-queue-attributes `
        --queue-url $QueueUrl `
        --attributes "VisibilityTimeout=$VisibilityTimeout" `
        --region $Region `
        --no-cli-pager
    
    Write-Host "? Visibility Timeout actualizado a $VisibilityTimeout segundos"
}

# 5. Crear Role IAM para Lambda
Write-Host "`n5. Creando IAM Role: OrderExportLambdaRole-$Environment..."
$TrustPolicy = @'
{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": {
      "Service": "lambda.amazonaws.com"
    },
    "Action": "sts:AssumeRole"
  }]
}
'@

try {
    aws iam create-role `
        --role-name "OrderExportLambdaRole-$Environment" `
        --assume-role-policy-document $TrustPolicy `
        --tags Key=Environment,Value=$Environment Key=Application,Value=OrderExport `
        --no-cli-pager
    Write-Host "? Role IAM creado"
} catch {
    Write-Host "??  Role ya existe"
}

# Adjuntar política básica de Lambda
Write-Host "Adjuntando política básica..."
try {
    aws iam attach-role-policy `
        --role-name "OrderExportLambdaRole-$Environment" `
        --policy-arn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole" `
        --no-cli-pager
} catch {
    Write-Host "??  Política ya adjunta"
}

# Crear y adjuntar política personalizada
Write-Host "Creando política personalizada..."
$PolicyDocument = @"
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
      "Resource": "arn:aws:dynamodb:${Region}:${AccountId}:table/OrderExportJobs-${Environment}"
    },
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject"
      ],
      "Resource": "arn:aws:s3:::order-exports-${AccountId}-${Environment}/*"
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
        "arn:aws:sqs:${Region}:${AccountId}:order-export-queue-${Environment}",
        "arn:aws:sqs:${Region}:${AccountId}:order-export-dlq-${Environment}"
      ]
    },
    {
      "Effect": "Allow",
      "Action": [
        "secretsmanager:GetSecretValue"
      ],
      "Resource": "arn:aws:secretsmanager:${Region}:${AccountId}:secret:*/prod/order-export-*"
    }
  ]
}
"@

try {
    aws iam put-role-policy `
        --role-name "OrderExportLambdaRole-$Environment" `
        --policy-name "OrderExportPolicy" `
        --policy-document $PolicyDocument `
        --no-cli-pager
    Write-Host "? Política personalizada creada"
} catch {
    Write-Host "??  Error creando política: $_"
}

Write-Host "`n==========================================="
Write-Host "? Recursos creados exitosamente!"
Write-Host "==========================================="
Write-Host "`n?? Resumen de recursos:"
Write-Host "  - DynamoDB: OrderExportJobs-$Environment"
Write-Host "  - S3 Bucket: order-exports-$AccountId-$Environment"
Write-Host "  - SQS Queue: order-export-queue-$Environment"
Write-Host "  - SQS DLQ: order-export-dlq-$Environment"
Write-Host "  - IAM Role: OrderExportLambdaRole-$Environment"
Write-Host "`n?? URLs:"
Write-Host "  - Queue URL: $QueueUrl"
Write-Host "  - DLQ URL: $DlqUrl"
Write-Host "`n?? Próximos pasos:"
Write-Host "  1. Compilar y empaquetar las funciones Lambda"
Write-Host "  2. Ejecutar .\deploy-lambdas.ps1"
Write-Host "  3. Configurar API Gateway"
Write-Host "  4. Crear secrets en Secrets Manager para cada tenant"
Write-Host ""

# Guardar información en archivo
$OutputFile = "aws-resources-$Environment.txt"
@"
Environment: $Environment
Region: $Region
Account ID: $AccountId
Created: $(Get-Date)

Resources:
- DynamoDB: OrderExportJobs-$Environment
- S3 Bucket: order-exports-$AccountId-$Environment
- SQS Queue: order-export-queue-$Environment ($QueueUrl)
- SQS DLQ: order-export-dlq-$Environment ($DlqUrl)
- IAM Role: OrderExportLambdaRole-$Environment
- Role ARN: arn:aws:iam::${AccountId}:role/OrderExportLambdaRole-$Environment
"@ | Out-File -FilePath $OutputFile -Encoding UTF8

Write-Host "??  Información guardada en: $OutputFile"
