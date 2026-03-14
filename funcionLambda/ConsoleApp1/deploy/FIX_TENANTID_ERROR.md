# Solución al Error: "TenantId is required"

## ? Problema
```json
{
  "error": "Bad Request",
  "message": "TenantId is required"
}
```

## ?? Causa
El serializador JSON de .NET espera nombres de propiedades en **PascalCase** (`TenantId`) pero se está enviando **camelCase** (`tenantId`).

## ? Soluciones Aplicadas

### 1. Actualizado el Modelo C# (ExportOrdersRequest.cs)
Ahora soporta **ambos formatos**:
```csharp
public class ExportOrdersRequest
{
    [JsonPropertyName("tenantId")]  // ? Soporta camelCase
    public string TenantId { get; set; }
    
    [JsonPropertyName("startDate")]
    public DateTime StartDate { get; set; }
    
    [JsonPropertyName("endDate")]
    public DateTime EndDate { get; set; }
    
    [JsonPropertyName("format")]
    public string Format { get; set; } = "CSV";
}
```

### 2. Configurado el Serializador (LambdaServices.cs)
```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true  // ? Case-insensitive
};
exportRequest = JsonSerializer.Deserialize<ExportOrdersRequest>(request.Body, options);
```

### 3. Actualizado el Script de Test
Ahora usa **PascalCase** por defecto:
```powershell
$body = @{
    TenantId = "Sportandem"    # ? PascalCase
    StartDate = "2024-01-01T00:00:00Z"
    EndDate = "2024-01-31T00:00:00Z"
    Format = "CSV"
} | ConvertTo-Json
```

## ?? Cómo Probar

### Opción 1: Recompilar y Redesplegar
```powershell
cd ..\ApiCatalogo\funcionLambda\ConsoleApp1\deploy
.\rebuild-and-deploy.ps1
```

### Opción 2: Solo Actualizar Código (sin cambiar handlers)
```powershell
cd ..\ApiCatalogo\funcionLambda\ConsoleApp1\deploy
.\deploy-lambdas.ps1 -RoleArn "arn:aws:iam::340663646958:role/LambdaExecutionRole"
```

### Opción 3: Test Detallado
```powershell
# Con PascalCase (recomendado)
.\test-order-export-detailed.ps1

# Con camelCase (también funciona ahora)
.\test-order-export-detailed.ps1 -UseCamelCase
```

## ?? Formatos JSON Soportados

### ? PascalCase (Recomendado)
```json
{
  "TenantId": "Sportandem",
  "StartDate": "2024-01-01T00:00:00Z",
  "EndDate": "2024-01-31T00:00:00Z",
  "Format": "CSV"
}
```

### ? camelCase (También funciona)
```json
{
  "tenantId": "Sportandem",
  "startDate": "2024-01-01T00:00:00Z",
  "endDate": "2024-01-31T00:00:00Z",
  "format": "CSV"
}
```

### ? Mixto (También funciona)
```json
{
  "TenantId": "Sportandem",
  "startDate": "2024-01-01T00:00:00Z",
  "EndDate": "2024-01-31T00:00:00Z",
  "format": "CSV"
}
```

## ?? Ejemplo de Petición curl

### Con PascalCase
```bash
curl -X POST https://q0wnv840ik.execute-api.eu-west-1.amazonaws.com/dev/exports/orders \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: 9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy" \
  -d '{
    "TenantId": "Sportandem",
    "StartDate": "2024-01-01T00:00:00Z",
    "EndDate": "2024-01-31T00:00:00Z",
    "Format": "CSV"
  }'
```

### Con camelCase
```bash
curl -X POST https://q0wnv840ik.execute-api.eu-west-1.amazonaws.com/dev/exports/orders \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: 9pQiqbPvCd7S0T5EysMQG8Nm7BRN58KI4F8q8SZy" \
  -d '{
    "tenantId": "Sportandem",
    "startDate": "2024-01-01T00:00:00Z",
    "endDate": "2024-01-31T00:00:00Z",
    "format": "CSV"
  }'
```

## ?? Verificar que Funciona

Después de redesplegar:

```powershell
# 1. Ejecutar test
.\test-order-export-api.ps1

# 2. O test detallado
.\test-order-export-detailed.ps1

# 3. Verificar logs
aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1
```

## ? Si Aún No Funciona

1. **Verificar que el código se desplegó**:
```bash
aws lambda get-function --function-name OrderExportAPI-dev --region eu-west-1 --query 'Configuration.LastModified'
```

2. **Verificar variables de entorno**:
```powershell
.\verify-env-vars.ps1
```

3. **Ver logs en CloudWatch**:
```bash
aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1
```

4. **Test manual con curl**:
```bash
# Copiar el comando de arriba y ejecutar
```

## ?? Archivos Modificados

1. ? `Models/ExportOrdersRequest.cs` - Agregado `[JsonPropertyName]`
2. ? `LambdaServices.cs` - Agregado `PropertyNameCaseInsensitive = true`
3. ? `test-order-export-api.ps1` - Cambiado a PascalCase
4. ? `test-order-export-detailed.ps1` - Nuevo script con diagnóstico

## ?? Próximos Pasos

1. Redesplegar la Lambda con los cambios
2. Ejecutar test: `.\test-order-export-api.ps1`
3. Si funciona, el job debe crearse y aparecer en DynamoDB
4. El Worker Lambda procesará el mensaje de SQS
5. El archivo CSV se generará en S3
