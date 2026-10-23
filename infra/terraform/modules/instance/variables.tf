variable "name_prefix" { type = string }
variable "environment" { type = string }
variable "region" { type = string }
variable "instance_type" { type = string }
variable "ami_id" {
  type    = string
  default = ""
}
variable "ssh_key_name" {
  type    = string
  default = ""
}
variable "admin_cidr" { type = string }
variable "subnet_id" {
  type    = string
  default = ""
}
variable "root_volume_size_gb" { type = number }
variable "enable_termination_protection" { type = bool }
variable "delete_root_volume_on_termination" { type = bool }
variable "tags" { type = map(string) }
