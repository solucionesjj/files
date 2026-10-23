variable "name_prefix" {
  description = "Name prefix for compute resources."
  type        = string
}

variable "environment" {
  description = "Deployment environment."
  type        = string

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "environment must be one of: dev, staging, prod."
  }
}

variable "region" {
  description = "AWS region for container environment variables."
  type        = string
}

variable "vpc_id" {
  description = "VPC ID."
  type        = string
}

variable "public_subnet_ids" {
  description = "Public subnet IDs for the ALB."
  type        = list(string)
}

variable "alb_security_group_id" {
  description = "Security group ID attached to the public ALB."
  type        = string
}

variable "api_security_group_id" {
  description = "Security group ID attached to API tasks."
  type        = string
}

variable "worker_security_group_id" {
  description = "Security group ID attached to worker tasks."
  type        = string
}

variable "private_subnet_ids" {
  description = "Private subnet IDs for ECS tasks."
  type        = list(string)
}

variable "bucket_name" {
  description = "Uploads bucket name."
  type        = string
}

variable "bucket_arn" {
  description = "Uploads bucket ARN."
  type        = string
}

variable "db_address" {
  description = "RDS hostname."
  type        = string
}

variable "db_port" {
  description = "RDS PostgreSQL port."
  type        = number
}

variable "db_name" {
  description = "Application database name."
  type        = string
}

variable "db_username" {
  description = "Application database username."
  type        = string
}

variable "db_password" {
  description = "PostgreSQL password injected into a Secrets Manager secret for ECS. Sensitive; never commit."
  type        = string
  sensitive   = true
}

variable "app_secrets" {
  description = "Additional ECS secret environment variables: map of ASP.NET Core config key to Secrets Manager ARN. The execution role may read these."
  type        = map(string)
  default     = {}
}

variable "app_environment" {
  description = "Additional non-secret environment variables (ASP.NET Core config keys), merged over the base set."
  type        = map(string)
  default     = {}
}

variable "otel_endpoint" {
  description = "OTLP endpoint for SigNoz/collector. Empty disables the environment variable."
  type        = string
  default     = ""
}

variable "api_image" {
  description = "API container image; empty uses the API ECR repository."
  type        = string
  default     = ""
}

variable "worker_image" {
  description = "Worker container image; empty uses the worker ECR repository."
  type        = string
  default     = ""
}

variable "api_cpu" {
  description = "Fargate CPU units for the API task."
  type        = number
  default     = 512
}

variable "api_memory" {
  description = "Fargate memory MiB for the API task."
  type        = number
  default     = 1024
}

variable "worker_cpu" {
  description = "Fargate CPU units for the worker task."
  type        = number
  default     = 512
}

variable "worker_memory" {
  description = "Fargate memory MiB for the worker task."
  type        = number
  default     = 1024
}

variable "api_desired_count" {
  description = "API service desired task count."
  type        = number
  default     = 2
}

variable "worker_desired_count" {
  description = "Worker service desired task count."
  type        = number
  default     = 1
}

variable "api_autoscaling_min" {
  description = "Minimum API task count."
  type        = number
  default     = 2
}

variable "api_autoscaling_max" {
  description = "Maximum API task count."
  type        = number
  default     = 8
}

variable "api_cpu_target" {
  description = "Target average CPU utilization for API autoscaling."
  type        = number
  default     = 60
}

variable "certificate_arn" {
  description = "Optional ACM certificate ARN for HTTPS. When empty, HTTP forwards directly to the API target group."
  type        = string
  default     = ""
}

variable "log_retention_days" {
  description = "CloudWatch log retention period."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Common tags applied to taggable resources."
  type        = map(string)
  default     = {}
}
