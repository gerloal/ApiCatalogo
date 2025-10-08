variable "region" {
  type        = string
  default     = "eu-west-1"
  description = "AWS region"
}

variable "bucket_name" {
  type        = string
  description = "S3 bucket donde suben los ficheros"
}

variable "queue_arn" {
  type        = string
  description = "ARN de la cola SQS donde enviarán mensajes"
}

# Lista de tenants (IDs legibles, p.ej. Sportandem, DemoCo)
variable "tenants" {
  type        = set(string)
  description = "Conjunto de tenants a provisionar"
}
