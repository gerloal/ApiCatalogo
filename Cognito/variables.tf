
variable "region" {
  description = "AWS region"
  type        = string
  default     = "eu-west-1"
}

variable "project_name" {
  description = "Project name (used for naming)"
  type        = string
}

variable "env" {
  description = "Environment tag (dev, prod, etc.)"
  type        = string
}

variable "domain_prefix" {
  description = "Cognito Hosted UI domain prefix (must be globally unique in region)"
  type        = string
}

variable "resource_server_identifier" {
  description = "Logical identifier for your API resource server (e.g., api)"
  type        = string
  default     = "api"
}

variable "scopes" {
  description = "List of OAuth scopes to expose on the resource server (names only)"
  type        = list(string)
  default     = ["catalog:write", "jobs:read"]
}

variable "tenants" {
  description = <<EOT
Map of tenants and their allowed scopes. Example:
{
  demo = { scopes = ["catalog:write", "jobs:read"] }
  acme = { scopes = ["catalog:write"] }
}
EOT
  type = map(object({
    scopes = list(string)
  }))
}
