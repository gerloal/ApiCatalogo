# Script para verificar y corregir permisos de CloudWatch Logs
# Uso: .\verify-lambda-permissions.ps1

param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "eu-west-1",
    
    [Parameter(Mandatory=$false)]
    [string]$RoleName = "OrderExportLambdaRole-dev"
)

Write-Host "==========================================="
Write-Host "?? Verificando Permisos de Lambda Role"
Write-Host "==========================================="

Write-Host "`nRole: $RoleName"
Write-Host "Region: $Region"

# 1. Verificar que el role existe
Write-Host "`n1?? Verificando que el role existe..."

try {
    $role = aws iam get-role `
        --role-name $RoleName `
        --output json | ConvertFrom-Json
    
    Write-Host "? Role encontrado:" -ForegroundColor Green
    Write-Host "   ARN: $($role.Role.Arn)"
    Write-Host "   Creado: $($role.Role.CreateDate)"
    
} catch {
    Write-Host "? Role no encontrado: $RoleName" -ForegroundColor Red
    Write-Host "   Error: $_"
    exit 1
}

# 2. Listar políticas adjuntas (managed policies)
Write-Host "`n2?? Verificando políticas adjuntas (Managed Policies)..."

try {
    $attachedPolicies = aws iam list-attached-role-policies `
        --role-name $RoleName `
        --output json | ConvertFrom-Json
    
    if ($attachedPolicies.AttachedPolicies.Count -gt 0) {
        Write-Host "? Políticas adjuntas encontradas:" -ForegroundColor Green
        foreach ($policy in $attachedPolicies.AttachedPolicies) {
            Write-Host "   - $($policy.PolicyName)" -ForegroundColor Cyan
            Write-Host "     ARN: $($policy.PolicyArn)"
        }
        
        # Verificar si tiene la política de CloudWatch Logs
        $hasCloudWatchPolicy = $attachedPolicies.AttachedPolicies | Where-Object { 
            $_.PolicyName -match "CloudWatch" -or 
            $_.PolicyName -match "Lambda.*Execution" 
        }
        
        if ($hasCloudWatchPolicy) {
            Write-Host "`n   ? Tiene política de CloudWatch/Lambda" -ForegroundColor Green
        } else {
            Write-Host "`n   ??  NO tiene política de CloudWatch Logs" -ForegroundColor Yellow
        }
    } else {
        Write-Host "??  No hay políticas adjuntas (managed)" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "? Error al listar políticas adjuntas" -ForegroundColor Red
    Write-Host "   $_"
}

# 3. Listar políticas inline
Write-Host "`n3?? Verificando políticas inline..."

try {
    $inlinePolicies = aws iam list-role-policies `
        --role-name $RoleName `
        --output json | ConvertFrom-Json
    
    if ($inlinePolicies.PolicyNames.Count -gt 0) {
        Write-Host "? Políticas inline encontradas:" -ForegroundColor Green
        
        foreach ($policyName in $inlinePolicies.PolicyNames) {
            Write-Host "`n   ?? Política: $policyName" -ForegroundColor Cyan
            
            $policyDoc = aws iam get-role-policy `
                --role-name $RoleName `
                --policy-name $policyName `
                --output json | ConvertFrom-Json
            
            $policyJson = $policyDoc.PolicyDocument | ConvertTo-Json -Depth 10
            Write-Host "   Contenido:"
            Write-Host "   $policyJson"
            
            # Verificar si tiene permisos de CloudWatch Logs
            $hasLogsPermissions = $policyJson -match "logs:CreateLogGroup" -or 
                                  $policyJson -match "logs:CreateLogStream" -or
                                  $policyJson -match "logs:PutLogEvents"
            
            if ($hasLogsPermissions) {
                Write-Host "   ? Tiene permisos de CloudWatch Logs" -ForegroundColor Green
            } else {
                Write-Host "   ??  NO tiene permisos de CloudWatch Logs" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "??  No hay políticas inline" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "? Error al listar políticas inline" -ForegroundColor Red
    Write-Host "   $_"
}

# 4. Verificar permisos necesarios
Write-Host "`n4?? Verificando permisos necesarios..."

$requiredPermissions = @{
    "CloudWatch Logs" = @("logs:CreateLogGroup", "logs:CreateLogStream", "logs:PutLogEvents")
    "DynamoDB" = @("dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:Query")
    "SQS" = @("sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes")
    "S3" = @("s3:PutObject", "s3:GetObject")
    "Secrets Manager" = @("secretsmanager:GetSecretValue")
}

Write-Host "`n?? Permisos requeridos para el Worker Lambda:"
foreach ($service in $requiredPermissions.Keys) {
    Write-Host "`n   $service :"
    foreach ($perm in $requiredPermissions[$service]) {
        Write-Host "   - $perm"
    }
}

# 5. Proponer solución
Write-Host "`n5?? Soluciones disponibles..."

Write-Host "`n?? Opción 1: Adjuntar política AWS Managed (Recomendado)"
Write-Host "   Esta política incluye permisos básicos de Lambda + CloudWatch Logs:"
Write-Host ""
Write-Host "   aws iam attach-role-policy \"
Write-Host "       --role-name $RoleName \"
Write-Host "       --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
Write-Host ""

Write-Host "`n?? Opción 2: Crear política inline personalizada"
Write-Host "   Esta opción da control granular sobre los permisos:"
Write-Host ""
Write-Host "   .\add-cloudwatch-permissions.ps1"
Write-Host ""

Write-Host "`n?? Opción 3: Adjuntar múltiples políticas AWS Managed"
Write-Host ""
Write-Host "   # CloudWatch Logs"
Write-Host "   aws iam attach-role-policy \"
Write-Host "       --role-name $RoleName \"
Write-Host "       --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
Write-Host ""
Write-Host "   # SQS"
Write-Host "   aws iam attach-role-policy \"
Write-Host "       --role-name $RoleName \"
Write-Host "       --policy-arn arn:aws:iam::aws:policy/service-role/AWSLambdaSQSQueueExecutionRole"
Write-Host ""

# 6. Ofrecer aplicar la solución
Write-Host "`n==========================================="
Write-Host "?? Aplicar Solución"
Write-Host "==========================================="

$apply = Read-Host "`n¿Quieres adjuntar AWSLambdaBasicExecutionRole ahora? (S/n)"

if ($apply -ne "n" -and $apply -ne "N") {
    Write-Host "`nAdjuntando política..."
    
    try {
        aws iam attach-role-policy `
            --role-name $RoleName `
            --policy-arn "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "? Política adjuntada exitosamente!" -ForegroundColor Green
            Write-Host "`nEsta política incluye permisos para:"
            Write-Host "   - logs:CreateLogGroup"
            Write-Host "   - logs:CreateLogStream"
            Write-Host "   - logs:PutLogEvents"
        } else {
            Write-Host "? Error al adjuntar política" -ForegroundColor Red
        }
        
    } catch {
        Write-Host "? Error: $_" -ForegroundColor Red
    }
}

# 7. Verificar permisos después de aplicar
if ($apply -ne "n" -and $apply -ne "N") {
    Write-Host "`n7?? Verificando permisos después de aplicar..."
    Start-Sleep -Seconds 2
    
    $attachedPolicies = aws iam list-attached-role-policies `
        --role-name $RoleName `
        --output json | ConvertFrom-Json
    
    Write-Host "`n? Políticas adjuntas actualizadas:" -ForegroundColor Green
    foreach ($policy in $attachedPolicies.AttachedPolicies) {
        Write-Host "   - $($policy.PolicyName)" -ForegroundColor Cyan
    }
}

Write-Host "`n==========================================="
Write-Host "? Verificación completada"
Write-Host "==========================================="

Write-Host "`n?? Próximos pasos:"
Write-Host "   1. Si adjuntaste la política, espera 1-2 minutos"
Write-Host "   2. Invoca la Lambda para generar logs:"
Write-Host "      .\force-lambda-execution.ps1"
Write-Host "   3. Verifica que se generen logs:"
Write-Host "      aws logs tail /aws/lambda/OrderExportWorker-dev --follow --region $Region"
