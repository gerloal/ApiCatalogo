
region        = "eu-west-1"
project_name  = "catalog-api"
env           = "dev"
domain_prefix = "catalog-api-dev-1234"
resource_server_identifier = "api"

# Allowlist of valid scopes system-wide
scopes = ["catalog:write", "jobs:read"]

# The module will read /Secrets/<tenant>.json for each tenant ID here
tenants = ["Sportandem"]
