$secretValue = @{
    AppKey             = "512834"
    AppSecret          = "w2Bamaj5ngQb41f7Sd5pb8AjFKFHD5gc"
    AccessToken        = "50000600911ttfj7iBwZOsE1c06d66bA6IwjmSf0wHcDcsgKVWFlltn1fSLqnROp"
    RefreshToken       = "50001600911ttfj7iBwZOsE14dc0d62A6IwjmSf0wHcDcsgKVWFlltn1fSLqnROp"
    ClientEmail        = "info@sportandem.com"
    ClientPartnerEmail = "info@sportandem.com"
    TenantId           = "sportandem"
} | ConvertTo-Json -Compress

aws secretsmanager create-secret `
    --name "/catalog-api/dev/tenants/sportandem/miravia" `
    --description "Miravia Open Platform credentials for tenant sportandem (dev)" `
    --secret-string $secretValue `
    --region eu-west-1
