
# Per-tenant config: /Secrets/<tenantId>.json

Create a file per tenant under the repository root:

/Secrets/demo.json
/Secrets/acme.json

Each file must be valid JSON with at least:
{
  "scopes": ["catalog:write", "jobs:read"],
  "spapi": {
    "AccessKey": "xx",
    "SecretKey": "xx",
    "RoleArn": "arn:aws:iam::123456789012:role/sapi-rol",
    "ClientId": "xx",
    "ClientSecret": "xxx",
    "RefreshToken": "xxx",
    "MarketPlaceID": "A1RKKUPIHCS9HS",
    "TenantId": "123456789012",
    "SellerId": "123456789012"
  }
}
