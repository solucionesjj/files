variable "region" {
  description = "AWS region in which all resources are created."
  type        = string
  default     = "us-east-1"

  validation {
    condition     = can(regex("^[a-z]{2}-[a-z]+-[0-9]+$", var.region))
    error_message = "region must be a valid AWS region name such as us-east-1."
  }
}

variable "environment" {
  description = "Deployment environment. One environment is managed per root invocation."
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "staging", "prod"], var.environment)
    error_message = "environment must be one of: dev, staging, prod."
  }
}

variable "vpc_cidr" {
  description = "CIDR block for the VPC. The default leaves enough room for two public and two private /24 subnets."
  type        = string
  default     = "10.20.0.0/16"

  validation {
    condition     = can(cidrhost(var.vpc_cidr, 0)) && can(cidrsubnet(var.vpc_cidr, 8, 11))
    error_message = "vpc_cidr must be a valid IPv4 CIDR large enough to support /24 subnets (for example, 10.20.0.0/16)."
  }
}

variable "nat_gateway_count" {
  description = "Number of NAT gateways. Use 1 for dev/staging and 2 for multi-AZ egress in prod."
  type        = number
  default     = 1

  validation {
    condition     = var.nat_gateway_count >= 1 && var.nat_gateway_count <= 2
    error_message = "nat_gateway_count must be 1 or 2; the network module creates exactly two AZs."
  }
}

variable "bucket_versioning" {
  description = "Enable S3 object versioning for uploads."
  type        = bool
  default     = false
}

variable "portal_origin" {
  description = "Exact browser origin allowed to PUT directly to S3, for example https://files.example.com. No trailing slash."
  type        = string
  default     = "http://localhost:4200"

  validation {
    condition     = can(regex("^https?://[^/]+$", var.portal_origin))
    error_message = "portal_origin must be an http(s) origin without a path or trailing slash."
  }
}

variable "db_engine_version" {
  description = "Pinned PostgreSQL 18.x engine version. RDS validates exact minor availability at apply time."
  type        = string
  default     = "18.6"

  validation {
    condition     = can(regex("^18\\.[0-9]+$", var.db_engine_version))
    error_message = "db_engine_version must be a PostgreSQL 18.x version such as 18.6."
  }
}

variable "db_instance_class" {
  description = "RDS instance class for PostgreSQL."
  type        = string
  default     = "db.t4g.medium"
}

variable "db_name" {
  description = "Initial database name created in the PostgreSQL instance."
  type        = string
  default     = "files"

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9_]{0,62}$", var.db_name))
    error_message = "db_name must start with a letter and contain only letters, digits, and underscores."
  }
}

variable "db_username" {
  description = "Master username for the PostgreSQL instance."
  type        = string
  default     = "files_admin"

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9_]{0,62}$", var.db_username))
    error_message = "db_username must start with a letter and contain only letters, digits, and underscores."
  }
}

variable "db_password" {
  description = "Master password for PostgreSQL. Supply through TF_VAR_db_password or a secure CI secret; never commit it."
  type        = string
  sensitive   = true

  validation {
    condition     = length(var.db_password) >= 16
    error_message = "db_password must be at least 16 characters long."
  }
}

variable "multi_az" {
  description = "Create a Multi-AZ RDS deployment. Recommended for prod."
  type        = bool
  default     = false
}

variable "deletion_protection" {
  description = "Enable RDS deletion protection. Recommended for prod."
  type        = bool
  default     = false
}

variable "app_secrets" {
  description = "Additional ECS secret environment variables: map of ASP.NET Core config key to Secrets Manager ARN (for example Entra__ClientSecret). The DB connection string is managed automatically."
  type        = map(string)
  default     = {}
  sensitive   = true
}

variable "app_environment" {
  description = "Additional non-secret ASP.NET Core environment variables, for example Entra__TenantId or Cors__AllowedOrigins__0."
  type        = map(string)
  default     = {}
}

variable "otel_endpoint" {
  description = "OTLP endpoint (for example http://otel-collector:4317). Empty leaves tracing exports disabled at the environment level."
  type        = string
  default     = ""
}

variable "api_image" {
  description = "Container image for the API. Empty uses the api ECR repository at the bootstrap tag."
  type        = string
  default     = ""
}

