<#
.SYNOPSIS
  Envía job(s) a la Lambda para el tenant Sportandem.
  Sube el fichero CSV a S3 (solo FeedCatalog) y encola el mensaje en SQS.

.PARAMETER Operation
  FeedCatalog  — Actualiza precios/stock en el marketplace.
  ExportOrders — Solicita exportación de pedidos (sin subida de fichero).

.PARAMETER Destination
  Amazon | Miravia | PcComponentes | All
  Por defecto: All (envía a los tres marketplaces).

.PARAMETER FilePath
  Ruta local al CSV con formato SKU;Stock;Precio.
  Obligatorio si Operation = FeedCatalog.

.PARAMETER StartDate
  Fecha inicio para ExportOrders (ISO 8601). Por defecto: hace 30 días.

.PARAMETER EndDate
  Fecha fin para ExportOrders (ISO 8601). Por defecto: ahora UTC.

.PARAMETER Environment
  dev | prod. Por defecto: dev.

.PARAMETER Region
  Región AWS. Por defecto: eu-west-1.

.EXAMPLE
  # Actualizar catálogo en los tres marketplaces
  .\sportandem-send-job.ps1 -Operation FeedCatalog -FilePath .\catalogo.csv

.EXAMPLE
  # Exportar pedidos solo de Miravia
  .\sportandem-send-job.ps1 -Operation ExportOrders -Destination Miravia

.EXAMPLE
  # Exportar pedidos Amazon del último mes en prod
  .\sportandem-send-job.ps1 -Operation ExportOrders -Destination Amazon -Environment prod
#>

param(
  [ValidateSet("FeedCatalog", "ExportOrders")]
  [string]$Operation   = "FeedCatalog",

  [ValidateSet("Amazon", "Miravia", "PcComponentes", "All")]
  [string]$Destination = "All",

  [string]$FilePath    = "",
  [string]$StartDate   = "",
  [string]$EndDate     = "",

  [ValidateSet("dev", "prod")]
  [string]$Environment = "dev",

  [string]$Region      = "eu-west-1"
)

# ─── Constantes ───────────────────────────────────────────────────────────────
$TenantId        = "sportandem"
$CatalogQueue    = "catalog-jobs-$Environment"   # FeedCatalog + ExportOrders (Miravia/PcC)
$OrderQueue      = "order-export-$Environment"   # ExportOrders Amazon (worker original)
$CatalogTable    = "CatalogJobsTable"            # se resuelve dinámicamente desde CloudFormation
$OrderTable      = "OrderExportJobs-$Environment"

$ErrorActionPreference = "Stop"
$ProgressPreference    = "SilentlyContinue"
$env:AWS_PAGER         = ""

# ─── Helpers ──────────────────────────────────────────────────────────────────
function Step($msg)  { Write-Host "`n• $msg" -ForegroundColor Cyan }
function Ok($msg)    { Write-Host "  ✓ $msg" -ForegroundColor Green }
function Warn($msg)  { Write-Host "  ! $msg" -ForegroundColor Yellow }
function Err($msg)   { Write-Host "  ✗ $msg" -ForegroundColor Red }
function Info($msg)  { Write-Host "    $msg" -ForegroundColor Gray }

