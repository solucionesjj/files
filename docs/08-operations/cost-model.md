# Cost Model (Single EC2 + S3 + ClamAV)

**Currency:** USD · **Region:** us-east-1 · **Basis:** AWS public on-demand prices, October 2026.
Verify with the AWS Pricing Calculator; excludes taxes, support, domain registration, and
engineering time. Initial phase: one EC2, one EBS volume, one S3 bucket, one Elastic IP, ClamAV
in a container.

## 1. Baseline assumptions

| Driver | Assumption |
|---|---|
| Host | EC2 `t3.large` on-demand (2 vCPU, 8 GiB, ClamAV + PostgreSQL + API/worker), 730 h/month |
| EBS | 100 GiB gp3, encrypted |
| Public IPv4 | Elastic IP attached |
| Stored objects | 500 GiB in S3 Standard (10,000 files) |
| New ingress | 50 GiB/month |
| Egress | 200 GiB/month (API streams from S3 to clients) |
| Requests | ~5M API calls/month; ~200k S3 GET, ~20k PUT |
| Backups | 50 GiB of nightly `pg_dump` in S3 (35-day lifecycle) |
| ClamAV | Container on the same host; signatures ~1.5 GiB; no extra EC2 |
| Telemetry | OTel Collector only; SigNoz external or not deployed |
| Domain/TLS | `files.sacservices.cloud` (Network Solutions); Caddy/Let's Encrypt |

## 2. Unit prices used

| Item | Price |
|---|---|
| EC2 `t3.large` | $0.0832 / hour |
| EC2 `t3.small` | $0.0208 / hour |
| EC2 `t3.xlarge` | $0.1664 / hour |
| EBS gp3 | $0.08 / GB-month |
| Elastic IP (attached) | $0.005 / hour (~$3.65/month) |
| S3 Standard storage | $0.023 / GB-month |
| S3 PUT/COPY/POST | $0.005 / 1,000 requests |
| S3 GET | $0.0004 / 1,000 requests |
| Internet egress (first 10 TB) | $0.09 / GB |
| EBS snapshot (optional) | ~$0.05 / GB-month |
| Route 53 hosted zone (not used; Network Solutions) | $0 |

## 3. Scenario A — Production baseline (`t3.large`, 100 GiB, ClamAV)

| Item | Calculation | Monthly |
|---|---|---|
| EC2 `t3.large` | 0.0832 × 730 | $60.74 |
| EBS 100 GiB gp3 | 100 × 0.08 | $8.00 |
| Elastic IP | 0.005 × 730 | $3.65 |
| S3 storage 500 GiB | 500 × 0.023 | $11.50 |
| S3 requests | ~200k GET + 20k PUT | $0.18 |
| Egress 200 GiB | 200 × 0.09 | $18.00 |
| Backup storage 50 GiB | 50 × 0.023 | $1.15 |
| Domain (Network Solutions, amortized) | external | $1.00 |
| **Total (no EBS snapshot)** | | **≈ $104/mo** |
| **Total (with 100 GiB snapshot)** | | **≈ $109/mo** |
| **Planning number (+10%)** | | **$115–125/mo** |

ClamAV adds no EC2 line item but is the reason for `t3.large` instead of `t3.medium`
(+~$30/month) and consumes ~2 GiB of RAM. Self-hosting SigNoz on the same host is tight; an
external SigNoz or a second `t3.small` adds roughly $20–40/month.

## 4. Scenario B — Development / staging

| Item | Monthly |
|---|---|
| EC2 `t3.small` (50 GiB gp3) | $15.18 + $4.00 |
| Elastic IP | $3.65 |
| S3 50 GiB + 20 GiB egress | $1.15 + $1.80 |
| Backups 10 GiB | $0.23 |
| **Total** | **≈ $26/mo** (plus domain; scanning off) |

Local development is $0 AWS (LocalStack). A staging copy of production adds ~$65–110/month; stop
the instance when idle (Elastic IP still billed while allocated).

## 5. Scenario C — Growth (12–18 months)

| Driver | Assumption |
|---|---|
| Host | `t3.xlarge` (4 vCPU, 16 GiB) |
| EBS | 500 GiB gp3 |
| Stored | 2 TiB |
| Egress | 1 TiB/month |
| Backups | 200 GiB |
| Telemetry | External SigNoz |

| Item | Monthly |
|---|---|
| EC2 `t3.xlarge` | $121.47 |
| EBS 500 GiB | $40.00 |
| Elastic IP | $3.65 |
| S3 2 TiB | $47.10 |
| Egress 1 TiB | $90.00 |
| Backups 200 GiB | $4.60 |
| S3 requests | ~$1 |
| **Total** | **≈ $308/mo** |
| **With +10%** | **≈ $340/mo** |

At growth, **egress and EC2** dominate; storage is ~15%. If traffic requires a second API host,
add its EC2/EBS and eventually a load balancer.

## 6. Unit economics (baseline)

| Metric | Value |
|---|---|
| All-in cost per stored GiB/month | $110 / 500 ≈ **$0.22** |
| Marginal storage cost | $0.023/GiB-month |
| Cost per download (5 MiB) | ≈ $0.0005 (egress + compute) |
| Cost per file/month | $110 / 10,000 ≈ **$0.011** |
| Cost per GB transferred | $0.09 egress + small API compute |
| Fixed vs variable | ~75% fixed (EC2, EBS, EIP, domain) |

The single node is cheap but has poor elasticity: capacity jumps come in instance-size steps, and
disk must be expanded before uploads/backups need it.

## 7. Optimization levers

| Lever | Estimated saving | Trade-off |
|---|---|---|
| `t3.medium` + scanning off | ~$30/mo | No ClamAV; not acceptable for production. |
| 1-year EC2 Savings Plan / Reserved Instance | ~25–30% of EC2 (~$15/mo) | Commitment. |
| Keep EBS at 100 GiB; lifecycle staging/orphans | Avoids growth | Monitor; resize when >70%. |
| S3 Intelligent-Tiering/IA for cold files | Up to 40% of storage | Retrieval fees; small at baseline. |
| Compress/shorten logs, OTel sampling | $1–5/mo | Less debugging detail. |
| External SigNoz instead of self-hosted | Avoids ~$20–40/mo host cost | Data leaves the host. |
| Delete idle staging EC2 / release EIP | ~$65–110/mo | Staging unavailable. |
| Negotiated egress / CDN later | 10–30% of egress | Architecture change; not in the initial phase. |

## 8. Budget controls

- AWS Budgets for the account with alerts at 80/100/120% of ~$120/month.
- Cost allocation tags: `Product=Files`, `Environment`, `Service` (api/worker/storage).
- Monthly review: EC2 utilization, EBS growth, S3 storage/egress, backup size, ClamAV memory.
- Guard against denial of wallet: upload quotas, 5 GiB cap, 3 concurrent sessions/1 scan, egress
  monitoring, public link limits.
- If SigNoz is external, include its subscription/fee separately.

## 9. Not included

- Engineering, on-call, and support time.
- Entra ID licensing (assumed existing Microsoft 365).
- Domain registration/SSL (Let's Encrypt is free; Network Solutions domain is external).
- Penetration testing, audits, legal.
- A second environment/HA/DR region.
- Data migration from the existing product.