variable "worker_image" {
  description = "Container image for the worker. Empty uses the worker ECR repository at the bootstrap tag."
  type        = string
  default     = ""
}

variable "api_cpu" {
  description = "Fargate CPU units for the API task."
  type        = number
  default     = 512

  validation {
    condition     = contains([256, 512, 1024, 2048, 4096, 8192, 16384], var.api_cpu)
    error_message = "api_cpu must be a valid Fargate CPU value."
  }
}

variable "api_memory" {
  description = "Fargate memory (MiB) for the API task."
  type        = number
  default     = 1024

  validation {
    condition     = var.api_memory >= 512
    error_message = "api_memory must be at least 512 MiB."
  }
}

variable "worker_cpu" {
  description = "Fargate CPU units for the worker task."
  type        = number
  default     = 512

  validation {
    condition     = contains([256, 512, 1024, 2048, 4096, 8192, 16384], var.worker_cpu)
    error_message = "worker_cpu must be a valid Fargate CPU value."
  }
}

variable "worker_memory" {
  description = "Fargate memory (MiB) for the worker task."
  type        = number
  default     = 1024

  validation {
    condition     = var.worker_memory >= 512
    error_message = "worker_memory must be at least 512 MiB."
  }
}

variable "api_desired_count" {
  description = "Baseline desired task count for the API service."
  type        = number
  default     = 2

  validation {
    condition     = var.api_desired_count >= 1
    error_message = "api_desired_count must be at least 1."
  }
}

variable "worker_desired_count" {
  description = "Desired task count for the worker service."
  type        = number
  default     = 1

  validation {
    condition     = var.worker_desired_count >= 1
    error_message = "worker_desired_count must be at least 1."
  }
}

variable "api_autoscaling_min" {
  description = "Minimum API task count for application autoscaling."
  type        = number
  default     = 2

  validation {
    condition     = var.api_autoscaling_min >= 1
    error_message = "api_autoscaling_min must be at least 1."
  }
}

variable "api_autoscaling_max" {
  description = "Maximum API task count for application autoscaling."
  type        = number
  default     = 8

  validation {
    condition     = var.api_autoscaling_max >= 1
    error_message = "api_autoscaling_max must be at least 1."
  }
}

variable "api_cpu_target" {
  description = "Target average CPU utilization percentage for API target tracking."
  type        = number
  default     = 60

  validation {
    condition     = var.api_cpu_target > 0 && var.api_cpu_target <= 100
    error_message = "api_cpu_target must be greater than 0 and at most 100."
  }
}

variable "certificate_arn" {
  description = "ACM certificate ARN for HTTPS. Leave empty in dev to create only an HTTP listener; prod must set this."
  type        = string
  default     = ""

  validation {
    condition     = (var.environment != "prod" || var.certificate_arn != "") && (var.certificate_arn == "" || can(regex("^arn:aws[a-zA-Z-]*:acm:[a-z0-9-]+:[0-9]{12}:certificate/.+$", var.certificate_arn)))
    error_message = "certificate_arn must be a valid ACM certificate ARN, and it is required when environment is prod."
  }
}

variable "log_retention_days" {
  description = "CloudWatch log retention for API and worker logs."
  type        = number
  default     = 30

  validation {
    condition     = contains([1, 3, 5, 7, 14, 30, 60, 90, 120, 150, 180, 365, 400, 545, 731, 1096, 1827, 2192, 2557, 2922, 3288, 3653], var.log_retention_days)
    error_message = "log_retention_days must be a retention value supported by CloudWatch Logs."
  }
}

variable "alarm_topic_arn" {
  description = "Existing SNS topic ARN for CloudWatch alarms. Leave empty to create a topic in this stack."
  type        = string
  default     = ""

  validation {
    condition     = var.alarm_topic_arn == "" || can(regex("^arn:aws[a-zA-Z-]*:sns:[a-z0-9-]+:[0-9]{12}:[A-Za-z0-9_-]+$", var.alarm_topic_arn))
    error_message = "alarm_topic_arn must be empty or a valid SNS topic ARN."
  }
}

variable "alarm_email" {
  description = "Optional email address subscribed to the created alarm topic. Ignored when alarm_topic_arn is supplied."
  type        = string
  default     = ""

  validation {
    condition     = var.alarm_email == "" || can(regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", var.alarm_email))
    error_message = "alarm_email must be empty or a valid email address."
  }
}
