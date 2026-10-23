locals {
  name_prefix = "files-${var.environment}"

  common_tags = {
    Environment = var.environment
    Product     = "Files"
    ManagedBy   = "Terraform"
  }
}
