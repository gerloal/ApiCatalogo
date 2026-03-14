#!/bin/bash

# Script para crear recursos AWS necesarios para Order Export API
# Uso: ./create-aws-resources.sh <environment> <region> <account-id>

set -e

# Parámetros
ENV=${1:-dev}
REGION=${2:-eu-west-1}
ACCOUNT_ID=${3:-$(aws sts get-caller-identity --query Account --output text)}

echo "==========================================="
echo "Creando recursos para Order Export API"
echo "Environment: $ENV"
echo "Region: $REGION"
echo "Account ID: $ACCOUNT_ID"
echo "==========================================="

# 1. Crear DynamoDB Table
echo ""
echo "1. Creando tabla DynamoDB: OrderExportJobs-${ENV}..."
aws dynamodb create-table \
    --table-name "OrderExportJobs-${ENV}" \
    --attribute-definitions \
        AttributeName=pk,AttributeType=S \
        AttributeName=sk,AttributeType=S \
    --key-schema \
        AttributeName=pk,KeyType=HASH \
        AttributeName=sk,KeyType=RANGE \
    --billing-mode PAY_PER_REQUEST \
    --tags Key=Environment,Value=$ENV Key=Application,Value=OrderExport \
    --region $REGION \
    --no-cli-pager || echo "Tabla ya existe"

# 2. Crear S3 Bucket
echo ""
echo "2. Creando bucket S3: order-exports-${ACCOUNT_ID}-${ENV}..."
aws s3api create-bucket \
    --bucket "order-exports-${ACCOUNT_ID}-${ENV}" \
    --region $REGION \
    --create-bucket-configuration LocationConstraint=$REGION \
    --no-cli-pager || echo "Bucket ya existe"

# Configurar encriptación
aws s3api put-bucket-encryption \
    --bucket "order-exports-${ACCOUNT_ID}-${ENV}" \
    --server-side-encryption-configuration '{
        "Rules": [{
            "ApplyServerSideEncryptionByDefault": {
                "SSEAlgorithm": "AES256"
            }
        }]
    }' \
    --region $REGION \
    --no-cli-pager

# Bloquear acceso público
aws s3api put-public-access-block \
    --bucket "order-exports-${ACCOUNT_ID}-${ENV}" \
    --public-access-block-configuration \
        BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true \
    --region $REGION \
    --no-cli-pager

# Configurar lifecycle para eliminar después de 7 días
aws s3api put-bucket-lifecycle-configuration \
    --bucket "order-exports-${ACCOUNT_ID}-${ENV}" \
    --lifecycle-configuration '{
        "Rules": [{
            "Id": "DeleteOldExports",
            "Status": "Enabled",
            "Expiration": {
                "Days": 7
            },
            "Prefix": "exports/"
        }]
    }' \
    --region $REGION \
    --no-cli-pager

# 3. Crear Dead Letter Queue
echo ""
echo "3. Creando Dead Letter Queue: order-export-dlq-${ENV}..."
DLQ_URL=$(aws sqs create-queue \
    --queue-name "order-export-dlq-${ENV}" \
    --attributes MessageRetentionPeriod=1209600 \
    --tags Environment=$ENV,Application=OrderExport \
    --region $REGION \
    --query 'QueueUrl' \
    --output text \
    --no-cli-pager || aws sqs get-queue-url --queue-name "order-export-dlq-${ENV}" --region $REGION --query 'QueueUrl' --output text)

DLQ_ARN=$(aws sqs get-queue-attributes \
    --queue-url "$DLQ_URL" \
    --attribute-names QueueArn \
    --region $REGION \
    --query 'Attributes.QueueArn' \
    --output text \
    --no-cli-pager)

echo "DLQ ARN: $DLQ_ARN"

# 4. Crear SQS Queue principal
echo ""
echo "4. Creando SQS Queue: order-export-${ENV}..."

# Lambda Worker timeout es 900 segundos (15 min)
# Visibility timeout debe ser al menos 6x mayor = 5400 segundos (90 min)
VISIBILITY_TIMEOUT=5400

