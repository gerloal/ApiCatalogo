
locals {
  name_prefix = "${var.project_name}-${var.env}"

  scope_objects = [for s in var.scopes : {
    name        = s
    description = "Scope ${s}"
  }]
}

# --- Cognito User Pool ---
resource "aws_cognito_user_pool" "this" {
  name = "${local.name_prefix}-user-pool"

  # For M2M no users are required, but keep defaults minimal
  password_policy {
    minimum_length    = 12
    require_lowercase = true
    require_numbers   = true
    require_symbols   = true
    require_uppercase = true
  }

  # Tags
  tags = {
    Project     = var.project_name
    Environment = var.env
  }
}

# --- Hosted UI Domain ---
resource "aws_cognito_user_pool_domain" "this" {
  domain       = var.domain_prefix
  user_pool_id = aws_cognito_user_pool.this.id
}

# --- Resource Server & Scopes ---
resource "aws_cognito_resource_server" "this" {
  identifier   = var.resource_server_identifier
  name         = "${local.name_prefix}-resource-server"
  user_pool_id = aws_cognito_user_pool.this.id

  dynamic "scope" {
    for_each = local.scope_objects
    content {
      scope_name        = scope.value.name
      scope_description = scope.value.description
    }
  }
}

# Build a map of scope -> full ARN (identifier/scope)
locals {
  scope_arns = {
    for s in var.scopes :
    s => "${aws_cognito_resource_server.this.identifier}/${s}"
  }
}

# --- App Clients per tenant (client credentials) ---
resource "aws_cognito_user_pool_client" "tenant" {
  for_each = var.tenants

  name         = "${local.name_prefix}-client-${each.key}"
  user_pool_id = aws_cognito_user_pool.this.id

  generate_secret = true

  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["client_credentials"]

  # Only allow the scopes listed for this tenant
  allowed_oauth_scopes = [for s in each.value.scopes : local.scope_arns[s]]

  prevent_user_existence_errors = "ENABLED"

  supported_identity_providers = ["COGNITO"]

  access_token_validity = 60
  token_validity_units {
    access_token = "minutes"
  }
}

# --- DynamoDB table for mapping clientId -> tenantId (optional but handy) ---
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

# Put an item per client with clientId -> tenantId + scopes
resource "aws_dynamodb_table_item" "tenant_items" {
  for_each = aws_cognito_user_pool_client.tenant

  table_name = aws_dynamodb_table.tenant_clients.name
  hash_key   = "clientId"

  item = jsonencode({
    clientId = { S = each.value.id } # 'id' on client is the client_id
    tenantId = { S = each.key }
    scopes   = { S = join(" ", var.tenants[each.key].scopes) }
    created  = { S = timestamp() }
  })
}

# --- Secrets Manager: store client_id and client_secret per tenant (optional but recommended) ---
resource "aws_secretsmanager_secret" "tenant" {
  for_each = aws_cognito_user_pool_client.tenant
  name     = "${local.name_prefix}/cognito/${each.key}"
  tags = {
    Project     = var.project_name
    Environment = var.env
    Tenant      = each.key
  }
}

resource "aws_secretsmanager_secret_version" "tenant" {
  for_each      = aws_cognito_user_pool_client.tenant
  secret_id     = aws_secretsmanager_secret.tenant[each.key].id
  secret_string = jsonencode({
    tenant      = each.key
    client_id   = each.value.id
    client_name = each.value.name
    client_secret = each.value.client_secret
    token_url   = "https://${aws_cognito_user_pool_domain.this.domain}.auth.${var.region}.amazoncognito.com/oauth2/token"
    scopes      = var.tenants[each.key].scopes
    issuer      = "https://cognito-idp.${var.region}.amazonaws.com/${aws_cognito_user_pool.this.id}"
    resource_server = aws_cognito_resource_server.this.identifier
  })
}

# --- Outputs ---
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

output "scopes" {
  value = var.scopes
}

output "tenant_client_ids" {
  value = { for k, c in aws_cognito_user_pool_client.tenant : k => c.id }
  sensitive = true
}

output "tenant_secret_arns" {
  value = { for k, s in aws_secretsmanager_secret.tenant : k => s.arn }
}
