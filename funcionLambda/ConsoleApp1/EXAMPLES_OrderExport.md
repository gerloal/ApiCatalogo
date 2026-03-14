# Ejemplos de Uso - Order Export API

## Configuración

### Variables de entorno
```bash
export API_ENDPOINT="https://your-api-id.execute-api.eu-west-1.amazonaws.com/prod"
export API_KEY="your-api-key-here"
export TENANT_ID="your-tenant-id"
```

## 1. Crear un nuevo job de exportación

### Request
```bash
curl -X POST "${API_ENDPOINT}/exports/orders" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${API_KEY}" \
  -d '{
    "tenantId": "'"${TENANT_ID}"'",
    "startDate": "2024-01-01T00:00:00Z",
    "endDate": "2024-01-31T23:59:59Z",
    "format": "CSV"
  }'
```

### Response (202 Accepted)
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "PENDING",
  "message": "Export job created successfully. Use GET /exports/orders/{jobId} to check status."
}
```

## 2. Verificar el estado del job

### Request con Query Parameter
```bash
JOB_ID="550e8400-e29b-41d4-a716-446655440000"

curl -X GET "${API_ENDPOINT}/exports/orders/${JOB_ID}?tenantId=${TENANT_ID}" \
  -H "X-Api-Key: ${API_KEY}"
```

### Request con Header
```bash
curl -X GET "${API_ENDPOINT}/exports/orders/${JOB_ID}" \
  -H "X-Api-Key: ${API_KEY}" \
  -H "X-Tenant-Id: ${TENANT_ID}"
```

### Response - Estado PENDING (200 OK)
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "your-tenant-id",
  "status": "PENDING",
  "totalOrders": 0,
  "totalLines": 0,
  "headersPresignedUrl": null,
  "linesPresignedUrl": null,
  "errorMessage": null,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:30:00Z"
}
```

### Response - Estado RUNNING (200 OK)
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "your-tenant-id",
  "status": "RUNNING",
  "totalOrders": 0,
  "totalLines": 0,
  "headersPresignedUrl": null,
  "linesPresignedUrl": null,
  "errorMessage": null,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:31:15Z"
}
```

### Response - Estado DONE (200 OK)
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "your-tenant-id",
  "status": "DONE",
  "totalOrders": 150,
  "totalLines": 320,
  "headersPresignedUrl": "https://order-exports-bucket.s3.amazonaws.com/exports/tenant-123/550e8400-e29b-41d4-a716-446655440000_headers.csv?X-Amz-Algorithm=...",
  "linesPresignedUrl": "https://order-exports-bucket.s3.amazonaws.com/exports/tenant-123/550e8400-e29b-41d4-a716-446655440000_lines.csv?X-Amz-Algorithm=...",
  "errorMessage": null,
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:35:30Z"
}
```

### Response - Estado FAILED (200 OK)
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "tenantId": "your-tenant-id",
  "status": "FAILED",
  "totalOrders": 0,
  "totalLines": 0,
  "headersPresignedUrl": null,
  "linesPresignedUrl": null,
  "errorMessage": "Error obteniendo pedidos de Amazon: Invalid credentials",
  "createdAt": "2024-01-15T10:30:00Z",
  "updatedAt": "2024-01-15T10:31:45Z"
}
```

## 3. Descargar archivos CSV

Una vez que el job está en estado DONE, usa las URLs pre-firmadas:

### Descargar Headers CSV
```bash
curl -o headers.csv "${HEADERS_URL}"
```

### Descargar Lines CSV
```bash
curl -o lines.csv "${LINES_URL}"
```

## Formato de los archivos CSV

### headers.csv
```csv
AmazonOrderId,PurchaseDate,OrderStatus,OrderTotal,Currency,BuyerEmail,BuyerName,ShipServiceLevel,ShippingAddress,City,StateOrRegion,PostalCode,CountryCode
111-1234567-1234567,2024-01-15 10:25:30,Shipped,89.99,EUR,buyer@example.com,John Doe,Standard,123 Main St,Madrid,Madrid,28001,ES
111-1234567-1234568,2024-01-16 14:30:15,Pending,45.50,EUR,buyer2@example.com,Jane Smith,Expedited,456 Oak Ave,Barcelona,Barcelona,08001,ES
```

### lines.csv
```csv
AmazonOrderId,OrderItemId,ASIN,SellerSKU,Title,QuantityOrdered,QuantityShipped,ItemPrice,Currency,ItemTax,ShippingPrice,ShippingTax
111-1234567-1234567,12345678901234,B09H73T814,SKU-001,Product Title 1,2,2,39.99,EUR,8.40,5.00,1.05
111-1234567-1234567,12345678901235,B08K3MNBVC,SKU-002,Product Title 2,1,1,45.00,EUR,9.45,0.00,0.00
111-1234567-1234568,12345678901236,B07XYZ1234,SKU-003,Product Title 3,1,0,45.50,EUR,9.56,0.00,0.00
```

## Errores comunes

### 400 Bad Request - Missing TenantId
```json
{
  "error": "Bad Request",
  "message": "TenantId is required"
}
```

### 400 Bad Request - Invalid Date Range
```json
{
  "error": "Bad Request",
  "message": "Date range cannot exceed 30 days"
}
```

### 401 Unauthorized
```json
{
  "error": "Unauthorized"
}
```

### 404 Not Found
```json
{
  "error": "Not Found",
  "message": "Job 550e8400-e29b-41d4-a716-446655440000 not found for tenant your-tenant-id"
}
```

### 500 Internal Server Error
```json
{
  "error": "Internal Server Error",
  "message": "Error details here"
}
```

## Script completo de ejemplo

```bash
#!/bin/bash

