
region         = "eu-west-1"
project_name   = "catalog-api"
env            = "dev"
domain_prefix  = "catalog-api-dev-1234"
resource_server_identifier = "api"
scopes = ["catalog:write", "jobs:read"]

tenants = {
  demo = { scopes = ["catalog:write", "jobs:read"] }
  acme = { scopes = ["catalog:write"] }
}
