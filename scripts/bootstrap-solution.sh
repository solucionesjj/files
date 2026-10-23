#!/usr/bin/env bash
# Scaffold the .NET solution and Angular workspace. Run once, from the repository root.
# Review this script before running; it is intentionally explicit and idempotency-guarded.
set -euo pipefail

cd "$(dirname "$0")/.."

if [[ -f Files.slnx || -f Files.sln ]]; then
  echo "A .NET solution already exists; refusing to overwrite. Remove it first if intended." >&2
  exit 1
fi

command -v dotnet >/dev/null || { echo "dotnet SDK 10 is required" >&2; exit 1; }
dotnet --version | grep -q '^10\.' || { echo "Expected .NET SDK 10.x, found $(dotnet --version)" >&2; exit 1; }

echo "== .NET solution =="
dotnet new sln -n Files

dotnet new classlib -n Files.Domain        -o src/Files.Domain        -f net10.0
dotnet new classlib -n Files.Application   -o src/Files.Application   -f net10.0
dotnet new classlib -n Files.Infrastructure -o src/Files.Infrastructure -f net10.0
dotnet new web      -n Files.Api           -o src/Files.Api           -f net10.0
dotnet new worker   -n Files.Worker        -o src/Files.Worker        -f net10.0
dotnet new xunit    -n Files.UnitTests     -o tests/Files.UnitTests   -f net10.0
dotnet new xunit    -n Files.IntegrationTests -o tests/Files.IntegrationTests -f net10.0
dotnet new xunit    -n Files.ContractTests -o tests/Files.ContractTests -f net10.0

SLN=$(ls Files.slnx Files.sln 2>/dev/null | head -n 1)
dotnet sln "$SLN" add \
  src/Files.Domain src/Files.Application src/Files.Infrastructure \
  src/Files.Api src/Files.Worker \
  tests/Files.UnitTests tests/Files.IntegrationTests tests/Files.ContractTests

echo "== project references =="
dotnet add src/Files.Application     reference src/Files.Domain
dotnet add src/Files.Infrastructure  reference src/Files.Application src/Files.Domain
dotnet add src/Files.Api             reference src/Files.Application src/Files.Infrastructure
dotnet add src/Files.Worker          reference src/Files.Application src/Files.Infrastructure
dotnet add tests/Files.UnitTests     reference src/Files.Domain src/Files.Application
dotnet add tests/Files.IntegrationTests reference src/Files.Api src/Files.Infrastructure
dotnet add tests/Files.ContractTests reference src/Files.Api

echo "== packages (versions resolved by the SDK; pin after the first build) =="
dotnet add src/Files.Application package FluentValidation
dotnet add src/Files.Application package Microsoft.Extensions.Logging.Abstractions

dotnet add src/Files.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/Files.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Files.Infrastructure package EFCore.NamingConventions
dotnet add src/Files.Infrastructure package Dapper
dotnet add src/Files.Infrastructure package AWSSDK.S3
dotnet add src/Files.Infrastructure package Microsoft.Graph
dotnet add src/Files.Infrastructure package Konscious.Security.Cryptography.Argon2

dotnet add src/Files.Api package Microsoft.AspNetCore.OpenApi
dotnet add src/Files.Api package Scalar.AspNetCore
dotnet add src/Files.Api package Microsoft.Identity.Web
dotnet add src/Files.Api package Serilog.AspNetCore
dotnet add src/Files.Api package FluentValidation.AspNetCore
dotnet add src/Files.Api package OpenTelemetry.Extensions.Hosting
dotnet add src/Files.Api package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/Files.Api package OpenTelemetry.Instrumentation.Http
dotnet add src/Files.Api package OpenTelemetry.Exporter.OpenTelemetryProtocol

dotnet add src/Files.Worker package Quartz.Extensions.Hosting
dotnet add src/Files.Worker package OpenTelemetry.Extensions.Hosting
dotnet add src/Files.Worker package OpenTelemetry.Exporter.OpenTelemetryProtocol

dotnet add tests/Files.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Files.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/Files.IntegrationTests package FluentAssertions
dotnet add tests/Files.IntegrationTests package NSubstitute
dotnet add tests/Files.UnitTests package FluentAssertions
dotnet add tests/Files.UnitTests package NSubstitute

echo "== Angular workspace =="
if [[ "${SKIP_WEB:-0}" != "1" ]]; then
  npx --yes @angular/cli@latest new web \
    --routing --style=scss --ssr=false --skip-git \
    --package-manager=npm --defaults
  (cd web && npm install bootstrap @popperjs/core @azure/msal-angular @azure/msal-browser)
fi

echo
echo "Scaffold complete. Next:"
echo "  1. Add Directory.Build.props / global.json (see docs/02-architecture/backend-design.md)."
echo "  2. Implement /health/live and /health/ready, options validation, OTel."
echo "  3. Add EF Core DbContext and repositories; run against docker compose PostgreSQL."
echo "  4. Update AGENTS.md and docs/09-delivery/roadmap.md progress."
