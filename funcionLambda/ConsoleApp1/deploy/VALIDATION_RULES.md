# Validaciones de ExportOrdersRequest

## ? Validaciones Activas

### 1. **Request Body Requerido**
```
Error: "Request body is required"
```
El cuerpo de la petición no puede estar vacío.

### 2. **TenantId Requerido**
```
Error: "TenantId is required"
```
El campo `TenantId` es obligatorio y no puede estar vacío.

### 3. **StartDate Requerido**
```
Error: "StartDate is required"
```
El campo `StartDate` es obligatorio y no puede ser la fecha por defecto (default).

### 4. **EndDate Requerido**
```
Error: "EndDate is required"
```
El campo `EndDate` es obligatorio y no puede ser la fecha por defecto (default).

### 5. **StartDate debe ser anterior a EndDate**
```
Error: "StartDate must be before EndDate"
```
La fecha de inicio debe ser anterior o igual a la fecha de fin.

**Ejemplo válido:**
```json
{
  "StartDate": "2024-01-01T00:00:00Z",
  "EndDate": "2024-01-31T00:00:00Z"
}
```

**Ejemplo inválido:**
```json
{
  "StartDate": "2024-01-31T00:00:00Z",
  "EndDate": "2024-01-01T00:00:00Z"  ? EndDate anterior a StartDate
}
```

### 6. **Rango Máximo de 30 Días**
```
Error: "Date range cannot exceed 30 days"
```
El rango entre `StartDate` y `EndDate` no puede superar 30 días.

**Ejemplo válido:**
```json
{
  "StartDate": "2024-01-01T00:00:00Z",
  "EndDate": "2024-01-30T00:00:00Z"  ? 29 días
}
```

**Ejemplo inválido:**
```json
{
  "StartDate": "2024-01-01T00:00:00Z",
  "EndDate": "2024-02-15T00:00:00Z"  ? 45 días
}
```

### 7. **Formato CSV Únicamente**
```
Error: "Only CSV format is supported"
```
Si se especifica el campo `Format`, debe ser "CSV" (case-insensitive).

**Ejemplos válidos:**
```json
{ "Format": "CSV" }    ?
{ "Format": "csv" }    ?
{ "Format": "Csv" }    ?
```

**Ejemplo inválido:**
```json
{ "Format": "JSON" }   ?
```

---

## ? Validaciones ELIMINADAS

### ~~EndDate no puede estar en el futuro~~ (ELIMINADA)
```
? Error: "EndDate cannot be in the future"  ? YA NO APLICA
```

**Motivo de eliminación:** Es válido exportar órdenes hasta una fecha futura. Por ejemplo:
- Exportar órdenes programadas
- Exportar órdenes con fecha de entrega futura
- Incluir todas las órdenes actuales hasta el momento de la ejecución

**Ahora válido:**
```json
{
  "StartDate": "2024-01-01T00:00:00Z",
  "EndDate": "2025-12-31T23:59:59Z"  ? Fecha futura permitida
}
```

---

## ?? Ejemplo de Petición Válida

```json
{
  "TenantId": "Sportandem",
  "StartDate": "2024-12-01T00:00:00Z",
  "EndDate": "2024-12-31T23:59:59Z",
  "Format": "CSV"
}
```

## ?? Ejemplos de Peticiones con Fechas Futuras (Ahora Válidas)

### Exportar hasta mañana
```json
{
  "TenantId": "Sportandem",
  "StartDate": "2024-12-01T00:00:00Z",
  "EndDate": "2024-12-26T00:00:00Z",
  "Format": "CSV"
}
```

### Exportar órdenes del próximo mes
```json
{
  "TenantId": "Sportandem",
  "StartDate": "2025-01-01T00:00:00Z",
  "EndDate": "2025-01-31T23:59:59Z",
  "Format": "CSV"
}
```

### Exportar órdenes del próximo año (respetando límite de 30 días)
```json
{
  "TenantId": "Sportandem",
  "StartDate": "2025-01-01T00:00:00Z",
  "EndDate": "2025-01-30T00:00:00Z",
  "Format": "CSV"
}
```

---

## ?? Probar Validaciones

### PowerShell
```powershell
# Test con fecha futura (ahora válido)
.\test-order-export-detailed.ps1

# O especificando fechas personalizadas
$body = @{
    TenantId = "Sportandem"
    StartDate = "2024-12-01T00:00:00Z"
    EndDate = "2025-01-01T00:00:00Z"
    Format = "CSV"
} | ConvertTo-Json

Invoke-RestMethod -Uri "https://your-api.execute-api.eu-west-1.amazonaws.com/dev/exports/orders" `
    -Method Post `
    -Body $body `
    -Headers @{"Content-Type"="application/json"; "X-Api-Key"="your-key"}
```

### cURL
```bash
curl -X POST https://your-api.execute-api.eu-west-1.amazonaws.com/dev/exports/orders \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: your-key" \
  -d '{
    "TenantId": "Sportandem",
    "StartDate": "2024-12-01T00:00:00Z",
    "EndDate": "2025-01-01T00:00:00Z",
    "Format": "CSV"
  }'
```

---

## ?? Desplegar Cambios

Para aplicar la corrección en AWS Lambda:

```powershell
cd ..\ApiCatalogo\funcionLambda\ConsoleApp1\deploy
.\rebuild-and-deploy.ps1
```

O solo actualizar el código:

```powershell
.\deploy-lambdas.ps1 -RoleArn "arn:aws:iam::340663646958:role/LambdaExecutionRole"
```

---

## ?? Notas Importantes

1. **Rango de 30 días sigue aplicando**: Aunque `EndDate` puede estar en el futuro, el rango entre `StartDate` y `EndDate` no puede exceder 30 días.

2. **Fechas en UTC**: Todas las fechas deben estar en formato ISO 8601 con zona horaria UTC (terminadas en `Z`).

3. **Amazon SP-API Limits**: Aunque nuestra API permite fechas futuras, verifica los límites de la API de Amazon SP-API para la obtención de órdenes.

4. **Órdenes Futuras**: Si exportas con fecha futura, solo obtendrás las órdenes que Amazon tenga registradas hasta ese momento. Las órdenes futuras reales solo aparecerán cuando se creen en Amazon.
