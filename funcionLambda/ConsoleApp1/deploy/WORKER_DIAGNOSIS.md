# ?? Diagnóstico: Worker Lambda No Se Dispara

## ? Problema Principal

El **OrderExportWorker-dev** no procesa los jobs de exportación aunque:
- ? Los jobs se crean correctamente en DynamoDB
- ? Los mensajes llegan a la cola SQS
- ? El Event Source Mapping está habilitado

## ?? Causa Raíz Identificada

**El secret de Amazon SP-API no existe en AWS Secrets Manager**

### Evidencia

1. **No hay logs en CloudWatch** del Worker
   - El log group `/aws/lambda/OrderExportWorker-dev` no existe
   - Esto indica que la función nunca se ejecutó exitosamente

2. **Invocación manual reveló el error:**
   ```
   ResourceNotFoundException: Secret not found
   at SecretManagerService.GetSpApiSecretAsync(...)
   ```

3. **El Worker busca el secret:**
   ```
   Sportandem/prod/order-export
   ```
   Pero este secret **NO EXISTE** en Secrets Manager

## ??? Solución Aplicada

### 1. Código Actualizado ?

**Archivo:** `SecretManagerService.cs`

**Cambio:** Intentar múltiples formatos de nombre de secret con mejor manejo de errores:

```csharp
// Intenta primero: /order-export/prod/tenants/Sportandem/spapi
// Si falla, intenta: Sportandem/prod/order-export
```

**Status:** ? Código actualizado y redespleado

### 2. Secret Pendiente de Crear ??

**Acción Requerida:** Crear el secret con las credenciales de Amazon SP-API

**Formato esperado:**
```
Name: Sportandem/prod/order-export
Value: {
  "ClientId": "amzn1.application-oa2-client.XXX",
  "ClientSecret": "XXX",
  "RefreshToken": "Atzr|XXX",
  "MarketPlaceID": "A1RKKUPIHCS9HS",
  "RoleArn": "arn:aws:iam::340663646958:role/SPAPIRole",
  "SellerId": "XXX",
  "TenantId": "Sportandem"
}
```

## ?? Checklist de Solución

- [x] Diagnosticar por qué el Worker no se dispara
- [x] Identificar que el problema es el secret faltante
- [x] Actualizar `SecretManagerService.cs` para manejar múltiples formatos
- [x] Redesplegar OrderExportWorker con el código actualizado
- [x] Recrear Event Source Mapping SQS ? Lambda
- [ ] **PENDIENTE: Crear secret en AWS Secrets Manager**
- [ ] **PENDIENTE: Verificar que el Worker procesa jobs correctamente**

## ?? Próximos Pasos

### Paso 1: Crear el Secret

**Opción A - AWS CLI:**
```bash
aws secretsmanager create-secret \
    --name "Sportandem/prod/order-export" \
    --description "Amazon SP-API credentials for Sportandem" \
    --secret-string '{
        "ClientId": "TU_CLIENT_ID",
        "ClientSecret": "TU_CLIENT_SECRET",
        "RefreshToken": "TU_REFRESH_TOKEN",
        "MarketPlaceID": "A1RKKUPIHCS9HS",
        "RoleArn": "arn:aws:iam::340663646958:role/SPAPIRole",
        "SellerId": "TU_SELLER_ID",
        "TenantId": "Sportandem"
    }' \
    --region eu-west-1
```

**Opción B - PowerShell:**
```powershell
.\create-sportandem-secret.ps1 `
    -ClientId "TU_CLIENT_ID" `
    -ClientSecret "TU_CLIENT_SECRET" `
    -RefreshToken "TU_REFRESH_TOKEN" `
    -RoleArn "arn:aws:iam::340663646958:role/SPAPIRole" `
    -SellerId "TU_SELLER_ID"
```

**Opción C - AWS Console:**
Ver guía detallada en `CREATE_SECRET_GUIDE.md`

### Paso 2: Verificar Permisos del Role

El role `OrderExportLambdaRole-dev` debe tener permisos para leer el secret:

```bash
# Verificar política inline del role
aws iam get-role-policy \
    --role-name OrderExportLambdaRole-dev \
    --policy-name OrderExportLambdaRolePolicy \
    --region eu-west-1
