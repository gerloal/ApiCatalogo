<#
.SYNOPSIS
  Envia job(s) a la Lambda para el tenant Sportandem.
  Sube el fichero CSV a S3 (solo FeedCatalog) y encola el mensaje en SQS.

.PARAMETER Operation
  FeedCatalog  - Actualiza precios/stock en el marketplace.
  ExportOrders - Solicita exportacion de pedidos.

.PARAMETER Market
  Amazon | Miravia | AliExpress | PcComponentes | Decathlon | All
  Por defecto: All (envia a todos los marketplaces configurados).

.PARAMETER FilePath
  Ruta local al CSV con formato SKU;Stock;Precio.
  Obligatorio si Operation = FeedCatalog.

.PARAMETER StartDate
  Fecha inicio para ExportOrders (ISO 8601). Por defecto: hace 30 dias.

.PARAMETER EndDate
  Fecha fin para ExportOrders (ISO 8601). Por defecto: ahora UTC.

.PARAMETER Environment
  dev | prod. Por defecto: dev.

.PARAMETER Region
  Region AWS. Por defecto: eu-west-1.

.EXAMPLE
  # Actualizar catalogo en todos los marketplaces
  .\sportandem-send-job.ps1 -Operation FeedCatalog -FilePath .\catalogo.csv

.EXAMPLE
  # Actualizar catalogo solo en Miravia
  .\sportandem-send-job.ps1 -Operation FeedCatalog -Market Miravia -FilePath .\catalogo.csv

.EXAMPLE
  # Exportar pedidos de Decathlon (ultimos 30 dias)
  .\sportandem-send-job.ps1 -Operation ExportOrders -Market Decathlon

.EXAMPLE
  # Exportar pedidos Amazon en produccion con rango explicito
  .\sportandem-send-job.ps1 -Operation ExportOrders -Market Amazon -Environment prod `
    -StartDate "2026-01-01T00:00:00Z" -EndDate "2026-01-31T23:59:59Z"

.EXAMPLE
  # Exportar pedidos de todos los marketplaces en dev
  .\sportandem-send-job.ps1 -Operation ExportOrders -Market All
#>

param(
  [ValidateSet("FeedCatalog", "ExportOrders")]
  [string]$Operation   = "FeedCatalog",

  [ValidateSet("Amazon", "Miravia", "AliExpress", "PcComponentes", "Decathlon", "All")]
  [string]$Market      = "All",

  [string]$FilePath    = "",
  [string]$StartDate   = "",
  [string]$EndDate     = "",

  [ValidateSet("dev", "prod")]
  [string]$Environment = "dev",

  [string]$Region      = "eu-west-1"
)

# Constantes
$TenantId     = "sportandem"
# dev usa "catalog-jobs" y "order-export-dev"; prod usa "catalog-jobs-prod" y "order-export-prod"
$CatalogQueue = if ($Environment -eq "dev") { "catalog-jobs" } else { "catalog-jobs-$Environment" }
$OrderQueue   = "order-export-$Environment"

$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"
$env:AWS_PAGER         = ""

# Helpers
function Step($msg) { Write-Host "" ; Write-Host "* $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "  OK  $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "  !!  $msg" -ForegroundColor Yellow }
function Err($msg)  { Write-Host "  ERR $msg" -ForegroundColor Red }
function Info($msg) { Write-Host "      $msg" -ForegroundColor Gray }

function Get-OperationName([string]$dest, [string]$op) {
  if ($op -eq "FeedCatalog") {
    switch ($dest) {
      "Amazon"        { return "FEED_CATALOG" }
      "Miravia"       { return "MIRAVIA_FEED_CATALOG" }
      "AliExpress"    { return "ALIEXPRESS_FEED_CATALOG" }
      "PcComponentes" { return "PCCOMPONENTES_FEED_CATALOG" }
      "Decathlon"     { return "DECATHLON_FEED_CATALOG" }
      "All"           { return "ALL_FEED_CATALOG" }
    }
  } else {
    switch ($dest) {
      "Amazon"        { return "EXPORT_ORDERS" }
      "Miravia"       { return "MIRAVIA_EXPORT_ORDERS" }
      "AliExpress"    { return "ALIEXPRESS_EXPORT_ORDERS" }
      "PcComponentes" { return "PCCOMPONENTES_EXPORT_ORDERS" }
      "Decathlon"     { return "DECATHLON_EXPORT_ORDERS" }
      "All"           { return "ALL_EXPORT_ORDERS" }
    }
  }
}

function Send-CatalogJobMessage {
  param([string]$Dest, [string]$OpName, [string]$Bucket, [string]$Key, [string]$QueueUrl)

  $jobId = [guid]::NewGuid().ToString("N")

  $payload = @{
    tenantId    = $TenantId
    jobId       = $jobId
    bucket      = $Bucket
    key         = $Key
    operation   = $OpName
    contentType = "text/csv"
  } | ConvertTo-Json -Compress

  $tmpFile = [System.IO.Path]::GetTempFileName()
  $payload | Out-File -Encoding utf8 $tmpFile

  aws sqs send-message `
    --queue-url $QueueUrl `
    --message-body file://$tmpFile `
    --region $Region | Out-Null

  Remove-Item $tmpFile -ErrorAction SilentlyContinue
  Ok "$Dest -> $OpName  [jobId=$jobId]"
  return $jobId
}

