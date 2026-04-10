# create-miravia-secret.ps1
# Crea el secreto de Miravia Sportandem en AWS Secrets Manager

$SecretName = "catalog-api/miravia/sportandem"
$Region     = "eu-west-1"

$SecretValue = @{
    AppKey       = "512834"
    AppSecret    = "w2Bamaj5ngQb41f7Sd5pb8AjFKFHD5gc"
    AccessToken  = "50000600911ttfj7iBwZOsE1c06d66bA6IwjmSf0wHcDcsgKVWFlltn1fSLqnROp"
    RefreshToken = "50001600911ttfj7iBwZOsE14dc0d62A6IwjmSf0wHcDcsgKVWFlltn1fSLqnROp"
    TenantId     = "sportandem"
} | ConvertTo-Json -Compress

Write-Host "Creando secreto '$SecretName' en $Region ..."

$existing = aws secretsmanager describe-secret --secret-id $SecretName --region $Region 2>$null

if ($existing) {
    Write-Host "El secreto ya existe, actualizando valor..."
    aws secretsmanager put-secret-value `
        --secret-id $SecretName `
        --secret-string $SecretValue `
        --region $Region
} else {
    Write-Host "Creando nuevo secreto..."
    aws secretsmanager create-secret `
        --name $SecretName `
        --description "Credenciales Miravia OpenPlatform para tenant Sportandem" `
        --secret-string $SecretValue `
        --region $Region
}

Write-Host "`n✅ Secreto '$SecretName' guardado correctamente." -ForegroundColor Green
Write-Host "⚠️  El AccessToken expira en 14 días. Usar RefreshToken para renovarlo." -ForegroundColor Yellow
