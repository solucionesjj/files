output "bucket_name" {
  description = "Name of the uploads S3 bucket."
  value       = module.storage.bucket_name
}

output "bucket_arn" {
  description = "ARN of the uploads S3 bucket."
  value       = module.storage.bucket_arn
}

output "db_endpoint" {
  description = "Connection endpoint (host:port) of the PostgreSQL instance."
  value       = module.database.db_endpoint
}

output "ecs_cluster_name" {
  description = "Name of the ECS cluster."
  value       = module.compute.cluster_name
}

output "api_ecr_repository_url" {
  description = "ECR repository URL for the API image."
  value       = module.compute.api_ecr_repository_url
}

output "worker_ecr_repository_url" {
  description = "ECR repository URL for the worker image."
  value       = module.compute.worker_ecr_repository_url
}

output "alb_dns_name" {
  description = "Public DNS name of the application load balancer."
  value       = module.compute.alb_dns_name
}

output "api_service_name" {
  description = "Name of the API ECS service."
  value       = module.compute.api_service_name
}

output "worker_service_name" {
  description = "Name of the worker ECS service."
  value       = module.compute.worker_service_name
}

output "alarm_topic_arn" {
  description = "SNS topic used for CloudWatch alarm actions."
  value       = module.observability.alarm_topic_arn
}

output "db_secret_arn" {
  description = "Secrets Manager ARN holding the PostgreSQL connection string."
  value       = module.compute.db_secret_arn
}
