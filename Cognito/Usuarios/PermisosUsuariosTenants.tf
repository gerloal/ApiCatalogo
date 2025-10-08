variable "tenant_id"   { default = "Sportandem" }
variable "bucket_name" { default = "catalog-api-dev-upload" }
variable "queue_arn"   { default = "arn:aws:sqs:eu-west-1:340663646958:catalog-jobs" }

resource "aws_iam_user" "tenant" {
  name = "tenant-${var.tenant_id}"
  tags = { Tenant = var.tenant_id }
}

# Access keys para el cliente (guárdalas y entrégalas por canal seguro)
resource "aws_iam_access_key" "tenant" {
  user = aws_iam_user.tenant.name
}

# Política S3 (prefijo del tenant)
data "aws_iam_policy_document" "s3_tenant" {
  statement {
    sid     = "AllowPutObjectOnlyInTenantPrefix"
    actions = ["s3:PutObject"]
    resources = [
      "arn:aws:s3:::${var.bucket_name}/tenants/${var.tenant_id}/*"
    ]
    effect = "Allow"
  }
}

resource "aws_iam_policy" "s3_tenant" {
  name   = "tenant-${var.tenant_id}-s3-put"
  policy = data.aws_iam_policy_document.s3_tenant.json
}

# Política SQS (solo enviar a tu cola)
data "aws_iam_policy_document" "sqs_tenant" {
  statement {
    sid     = "AllowSendMessageToJobsQueue"
    actions = ["sqs:SendMessage"]
    resources = [var.queue_arn]
    effect = "Allow"
  }
}

resource "aws_iam_policy" "sqs_tenant" {
  name   = "tenant-${var.tenant_id}-sqs-send"
  policy = data.aws_iam_policy_document.sqs_tenant.json
}

# Adjunta políticas al usuario
resource "aws_iam_user_policy_attachment" "attach_s3" {
  user       = aws_iam_user.tenant.name
  policy_arn = aws_iam_policy.s3_tenant.arn
}
resource "aws_iam_user_policy_attachment" "attach_sqs" {
  user       = aws_iam_user.tenant.name
  policy_arn = aws_iam_policy.sqs_tenant.arn
}

# Outputs (muestra access key y secret una vez)
output "tenant_access_key_id" {
  value     = aws_iam_access_key.tenant.id
  sensitive = true
}
output "tenant_secret_access_key" {
  value     = aws_iam_access_key.tenant.secret
  sensitive = true
}
