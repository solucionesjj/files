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

  # Remote state is strongly recommended. Bootstrap a versioned, encrypted,
  # public-access-blocked S3 bucket and one state key per environment, then
  # uncomment and adjust this block before running `terraform init`.
  #
  # backend "s3" {
  #   bucket       = "REPLACE_WITH_STATE_BUCKET"
  #   key          = "files/dev/terraform.tfstate"
  #   region       = "us-east-1"
  #   encrypt      = true
  #   use_lockfile = true
  # }
}
