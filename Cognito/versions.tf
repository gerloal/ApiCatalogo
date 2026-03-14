
terraform {
  required_version = ">= 1.5.0"
  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = ">= 5.0"
    }
  }

  backend "s3" {
    bucket  = "apicatalogo-terraform-state-340663646958"
    key     = "cognito/terraform.tfstate"
    region  = "eu-west-1"
    encrypt = true
  }
}