function Send-ExportOrdersMessage {
  param([string]$Dest, [string]$OpName, [string]$Start, [string]$End, [string]$QueueUrl)

  $jobId = [guid]::NewGuid().ToString("N")

  $payload = @{
    TenantId  = $TenantId
    JobId     = $jobId
    StartDate = $Start
    EndDate   = $End
    Format    = "CSV"
    Operation = $OpName
  } | ConvertTo-Json -Compress

  $tmpFile = [System.IO.Path]::GetTempFileName()
  $payload | Out-File -Encoding utf8 $tmpFile

  aws sqs send-message `
    --queue-url $QueueUrl `
    --message-body file://$tmpFile `
    --region $Region | Out-Null

  Remove-Item $tmpFile -ErrorAction SilentlyContinue
  Ok "$Dest -> $OpName  [jobId=$jobId]"
  return $jobId
}

# Main
try {
  # 1. Verificar AWS CLI
  Step "Verificando AWS CLI"
  if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
    throw "AWS CLI no encontrado. Instalalo y ejecuta 'aws configure'."
  }
  $accountId = (aws sts get-caller-identity --query Account --output text)
  if (-not $accountId) { throw "No se pudo obtener AccountId. Ejecuta 'aws configure'." }
  Ok "Cuenta: $accountId | Region: $Region | Entorno: $Environment"

  # 2. Validar parametros segun operacion
  $uploadBucket = "catalog-uploads-$accountId"

  if ($Operation -eq "FeedCatalog") {
    if ([string]::IsNullOrWhiteSpace($FilePath) -or -not (Test-Path $FilePath)) {
      throw "Se requiere -FilePath con la ruta al CSV para FeedCatalog."
    }
    $sizeBytes = (Get-Item $FilePath).Length
    Info "Fichero: $FilePath ($sizeBytes bytes)"
  } else {
    if ([string]::IsNullOrWhiteSpace($EndDate)) {
      $EndDate = (Get-Date).ToUniversalTime().ToString("o")
    }
    if ([string]::IsNullOrWhiteSpace($StartDate)) {
      $StartDate = (Get-Date).ToUniversalTime().AddDays(-30).ToString("o")
    }
    Info "Rango: $StartDate -> $EndDate"
  }

  # 3. Subida a S3 (solo FeedCatalog)
  $s3Key = ""
  if ($Operation -eq "FeedCatalog") {
    Step "Subiendo fichero a S3"
    $dateSlash = Get-Date -Format yyyy/MM/dd
    $jobIdBase = [guid]::NewGuid().ToString("N")
    $s3Key     = "tenants/$TenantId/$dateSlash/${jobIdBase}_catalog.csv"

    aws s3 cp "$FilePath" "s3://$uploadBucket/$s3Key" --region $Region | Out-Null
    Ok "s3://$uploadBucket/$s3Key"
  }

  # 4. Resolver URLs de colas
  Step "Resolviendo URLs de colas SQS"
  $catalogQueueUrl = "https://sqs.$Region.amazonaws.com/$accountId/$CatalogQueue"
  $orderQueueUrl   = "https://sqs.$Region.amazonaws.com/$accountId/$OrderQueue"
  Info "Catalog queue : $catalogQueueUrl"
  Info "Order queue   : $orderQueueUrl"

  # 5. Enviar mensajes
  Step "Enviando mensajes SQS"
  $sentJobs = @()

  # "All" se envia como un unico mensaje con operacion ALL_*
  if ($Market -eq "All") {
    $opName = Get-OperationName -dest "All" -op $Operation

    if ($Operation -eq "FeedCatalog") {
      $jid = Send-CatalogJobMessage -Dest "All" -OpName $opName `
               -Bucket $uploadBucket -Key $s3Key -QueueUrl $catalogQueueUrl
      $sentJobs += [PSCustomObject]@{ Market="All"; Op=$opName; JobId=$jid; Queue=$CatalogQueue }
    } else {
      $jid = Send-ExportOrdersMessage -Dest "All" -OpName $opName `
               -Start $StartDate -End $EndDate -QueueUrl $catalogQueueUrl
      $sentJobs += [PSCustomObject]@{ Market="All"; Op=$opName; JobId=$jid; Queue=$CatalogQueue }
    }
  } else {
    $opName = Get-OperationName -dest $Market -op $Operation

    if ($Operation -eq "FeedCatalog") {
      $jid = Send-CatalogJobMessage -Dest $Market -OpName $opName `
               -Bucket $uploadBucket -Key $s3Key -QueueUrl $catalogQueueUrl
      $sentJobs += [PSCustomObject]@{ Market=$Market; Op=$opName; JobId=$jid; Queue=$CatalogQueue }

    } else {
      # ExportOrders Amazon -> order-export queue (worker dedicado)
      # ExportOrders resto  -> catalog-jobs queue (CatalogoService)
      if ($Market -eq "Amazon") {
        $jid = Send-ExportOrdersMessage -Dest $Market -OpName $opName `
                 -Start $StartDate -End $EndDate -QueueUrl $orderQueueUrl
        $sentJobs += [PSCustomObject]@{ Market=$Market; Op=$opName; JobId=$jid; Queue=$OrderQueue }
      } else {
        $jid = Send-ExportOrdersMessage -Dest $Market -OpName $opName `
                 -Start $StartDate -End $EndDate -QueueUrl $catalogQueueUrl
        $sentJobs += [PSCustomObject]@{ Market=$Market; Op=$opName; JobId=$jid; Queue=$CatalogQueue }
      }
    }
  }

  # 6. Resumen
  Write-Host ""
  Write-Host "-----------------------------------------------" -ForegroundColor Magenta
  Write-Host " SPORTANDEM - Jobs encolados" -ForegroundColor Magenta
  Write-Host "-----------------------------------------------" -ForegroundColor Magenta
  Write-Host " Tenant    : $TenantId"
  Write-Host " Operacion : $Operation"
  Write-Host " Entorno   : $Environment"
  if ($s3Key) {
    Write-Host " S3        : s3://$uploadBucket/$s3Key"
  }
  Write-Host ""
  $sentJobs | Format-Table -AutoSize Market, Op, JobId, Queue
  Write-Host "-----------------------------------------------" -ForegroundColor Magenta
}
catch {
  Err $_.Exception.Message
  exit 1
}
