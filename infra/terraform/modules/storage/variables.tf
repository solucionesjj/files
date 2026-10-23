variable "environment" {
  description = "Deployment environment used in the bucket name."
  type        = string

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "environment must be one of: dev, staging, prod."
  }
}

variable "bucket_versioning" {
  description = "Enable object versioning."
  type        = bool
  default     = false
}

variable "portal_origin" {
  description = "Browser origin permitted to upload directly to S3."
  type        = string
}

variable "tags" {
  description = "Common tags applied to taggable resources."
  type        = map(string)
  default     = {}
}
