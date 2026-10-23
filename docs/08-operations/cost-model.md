# Cost Model

**Currency:** USD · **Region:** us-east-1 · **Basis:** AWS public on-demand prices, October 2026.
Prices are estimates; validate with the AWS Pricing Calculator before committing a budget
(OQ-20). Excludes taxes, support plans, and one-time engineering.

## 1. Baseline assumptions (production)

| Driver | Assumption |
|---|---|
| Stored objects | 500 GiB (10,000 files) |
| New ingress | 50 GiB/month |
| Egress (downloads) | 200 GiB/month |
| API requests | 5 million/month |
| S3 GET requests | 100,000/month |
| Average download size | 5 MiB |
| Database | RDS PostgreSQL 18, `db.t4g.medium`, Multi-AZ, 100 GiB gp3 |
| API compute | 2 × (0.5 vCPU, 1 GiB) Fargate |
| Worker compute | 1 × (0.5 vCPU, 1 GiB) Fargate |
| NAT | 1–2 gateways, 100 GiB processed |
| Telemetry | Self-hosted SigNoz (1 task) |
| Logs | 20 GiB/month |

## 2. Unit prices used

| Item | Price |
|---|---|
| S3 Standard storage | $0.023 / GB-month |
| S3 PUT/COPY/POST | $0.005 / 1,000 requests |
| S3 GET | $0.0004 / 1,000 requests |
| Internet egress (first 10 TB) | $0.09 / GB |
| RDS `db.t4g.medium` | $0.0656 / hour |
| RDS `db.t4g.large` | $0.1312 / hour |
| RDS gp3 storage | $0.115 / GB-month (Multi-AZ ≈ 2×) |
| Fargate vCPU | $0.04048 / vCPU-hour |
| Fargate memory | $0.004445 / GB-hour |
| ALB | $0.0225 / hour + $0.008 / LCU-hour |
| NAT Gateway | $0.045 / hour + $0.045 / GB |
| CloudWatch Logs ingest | $0.50 / GB |
| CloudWatch Logs storage | $0.03 / GB-month |
| ECR storage | $0.10 / GB-month |
| Route 53 hosted zone | $0.50 / month |
| ACM certificate | $0 |

## 3. Scenario A — Development

| Item | Calculation | Monthly |
|---|---|---|
| RDS t4g.small, single-AZ, 20 GiB | 0.0328 × 730 + 20 × 0.115 | $26 |
| Fargate API 0.25 vCPU/0.5 GiB | 0.25 × 730 × 0.04048 + 0.5 × 730 × 0.004445 | $9 |
| Fargate worker 0.25 vCPU/0.5 GiB | same | $9 |
| NAT (1) + 30 GiB | 0.045 × 730 + 30 × 0.045 | $34 |
| ALB | 0.0225 × 730 + 1 LCU | $22 |
| S3 20 GiB + 10 GiB egress | 20 × 0.023 + 10 × 0.09 | $1.4 |
| Logs 5 GiB + metrics | 5 × 0.5 + 3 | $5.5 |
| ECR + Route 53 | — | $1 |
| SigNoz | local/disabled | $0 |
| **Total** | | **≈ $108/mo** |

## 4. Scenario B — Production baseline

| Item | Calculation | Monthly |
|---|---|---|
| RDS t4g.medium Multi-AZ, 100 GiB gp3 | 0.0656 × 730 × 2 + 100 × 0.115 × 2 | $119 |
| Backup storage over free tier | estimate | $0–10 |
| Fargate API 2 × (0.5 vCPU, 1 GiB) | 1 × 730 × 0.04048 + 2 × 730 × 0.004445 | $36 |
| Fargate worker (0.5 vCPU, 1 GiB) | 0.5 × 730 × 0.04048 + 1 × 730 × 0.004445 | $18 |
| NAT (1) + 100 GiB | 0.045 × 730 + 100 × 0.045 | $37 |
| NAT (second, HA) | 0.045 × 730 | +$33 |
| ALB + 1 LCU avg | 0.0225 × 730 + 0.008 × 730 | $22 |
| S3 storage 500 GiB | 500 × 0.023 | $12 |
| S3 requests | ~5k PUT + 100k GET, negligible | $0.1 |
| Egress 200 GiB | 200 × 0.09 | $18 |
| CloudWatch Logs 20 GiB | 20 × 0.5 + storage + alarms | $21 |
| SigNoz (1 task, 1 vCPU/2 GiB + storage) | ~$36 + storage/collector | $43 |
| ECR, Route 53, KMS | — | $2 |
| **Total (one NAT)** | | **≈ $326/mo** |
| **Total (two NATs, HA)** | | **≈ $359/mo** |
| **Planning number (with 10% buffer)** | | **$360–400/mo** |

