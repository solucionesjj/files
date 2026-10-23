module "network" {
  source = "./modules/network"

  name_prefix       = local.name_prefix
  vpc_cidr          = var.vpc_cidr
  nat_gateway_count = var.nat_gateway_count
  tags              = local.common_tags
}

module "security" {
  source = "./modules/security"

  name_prefix = local.name_prefix
  vpc_id      = module.network.vpc_id
  app_port    = 8080
  db_port     = 5432
  tags        = local.common_tags
}

module "storage" {
  source = "./modules/storage"

  environment       = var.environment
  bucket_versioning = var.bucket_versioning
  portal_origin     = var.portal_origin
  tags              = local.common_tags
}

module "database" {
  source = "./modules/database"

  name_prefix           = local.name_prefix
  environment           = var.environment
  private_subnet_ids    = module.network.private_subnet_ids
  rds_security_group_id = module.security.rds_security_group_id
  engine_version        = var.db_engine_version
  instance_class        = var.db_instance_class
  db_name               = var.db_name
  db_username           = var.db_username
  db_password           = var.db_password
  multi_az              = var.multi_az
  deletion_protection   = var.deletion_protection
  tags                  = local.common_tags
}

module "compute" {
  source = "./modules/compute"

  name_prefix        = local.name_prefix
  environment        = var.environment
  region             = var.region
  vpc_id             = module.network.vpc_id
  public_subnet_ids  = module.network.public_subnet_ids
  private_subnet_ids = module.network.private_subnet_ids

  alb_security_group_id    = module.security.alb_security_group_id
  api_security_group_id    = module.security.api_security_group_id
  worker_security_group_id = module.security.worker_security_group_id

  bucket_name = module.storage.bucket_name
  bucket_arn  = module.storage.bucket_arn

  db_address  = module.database.db_address
  db_port     = module.database.db_port
  db_name     = var.db_name
  db_username = var.db_username
  db_password = var.db_password

  app_secrets     = var.app_secrets
  app_environment = var.app_environment
  otel_endpoint   = var.otel_endpoint

  api_image    = var.api_image
  worker_image = var.worker_image

  api_cpu       = var.api_cpu
  api_memory    = var.api_memory
  worker_cpu    = var.worker_cpu
  worker_memory = var.worker_memory

  api_desired_count    = var.api_desired_count
  worker_desired_count = var.worker_desired_count
  api_autoscaling_min  = var.api_autoscaling_min
  api_autoscaling_max  = var.api_autoscaling_max
  api_cpu_target       = var.api_cpu_target

  certificate_arn    = var.certificate_arn
  log_retention_days = var.log_retention_days
  tags               = local.common_tags
}

module "observability" {
  source = "./modules/observability"

  name_prefix                 = local.name_prefix
  alb_arn_suffix              = module.compute.alb_arn_suffix
  api_target_group_arn_suffix = module.compute.api_target_group_arn_suffix
  db_instance_identifier      = module.database.db_instance_identifier
  cluster_name                = module.compute.cluster_name
  api_service_name            = module.compute.api_service_name
  api_desired_count           = var.api_desired_count
  alarm_topic_arn             = var.alarm_topic_arn
  alarm_email                 = var.alarm_email
  tags                        = local.common_tags
}
