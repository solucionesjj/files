output "instance_id" {
  description = "EC2 instance ID."
  value       = module.instance.instance_id
}

output "public_ip" {
  description = "Elastic IP address; point DNS at it."
  value       = module.instance.public_ip
}

output "ssh_command" {
  description = "SSH command (replace the key path if needed)."
  value       = module.instance.ssh_command
}

output "bucket_name" {
  description = "Private S3 bucket for Files."
  value       = module.storage.bucket_name
}

output "bucket_arn" {
  description = "ARN of the private S3 bucket."
  value       = module.storage.bucket_arn
}

output "instance_role_arn" {
  description = "Instance role allowed to access S3 objects."
  value       = module.instance.role_arn
}

output "security_group_id" {
  description = "Security group attached to the instance."
  value       = module.instance.security_group_id
}
