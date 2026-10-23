output "instance_id" { value = aws_instance.this.id }
output "public_ip" { value = aws_eip.this.public_ip }
output "private_ip" { value = aws_instance.this.private_ip }
output "role_arn" { value = aws_iam_role.instance.arn }
output "role_name" { value = aws_iam_role.instance.name }
output "security_group_id" { value = aws_security_group.this.id }
output "ssh_command" {
  value = "ssh -i <path-to-key>.pem ubuntu@${aws_eip.this.public_ip}"
}
