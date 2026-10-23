variable "name_prefix" {
  description = "Name prefix for security groups."
  type        = string
}

variable "vpc_id" {
  description = "VPC in which the security groups are created."
  type        = string
}

variable "app_port" {
  description = "API container port allowed from the ALB."
  type        = number
  default     = 8080

  validation {
    condition     = var.app_port >= 1 && var.app_port <= 65535
    error_message = "app_port must be a valid TCP port."
  }
}

variable "db_port" {
  description = "PostgreSQL port allowed from ECS tasks."
  type        = number
  default     = 5432

  validation {
    condition     = var.db_port >= 1 && var.db_port <= 65535
    error_message = "db_port must be a valid TCP port."
  }
}

variable "tags" {
  description = "Common tags applied to taggable resources."
  type        = map(string)
  default     = {}
}
