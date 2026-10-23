output "vpc_id" {
  description = "ID of the VPC."
  value       = aws_vpc.this.id
}

output "public_subnet_ids" {
  description = "IDs of the two public subnets."
  value       = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  description = "IDs of the two private subnets."
  value       = aws_subnet.private[*].id
}

output "availability_zones" {
  description = "Availability zones used by the VPC."
  value       = local.availability_zones
}

output "nat_gateway_ids" {
  description = "IDs of the NAT gateways."
  value       = aws_nat_gateway.this[*].id
}

output "private_route_table_ids" {
  description = "IDs of the private route tables used by the S3 gateway endpoint."
  value       = aws_route_table.private[*].id
}
