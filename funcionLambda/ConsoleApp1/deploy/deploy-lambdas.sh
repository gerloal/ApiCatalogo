#!/bin/bash

# Script para desplegar funciones Lambda para Order Export API
# Uso: ./deploy-lambdas.sh <environment> <region> <role-arn>

set -e

# Parámetros
ENV=${1:-dev}
REGION=${2:-eu-west-1}
ROLE_ARN=${3}

if [ -z "$ROLE_ARN" ]; then
    echo "Error: Role ARN es requerido"
    echo "Uso: ./deploy-lambdas.sh <environment> <region> <role-arn>"
    echo "Ejemplo: ./deploy-lambdas.sh dev eu-west-1 arn:aws:iam::123456789:role/OrderExportLambdaRole-dev"
    exit 1
fi

# Obtener Account ID
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
QUEUE_URL="https://sqs.$REGION.amazonaws.com/$ACCOUNT_ID/order-export-$ENVIRONMENT"

echo "==========================================="
echo "Desplegando funciones Lambda"
echo "Environment: $ENV"
echo "Region: $REGION"
echo "Role ARN: $ROLE_ARN"
echo "==========================================="

# Directorio del proyecto
PROJECT_DIR="../"
DEPLOY_DIR="./package"

# 1. Compilar proyecto
echo ""
echo "1. Compilando proyecto .NET..."
cd "$PROJECT_DIR"
dotnet clean
dotnet restore
dotnet build -c Release

# 2. Publicar para Lambda
echo ""
echo "2. Publicando para Lambda (linux-arm64)..."
dotnet publish -c Release -r linux-arm64 --self-contained false -o ./publish

# 3. Crear paquete ZIP
echo ""
echo "3. Creando paquete ZIP..."
cd ./publish
zip -r ../lambda-package.zip . -q
cd ..

PACKAGE_PATH="./lambda-package.zip"

if [ ! -f "$PACKAGE_PATH" ]; then
    echo "Error: No se pudo crear el paquete ZIP"
    exit 1
fi

echo "Paquete creado: $PACKAGE_PATH ($(du -h $PACKAGE_PATH | cut -f1))"

# 4. Crear o actualizar Lambda: OrderExportAPI
echo ""
echo "4. Desplegando Lambda: OrderExportAPI-${ENV}..."

# Verificar si existe
if aws lambda get-function --function-name "OrderExportAPI-${ENV}" --region $REGION &>/dev/null; then
    echo "Actualizando función existente..."
    aws lambda update-function-code \
        --function-name "OrderExportAPI-${ENV}" \
        --zip-file "fileb://${PACKAGE_PATH}" \
        --region $REGION \
        --no-cli-pager
    
    # Esperar a que esté lista
    aws lambda wait function-updated \
        --function-name "OrderExportAPI-${ENV}" \
        --region $REGION
    
    # Actualizar configuración
    aws lambda update-function-configuration \
        --function-name "OrderExportAPI-${ENV}" \
        --timeout 30 \
        --memory-size 512 \
        --environment "Variables={
            DYNAMODB_TABLE=OrderExportJobs-${ENV},
            SQS_QUEUE_URL=${QUEUE_URL},
            API_KEY=
        }" \
        --region $REGION \
        --no-cli-pager
else
    echo "Creando nueva función..."
    aws lambda create-function \
        --function-name "OrderExportAPI-${ENV}" \
        --runtime dotnet8 \
        --role "$ROLE_ARN" \
        --handler "ProcessFileOnQueue::FuncionLambda.LambdaServices::FunctionHandler" \
        --zip-file "fileb://${PACKAGE_PATH}" \
        --timeout 30 \
        --memory-size 512 \
        --architectures arm64 \
        --environment "Variables={
            DYNAMODB_TABLE=OrderExportJobs-${ENV},
            SQS_QUEUE_URL=${QUEUE_URL},
            API_KEY=
        }" \
        --tags Environment=$ENV,Application=OrderExport \
        --region $REGION \
        --no-cli-pager
fi

API_FUNCTION_ARN=$(aws lambda get-function \
    --function-name "OrderExportAPI-${ENV}" \
    --region $REGION \
    --query 'Configuration.FunctionArn' \
    --output text)

echo "? OrderExportAPI-${ENV} desplegada: $API_FUNCTION_ARN"

# 5. Crear o actualizar Lambda: OrderExportWorker
echo ""
echo "5. Desplegando Lambda: OrderExportWorker-${ENV}..."

