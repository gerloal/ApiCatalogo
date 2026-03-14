# Guía Rápida: Crear Secret de Sportandem

## ? Problema Identificado

El Worker Lambda falla porque **el secret de Sportandem no existe** en AWS Secrets Manager.

**Error:**
```
ResourceNotFoundException: Secret not found
```

## ? Solución: Crear el Secret

### Formato del Secret

El Worker busca el secret con este nombre:
```
Sportandem/prod/order-export
```

### Opción 1: Crear desde AWS CLI (Recomendado)

```bash
aws secretsmanager create-secret \
    --name "Sportandem/prod/order-export" \
    --description "Amazon SP-API credentials for Sportandem" \
    --secret-string '{
        "ClientId": "amzn1.application-oa2-client.YOUR_CLIENT_ID",
        "ClientSecret": "YOUR_CLIENT_SECRET",
        "RefreshToken": "Atzr|YOUR_REFRESH_TOKEN",
        "MarketPlaceID": "A1RKKUPIHCS9HS",
        "RoleArn": "arn:aws:iam::340663646958:role/SPAPIRole",
        "SellerId": "YOUR_SELLER_ID",
        "TenantId": "Sportandem"
    }' \
    --region eu-west-1
```

### Opción 2: Crear desde la Consola de AWS

1. Ve a **AWS Secrets Manager** ? **Store a new secret**
2. Selecciona **Other type of secret**
3. En **Key/value pairs**, selecciona **Plaintext** y pega:

```json
{
  "ClientId": "amzn1.application-oa2-client.YOUR_CLIENT_ID",
  "ClientSecret": "YOUR_CLIENT_SECRET",
  "RefreshToken": "Atzr|YOUR_REFRESH_TOKEN",
  "MarketPlaceID": "A1RKKUPIHCS9HS",
  "RoleArn": "arn:aws:iam::340663646958:role/SPAPIRole",
  "SellerId": "YOUR_SELLER_ID",
  "TenantId": "Sportandem"
}
```

4. Click **Next**
5. **Secret name:** `Sportandem/prod/order-export`
6. **Description:** `Amazon SP-API credentials for Sportandem`
7. Click **Next** ? **Next** ? **Store**

### Opción 3: Usar el Script PowerShell

```powershell
.\create-sportandem-secret.ps1 `
    -ClientId "amzn1.application-oa2-client.YOUR_CLIENT_ID" `
    -ClientSecret "YOUR_CLIENT_SECRET" `
    -RefreshToken "Atzr|YOUR_REFRESH_TOKEN" `
    -RoleArn "arn:aws:iam::340663646958:role/SPAPIRole" `
    -SellerId "YOUR_SELLER_ID"
```

## ?? Dónde Obtener las Credenciales

### Amazon Seller Central

1. Ve a **Settings** ? **User Permissions**
2. Click en tu usuario ? **Developer** tab
3. Click **View** en **LWA Credentials**
4. Copia:
   - **Client ID**
   - **Client Secret**
   - **Refresh Token** (si ya lo generaste)

### Si no tienes Refresh Token

Necesitas autorizarte con Amazon SP-API:

1. Ve a **Seller Central** ? **Apps & Services** ? **Develop Apps**
2. O contacta con el administrador de Sportandem para que te proporcione las credenciales

## ? Verificar que el Secret Funciona

### 1. Verificar que existe

```bash
aws secretsmanager get-secret-value \
    --secret-id "Sportandem/prod/order-export" \
    --region eu-west-1 \
    --query 'SecretString' \
    --output text
```

### 2. Crear un nuevo job de exportación

```powershell
.\test-order-export-detailed.ps1
```

### 3. Ver logs del Worker

```bash
aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1
```

Deberías ver:
```
? Secret found with format: Sportandem/prod/order-export
Processing export job...
```

## ?? Troubleshooting

### Error: "Secret not found"

**Verifica el nombre exacto:**
```bash
aws secretsmanager list-secrets --region eu-west-1 --query 'SecretList[].Name'
```

El nombre **debe ser exactamente**: `Sportandem/prod/order-export` (case-sensitive)

### Error: "Access Denied"

El role de Lambda necesita permisos para leer secrets:

```bash
# Verificar permisos del role
aws iam get-role-policy \
    --role-name OrderExportLambdaRole-dev \
    --policy-name OrderExportLambdaRolePolicy
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

## ?? Siguiente Paso

Una vez creado el secret:

1. **Espera 1-2 minutos** para que se propaguen los cambios
2. **Crea un nuevo job:**
   ```powershell
   .\test-order-export-detailed.ps1
   ```
3. **Monitorea los logs:**
   ```bash
   aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region eu-west-1
   ```

El Worker debería procesar el job automáticamente y generar el archivo CSV en S3.
