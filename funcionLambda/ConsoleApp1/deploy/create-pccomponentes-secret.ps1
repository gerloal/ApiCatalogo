<#
.SYNOPSIS
  Crea o actualiza el secret de PcComponentes (Mirakl) en AWS Secrets Manager.

.PARAMETER TenantId
  Identificador del tenant (ej. sportandem). Obligatorio.

.PARAMETER ApiKey
  API Key de la instancia Mirakl de PcComponentes. Obligatorio.

.PARAMETER BaseUrl
  URL base de producción de la instancia Mirakl (ej. https://pccomponentes.mirakl.net). Obligatorio.

.PARAMETER SandboxBaseUrl
  URL del entorno sandbox/staging de PcComponentes. Obligatorio.

.PARAMETER ClientEmail
  Email del cliente al que se envían notificaciones. Obligatorio.

.PARAMETER ClientPartnerEmail
  Email verificado en SES usado como remitente. Obligatorio.

.PARAMETER Environment
  dev | prod. Por defecto: prod.

.PARAMETER Region
  Región AWS. Por defecto: eu-west-1.

.EXAMPLE
  # Crear secret de producción
  .\create-pccomponentes-secret.ps1 `
    -TenantId sportandem `
    -ApiKey "tu-api-key-mirakl" `
    -BaseUrl "https://pccomponentes.mirakl.net" `
    -SandboxBaseUrl "https://pccomponentes-sandbox.mirakl.net" `
    -ClientEmail "cliente@sportandem.com" `
    -ClientPartnerEmail "noreply@tudominio.com" `
    -Environment prod

.EXAMPLE
  # Crear secret de desarrollo (apunta al sandbox)
  .\create-pccomponentes-secret.ps1 `
    -TenantId sportandem `
    -ApiKey "tu-api-key-sandbox" `
    -BaseUrl "https://pccomponentes-sandbox.mirakl.net" `
    -SandboxBaseUrl "https://pccomponentes-sandbox.mirakl.net" `
    -ClientEmail "cliente@sportandem.com" `
    -ClientPartnerEmail "noreply@tudominio.com" `
    -Environment dev
#>

param(
  [Parameter(Mandatory=$true)]  [string]$TenantId,
  [Parameter(Mandatory=$true)]  [string]$ApiKey,
  [Parameter(Mandatory=$true)]  [string]$BaseUrl,
  [Parameter(Mandatory=$true)]  [string]$SandboxBaseUrl,
  [Parameter(Mandatory=$true)]  [string]$ClientEmail,
  [Parameter(Mandatory=$true)]  [string]$ClientPartnerEmail,
  [ValidateSet("dev","prod")]
  [string]$Environment = "prod",
  [string]$Region      = "eu-west-1"
)

$ErrorActionPreference = "Stop"
$env:AWS_PAGER = ""

$secretName = "/catalog-api/$Environment/tenants/$TenantId/pccomponentes"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " PcComponentes Secret — $Environment" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Secret : $secretName"
Write-Host " Tenant : $TenantId"
Write-Host " Region : $Region"
Write-Host ""

# PcComponentesSecret usa camelCase (JsonPropertyName)
$secretValue = [ordered]@{
  apiKey              = $ApiKey
  baseUrl             = $BaseUrl
  sandboxBaseUrl      = $SandboxBaseUrl
  tenantId            = $TenantId
  clientEmail         = $ClientEmail
  clientPartnerEmail  = $ClientPartnerEmail
} | ConvertTo-Json -Compress

# Verificar si ya existe
$exists = $false
try {
  $info = aws secretsmanager describe-secret --secret-id $secretName --region $Region --output json 2>$null | ConvertFrom-Json
  if ($info) { $exists = $true }
} catch { }

if ($exists) {
  Write-Host "! Secret ya existe (ARN: $($info.ARN))" -ForegroundColor Yellow
  $confirm = Read-Host "  ¿Actualizar? (s/N)"
  if ($confirm -notmatch '^[sS]$') { Write-Host "Cancelado."; exit 0 }

  aws secretsmanager update-secret `
    --secret-id $secretName `
    --secret-string $secretValue `
    --region $Region | Out-Null

  Write-Host "✓ Secret actualizado correctamente." -ForegroundColor Green
} else {
  aws secretsmanager create-secret `
    --name $secretName `
    --description "PcComponentes (Mirakl) credentials for tenant $TenantId ($Environment)" `
    --secret-string $secretValue `
    --region $Region | Out-Null

  Write-Host "✓ Secret creado correctamente." -ForegroundColor Green
}

Write-Host ""
Write-Host " Campos almacenados:" -ForegroundColor Gray
Write-Host "   apiKey             : $($ApiKey.Substring(0,[Math]::Min(6,$ApiKey.Length)))..." -ForegroundColor Gray
Write-Host "   baseUrl            : $BaseUrl" -ForegroundColor Gray
Write-Host "   sandboxBaseUrl     : $SandboxBaseUrl" -ForegroundColor Gray
Write-Host "   tenantId           : $TenantId" -ForegroundColor Gray
Write-Host "   clientEmail        : $ClientEmail" -ForegroundColor Gray
Write-Host "   clientPartnerEmail : $ClientPartnerEmail" -ForegroundColor Gray
Write-Host ""
Write-Host " Verificar:" -ForegroundColor Gray
Write-Host "   aws secretsmanager get-secret-value --secret-id $secretName --region $Region" -ForegroundColor Gray
