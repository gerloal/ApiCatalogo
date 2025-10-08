function Write-JobQueued {
    <#
    .SYNOPSIS
      Crea (PutItem) un Job en DynamoDB con estado QUEUED.

    .PARAMETER Tenant
      Identificador del cliente (tenantId).

    .PARAMETER Region
      Región AWS.

    .PARAMETER TableName
      Nombre de la tabla DynamoDB (por defecto: Jobs).

    .PARAMETER Pk
      Partition Key completo. Si no lo pasas, se construye como TENANT#{Tenant}.

    .PARAMETER Sk
      Sort Key completo. Si no lo pasas, se construye como JOB#{yyyyMMdd}#{JobId}.

    .PARAMETER JobId
      Identificador del job (si no lo pasas, se genera un GUID).

    .PARAMETER S3Key
      Key del objeto en S3 (tenants/{tenant}/{yyyy/MM/dd}/{jobId}_catalog.json, por ejemplo).

    .PARAMETER SizeBytes
      Tamaño del archivo en bytes (opcional).
    #>
    param(
        [Parameter(Mandatory=$true)][string]$Tenant,
        [string]$Region     = "eu-west-1",
        [string]$TableName  = "Jobs",
        [string]$Pk,
        [string]$Sk,
        [string]$JobId,
        [Parameter(Mandatory=$true)][string]$S3Key,
        [Nullable[long]]$SizeBytes
    )

    # Helpers de salida (opcionales)
    function Out-Step($msg){ Write-Host "• $msg" -ForegroundColor Cyan }
    function Out-Ok($msg)  { Write-Host "  ✓ $msg" -ForegroundColor Green }

    # Defaults
    if ([string]::IsNullOrWhiteSpace($JobId)) { $JobId = [guid]::NewGuid().ToString("N") }
    if ([string]::IsNullOrWhiteSpace($Pk))    { $Pk    = "TENANT#$Tenant" }
    if ([string]::IsNullOrWhiteSpace($Sk))    { $Sk    = "JOB#$(Get-Date -Format yyyyMMdd)#$JobId" }

    Out-Step "Creando ítem en DynamoDB (QUEUED)"
    $itemObj = @{
        pk        = @{ S = $Pk }
        sk        = @{ S = $Sk }
        status    = @{ S = "QUEUED" }
        fileKey   = @{ S = $S3Key }
        tenant    = @{ S = $Tenant }
        createdAt = @{ S = (Get-Date).ToUniversalTime().ToString("o") }
    }

    if ($SizeBytes -ne $null) { $itemObj.size = @{ N = "$SizeBytes" } }

       $itemJson  = ($itemObj | ConvertTo-Json -Depth 6)
    $itemPath  = Join-Path $PWD "item.json"
    $itemJson  | Out-File -Encoding utf8 $itemPath
    $itemAbs   = (Resolve-Path $itemPath).Path

    # Ejecuta AWS CLI y captura stdout+stderr para ver el error real si falla
    $awsCmd = @(
        'dynamodb','put-item',
        '--table-name', $TableName,
        '--item', "file://$itemAbs",
        '--region', $Region,
        '--cli-binary-format','raw-in-base64-out'
    )
    $out = & aws $awsCmd 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host $out -ForegroundColor Red
        throw "AWS CLI put-item failed (exit $LASTEXITCODE)"
    }

    Out-Ok "PutItem OK ($TableName) → pk='$Pk' sk='$Sk'"

}