The original target of $180–320 is achievable only with a single NAT, single-AZ database, or
SigNoz disabled; those reduce resilience. This document recommends the $360–400 range for a
production-grade baseline.

## 5. Scenario C — Growth (12–18 months)

| Driver | Assumption |
|---|---|
| Stored | 2 TiB |
| Egress | 1 TiB/month |
| Requests | 20 million/month |
| API | 4 × (1 vCPU, 2 GiB) |
| Worker | 2 × (0.5 vCPU, 1 GiB) |
| DB | t4g.large Multi-AZ, 200 GiB |
| NAT | 2, 500 GiB processed |
| SigNoz | 2 tasks |

| Item | Monthly |
|---|---|
| RDS (0.1312 × 730 × 2 + 200 × 0.115 × 2) | $238 |
| Fargate API (4 vCPU × 730 × 0.04048 + 8 GiB × 730 × 0.004445) | $144 |
| Fargate worker | $36 |
| NAT (2 × 32.85 + 500 × 0.045) | $88 |
| ALB + LCU | $42 |
| S3 storage 2 TiB | $47 |
| Egress 1 TiB | $90 |
| Logs 100 GiB + metrics | $65 |
| SigNoz | $86 |
| Other | $5 |
| **Total** | **≈ $841/mo** |
| **With 10% buffer** | **≈ $925/mo** |

At growth, **egress (11%) and the database (28%)** dominate; storage itself is only ~6%.

## 6. Unit economics (baseline)

| Metric | Value |
|---|---|
| All-in cost per stored GiB/month | $360 / 500 ≈ **$0.72** (marginal S3: $0.023) |
| Cost per download (5 MiB) | ≈ $0.0005 (egress + request + compute share) |
| Cost per file/month | $360 / 10,000 ≈ **$0.036** |
| Cost per GB transferred | $0.09 egress + ~$0.001 proxy compute |
| Fixed vs variable | ~65% fixed (DB, NAT, ALB, SigNoz) at baseline |

The high fixed share means small deployments are relatively expensive; consolidating dev/staging
or using a shared SigNoz instance improves the ratio.

## 7. Optimization levers

| Lever | Estimated saving | Trade-off |
|---|---|---|
| One NAT in production | $33/mo | No NAT HA; single-AZ egress dependency |
| VPC endpoints for S3/ECR/Logs | $5–20/mo at baseline; more at growth | Setup complexity; endpoint hourly cost |
| Fargate Savings Plan (1 yr) | ~20% of compute ($11/mo baseline) | Commitment |
| Worker on Fargate Spot | ~50% of worker ($9/mo) | Interruption tolerance (jobs are idempotent) |
| S3 Intelligent-Tiering / IA after 90 days | Up to 40% of storage cost | Retrieval fees; small at baseline |
| Log volume reduction (sampling, levels) | $5–10/mo | Less debugging detail |
| SigNoz self-hosted vs SaaS | $50–150/mo | Operational effort |
| Delete stale dev environments | $100+/mo per environment | — |
| Negotiated egress (CloudFront/private pricing) | 10–30% of egress | Contract; CDN architecture change |
| RDS reserved instance (1 yr) | ~30% of DB compute | Commitment |

## 8. Budget controls

- AWS Budgets per environment with alerts at 80%, 100%, and 120% of the monthly plan.
- Cost allocation tags: `Product=Files`, `Environment`, `Service` (api/worker/storage/db).
- Cost anomaly detection on S3 egress and NAT processed bytes (denial-of-wallet).
- Monthly review: storage growth, egress per integration, top files by bytes, Fargate utilization,
  DB storage headroom.
- Chargeback (optional): attribute S3 storage and egress to integrations via `daily_usage` and
  object metadata (approximation; S3 cost allocation by prefix is not exact).

## 9. What is not included

- Engineering, support, and on-call time.
- Entra ID licensing (assumed existing Microsoft 365 E3/E5).
- Domain registration and legal/compliance costs.
- Penetration testing and audits.
- Disaster-recovery region duplication (adds ~60–80% of baseline if warm).
- Data migration from the existing product.
