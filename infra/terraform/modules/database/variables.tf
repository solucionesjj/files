variable "environment" {
  description = "Deployment environment (controls backup retention defaults)."
  type        = string
}

variable "name_prefix" {
  description = "Name prefix for the RDS resources."
  type        = string
}

variable "private_subnet_ids" {
  description = "Private subnet IDs for the DB subnet group."
  type        = list(string)
}

variable "rds_security_group_id" {
  description = "Security group attached to RDS; its ingress is defined by the security module."
  type        = string
}

variable "engine_version" {
  description = "Pinned PostgreSQL 18.x version."
  type        = string
  default     = "18.6"
}

variable "instance_class" {
  description = "RDS instance class."
  type        = string
  default     = "db.t4g.medium"
}

variable "db_name" {
  description = "Initial database name."
  type        = string
}

variable "db_username" {
  description = "Master username."
  type        = string
}

variable "db_password" {
  description = "Master password. Provided as a sensitive input."
  type        = string
  sensitive   = true
}

variable "multi_az" {
  description = "Enable Multi-AZ."
  type        = bool
  default     = false
}

variable "deletion_protection" {
  description = "Enable deletion protection."
  type        = bool
  default     = false
}

variable "tags" {
  description = "Common tags applied to taggable resources."
  type        = map(string)
  default     = {}
}
