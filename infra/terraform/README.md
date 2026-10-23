# Files — AWS single-node Terraform

This stack provisions the construction-phase footprint: **one Ubuntu 24.04 EC2 instance with an
Elastic IP and one private S3 bucket**. There is no ALB, ECS, RDS, ECR, NAT gateway, or
CloudWatch. PostgreSQL runs in Docker on the instance; Caddy terminates TLS.

```
Internet → Elastic IP → EC2 security group (80/443, 22 from admin_cidr)
                              └─ Docker Compose: Caddy → API → PostgreSQL
                                 API/Worker → S3 (instance role only)
```

## Layout

```
infra/terraform/
├── versions.tf / providers.tf / variables.tf / locals.tf
├── main.tf / outputs.tf
├── terraform.tfvars.example
├── envs/{dev,prod}.tfvars.example
└── modules/
    ├── instance/   # Ubuntu 24.04 AMI, EC2, EIP, SG, IAM role/profile, user_data
    └── storage/    # private bucket, BPA, SSE, lifecycle, exclusive-access policy
```

## Prerequisites

- Terraform ≥1.9 (tested with 1.13).
- AWS credentials allowed to create EC2/EIP/SG/IAM/S3 resources.
- A default VPC with at least one public subnet, **or** set `vpc_id`/`subnet_id` to an existing public subnet.
- A DNS name (recommended) that will point at the Elastic IP; Caddy needs it for trusted TLS.
- An existing EC2 key pair for SSH (`ssh_key_name`), or console access if empty.
- Your administrator CIDR for port 22. `admin_cidr` is required and must not be `0.0.0.0/0`.

## Apply

```bash
cd infra/terraform
terraform init
export TF_VAR_admin_cidr="203.0.113.10/32"
export TF_VAR_ssh_key_name="files-admin"
terraform plan  -var-file=envs/prod.tfvars -out=prod.tfplan
terraform apply prod.tfplan
```

Outputs: `public_ip`, `ssh_command`, `bucket_name`/`bucket_arn`, `instance_id`, `instance_role_arn`.

## What gets created

- Canonical Ubuntu 24.04 LTS x86_64 AMI (override with `ami_id`), `t3.large` default (ClamAV needs ~2 GiB), 100 GiB
  encrypted gp3 root volume, IMDSv2 required, termination protection enabled.
- Elastic IP associated with the instance.
- Security group: 80/443 from the internet; 22 only from `admin_cidr`; all egress.
- Instance role + profile; **no S3 policy in the role itself** — the root module attaches a
  prefix-scoped policy after the bucket exists.
- Private S3 bucket: BPA (4 flags), owner-enforced, SSE-S3, versioning off by default, lifecycle
  for incomplete multipart (7 d), `staging/` (1 d), `orphan/` (7 d), `backups/postgres/`
  (`backup_retention_days`, 35 default).
- Bucket policy:
  - denies HTTP (non-TLS) requests;
  - **denies object/list/multipart actions to every principal except the EC2 instance role**
    (`aws:PrincipalArn`); no presigned URLs, no CORS, no other reader/writer.
- `user_data` installs Docker/Compose, AWS CLI, UFW, fail2ban, unattended upgrades, chrony,
  creates `/opt/files` and `/var/lib/files/{staging,postgres,portal}`, 2 GiB swap, and enables
  the firewall. It does **not** clone the app or write secrets.

## Existing resources

If the EC2 instance or bucket already exists, import it into this stack (or use a separate stack
and only manage the bucket policy), for example:

```bash
terraform import module.storage.aws_s3_bucket.this <bucket-name>
terraform import module.instance.aws_instance.this <instance-id>
```

`bucket_name` only overrides the name of a **managed** bucket; it does not attach an unmanaged one.

## After apply (manual, documented in `deploy/README.md`)

1. Create `A files.sacservices.cloud → <public_ip>` at Network Solutions.
2. SSH: `ssh -i <key>.pem ubuntu@<public_ip>`.
3. Copy the `deploy/` directory and create `/opt/files/.env` (mode 0600) from
   `deploy/env.prod.example`; fill DB/Entra/Graph/pepper values.
4. `docker compose -f deploy/docker-compose.prod.yml up -d postgres`
5. Run migrations, build/start API/worker/Caddy, copy the SPA build to `/var/lib/files/portal`.
6. Install `deploy/backup.sh` in cron and run one backup; verify `backups/postgres/_latest`.
7. Run `deploy/smoke.sh`.

## Security notes

- **S3 exclusivity:** the bucket policy is the control; the instance role is the only principal
  with data access. Infrastructure automation can change bucket configuration but not read/write
  objects. No static AWS access keys exist on the host.
- **Public exposure:** only Caddy listens publicly. The API is not published, PostgreSQL has no
  host port, and SSH is restricted by both the security group and UFW.
- **TLS:** Caddy obtains a trusted certificate only when a DNS name points at the Elastic IP. A
  bare IP cannot get a public certificate; do not run self-signed TLS as production.
- **Secrets:** not managed by Terraform. They live in `/opt/files/.env`, provisioned by the deploy
  pipeline. Terraform state contains no application secrets.
- **Backups:** nightly `pg_dump` to S3; no PITR. RPO 24 h/RTO 4 h in this phase. Test restores.
- **Patching:** unattended upgrades plus a monthly reboot window; Docker images rebuilt weekly.
- The default VPC/subnet is used for simplicity; restrict SSH and keep the instance patched.

## Destroying

Termination protection and `delete_on_termination = false` protect data, but they also block a
clean `terraform destroy`. Set `enable_termination_protection = false`, run `terraform apply`, then
`terraform destroy`; the EBS volume remains and must be deleted manually after a verified backup.
The Elastic IP is released, so DNS must be updated or recreated.

## Intentionally omitted

- ALB/ECS/RDS/ECR/CloudWatch/NAT/Route 53/WAF
- ACM (Caddy uses Let's Encrypt instead)
- Secrets Manager (env file on the host for now)
- Multi-AZ/HA, PITR, S3 replication, object lock
- Docker image builds, app deployment, migrations, backups (see `deploy/`)

## Scale-up later

Move PostgreSQL to RDS, run the API on ECS/ALB or multiple EC2 instances, add a load balancer,
CloudWatch/WAF, PITR, and centralized telemetry. See `docs/08-operations/infrastructure-and-iac.md`.
