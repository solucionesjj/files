module "instance" {
  source = "./modules/instance"

  name_prefix                       = local.name_prefix
  environment                       = var.environment
  region                            = var.region
  instance_type                     = var.instance_type
  ami_id                            = var.ami_id
  ssh_key_name                      = var.ssh_key_name
  admin_cidr                        = var.admin_cidr
  subnet_id                         = var.subnet_id
  root_volume_size_gb               = var.root_volume_size_gb
  enable_termination_protection     = var.enable_termination_protection
  delete_root_volume_on_termination = var.delete_root_volume_on_termination
  tags                              = local.common_tags
}

module "storage" {
  source = "./modules/storage"

  environment           = var.environment
  bucket_name           = var.bucket_name
  bucket_versioning     = var.bucket_versioning
  backup_retention_days = var.backup_retention_days
  files_principal_arn   = module.instance.role_arn
  tags                  = local.common_tags
}

# The only identity allowed to read/write objects. The bucket policy separately
# denies every other principal, so this policy and the bucket policy must agree.
data "aws_iam_policy_document" "files_s3" {
  statement {
    sid    = "ObjectAccess"
    effect = "Allow"
    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:DeleteObject",
      "s3:AbortMultipartUpload",
      "s3:ListMultipartUploadParts",
    ]
    resources = [
      "${module.storage.bucket_arn}/f/*",
      "${module.storage.bucket_arn}/staging/*",
      "${module.storage.bucket_arn}/orphan/*",
      "${module.storage.bucket_arn}/backups/*",
    ]
  }

  statement {
    sid    = "BucketList"
    effect = "Allow"
    actions = [
      "s3:ListBucket",
      "s3:ListBucketMultipartUploads",
    ]
    resources = [module.storage.bucket_arn]

    condition {
      test     = "StringLike"
      variable = "s3:prefix"
      values = [
        "",
        "f/",
        "f/*",
        "staging/",
        "staging/*",
        "orphan/",
        "orphan/*",
        "backups/",
        "backups/*",
      ]
    }
  }
}

resource "aws_iam_role_policy" "files_s3" {
  name   = "${local.name_prefix}-s3"
  role   = module.instance.role_name
  policy = data.aws_iam_policy_document.files_s3.json
}
