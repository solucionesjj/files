variable "name_prefix" {
  description = "Name prefix applied to network resources."
  type        = string
}

variable "vpc_cidr" {
  description = "CIDR block for the VPC."
  type        = string
}

variable "nat_gateway_count" {
  description = "Number of NAT gateways to create (1 or 2)."
  type        = number

  validation {
    condition     = var.nat_gateway_count >= 1 && var.nat_gateway_count <= 2
    error_message = "nat_gateway_count must be 1 or 2."
  }
}

variable "tags" {
  description = "Common tags applied to taggable resources."
  type        = map(string)
  default     = {}
}
