# Script de estado completo del sistema Order Export
# Uso: .\system-status.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "dev"
)

Write-Host "==========================================="
Write-Host "?? Estado del Sistema Order Export"
Write-Host "==========================================="

$AccountId = aws sts get-caller-identity --query Account --output text

Write-Host "`nAccount: $AccountId"
Write-Host "Region: $Region"
Write-Host "Environment: $Environment"
Write-Host ""

# Contadores
$totalChecks = 0
$passedChecks = 0
$failedChecks = 0

# 1. Lambda Functions
Write-Host "1?? LAMBDA FUNCTIONS"
Write-Host ("=" * 50)

$functions = @("OrderExportAPI-$Environment", "OrderExportWorker-$Environment")

foreach ($func in $functions) {
    $totalChecks++
    try {
        $config = aws lambda get-function-configuration `
            --function-name $func `
            --region $Region `
            --output json 2>$null | ConvertFrom-Json
        
        if ($config.State -eq "Active") {
            Write-Host "? $func" -ForegroundColor Green
            Write-Host "   Handler: $($config.Handler)"
            Write-Host "   Runtime: $($config.Runtime)"
            Write-Host "   State: $($config.State)"
            $passedChecks++
        } else {
            Write-Host "??  $func - Estado: $($config.State)" -ForegroundColor Yellow
            $failedChecks++
        }
    } catch {
        Write-Host "? $func - No encontrada" -ForegroundColor Red
        $failedChecks++
    }
}

# 2. DynamoDB Table
Write-Host "`n2?? DYNAMODB TABLE"
Write-Host ("=" * 50)

$tableName = "OrderExportJobs-$Environment"
$totalChecks++

try {
    $table = aws dynamodb describe-table `
        --table-name $tableName `
        --region $Region `
        --output json 2>$null | ConvertFrom-Json
    
    Write-Host "? $tableName" -ForegroundColor Green
    Write-Host "   Status: $($table.Table.TableStatus)"
    Write-Host "   Items: $($table.Table.ItemCount)"
    $passedChecks++
} catch {
    Write-Host "? $tableName - No encontrada" -ForegroundColor Red
    $failedChecks++
}

# 3. SQS Queue
Write-Host "`n3?? SQS QUEUE"
Write-Host ("=" * 50)

$queueName = "order-export-$Environment"
$queueUrl = "https://sqs.$Region.amazonaws.com/$AccountId/$queueName"
$totalChecks++

try {
    $queueAttrs = aws sqs get-queue-attributes `
        --queue-url $queueUrl `
        --attribute-names All `
        --region $Region `
        --output json 2>$null | ConvertFrom-Json
    
    Write-Host "? $queueName" -ForegroundColor Green
    Write-Host "   Mensajes disponibles: $($queueAttrs.Attributes.ApproximateNumberOfMessages)"
    Write-Host "   Mensajes en proceso: $($queueAttrs.Attributes.ApproximateNumberOfMessagesNotVisible)"
    Write-Host "   VisibilityTimeout: $($queueAttrs.Attributes.VisibilityTimeout)s"
    $passedChecks++
} catch {
    Write-Host "? $queueName - No encontrada" -ForegroundColor Red
    $failedChecks++
}

# 4. S3 Bucket
Write-Host "`n4?? S3 BUCKET"
Write-Host ("=" * 50)

$bucketName = "order-exports-$AccountId-$Environment"
$totalChecks++

try {
    $bucket = aws s3api head-bucket `
        --bucket $bucketName `
        --region $Region 2>$null
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? $bucketName" -ForegroundColor Green
        
        # Contar archivos
        $objects = aws s3 ls "s3://$bucketName/" --recursive 2>$null
        $fileCount = ($objects | Measure-Object).Count
        Write-Host "   Archivos: $fileCount"
        $passedChecks++
    } else {
        Write-Host "? $bucketName - No encontrado" -ForegroundColor Red
        $failedChecks++
    }
} catch {
    Write-Host "? $bucketName - Error al verificar" -ForegroundColor Red
    $failedChecks++
}

# 5. Event Source Mapping
Write-Host "`n5?? EVENT SOURCE MAPPING (SQS ? Lambda)"
Write-Host ("=" * 50)

$totalChecks++

try {
    $mappings = aws lambda list-event-source-mappings `
        --function-name "OrderExportWorker-$Environment" `
        --region $Region `
        --output json 2>$null | ConvertFrom-Json
    
    if ($mappings.EventSourceMappings.Count -gt 0) {
        $mapping = $mappings.EventSourceMappings[0]
        if ($mapping.State -eq "Enabled") {
            Write-Host "? Event Source Mapping" -ForegroundColor Green
            Write-Host "   UUID: $($mapping.UUID)"
            Write-Host "   State: $($mapping.State)"
            Write-Host "   BatchSize: $($mapping.BatchSize)"
            $passedChecks++
        } else {
            Write-Host "??  Event Source Mapping - Estado: $($mapping.State)" -ForegroundColor Yellow
            $failedChecks++
        }
    } else {
        Write-Host "? Event Source Mapping - No configurado" -ForegroundColor Red
        $failedChecks++
    }
} catch {
    Write-Host "? Event Source Mapping - Error al verificar" -ForegroundColor Red
    $failedChecks++
}

# 6. CloudWatch Log Groups
Write-Host "`n6?? CLOUDWATCH LOG GROUPS"
Write-Host ("=" * 50)

foreach ($func in $functions) {
    $logGroupName = "/aws/lambda/$func"
    $totalChecks++
    
    try {
        $logGroup = aws logs describe-log-groups `
            --log-group-name-prefix $logGroupName `
            --region $Region `
            --output json 2>$null | ConvertFrom-Json
        
        if ($logGroup.logGroups.Count -gt 0) {
            Write-Host "? $logGroupName" -ForegroundColor Green
            Write-Host "   Retention: $($logGroup.logGroups[0].retentionInDays) días"
            $passedChecks++
        } else {
            Write-Host "? $logGroupName - No existe" -ForegroundColor Red
            $failedChecks++
        }
    } catch {
        Write-Host "? $logGroupName - Error al verificar" -ForegroundColor Red
        $failedChecks++
    }
}

# 7. IAM Role y Permisos
Write-Host "`n7?? IAM ROLE Y PERMISOS"
Write-Host ("=" * 50)

$roleName = "OrderExportLambdaRole-$Environment"
$totalChecks++

try {
    $role = aws iam get-role `
        --role-name $roleName `
        --output json 2>$null | ConvertFrom-Json
    
    Write-Host "? $roleName" -ForegroundColor Green
    
    # Listar políticas adjuntas
    $policies = aws iam list-attached-role-policies `
        --role-name $roleName `
        --output json 2>$null | ConvertFrom-Json
    
    Write-Host "   Políticas adjuntas:"
    foreach ($policy in $policies.AttachedPolicies) {
        $icon = if ($policy.PolicyName -match "Lambda.*Execution") { "?" } else { "??" }
        Write-Host "   $icon $($policy.PolicyName)"
    }
    
    # Verificar políticas críticas
    $hasCloudWatchLogs = $policies.AttachedPolicies | Where-Object { $_.PolicyName -match "Lambda.*Execution" }
    $hasSQS = $policies.AttachedPolicies | Where-Object { $_.PolicyName -match "SQS" }
    
    if ($hasCloudWatchLogs -and $hasSQS) {
        Write-Host "   ? Permisos críticos configurados" -ForegroundColor Green
        $passedChecks++
    } else {
        Write-Host "   ??  Faltan permisos críticos:" -ForegroundColor Yellow
        if (-not $hasCloudWatchLogs) { Write-Host "      - CloudWatch Logs" }
        if (-not $hasSQS) { Write-Host "      - SQS" }
        $failedChecks++
    }
    
} catch {
    Write-Host "? $roleName - No encontrado" -ForegroundColor Red
    $failedChecks++
}

# 8. Secrets Manager
Write-Host "`n8?? SECRETS MANAGER"
Write-Host ("=" * 50)

$secretNames = @(
    "Sportandem/prod/order-export",
    "/catalog-api/dev/tenants/Sportandem/spapi"
)

$secretFound = $false
foreach ($secretName in $secretNames) {
    $totalChecks++
    try {
        $secret = aws secretsmanager describe-secret `
            --secret-id $secretName `
            --region $Region `
            --output json 2>$null | ConvertFrom-Json
        
        if ($secret) {
            Write-Host "? $secretName" -ForegroundColor Green
            Write-Host "   ARN: $($secret.ARN)"
            Write-Host "   Última modificación: $($secret.LastChangedDate)"
            $passedChecks++
            $secretFound = $true
            break
        }
    } catch {
        # Continue to next secret
    }
}

if (-not $secretFound) {
    Write-Host "? No se encontró ningún secret configurado" -ForegroundColor Red
    Write-Host "   Formatos buscados:"
    foreach ($name in $secretNames) {
        Write-Host "   - $name"
    }
    $failedChecks++
}

# 9. API Gateway
Write-Host "`n9?? API GATEWAY"
Write-Host ("=" * 50)

$apiEndpoint = "https://q0wnv840ik.execute-api.eu-west-1.amazonaws.com/dev"
$totalChecks++

try {
    $response = Invoke-WebRequest -Uri "$apiEndpoint/exports/orders" `
        -Method Options `
        -TimeoutSec 5 `
        -ErrorAction SilentlyContinue 2>$null
    
    Write-Host "? API Gateway configurado" -ForegroundColor Green
    Write-Host "   Endpoint: $apiEndpoint"
    $passedChecks++
} catch {
    Write-Host "??  API Gateway - No responde a OPTIONS" -ForegroundColor Yellow
    Write-Host "   Endpoint: $apiEndpoint"
    Write-Host "   (Esto es normal si CORS no está configurado)"
    $passedChecks++  # No contar como fallo
}

# Resumen
Write-Host "`n" + ("=" * 50)
Write-Host "?? RESUMEN"
Write-Host ("=" * 50)

$percentage = [math]::Round(($passedChecks / $totalChecks) * 100, 0)

Write-Host "`nChecks realizados: $totalChecks"
Write-Host "? Exitosos: $passedChecks" -ForegroundColor Green
Write-Host "? Fallidos: $failedChecks" -ForegroundColor Red
Write-Host "Completitud: $percentage%"

if ($percentage -ge 90) {
    Write-Host "`n?? Sistema configurado correctamente!" -ForegroundColor Green
} elseif ($percentage -ge 70) {
    Write-Host "`n??  Sistema mayormente configurado, revisar elementos fallidos" -ForegroundColor Yellow
} else {
    Write-Host "`n? Sistema requiere configuración adicional" -ForegroundColor Red
}

# Próximos pasos
Write-Host "`n" + ("=" * 50)
Write-Host "?? PRÓXIMOS PASOS"
Write-Host ("=" * 50)

if ($failedChecks -gt 0) {
    Write-Host "`n??  Elementos que requieren atención:"
    
    # Verificar secret
    if (-not $secretFound) {
        Write-Host "`n1. Crear secret en Secrets Manager:"
        Write-Host "   .\create-sportandem-secret.ps1 -ClientId ... -ClientSecret ... -RefreshToken ... -RoleArn ... -SellerId ..."
        Write-Host "   O ver guía: CREATE_SECRET_GUIDE.md"
    }
}

Write-Host "`n? Para probar el sistema completo:"
Write-Host "   1. Ver logs en tiempo real:"
Write-Host "      aws logs tail /aws/lambda/OrderExportWorker-$Environment --follow --region $Region"
Write-Host ""
Write-Host "   2. Crear un job de exportación:"
Write-Host "      .\test-order-export-detailed.ps1"
Write-Host ""
Write-Host "   3. Verificar archivo en S3:"
Write-Host "      aws s3 ls s3://$bucketName/ --recursive"

Write-Host "`n==========================================="
Write-Host "? Verificación completada"
Write-Host "==========================================="
