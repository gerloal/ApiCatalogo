# ? CloudWatch Log Groups Creados

## ?? Resumen

Los log groups de CloudWatch se han creado exitosamente:

### Log Groups Creados

| Log Group | ARN | Retention |
|-----------|-----|-----------|
| `/aws/lambda/OrderExportAPI-dev` | `arn:aws:logs:eu-west-1:340663646958:log-group:/aws/lambda/OrderExportAPI-dev` | 7 días |
| `/aws/lambda/OrderExportWorker-dev` | `arn:aws:logs:eu-west-1:340663646958:log-group:/aws/lambda/OrderExportWorker-dev` | 7 días |

## ?? Cómo se Crearon

### Opción 1: Script Automático (Usado) ?

```powershell
.\create-lambda-log-groups.ps1
```

Este script:
- ? Verifica si los log groups ya existen
- ? Crea los log groups si no existen
- ? Configura retention policy de 7 días
- ? Muestra el estado de cada log group

### Opción 2: Desde AWS CLI

```bash
# Crear log group
aws logs create-log-group \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --region eu-west-1

# Configurar retention
aws logs put-retention-policy \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --retention-in-days 7 \
    --region eu-west-1
```

### Opción 3: Automático al Desplegar

El script `deploy-lambdas.ps1` ahora crea automáticamente los log groups en el paso 7.

## ?? Verificar Log Groups

### Ver todos los log groups de Lambda

```bash
aws logs describe-log-groups \
    --log-group-name-prefix /aws/lambda/ \
    --region eu-west-1 \
    --query 'logGroups[].[logGroupName,creationTime,retentionInDays,storedBytes]' \
    --output table
```

### Ver log group específico

```bash
aws logs describe-log-groups \
    --log-group-name-prefix /aws/lambda/OrderExportWorker-dev \
    --region eu-west-1
```

## ?? Ver Logs en Tiempo Real

### OrderExportAPI

```bash
aws logs tail /aws/lambda/OrderExportAPI-dev --follow --region eu-west-1
```

### OrderExportWorker

```bash
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1
```

### Ver últimos logs (sin follow)

```bash
# Últimos 10 minutos
aws logs tail /aws/lambda/OrderExportWorker-dev --since 10m --region eu-west-1

# Última hora
aws logs tail /aws/lambda/OrderExportWorker-dev --since 1h --region eu-west-1

# Desde una fecha específica
aws logs tail /aws/lambda/OrderExportWorker-dev --since 2026-01-14T14:00:00 --region eu-west-1
```

## ?? Buscar en Logs

### Buscar errores

```bash
aws logs filter-log-events \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --filter-pattern "ERROR" \
    --region eu-west-1
```

### Buscar por tenant

```bash
aws logs filter-log-events \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --filter-pattern "Sportandem" \
    --region eu-west-1
```

### Buscar por JobId

```bash
aws logs filter-log-events \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --filter-pattern "3d84e9e6-04cf-4844-9c5b-5b7073d42487" \
    --region eu-west-1
```

## ??? Eliminar Logs (Opcional)

### Eliminar log streams antiguos

Los logs se eliminan automáticamente después de 7 días según la retention policy.

### Cambiar retention policy

```bash
# Cambiar a 14 días
aws logs put-retention-policy \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --retention-in-days 14 \
    --region eu-west-1

# Cambiar a indefinido (no se eliminan)
aws logs delete-retention-policy \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --region eu-west-1
```

### Eliminar log group completamente

```bash
aws logs delete-log-group \
    --log-group-name /aws/lambda/OrderExportWorker-dev \
    --region eu-west-1
```

## ?? Consola Web de AWS

También puedes ver los logs desde la consola web:

1. Ve a **CloudWatch** ? **Log groups**
2. Busca `/aws/lambda/OrderExportWorker-dev`
3. Click en el log group
4. Verás todos los **Log streams** (cada ejecución de Lambda crea un stream)
5. Click en un stream para ver los logs

**URL directa:**
```
https://eu-west-1.console.aws.amazon.com/cloudwatch/home?region=eu-west-1#logsV2:log-groups/log-group/$252Faws$252Flambda$252FOrderExportWorker-dev
```

## ?? Próximos Pasos

### 1. Crear el Secret en Secrets Manager

El Worker seguirá fallando hasta que crees el secret:

```bash
aws secretsmanager create-secret \
    --name "Sportandem/prod/order-export" \
    --secret-string '{
        "ClientId": "amzn1.application-oa2-client.TU_CLIENT_ID",
        "ClientSecret": "TU_CLIENT_SECRET",
        "RefreshToken": "Atzr|TU_REFRESH_TOKEN",
        "MarketPlaceID": "A1RKKUPIHCS9HS",
        "RoleArn": "arn:aws:iam::340663646958:role/SPAPIRole",
        "SellerId": "TU_SELLER_ID",
        "TenantId": "Sportandem"
    }' \
    --region eu-west-1
```

Ver guía completa: `CREATE_SECRET_GUIDE.md`

### 2. Probar el Flujo Completo

```powershell
# Terminal 1: Ver logs en tiempo real
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1

# Terminal 2: Crear un job
.\test-order-export-detailed.ps1
```

### 3. Verificar que el Worker Procesa el Job

En los logs deberías ver:
```
START RequestId: xxx
Processing export job xxx for tenant Sportandem
Attempting to get secret: /catalog-api/prod/tenants/Sportandem/spapi
Secret not found, trying alternative format: Sportandem/prod/order-export
? Secret found with format: Sportandem/prod/order-export
ClientId: amzn1.application-oa2-client.XXX
RoleArn: arn:aws:iam::340663646958:role/SPAPIRole
Fetching orders from Amazon SP-API...
Generated CSV with X orders
Uploaded to S3: Sportandem/orders_xxx.csv
Job completed successfully
END RequestId: xxx
```

## ? Estado Actual

- [x] **Log groups creados**
- [x] **Retention policy configurado (7 días)**
- [ ] **Pendiente: Crear secret en Secrets Manager**
- [ ] **Pendiente: Verificar que el Worker procesa jobs**

---

**Creado:** 2026-01-14 14:25
**Log Groups:**
- `/aws/lambda/OrderExportAPI-dev` ?
- `/aws/lambda/OrderExportWorker-dev` ?
