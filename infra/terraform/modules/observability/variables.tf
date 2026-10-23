variable "name_prefix" {
  description = "Name prefix for alarm resources."
  type        = string
}

variable "alb_arn_suffix" {
  description = "ALB ARN suffix used in CloudWatch dimensions."
  type        = string
}

variable "api_target_group_arn_suffix" {
  description = "API target group ARN suffix used in CloudWatch dimensions."
  type        = string
}

variable "db_instance_identifier" {
  description = "RDS instance identifier."
  type        = string
}

variable "cluster_name" {
  description = "ECS cluster name."
  type        = string
}

variable "api_service_name" {
  description = "API ECS service name."
  type        = string
}

variable "api_desired_count" {
  description = "Baseline desired API task count, used as the running-task alarm threshold."
  type        = number
}

variable "alarm_topic_arn" {
  description = "Existing SNS topic ARN. Empty creates a topic."
  type        = string
  default     = ""
}

variable "alarm_email" {
  description = "Optional email subscription for the created alarm topic."
  type        = string
  default     = ""
}

variable "tags" {
  description = "Common tags applied to taggable resources."
  type        = map(string)
  default     = {}
}
