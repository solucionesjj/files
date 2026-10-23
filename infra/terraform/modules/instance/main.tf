data "aws_ami" "ubuntu" {
  most_recent = true
  owners      = ["099720109477"] # Canonical

  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-amd64-server-*"]
  }

  filter {
    name   = "virtualization-type"
    values = ["hvm"]
  }

  filter {
    name   = "architecture"
    values = ["x86_64"]
  }
}

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

locals {
  ami_id    = var.ami_id != "" ? var.ami_id : data.aws_ami.ubuntu.id
  subnet_id = var.subnet_id != "" ? var.subnet_id : tolist(data.aws_subnets.default.ids)[0]
}

resource "aws_security_group" "this" {
  name_prefix = "${var.name_prefix}-"
  description = "Files single-node instance: 80/443 public, SSH from admin_cidr only"
  vpc_id      = data.aws_vpc.default.id

  ingress {
    description = "HTTP (redirected to HTTPS by Caddy)"
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "HTTPS"
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    description = "SSH from the administrator network only"
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = [var.admin_cidr]
  }

  egress {
    description = "Outbound to S3, Entra ID, Graph, package mirrors, SigNoz"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-sg"
  })
}

data "aws_iam_policy_document" "ec2_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "instance" {
  name               = "${var.name_prefix}-instance"
  assume_role_policy = data.aws_iam_policy_document.ec2_assume_role.json

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-instance"
  })
}

resource "aws_iam_instance_profile" "instance" {
  name = "${var.name_prefix}-instance"
  role = aws_iam_role.instance.name

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-instance"
  })
}

resource "aws_instance" "this" {
  ami                         = local.ami_id
  instance_type               = var.instance_type
  subnet_id                   = local.subnet_id
  vpc_security_group_ids      = [aws_security_group.this.id]
  iam_instance_profile        = aws_iam_instance_profile.instance.name
  key_name                    = var.ssh_key_name != "" ? var.ssh_key_name : null
  associate_public_ip_address = false
  disable_api_termination     = var.enable_termination_protection
  user_data_replace_on_change = false

  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required"
    http_put_response_hop_limit = 2
  }

  root_block_device {
    volume_type           = "gp3"
    volume_size           = var.root_volume_size_gb
    encrypted             = true
    delete_on_termination = var.delete_root_volume_on_termination

    tags = merge(var.tags, {
      Name = "${var.name_prefix}-root"
    })
  }

  user_data = templatefile("${path.module}/user_data.sh.tftpl", {
    admin_cidr = var.admin_cidr
  })

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-app"
  })

  lifecycle {
    ignore_changes = [ami]
  }

  depends_on = [aws_iam_instance_profile.instance]
}

resource "aws_eip" "this" {
  domain = "vpc"

  tags = merge(var.tags, {
    Name = "${var.name_prefix}-eip"
  })
}

resource "aws_eip_association" "this" {
  instance_id   = aws_instance.this.id
  allocation_id = aws_eip.this.id
}