```

Debe incluir:
```json
{
  "Effect": "Allow",
  "Action": [
    "secretsmanager:GetSecretValue"
  ],
  "Resource": "arn:aws:secretsmanager:eu-west-1:340663646958:secret:Sportandem/prod/order-export*"
}
```

### Paso 3: Probar el Flujo Completo

```powershell
# 1. Crear un nuevo job
.\test-order-export-detailed.ps1

# 2. Monitorear logs del Worker en tiempo real
# (en otra terminal)
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1

# 3. Verificar estado del job después de ~1 minuto
# El job debería pasar de PENDING ? RUNNING ? COMPLETED
```

## ?? Estado Actual del Sistema

| Componente | Estado | Notas |
|------------|--------|-------|
| OrderExportAPI | ? Funcionando | Crea jobs y envía mensajes a SQS |
| DynamoDB Table | ? Funcionando | Jobs se guardan correctamente |
| SQS Queue | ? Funcionando | Mensajes llegan correctamente |
| Event Source Mapping | ? Habilitado | SQS ? Lambda trigger activo |
| OrderExportWorker | ?? Desplegado | Falla por secret faltante |
| Secret en Secrets Manager | ? **NO EXISTE** | **ACCIÓN REQUERIDA** |
| S3 Bucket | ? Existe | order-exports-340663646958-dev |

## ?? Cómo Verificar que Todo Funciona

### 1. Verificar Secret Existe

```bash
aws secretsmanager describe-secret \
    --secret-id "Sportandem/prod/order-export" \
    --region eu-west-1
```

**Resultado esperado:**
```json
{
    "ARN": "arn:aws:secretsmanager:eu-west-1:340663646958:secret:Sportandem/prod/order-export-XXXXX",
    "Name": "Sportandem/prod/order-export",
    "Description": "Amazon SP-API credentials for Sportandem",
    "LastChangedDate": "..."
}
```

### 2. Crear Job y Ver Logs

```powershell
# Terminal 1: Crear job
.\test-order-export-detailed.ps1

# Terminal 2: Ver logs en tiempo real
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1
```

**Logs esperados (exitosos):**
```
START RequestId: xxx
Received 1 SQS message
Attempting to get secret: /order-export/prod/tenants/Sportandem/spapi
Secret not found, trying alternative format: Sportandem/prod/order-export
? Secret found with format: Sportandem/prod/order-export
Processing export job xxx for tenant Sportandem
ClientId: amzn1.application-oa2-client.XXX
RoleArn: arn:aws:iam::340663646958:role/SPAPIRole
Fetching orders from Amazon SP-API...
Generated CSV with X orders
Uploaded to S3: Sportandem/orders_xxx.csv
Job completed successfully
END RequestId: xxx
```

### 3. Verificar Archivo en S3

```bash
aws s3 ls s3://order-exports-340663646958-dev/Sportandem/
```

**Resultado esperado:**
```
2026-01-14 14:30:00      12345 orders_abc123def.csv
```

## ?? Documentación Relacionada

- `CREATE_SECRET_GUIDE.md` - Guía para crear el secret
- `DEPLOYMENT_SUCCESS.md` - Resumen de todo el despliegue
- `ENVIRONMENT_VARIABLES.md` - Variables de entorno
- `diagnose-worker.ps1` - Script de diagnóstico
- `fix-stuck-messages.ps1` - Script para liberar mensajes bloqueados

## ?? Importante

**El Worker NO procesará ningún job hasta que se cree el secret en Secrets Manager.**

Una vez creado el secret:
1. ?? Espera 1-2 minutos
2. ?? Los mensajes en la cola SQS se procesarán automáticamente
3. ? Los jobs pasarán a estado COMPLETED
4. ?? Los archivos CSV aparecerán en S3

---

**Última actualización:** 2026-01-14 14:15
**Estado:** Esperando creación del secret en Secrets Manager