# Verificar si existe
if aws lambda get-function --function-name "OrderExportWorker-${ENV}" --region $REGION &>/dev/null; then
    echo "Actualizando función existente..."
    aws lambda update-function-code \
        --function-name "OrderExportWorker-${ENV}" \
        --zip-file "fileb://${PACKAGE_PATH}" \
        --region $REGION \
        --no-cli-pager
    
    # Esperar a que esté lista
    aws lambda wait function-updated \
        --function-name "OrderExportWorker-${ENV}" \
        --region $REGION
    
    # Actualizar configuración
    aws lambda update-function-configuration \
        --function-name "OrderExportWorker-${ENV}" \
        --timeout 900 \
        --memory-size 1024 \
        --environment "Variables={
            DYNAMODB_TABLE=OrderExportJobs-${ENV},
            S3_BUCKET=order-exports-${ACCOUNT_ID}-${ENV}
        }" \
        --region $REGION \
        --no-cli-pager
else
    echo "Creando nueva función..."
    aws lambda create-function \
        --function-name "OrderExportWorker-${ENV}" \
        --runtime dotnet8 \
        --role "$ROLE_ARN" \
        --handler "ProcessFileOnQueue::FuncionLambda.OrderExportWorker::FunctionHandler" \
        --zip-file "fileb://${PACKAGE_PATH}" \
        --timeout 900 \
        --memory-size 1024 \
        --architectures arm64 \
        --environment "Variables={
            DYNAMODB_TABLE=OrderExportJobs-${ENV},
            S3_BUCKET=order-exports-${ACCOUNT_ID}-${ENV}
        }" \
        --tags Environment=$ENV,Application=OrderExport \
        --region $REGION \
        --no-cli-pager
fi

WORKER_FUNCTION_ARN=$(aws lambda get-function \
    --function-name "OrderExportWorker-${ENV}" \
    --region $REGION \
    --query 'Configuration.FunctionArn' \
    --output text)

echo "? OrderExportWorker-${ENV} desplegada: $WORKER_FUNCTION_ARN"

# 6. Configurar SQS como trigger para OrderExportWorker
echo ""
echo "6. Configurando SQS trigger para OrderExportWorker..."

QUEUE_ARN="arn:aws:sqs:${REGION}:${ACCOUNT_ID}:order-export-${ENV}"

# Eliminar event source mapping existente si hay
EXISTING_UUID=$(aws lambda list-event-source-mappings \
    --function-name "OrderExportWorker-${ENV}" \
    --region $REGION \
    --query "EventSourceMappings[?EventSourceArn=='${QUEUE_ARN}'].UUID" \
    --output text 2>/dev/null || echo "")

if [ ! -z "$EXISTING_UUID" ]; then
    echo "Eliminando event source mapping existente..."
    aws lambda delete-event-source-mapping \
        --uuid "$EXISTING_UUID" \
        --region $REGION \
        --no-cli-pager
    sleep 5
fi

# Crear nuevo event source mapping
aws lambda create-event-source-mapping \
    --function-name "OrderExportWorker-${ENV}" \
    --event-source-arn "$QUEUE_ARN" \
    --batch-size 1 \
    --enabled \
    --region $REGION \
    --no-cli-pager

echo "? SQS trigger configurado"

# 7. Crear API Gateway
echo ""
echo "7. Configurando API Gateway..."
echo "??  Nota: API Gateway debe configurarse manualmente o con SAM/CloudFormation"
echo "    Endpoints requeridos:"
echo "      - POST /exports/orders ? OrderExportAPI-${ENV}"
echo "      - GET /exports/orders/{jobId} ? OrderExportAPI-${ENV}"

# Limpiar archivos temporales
echo ""
echo "8. Limpiando archivos temporales..."
rm -rf ./publish
rm -f ./lambda-package.zip

echo ""
echo "==========================================="
echo "? Despliegue completado exitosamente!"
echo "==========================================="
echo ""
echo "?? Funciones Lambda desplegadas:"
echo "  - OrderExportAPI-${ENV}: $API_FUNCTION_ARN"
echo "  - OrderExportWorker-${ENV}: $WORKER_FUNCTION_ARN"
echo ""
echo "?? Próximos pasos:"
echo "  1. Configurar API Gateway manualmente o usar template.yaml con SAM"
echo "  2. Crear secrets en Secrets Manager para cada tenant:"
echo "     aws secretsmanager create-secret --name '{tenantId}/prod/order-export' --secret-string '...'"
echo "  3. Probar la API con:"
echo "     curl -X POST {api-endpoint}/exports/orders -d '{...}'"
echo ""
