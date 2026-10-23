locals {
  identifier                = "${var.name_prefix}-postgres"
  final_snapshot_identifier = "${local.identifier}-final"
}

resource "aws_db_subnet_group" "this" {
  name       = "${var.name_prefix}-db-subnets"
  subnet_ids = var.private_subnet_ids

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-db-subnets"
  })
}

resource "aws_db_parameter_group" "this" {
  name        = "${var.name_prefix}-postgres18"
  family      = "postgres18"
  description = "PostgreSQL 18 parameters for ${var.name_prefix}"

  parameter {
    name         = "rds.force_ssl"
    value        = "1"
    apply_method = "immediate"
  }

  parameter {
    name         = "timezone"
    value        = "UTC"
    apply_method = "immediate"
  }

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-postgres18"
  })
}

resource "aws_db_instance" "this" {
  identifier = local.identifier

  engine         = "postgres"
  engine_version = var.engine_version
  instance_class = var.instance_class

  allocated_storage     = 100
  max_allocated_storage = 500
  storage_type          = "gp3"
  storage_encrypted     = true

  db_name  = var.db_name
  username = var.db_username
  password = var.db_password
  port     = 5432

  multi_az = var.multi_az

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [var.rds_security_group_id]
  parameter_group_name   = aws_db_parameter_group.this.name
  publicly_accessible    = false

  backup_retention_period = var.environment == "prod" ? 35 : (var.environment == "staging" ? 14 : 7)

  skip_final_snapshot       = false
  final_snapshot_identifier = local.final_snapshot_identifier
  deletion_protection       = var.deletion_protection

  tags = merge(var.tags, {
    Name = local.identifier
  })
}
