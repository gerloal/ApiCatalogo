<# 
.SYNOPSIS
  Demo end-to-end: S3 + DynamoDB + SQS (multi-tenant)

.PARAMETER Tenant
  Identificador del cliente (tenantId). Por defecto "demo".

.PARAMETER Region
  Región AWS. Por defecto "eu-west-1".

.PARAMETER TableName
  Tabla DynamoDB. Por defecto "Jobs".

.PARAMETER QueueName
  Nombre de la cola SQS. Por defecto "catalog-jobs".

.PARAMETER Bucket
  Bucket S3 (si no lo pasas, usa catalog-uploads-<accountId>).

.PARAMETER FilePath
  Ruta al fichero local a subir. Si no existe, se crea un catalog.json temporal.

.EXAMPLE
  .\Demo-Flujo-Catalogo.ps1 -Tenant demo -Region eu-west-1
#>

param(
  [string]$Tenant   = "demo",
  [string]$Region   = "eu-west-1",
  [string]$TableName = "Jobs",
  [string]$QueueName = "catalog-jobs",
  [string]$Bucket    = "",
  [string]$FilePath  = ""
)

# ===== Utilidades =====
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$env:AWS_PAGER = ""   # evita paginador en CLI

function Ensure-Cli($name) {
  if (-not (Get-Command $name -ErrorAction SilentlyContinue)) {
    throw "No se encontró '$name' en el PATH. Instálalo y vuelve a ejecutar."
  }
}

function New-TempCatalogIfNeeded {
  param([string]$Path)
  if (-not (Test-Path $Path)) {
    '{"catalog":"demo","items":[{"sku":"SKU-1","title":"Producto demo","price":9.99}]}' | Out-File -Encoding utf8 $Path
  }
}

function Out-Step($msg) { Write-Host "• $msg" -ForegroundColor Cyan }
function Out-Ok($msg)   { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Out-Warn($msg) { Write-Host "  ! $msg" -ForegroundColor Yellow }
function Out-Err($msg)  { Write-Host "  ✗ $msg" -ForegroundColor Red }

try {
  Out-Step "Comprobando dependencias"
  Ensure-Cli "aws"
  Ensure-Cli "powershell"  | Out-Null
  Ensure-Cli "dotnet"      | Out-Null # opcional, por si quieres probar tu worker local
  Out-Ok "AWS CLI disponible"

  Out-Step "Resolviendo cuenta/región"
  $accountId = (aws sts get-caller-identity --query Account --output text)
  if (-not $accountId) { throw "No se pudo obtener AccountId; ejecuta 'aws configure' antes." }
  if ([string]::IsNullOrWhiteSpace($Bucket)) {
    $Bucket = "catalog-uploads-$accountId"
  }
  $queueUrl = "https://sqs.$Region.amazonaws.com/$accountId/$QueueName"
  Out-Ok "Cuenta: $accountId | Región: $Region"
  Out-Ok "Bucket: $Bucket | QueueUrl: $queueUrl | Tabla: $TableName"

  Out-Step "Preparando identificadores del Job"
  $jobId  = [guid]::NewGuid().ToString("N")
  $dateYmd = Get-Date -Format yyyyMMdd
  $dateSl  = Get-Date -Format yyyy/MM/dd
  $pk = "TENANT#$Tenant"
  $sk = "JOB#$dateYmd#$jobId"
  $key = "tenants/$Tenant/$dateSl/${jobId}_catalog.json"
  Out-Ok "tenantId=$Tenant | jobId=$jobId"
  Out-Ok "pk=$pk | sk=$sk"
  Out-Ok "S3 key=s3://$Bucket/$key"

  Out-Step "Generando fichero de catálogo"
  if ([string]::IsNullOrWhiteSpace($FilePath)) {
    $FilePath = Join-Path $PWD "catalog.json"
  }
  New-TempCatalogIfNeeded -Path $FilePath
  $sizeBytes = (Get-Item $FilePath).Length
  Out-Ok "$FilePath ($sizeBytes bytes)"

  Out-Step "Subiendo a S3"
  aws s3 cp "$FilePath" "s3://$Bucket/$key" --region $Region | Out-Null
  Out-Ok "Subida OK"

  Out-Step "Creando ítem en DynamoDB (QUEUED)"
  $itemObj = @{
    pk      = @{ S = $pk }
    sk      = @{ S = $sk }
    status  = @{ S = "QUEUED" }
    fileKey = @{ S = $key }
    tenant  = @{ S = $Tenant }
    size    = @{ N = "$sizeBytes" }
    createdAt = @{ S = (Get-Date).ToUniversalTime().ToString("o") }
  } | ConvertTo-Json -Depth 6
  $itemPath = Join-Path $PWD "item.json"
  $itemObj | Out-File -Encoding utf8 $itemPath

  aws dynamodb put-item `
    --table-name $TableName `
    --item file://$itemPath `
    --region $Region | Out-Null
  Out-Ok "PutItem OK ($TableName)"

  Out-Step "Encolando mensaje en SQS"
  $payload = @{
    tenantId = $Tenant
    jobId    = $jobId
    bucket   = $Bucket
    key      = $key
    fileName = (Split-Path $FilePath -Leaf)
  } | ConvertTo-Json -Compress
  $payloadPath = Join-Path $PWD "payload.json"
  $payload | Out-File -Encoding utf8 $payloadPath

  aws sqs send-message `
    --queue-url $queueUrl `
    --message-body file://$payloadPath `
    --region $Region | Out-Null
  Out-Ok "SendMessage OK ($QueueName)"

  Out-Step "Verificando lectura S3 (lista prefijo del tenant)"
  aws s3 ls "s3://$Bucket/tenants/$Tenant/$dateSl/" --region $Region
  Out-Ok "Objeto visible en S3"

  Out-Step "Verificando GetItem en DynamoDB"
  $keyObj = @{
    pk = @{ S = $pk }
    sk = @{ S = $sk }
  } | ConvertTo-Json -Depth 5
  $keyPath = Join-Path $PWD "key.json"
  $keyObj | Out-File -Encoding utf8 $keyPath

  $itemRead = aws dynamodb get-item `
    --table-name $TableName `
    --key file://$keyPath `
    --region $Region
  if (-not $itemRead) { throw "GetItem no devolvió datos" }
  Out-Ok "GetItem OK"

  Write-Host ""
  Write-Host "================= RESUMEN =================" -ForegroundColor Magenta
  Write-Host "Tenant:        $Tenant"
  Write-Host "JobId:         $jobId"
  Write-Host "PK/SK:         $pk | $sk"
  Write-Host "S3 object:     s3://$Bucket/$key"
  Write-Host "DynamoDB:      $TableName"
  Write-Host "SQS Queue:     $queueUrl"
  Write-Host "===========================================" -ForegroundColor Magenta
  Write-Host "Archivos auxiliares: item.json, payload.json, key.json" -ForegroundColor DarkGray
  Write-Host "Listo. Si tu worker está ejecutándose, debería procesar el mensaje y actualizar el estado a SUCCEEDED." -ForegroundColor Green
}
catch {
  Out-Err $_.Exception.Message
  exit 1
}
