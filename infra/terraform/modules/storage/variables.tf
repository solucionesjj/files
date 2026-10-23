variable "environment" { type = string }
variable "bucket_name" {
  type    = string
  default = ""
}
variable "bucket_versioning" { type = bool }
variable "backup_retention_days" { type = number }
variable "files_principal_arn" { type = string }
variable "tags" { type = map(string) }
