variable "region" {
  description = "AWS region for the EC2 instance, EBS volume, and S3 bucket."
  type        = string
  default     = "us-east-1"

  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[0-9]+$", var.region))
    error_message = "region must be a valid AWS region name such as us-east-1."
  }
}

variable "environment" {
  description = "Deployment environment used in resource names and tags."
  type        = string
  default     = "prod"

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "environment must be one of: dev, staging, prod."
  }
}

variable "instance_type" {
  description = "EC2 instance type. t3.medium (2 vCPU/4 GiB) is the recommended starting point."
  type        = string
  default     = "t3.medium"
}

variable "ami_id" {
  description = "Optional AMI override. Empty selects the latest Canonical Ubuntu 24.04 LTS x86_64 image."
  type        = string
  default     = ""
}

variable "ssh_key_name" {
  description = "Existing EC2 key pair name for SSH. Empty means no key pair is attached (console/SSM access only)."
  type        = string
  default     = ""
}

variable "admin_cidr" {
  description = "CIDR allowed to reach SSH (port 22). Required; never 0.0.0.0/0."
  type        = string

  validation {
    condition     = can(cidrhost(var.admin_cidr, 0)) && var.admin_cidr != "0.0.0.0/0"
    error_message = "admin_cidr must be a valid CIDR and must not be 0.0.0.0/0."
  }
}

variable "vpc_id" {
  description = "Optional VPC ID. Empty uses the account's default VPC."
  type        = string
  default     = ""
}

variable "subnet_id" {
  description = "Optional public subnet ID. Empty uses the first subnet of the selected VPC."
  type        = string
  default     = ""
}

variable "root_volume_size_gb" {
  description = "Root EBS volume size. Must hold PostgreSQL, staging uploads, and the OS."
  type        = number
  default     = 100

  validation {
    condition     = var.root_volume_size_gb >= 30 && var.root_volume_size_gb <= 16384
    error_message = "root_volume_size_gb must be between 30 and 16384."
  }
}

variable "enable_termination_protection" {
  description = "Enable EC2 termination protection (recommended)."
  type        = bool
  default     = true
}

variable "delete_root_volume_on_termination" {
  description = "Delete the EBS root volume with the instance. Keep false for safety; snapshots/backups are separate."
  type        = bool
  default     = false
}

variable "bucket_name" {
  description = "Optional S3 bucket name override. Empty generates files-<env>-<account>-<region>-<suffix>."
  type        = string
  default     = ""
}

variable "bucket_versioning" {
  description = "Enable S3 versioning. Keep false (ADR-0008) to avoid hidden copies of deleted objects."
  type        = bool
  default     = false
}

variable "backup_retention_days" {
  description = "Lifecycle retention for backups/postgres/ objects."
  type        = number
  default     = 35

  validation {
    condition     = var.backup_retention_days >= 1 && var.backup_retention_days <= 365
    error_message = "backup_retention_days must be between 1 and 365."
  }
}
