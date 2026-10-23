terraform {
  required_version = ">= 1.9.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  # Remote state (recommended). Bootstrap the bucket/versioning/encryption once,
  # then uncomment and configure. Never commit state or real tfvars.
  #
  # backend "s3" {
  #   bucket       = "files-tfstate-<account-id>-us-east-1"
  #   key          = "files/prod/terraform.tfstate"
  #   region       = "us-east-1"
  #   encrypt      = true
  #   use_lockfile = true
  # }
}
