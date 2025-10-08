
# Cognito OAuth2 M2M (Client Credentials) — Multi-tenant

Este módulo crea:
- **Cognito User Pool** + **Hosted UI Domain**
- **Resource Server** con *scopes* (p. ej. `catalog:write`, `jobs:read`)
- **App Client por tenant** (grant `client_credentials`)
- **DynamoDB** (`<project>-<env>-tenant-clients`) con el mapeo `clientId -> tenantId`
- **Secrets Manager** con `client_id` y `client_secret` por tenant

## Variables clave

- `project_name`, `env`
- `domain_prefix`: único por región (Hosted UI)
- `resource_server_identifier`: id lógico de tu API (p. ej. `api`)
- `scopes`: lista de scopes globales disponibles
- `tenants`: mapa de tenants y sus scopes permitidos

## Ejemplo de `tenants.tfvars`

```
region         = "eu-west-1"
project_name   = "catalog-api"
env            = "dev"
domain_prefix  = "catalog-api-dev-1234" # debe ser único
resource_server_identifier = "api"
scopes = ["catalog:write", "jobs:read"]

tenants = {
  demo = { scopes = ["catalog:write", "jobs:read"] }
  acme = { scopes = ["catalog:write"] }
}
```

## Comandos

```bash
terraform init
terraform plan -var-file="tenants.tfvars"
terraform apply -var-file="tenants.tfvars"
```

## Cómo obtiene el token un tenant (cURL)

```bash
# Reemplaza:
#  - DOMAIN_PREFIX, REGION
#  - CLIENT_ID / CLIENT_SECRET del tenant (en Secrets Manager)
#  - scopes necesarios (deben estar permitidos en ese cliente)

curl -X POST   "https://DOMAIN_PREFIX.auth.REGION.amazoncognito.com/oauth2/token"   -u "CLIENT_ID:CLIENT_SECRET"   -H "Content-Type: application/x-www-form-urlencoded"   -d "grant_type=client_credentials&scope=catalog:write jobs:read"
```

Respuesta:
```json
{
  "access_token": "<JWT>",
  "expires_in": 3600,
  "token_type": "Bearer",
  "scope": "catalog:write jobs:read"
}
```

## Validación en tu API

- Verifica `iss` = `https://cognito-idp.<region>.amazonaws.com/<userPoolId>`
- Extrae `client_id` del token y mapea a `tenantId` (tabla DynamoDB)
- Verifica `scope` requerido por endpoint

## Notas

- **No** se crean usuarios; esto es M2M puro.
- **Client secret** por tenant queda almacenado en **Secrets Manager** (JSON).
- Si deshabilitas un tenant, elimina su *app client* o rota el secret.