QUEUE_URL=$(aws sqs create-queue \
    --queue-name "order-export-${ENV}" \
    --attributes \
        VisibilityTimeout=${VISIBILITY_TIMEOUT},\
        MessageRetentionPeriod=345600,\
        RedrivePolicy="{\"deadLetterTargetArn\":\"${DLQ_ARN}\",\"maxReceiveCount\":\"3\"}" \
    --tags Environment=$ENV,Application=OrderExport \
    --region $REGION \
    --query 'QueueUrl' \
    --output text \
    --no-cli-pager || aws sqs get-queue-url --queue-name "order-export-${ENV}" --region $REGION --query 'QueueUrl' --output text)

echo "Queue URL: $QUEUE_URL"
echo "? Queue configurada con Visibility Timeout: ${VISIBILITY_TIMEOUT} segundos (90 minutos)"

# 5. Crear Role IAM para Lambda (si no usas uno existente)
echo ""
echo "5. Creando IAM Role: OrderExportLambdaRole-${ENV}..."
TRUST_POLICY='{
  "Version": "2012-10-17",
  "Statement": [{
    "Effect": "Allow",
    "Principal": {
      "Service": "lambda.amazonaws.com"
    },
    "Action": "sts:AssumeRole"
  }]
}'

aws iam create-role \
    --role-name "OrderExportLambdaRole-${ENV}" \
    --assume-role-policy-document "$TRUST_POLICY" \
    --tags Key=Environment,Value=$ENV Key=Application,Value=OrderExport \
    --no-cli-pager || echo "Role ya existe"

# Adjuntar política básica de Lambda
aws iam attach-role-policy \
    --role-name "OrderExportLambdaRole-${ENV}" \
    --policy-arn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole" \
    --no-cli-pager || echo "Política ya adjunta"

# Crear y adjuntar política personalizada
POLICY_DOCUMENT="{
  \"Version\": \"2012-10-17\",
  \"Statement\": [
    {
      \"Effect\": \"Allow\",
      \"Action\": [
        \"dynamodb:PutItem\",
        \"dynamodb:GetItem\",
        \"dynamodb:UpdateItem\",
        \"dynamodb:Query\"
      ],
      \"Resource\": \"arn:aws:dynamodb:${REGION}:${ACCOUNT_ID}:table/OrderExportJobs-${ENV}\"
    },
    {
      \"Effect\": \"Allow\",
      \"Action\": [
        \"s3:PutObject\",
        \"s3:GetObject\"
      ],
      \"Resource\": \"arn:aws:s3:::order-exports-${ACCOUNT_ID}-${ENV}/*\"
    },
    {
      \"Effect\": \"Allow\",
      \"Action\": [
        \"sqs:SendMessage\",
        \"sqs:ReceiveMessage\",
        \"sqs:DeleteMessage\",
        \"sqs:GetQueueAttributes\"
      ],
      \"Resource\": [
        \"arn:aws:sqs:${REGION}:${ACCOUNT_ID}:order-export-queue-${ENV}\",
        \"arn:aws:sqs:${REGION}:${ACCOUNT_ID}:order-export-dlq-${ENV}\"
      ]
    },
    {
      \"Effect\": \"Allow\",
      \"Action\": [
        \"secretsmanager:GetSecretValue\"
      ],
      \"Resource\": \"arn:aws:secretsmanager:${REGION}:${ACCOUNT_ID}:secret:*/prod/order-export-*\"
    }
  ]
}"

aws iam put-role-policy \
    --role-name "OrderExportLambdaRole-${ENV}" \
    --policy-name "OrderExportPolicy" \
    --policy-document "$POLICY_DOCUMENT" \
    --no-cli-pager || echo "Política ya existe"

echo ""
echo "==========================================="
echo "? Recursos creados exitosamente!"
echo "==========================================="
echo ""
echo "?? Resumen de recursos:"
echo "  - DynamoDB: OrderExportJobs-${ENV}"
echo "  - S3 Bucket: order-exports-${ACCOUNT_ID}-${ENV}"
echo "  - SQS Queue: order-export-${ENV}"
echo "  - SQS DLQ: order-export-dlq-${ENV}"
echo "  - IAM Role: OrderExportLambdaRole-${ENV}"
echo ""
echo "?? URLs:"
echo "  - Queue URL: $QUEUE_URL"
echo "  - DLQ URL: $DLQ_URL"
echo ""
echo "?? Próximos pasos:"
echo "  1. Compilar y empaquetar las funciones Lambda"
echo "  2. Crear las funciones Lambda con deploy-lambdas.sh"
echo "  3. Configurar API Gateway"
echo "  4. Crear secrets en Secrets Manager para cada tenant"
echo ""
