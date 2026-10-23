#!/usr/bin/env python3
"""Validate the design package: links, code fences, IDs, and required artifacts.

Run from anywhere: `python3 scripts/check_docs.py`. Exits non-zero on any error.
Anchors are not validated (GitHub slug rules vary); files, IDs, and fences are.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
errors: list[str] = []

REQUIRED = [
    "AGENTS.md",
    "README.md",
    "docs/01-product/requirements.md",
    "docs/01-product/user-stories.md",
    "docs/01-product/rbac-and-personas.md",
    "docs/01-product/decisions.md",
    "docs/01-product/open-questions.md",
    "docs/02-architecture/architecture.md",
    "docs/02-architecture/backend-design.md",
    "docs/02-architecture/frontend-design.md",
    "docs/03-data/data-model.md",
    "docs/04-api/api-conventions.md",
    "docs/04-api/error-catalog.md",
    "docs/04-api/openapi/files-api.v1.yaml",
    "docs/05-security/entra-id-authentication.md",
    "docs/05-security/machine-identity-and-authorization.md",
    "docs/05-security/threat-model.md",
    "docs/06-workflows/file-lifecycle.md",
    "docs/06-workflows/upload-and-download.md",
    "docs/06-workflows/retention-notifications-deletion.md",
    "docs/07-quality/testing-strategy.md",
    "docs/08-operations/infrastructure-and-iac.md",
    "docs/08-operations/deployment-and-rollback.md",
    "docs/08-operations/observability-and-slos.md",
    "docs/08-operations/cost-model.md",
    "docs/08-operations/runbooks.md",
    "docs/09-delivery/roadmap.md",
    "docs/09-delivery/backlog.md",
    "docs/09-delivery/traceability.md",
    "db/migrations/V1__initial_schema.sql",
    "db/migrations/V2__seed_reference_data.sql",
    "db/README.md",
    "docker-compose.yml",
    ".env.example",
    "Makefile",
    "infra/local/otel-collector.yaml",
    "infra/local/s3/init-s3.sh",
    "deploy/docker-compose.prod.yml",
    "deploy/Caddyfile",
    "deploy/env.prod.example",
    "deploy/backup.sh",
    "deploy/restore.sh",
    "deploy/smoke.sh",
    "deploy/README.md",
    "deploy/files-backup.cron",
    "deploy/otel-collector.yaml",
    "deploy/clamav/clamd.conf",
    ".github/workflows/deploy.yml",
]

for rel in REQUIRED:
    if not (ROOT / rel).exists():
        errors.append(f"missing required file: {rel}")

md_files = sorted(
    set(
        list(ROOT.glob("*.md"))
        + list((ROOT / "docs").rglob("*.md"))
        + list((ROOT / "infra").rglob("*.md"))
        + list((ROOT / "db").rglob("*.md"))
    )
)

LINK_RE = re.compile(r"!?\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")
FENCE_RE = re.compile(r"^\s*```")

link_count = 0
for md in md_files:
    text = md.read_text(encoding="utf-8")
    rel = md.relative_to(ROOT)

    if len(FENCE_RE.findall(text)) % 2 != 0:
        errors.append(f"{rel}: unbalanced code fences")

    for target in LINK_RE.findall(text):
        if target.startswith(("http://", "https://", "mailto:", "tel:", "#")):
            continue
        if "{{" in target or target.startswith("<"):
            continue
        path_part = target.split("#", 1)[0].split("?", 1)[0]
        if not path_part:
            continue
        link_count += 1
        resolved = (md.parent / path_part).resolve()
        try:
            resolved.relative_to(ROOT)
        except ValueError:
            errors.append(f"{rel}: link escapes the repository: {target}")
            continue
        if not resolved.exists():
            errors.append(f"{rel}: broken link: {target}")

# --- ID consistency -----------------------------------------------------------
req_text = (ROOT / "docs/01-product/requirements.md").read_text(encoding="utf-8")
story_text = (ROOT / "docs/01-product/user-stories.md").read_text(encoding="utf-8")
decisions_text = (ROOT / "docs/01-product/decisions.md").read_text(encoding="utf-8")
oq_text = (ROOT / "docs/01-product/open-questions.md").read_text(encoding="utf-8")

fr_ids = set(re.findall(r"\bFR-[A-Z0-9]+-\d{3}\b", req_text))
nfr_ids = set(re.findall(r"\bNFR-\d{3}\b", req_text))
story_ids = set(re.findall(r"\bUS-\d{3}\b", story_text))
decision_ids = set(re.findall(r"\bD-\d{2}\b", decisions_text))
oq_ids = set(re.findall(r"\bOQ-\d{2}\b", oq_text))

if not fr_ids:
    errors.append("requirements.md: no FR-xxx IDs found")
if not story_ids:
    errors.append("user-stories.md: no US-xxx IDs found")

adr_dir = ROOT / "docs/02-architecture/adr"
adr_files = {p.name[:8] for p in adr_dir.glob("ADR-*.md")}  # ADR-0001

for md in md_files:
    text = md.read_text(encoding="utf-8")
    rel = md.relative_to(ROOT)
    for fid in re.findall(r"\bFR-[A-Z0-9]+-\d{3}\b", text):
        if fid not in fr_ids:
            errors.append(f"{rel}: unknown requirement ID {fid}")
    for nid in re.findall(r"\bNFR-\d{3}\b", text):
        if nid not in nfr_ids:
            errors.append(f"{rel}: unknown NFR ID {nid}")
    for sid in re.findall(r"\bUS-\d{3}\b", text):
        if sid not in story_ids:
            errors.append(f"{rel}: unknown story ID {sid}")
    for did in re.findall(r"\bD-\d{2}\b", text):
        if did not in decision_ids:
            errors.append(f"{rel}: unknown decision ID {did}")
    for oid in re.findall(r"\bOQ-\d{2}\b", text):
        if oid not in oq_ids:
            errors.append(f"{rel}: unknown open-question ID {oid}")
    for aid in set(re.findall(r"\bADR-(\d{4})\b", text)):
        if f"ADR-{aid}" not in adr_files:
            errors.append(f"{rel}: reference to missing ADR-{aid}")

# --- OpenAPI sanity -----------------------------------------------------------
try:
    import yaml  # type: ignore

    spec_path = ROOT / "docs/04-api/openapi/files-api.v1.yaml"
    spec = yaml.safe_load(spec_path.read_text(encoding="utf-8"))
    if spec.get("openapi") != "3.1.0":
        errors.append("OpenAPI: expected openapi 3.1.0")
    ops = sum(len(v) for v in spec.get("paths", {}).values())
    if ops < 40:
        errors.append(f"OpenAPI: unexpectedly few operations ({ops})")
    comps = spec.get("components", {})
    referenced = set(
        re.findall(r"#/components/(\w+)/(\w+)", spec_path.read_text(encoding="utf-8"))
    )
    for kind, name in referenced:
        if name not in comps.get(kind, {}):
            errors.append(f"OpenAPI: missing {kind}/{name}")
except ImportError:
    print("warning: PyYAML not installed; skipping OpenAPI parse", file=sys.stderr)
except Exception as exc:  # noqa: BLE001
    errors.append(f"OpenAPI parse failed: {exc}")

# --- Report -------------------------------------------------------------------
print(f"docs: {len(md_files)} markdown files, {link_count} local links checked")
print(f"ids: {len(fr_ids)} FR, {len(nfr_ids)} NFR, {len(story_ids)} stories, "
      f"{len(decision_ids)} decisions, {len(oq_ids)} open questions, {len(adr_files)} ADRs")

if errors:
    print(f"\n{len(errors)} error(s):", file=sys.stderr)
    for err in errors:
        print(f"  - {err}", file=sys.stderr)
    sys.exit(1)

print("OK: documentation package is consistent")