API_ENDPOINT="https://your-api-id.execute-api.eu-west-1.amazonaws.com/prod"
API_KEY="your-api-key-here"
TENANT_ID="your-tenant-id"

# 1. Crear job
echo "Creating export job..."
RESPONSE=$(curl -s -X POST "${API_ENDPOINT}/exports/orders" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${API_KEY}" \
  -d '{
    "tenantId": "'"${TENANT_ID}"'",
    "startDate": "2024-01-01T00:00:00Z",
    "endDate": "2024-01-31T23:59:59Z",
    "format": "CSV"
  }')

JOB_ID=$(echo $RESPONSE | jq -r '.jobId')
echo "Job created: $JOB_ID"

# 2. Esperar y verificar estado
echo "Waiting for job to complete..."
STATUS="PENDING"
while [ "$STATUS" != "DONE" ] && [ "$STATUS" != "FAILED" ]; do
  sleep 10
  
  STATUS_RESPONSE=$(curl -s -X GET "${API_ENDPOINT}/exports/orders/${JOB_ID}?tenantId=${TENANT_ID}" \
    -H "X-Api-Key: ${API_KEY}")
  
  STATUS=$(echo $STATUS_RESPONSE | jq -r '.status')
  echo "Current status: $STATUS"
done

# 3. Si está DONE, descargar archivos
if [ "$STATUS" == "DONE" ]; then
  echo "Job completed successfully!"
  
  HEADERS_URL=$(echo $STATUS_RESPONSE | jq -r '.headersPresignedUrl')
  LINES_URL=$(echo $STATUS_RESPONSE | jq -r '.linesPresignedUrl')
  
  echo "Downloading headers..."
  curl -o "headers_${JOB_ID}.csv" "$HEADERS_URL"
  
  echo "Downloading lines..."
  curl -o "lines_${JOB_ID}.csv" "$LINES_URL"
  
  echo "Download complete!"
  echo "Headers file: headers_${JOB_ID}.csv"
  echo "Lines file: lines_${JOB_ID}.csv"
else
  echo "Job failed!"
  ERROR_MSG=$(echo $STATUS_RESPONSE | jq -r '.errorMessage')
  echo "Error: $ERROR_MSG"
fi
```

## Integración con Python

```python
import requests
import time
import json

API_ENDPOINT = "https://your-api-id.execute-api.eu-west-1.amazonaws.com/prod"
API_KEY = "your-api-key-here"
TENANT_ID = "your-tenant-id"

def create_export_job(tenant_id, start_date, end_date):
    """Crea un nuevo job de exportación"""
    url = f"{API_ENDPOINT}/exports/orders"
    headers = {
        "Content-Type": "application/json",
        "X-Api-Key": API_KEY
    }
    data = {
        "tenantId": tenant_id,
        "startDate": start_date,
        "endDate": end_date,
        "format": "CSV"
    }
    
    response = requests.post(url, headers=headers, json=data)
    response.raise_for_status()
    return response.json()

def get_job_status(tenant_id, job_id):
    """Obtiene el estado de un job"""
    url = f"{API_ENDPOINT}/exports/orders/{job_id}"
    headers = {
        "X-Api-Key": API_KEY,
        "X-Tenant-Id": tenant_id
    }
    
    response = requests.get(url, headers=headers)
    response.raise_for_status()
    return response.json()

def download_file(url, filename):
    """Descarga un archivo desde una URL"""
    response = requests.get(url)
    response.raise_for_status()
    
    with open(filename, 'wb') as f:
        f.write(response.content)

def export_orders(tenant_id, start_date, end_date):
    """Proceso completo de exportación"""
    # Crear job
    print("Creating export job...")
    job_response = create_export_job(tenant_id, start_date, end_date)
    job_id = job_response['jobId']
    print(f"Job created: {job_id}")
    
    # Esperar a que termine
    print("Waiting for job to complete...")
    while True:
        status = get_job_status(tenant_id, job_id)
        print(f"Status: {status['status']}")
        
        if status['status'] == 'DONE':
            print("Job completed successfully!")
            
            # Descargar archivos
            print("Downloading files...")
            download_file(status['headersPresignedUrl'], f'headers_{job_id}.csv')
            download_file(status['linesPresignedUrl'], f'lines_{job_id}.csv')
            print("Download complete!")
            return True
            
        elif status['status'] == 'FAILED':
            print(f"Job failed: {status.get('errorMessage')}")
            return False
            
        time.sleep(10)

# Uso
if __name__ == "__main__":
    export_orders(
        TENANT_ID,
        "2024-01-01T00:00:00Z",
        "2024-01-31T23:59:59Z"
    )
```

## Notas importantes

1. **URLs Pre-firmadas**: Las URLs tienen una validez de 7 días. Descarga los archivos antes de que expiren.

2. **Rate Limiting**: La API de Amazon SP tiene límites de tasa. El sistema implementa delays entre peticiones.

3. **Rango de fechas**: El máximo rango es de 30 días por petición.

4. **Tiempo de procesamiento**: Dependiendo del número de pedidos, el job puede tardar de 1 a 15 minutos.

5. **Retries**: Si un job falla, puedes crear uno nuevo. Los mensajes fallidos van a una DLQ después de 3 intentos.

6. **Seguridad**: Nunca compartas tus API Keys ni URLs pre-firmadas públicamente.