function Get-OperationName([string]$dest, [string]$op) {
  if ($op -eq "FeedCatalog") {
    switch ($dest) {
      "Amazon"        { return "FEED_CATALOG" }
      "Miravia"       { return "MIRAVIA_FEED_CATALOG" }
      "PcComponentes" { return "PCCOMPONENTES_FEED_CATALOG" }
    }
  } else {
    switch ($dest) {
      "Amazon"        { return "EXPORT_ORDERS" }
      "Miravia"       { return "MIRAVIA_EXPORT_ORDERS" }
      "PcComponentes" { return "PCCOMPONENTES_EXPORT_ORDERS" }
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
  Ok "$Dest → $OpName  [jobId=$jobId]"
  return $jobId
}

function Send-ExportOrdersMessage {
  param([string]$Dest, [string]$OpName, [string]$Start, [string]$End, [string]$QueueUrl)

  $jobId = [guid]::NewGuid().ToString("N")

  # ExportOrdersQueueMessage usa PascalCase
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
  Ok "$Dest → $OpName  [jobId=$jobId]"
  return $jobId
}

# ─── Main ─────────────────────────────────────────────────────────────────────
try {
  # 1. Verificar AWS CLI
  Step "Verificando AWS CLI"
  if (-not (Get-Command aws -ErrorAction SilentlyContinue)) {
    throw "AWS CLI no encontrado. Instálalo y ejecuta 'aws configure'."
  }
  $accountId = (aws sts get-caller-identity --query Account --output text)
  if (-not $accountId) { throw "No se pudo obtener AccountId. Ejecuta 'aws configure'." }
  Ok "Cuenta: $accountId | Región: $Region | Entorno: $Environment"

  # 2. Determinar destinos
  $destinations = if ($Destination -eq "All") {
    @("Amazon", "Miravia", "PcComponentes")
  } else {
    @($Destination)
  }
  Info "Destinos: $($destinations -join ', ') | Operación: $Operation | Tenant: $TenantId"

  # 3. Validar parámetros según operación
  $uploadKey    = ""
  $uploadBucket = "catalog-uploads-$accountId"

  if ($Operation -eq "FeedCatalog") {
    if ([string]::IsNullOrWhiteSpace($FilePath) -or -not (Test-Path $FilePath)) {
      throw "Se requiere -FilePath con la ruta al CSV para FeedCatalog."
    }
    $sizeBytes = (Get-Item $FilePath).Length
    Info "Fichero: $FilePath ($sizeBytes bytes)"
  } else {
    # ExportOrders: calcular fechas por defecto
    if ([string]::IsNullOrWhiteSpace($EndDate)) {
      $EndDate = (Get-Date).ToUniversalTime().ToString("o")
    }
    if ([string]::IsNullOrWhiteSpace($StartDate)) {
      $StartDate = (Get-Date).ToUniversalTime().AddDays(-30).ToString("o")
    }
    Info "Rango: $StartDate → $EndDate"
  }

  # 4. Subida a S3 (solo FeedCatalog)
  $s3Key = ""
  if ($Operation -eq "FeedCatalog") {
    Step "Subiendo fichero a S3"
    $dateSlash = Get-Date -Format yyyy/MM/dd
    $jobIdBase = [guid]::NewGuid().ToString("N")
    $s3Key     = "tenants/$TenantId/$dateSlash/${jobIdBase}_catalog.csv"

    aws s3 cp "$FilePath" "s3://$uploadBucket/$s3Key" --region $Region | Out-Null
    Ok "s3://$uploadBucket/$s3Key"
  }

  # 5. Resolver URLs de colas
  Step "Resolviendo URLs de colas SQS"
  $catalogQueueUrl = "https://sqs.$Region.amazonaws.com/$accountId/$CatalogQueue"
  $orderQueueUrl   = "https://sqs.$Region.amazonaws.com/$accountId/$OrderQueue"
  Info "Catalog queue : $catalogQueueUrl"
  Info "Order queue   : $orderQueueUrl"

  # 6. Enviar mensajes
  Step "Enviando mensajes SQS"
  $sentJobs = @()

  foreach ($dest in $destinations) {
    $opName = Get-OperationName -dest $dest -op $Operation

    if ($Operation -eq "FeedCatalog") {
      # Todos los FeedCatalog van a catalog-jobs
      $jid = Send-CatalogJobMessage -Dest $dest -OpName $opName `
               -Bucket $uploadBucket -Key $s3Key -QueueUrl $catalogQueueUrl
      $sentJobs += [PSCustomObject]@{ Dest=$dest; Op=$opName; JobId=$jid; Queue=$CatalogQueue }

    } else {
      # ExportOrders Amazon → order-export queue (worker dedicado)
      # ExportOrders Miravia / PcComponentes → catalog-jobs queue (CatalogoService)
      if ($dest -eq "Amazon") {
        $jid = Send-ExportOrdersMessage -Dest $dest -OpName $opName `
                 -Start $StartDate -End $EndDate -QueueUrl $orderQueueUrl
        $sentJobs += [PSCustomObject]@{ Dest=$dest; Op=$opName; JobId=$jid; Queue=$OrderQueue }
      } else {
        # Miravia/PcC export orders usan QueueMsg (camelCase) con operación específica
        $jid = Send-CatalogJobMessage -Dest $dest -OpName $opName `
                 -Bucket "order-exports-$accountId-$Environment" -Key "" `
                 -QueueUrl $catalogQueueUrl
        $sentJobs += [PSCustomObject]@{ Dest=$dest; Op=$opName; JobId=$jid; Queue=$CatalogQueue }
      }
    }
  }

  # 7. Resumen
  Write-Host ""
  Write-Host "═══════════════════════════════════════════════" -ForegroundColor Magenta
  Write-Host " SPORTANDEM — Jobs encolados" -ForegroundColor Magenta
  Write-Host "═══════════════════════════════════════════════" -ForegroundColor Magenta
  Write-Host " Tenant     : $TenantId"
  Write-Host " Operación  : $Operation"
  Write-Host " Entorno    : $Environment"
  if ($s3Key) {
    Write-Host " S3 fichero : s3://$uploadBucket/$s3Key"
  }
  Write-Host ""
  $sentJobs | Format-Table -AutoSize Dest, Op, JobId, Queue
  Write-Host "═══════════════════════════════════════════════" -ForegroundColor Magenta
}
catch {
  Err $_.Exception.Message
  exit 1
}
