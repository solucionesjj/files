output "alb_security_group_id" {
  description = "Security group attached to the ALB."
  value       = aws_security_group.alb.id
}

output "api_security_group_id" {
  description = "Security group attached to API tasks."
  value       = aws_security_group.api.id
}

output "worker_security_group_id" {
  description = "Security group attached to worker tasks."
  value       = aws_security_group.worker.id
}

output "rds_security_group_id" {
  description = "Security group attached to RDS."
  value       = aws_security_group.rds.id
}
