<#
.SYNOPSIS
  Crea o actualiza el secret de Miravia en AWS Secrets Manager.

.PARAMETER TenantId
  Identificador del tenant (ej. sportandem). Obligatorio.

.PARAMETER AppKey
  App Key de la Miravia Open Platform. Obligatorio.

.PARAMETER AppSecret
  App Secret de la Miravia Open Platform. Obligatorio.

.PARAMETER AccessToken
  Access Token de la Miravia Open Platform. Obligatorio.

.PARAMETER ClientEmail
  Email del cliente al que se envían notificaciones de resultado. Obligatorio.

.PARAMETER ClientPartnerEmail
  Email verificado en SES usado como remitente. Obligatorio.

.PARAMETER Environment
  dev | prod. Por defecto: prod.

.PARAMETER Region
  Región AWS. Por defecto: eu-west-1.

.EXAMPLE
  .\create-miravia-secret.ps1 `
    -TenantId sportandem `
    -AppKey "12345678" `
    -AppSecret "abcdef..." `
    -AccessToken "xxxx..." `
    -ClientEmail "cliente@sportandem.com" `
    -ClientPartnerEmail "noreply@tudominio.com" `
    -Environment prod
#>

param(
  [Parameter(Mandatory=$true)]  [string]$TenantId,
  [Parameter(Mandatory=$true)]  [string]$AppKey,
  [Parameter(Mandatory=$true)]  [string]$AppSecret,
  [Parameter(Mandatory=$true)]  [string]$AccessToken,
  [Parameter(Mandatory=$true)]  [string]$ClientEmail,
  [Parameter(Mandatory=$true)]  [string]$ClientPartnerEmail,
  [ValidateSet("dev","prod")]
  [string]$Environment = "prod",
  [string]$Region      = "eu-west-1"
)

$ErrorActionPreference = "Stop"
$env:AWS_PAGER = ""

$secretName = "/catalog-api/$Environment/tenants/$TenantId/miravia"

Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Miravia Secret — $Environment" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host " Secret : $secretName"
Write-Host " Tenant : $TenantId"
Write-Host " Region : $Region"
Write-Host ""

$secretValue = [ordered]@{
  AppKey              = $AppKey
  AppSecret           = $AppSecret
  AccessToken         = $AccessToken
  ClientEmail         = $ClientEmail
  ClientPartnerEmail  = $ClientPartnerEmail
  TenantId            = $TenantId
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
    --description "Miravia Open Platform credentials for tenant $TenantId ($Environment)" `
    --secret-string $secretValue `
    --region $Region | Out-Null

  Write-Host "✓ Secret creado correctamente." -ForegroundColor Green
}

Write-Host ""
Write-Host " Campos almacenados:" -ForegroundColor Gray
Write-Host "   AppKey             : $($AppKey.Substring(0,[Math]::Min(6,$AppKey.Length)))..." -ForegroundColor Gray
Write-Host "   AppSecret          : ******" -ForegroundColor Gray
Write-Host "   AccessToken        : ******" -ForegroundColor Gray
Write-Host "   ClientEmail        : $ClientEmail" -ForegroundColor Gray
Write-Host "   ClientPartnerEmail : $ClientPartnerEmail" -ForegroundColor Gray
Write-Host "   TenantId           : $TenantId" -ForegroundColor Gray
Write-Host ""
Write-Host " Verificar:" -ForegroundColor Gray
Write-Host "   aws secretsmanager get-secret-value --secret-id $secretName --region $Region" -ForegroundColor Gray
