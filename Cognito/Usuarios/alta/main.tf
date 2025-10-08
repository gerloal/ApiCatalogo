locals {
  # Deriva la URL de la cola a partir del ARN
  # arn:aws:sqs:<region>:<accountId>:<queueName> -> https://sqs.<region>.amazonaws.com/<accountId>/<queueName>
  queue_url   = "https://sqs.eu-west-1.amazonaws.com/340663646958/catalog-jobs"
}

# 1) Usuario IAM por tenant
resource "aws_iam_user" "tenant" {
  for_each = var.tenants
  name     = "tenant-${each.key}"
  tags = {
    Tenant = each.key
    Role   = "catalog-uploader"
  }
}

# 2) Access key/secret (entregar al cliente)
resource "aws_iam_access_key" "tenant" {
  for_each = aws_iam_user.tenant
  user     = each.value.name
}

# 3) Política S3: PutObject SOLO en su prefijo
data "aws_iam_policy_document" "s3_tenant" {
  for_each = var.tenants

  statement {
    sid     = "AllowPutObjectOnlyInTenantPrefix"
    effect  = "Allow"
    actions = ["s3:PutObject"]

    resources = [
      "arn:aws:s3:::${var.bucket_name}/tenants/${each.key}/*"
    ]
  }
}

resource "aws_iam_policy" "s3_tenant" {
  for_each = data.aws_iam_policy_document.s3_tenant
  name     = "tenant-${each.key}-s3-put"
  policy   = each.value.json
}

# 4) Política SQS: SendMessage solo a TU cola
data "aws_iam_policy_document" "sqs_tenant" {
  for_each = var.tenants

  statement {
    sid     = "AllowSendMessageToJobsQueue"
    effect  = "Allow"
    actions = ["sqs:SendMessage"]
    resources = [var.queue_arn]
  }
}

resource "aws_iam_policy" "sqs_tenant" {
  for_each = data.aws_iam_policy_document.sqs_tenant
  name     = "tenant-${each.key}-sqs-send"
  policy   = each.value.json
}

# 5) Adjuntar políticas al usuario
resource "aws_iam_user_policy_attachment" "attach_s3" {
  for_each   = aws_iam_user.tenant
  user       = each.value.name
  policy_arn = aws_iam_policy.s3_tenant[each.key].arn
}

resource "aws_iam_user_policy_attachment" "attach_sqs" {
  for_each   = aws_iam_user.tenant
  user       = each.value.name
  policy_arn = aws_iam_policy.sqs_tenant[each.key].arn
}

# 6) Outputs (sensibles) con las credenciales del cliente
output "tenant_access_keys" {
  description = "Access key ID por tenant"
  value       = { for t, k in aws_iam_access_key.tenant : t => k.id }
  sensitive   = true
}

output "tenant_secret_keys" {
  description = "Secret access key por tenant (mostrar solo una vez)"
  value       = { for t, k in aws_iam_access_key.tenant : t => k.secret }
  sensitive   = true
}

# Útil para scripts del cliente
output "queue_url" {
  description = "URL de la cola SQS"
  value       = local.queue_url
}

output "s3_tenant_prefixes" {
  description = "Prefijo S3 permitido por tenant"
  value       = { for t in var.tenants : t => "s3://${var.bucket_name}/tenants/${t}/" }
}
