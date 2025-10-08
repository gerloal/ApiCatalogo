
locals {
  name_prefix = "${var.project_name}-${var.env}"
}

resource "aws_cognito_user_pool" "this" {
  name = "${local.name_prefix}-user-pool"

  password_policy {
    minimum_length    = 12
    require_lowercase = true
    require_numbers   = true
    require_symbols   = true
    require_uppercase = true
  }

  tags = {
    Project     = var.project_name
    Environment = var.env
  }
}

resource "aws_cognito_user_pool_domain" "this" {
  domain       = var.domain_prefix
  user_pool_id = aws_cognito_user_pool.this.id
}

resource "aws_cognito_resource_server" "this" {
  identifier   = var.resource_server_identifier
  name         = "${local.name_prefix}-resource-server"
  user_pool_id = aws_cognito_user_pool.this.id

  dynamic "scope" {
    for_each = var.scopes
    content {
      scope_name        = scope.value
      scope_description = "Scope ${scope.value}"
    }
  }
}

locals {
  scope_arns = {
    for s in var.scopes :
    s => "${aws_cognito_resource_server.this.identifier}/${s}"
  }
}

locals {
  tenant_configs = {
    for t in var.tenants :
    t => jsondecode(file("${path.root}/Secrets/${t}.json"))
  }

  tenant_scopes = {
    for t, cfg in local.tenant_configs :
    t => [for s in cfg.scopes : s if contains(var.scopes, s)]
  }
}

resource "aws_cognito_user_pool_client" "tenant" {
  for_each = local.tenant_configs

  name         = "${local.name_prefix}-client-${each.key}"
  user_pool_id = aws_cognito_user_pool.this.id

  generate_secret = true

  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["client_credentials"]

  allowed_oauth_scopes = [for s in local.tenant_scopes[each.key] : local.scope_arns[s]]

  prevent_user_existence_errors = "ENABLED"
  supported_identity_providers  = ["COGNITO"]

  access_token_validity = 60
  token_validity_units {
    access_token = "minutes"
  }
}

resource "aws_dynamodb_table" "tenant_clients" {
  name         = "${local.name_prefix}-tenant-clients"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "clientId"

  attribute {
    name = "clientId"
    type = "S"
  }

  tags = {
    Project     = var.project_name
    Environment = var.env
  }
}

resource "aws_dynamodb_table_item" "tenant_items" {
  for_each = aws_cognito_user_pool_client.tenant

  table_name = aws_dynamodb_table.tenant_clients.name
  hash_key   = "clientId"

  item = jsonencode({
    clientId = { S = each.value.id }
    tenantId = { S = each.key }
    scopes   = { S = join(" ", local.tenant_scopes[each.key]) }
    created  = { S = timestamp() }
  })
}

resource "aws_secretsmanager_secret" "tenant_client" {
  for_each = aws_cognito_user_pool_client.tenant
  name     = "${local.name_prefix}/cognito/${each.key}"
  tags = {
    Project     = var.project_name
    Environment = var.env
    Tenant      = each.key
  }
}

resource "aws_secretsmanager_secret_version" "tenant_client" {
  for_each      = aws_secretsmanager_secret.tenant_client
  secret_id     = each.value.id
  secret_string = jsonencode({
    tenant          = each.key
    client_id       = aws_cognito_user_pool_client.tenant[each.key].id
    client_name     = aws_cognito_user_pool_client.tenant[each.key].name
    client_secret   = aws_cognito_user_pool_client.tenant[each.key].client_secret
    token_url       = "https://${aws_cognito_user_pool_domain.this.domain}.auth.${var.region}.amazoncognito.com/oauth2/token"
    scopes          = local.tenant_scopes[each.key]
    issuer          = "https://cognito-idp.${var.region}.amazonaws.com/${aws_cognito_user_pool.this.id}"
    resource_server = aws_cognito_resource_server.this.identifier
  })
}

resource "aws_secretsmanager_secret" "spapi" {
  for_each   = local.tenant_configs
  name       = "/${var.project_name}/${var.env}/tenants/${each.key}/spapi"
  description = "SP-API & LWA creds for tenant ${each.key}"
  tags = {
    Project     = var.project_name
    Environment = var.env
    Tenant      = each.key
  }
}

resource "aws_secretsmanager_secret_version" "spapi" {
  for_each      = local.tenant_configs
  secret_id     = aws_secretsmanager_secret.spapi[each.key].id
  secret_string = jsonencode(each.value.spapi)
}

output "user_pool_id" {
  value = aws_cognito_user_pool.this.id
}

output "issuer" {
  value = "https://cognito-idp.${var.region}.amazonaws.com/${aws_cognito_user_pool.this.id}"
}

output "token_url" {
  value = "https://${aws_cognito_user_pool_domain.this.domain}.auth.${var.region}.amazoncognito.com/oauth2/token"
}

output "resource_server_identifier" {
  value = aws_cognito_resource_server.this.identifier
}

output "tenant_client_ids" {
  value     = { for k, c in aws_cognito_user_pool_client.tenant : k => c.id }
  sensitive = true
}

output "tenant_secret_arns" {
  value = { for k, s in aws_secretsmanager_secret.tenant_client : k => s.arn }
}

output "spapi_secret_arns" {
  value = { for k, s in aws_secretsmanager_secret.spapi : k => s.arn }
}
