# Comandos para probar Order Export API - Tenant Sportandem
# ================================================================

## CONFIGURACIÓN
API_ENDPOINT="https://TU-API-ID.execute-api.eu-west-1.amazonaws.com/dev"  # ? CAMBIAR
TENANT_ID="Sportandem"
API_KEY="9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy"

## 1. CREAR JOB DE EXPORTACIÓN
## ================================================================

# Crear job para los últimos 30 días
curl -X POST "${API_ENDPOINT}/exports/orders" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${API_KEY}" \
  -d '{
    "tenantId": "Sportandem",
    "startDate": "2024-12-01T00:00:00Z",
    "endDate": "2024-12-31T23:59:59Z",
    "format": "CSV"
  }'

# Respuesta esperada (guardar el jobId):
# {
#   "jobId": "550e8400-e29b-41d4-a716-446655440000",
#   "status": "PENDING",
#   "message": "Export job created successfully..."
# }


## 2. VERIFICAR ESTADO DEL JOB
## ================================================================

# Reemplazar {jobId} con el ID obtenido en el paso 1
JOB_ID="550e8400-e29b-41d4-a716-446655440000"  # ? CAMBIAR

curl -X GET "${API_ENDPOINT}/exports/orders/${JOB_ID}?tenantId=${TENANT_ID}" \
  -H "X-Api-Key: ${API_KEY}"

# Estados posibles:
# - PENDING: En cola, esperando procesamiento
# - RUNNING: Procesando pedidos
# - DONE: Completado, URLs disponibles
# - FAILED: Error (ver errorMessage)


## 3. DESCARGAR ARCHIVOS (cuando status = DONE)
## ================================================================

# La respuesta cuando está DONE incluye URLs pre-firmadas:
# {
#   "jobId": "...",
#   "status": "DONE",
#   "totalOrders": 150,
#   "totalLines": 320,
#   "headersPresignedUrl": "https://s3.amazonaws.com/...",
#   "linesPresignedUrl": "https://s3.amazonaws.com/..."
# }

# Descargar headers.csv
curl -o "sportandem_headers.csv" "{headersPresignedUrl}"

# Descargar lines.csv
curl -o "sportandem_lines.csv" "{linesPresignedUrl}"


## EJEMPLO COMPLETO CON BASH
## ================================================================

#!/bin/bash

API_ENDPOINT="https://TU-API-ID.execute-api.eu-west-1.amazonaws.com/dev"
TENANT_ID="Sportandem"
API_KEY="9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy"

# Calcular fechas (últimos 30 días)
END_DATE=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
START_DATE=$(date -u -d "30 days ago" +"%Y-%m-%dT%H:%M:%SZ")

echo "Creando job de exportación..."
echo "Fechas: $START_DATE a $END_DATE"

# Crear job
RESPONSE=$(curl -s -X POST "${API_ENDPOINT}/exports/orders" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${API_KEY}" \
  -d "{
    \"tenantId\": \"${TENANT_ID}\",
    \"startDate\": \"${START_DATE}\",
    \"endDate\": \"${END_DATE}\",
    \"format\": \"CSV\"
  }")

echo "Response: $RESPONSE"

# Extraer jobId
JOB_ID=$(echo $RESPONSE | grep -o '"jobId":"[^"]*' | cut -d'"' -f4)
echo "Job ID: $JOB_ID"

# Monitorear estado
echo "Monitoreando estado..."
while true; do
  sleep 10
  
  STATUS_RESPONSE=$(curl -s -X GET "${API_ENDPOINT}/exports/orders/${JOB_ID}?tenantId=${TENANT_ID}" \
    -H "X-Api-Key: ${API_KEY}")
  
  STATUS=$(echo $STATUS_RESPONSE | grep -o '"status":"[^"]*' | cut -d'"' -f4)
  echo "Status: $STATUS"
  
  if [ "$STATUS" = "DONE" ]; then
    echo "? Exportación completada!"
    echo $STATUS_RESPONSE | jq .
    
    # Extraer URLs
    HEADERS_URL=$(echo $STATUS_RESPONSE | grep -o '"headersPresignedUrl":"[^"]*' | cut -d'"' -f4)
    LINES_URL=$(echo $STATUS_RESPONSE | grep -o '"linesPresignedUrl":"[^"]*' | cut -d'"' -f4)
    
    # Descargar archivos
    echo "Descargando archivos..."
    curl -o "headers_${JOB_ID}.csv" "$HEADERS_URL"
    curl -o "lines_${JOB_ID}.csv" "$LINES_URL"
    
    echo "? Archivos descargados!"
    break
  elif [ "$STATUS" = "FAILED" ]; then
    echo "? Exportación falló!"
    echo $STATUS_RESPONSE | jq .
    break
  fi
done


## VERIFICACIÓN DE SECRETS MANAGER
## ================================================================

# Antes de probar, verifica que existe el secret para Sportandem
aws secretsmanager describe-secret \
  --secret-id "Sportandem/prod/order-export" \
  --region eu-west-1

# Si no existe, créalo:
aws secretsmanager create-secret \
  --name "Sportandem/prod/order-export" \
  --description "Amazon SP-API credentials for Sportandem" \
  --secret-string '{
    "ClientId": "amzn1.application-oa2-client.xxxxx",
    "ClientSecret": "xxxxx",
    "RefreshToken": "Atzr|xxxxx",
    "MarketPlaceID": "A1RKKUPIHCS9HS",
    "RoleArn": "arn:aws:iam::123456789:role/SPAPIRole",
    "SellerId": "A1XXXXX",
    "TenantId": "Sportandem",
    "ClientEmail": "email@sportandem.com",
    "ClientPartnerEmail": "partner@sportandem.com"
  }' \
  --region eu-west-1


## TROUBLESHOOTING
## ================================================================

# Ver logs de Lambda API
aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1

# Ver logs de Lambda Worker
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1

# Ver mensajes en SQS
aws sqs receive-message \
  --queue-url "https://sqs.eu-west-1.amazonaws.com/{account-id}/order-export-dev" \
  --region eu-west-1

# Ver items en DynamoDB
aws dynamodb scan \
  --table-name OrderExportJobs-dev \
  --filter-expression "tenantId = :tid" \
  --expression-attribute-values '{":tid":{"S":"Sportandem"}}' \
  --region eu-west-1

# Listar archivos en S3
aws s3 ls s3://order-exports-{account-id}-dev/exports/Sportandem/ --region eu-west-1
