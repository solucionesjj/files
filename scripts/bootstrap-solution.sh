#!/usr/bin/env bash
# Scaffold the .NET solution and Angular workspace. Run once, from the repository root.
# Review this script before running; it is intentionally explicit and idempotency-guarded.
#
# This script bootstraps the S1 (M0) foundation: projects, references, and the packages those
# projects actually use. Persistence/storage/email/ClamAV packages are added by later sessions
# (S2+), not here, to keep the S1 build small and its dependency surface auditable.
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

echo "== packages (S1 minimal set; versions pinned by the SDK on first restore) =="
dotnet add src/Files.Application package FluentValidation
dotnet add src/Files.Application package Microsoft.Extensions.Logging.Abstractions
dotnet add src/Files.Application package Microsoft.Extensions.Configuration.Abstractions

dotnet add src/Files.Infrastructure package Npgsql
dotnet add src/Files.Infrastructure package Microsoft.Extensions.Diagnostics.HealthChecks
dotnet add src/Files.Infrastructure package Microsoft.Extensions.Configuration.Abstractions

for p in \
  Serilog.AspNetCore Serilog.Sinks.Console Serilog.Formatting.Compact Serilog.Sinks.OpenTelemetry \
  OpenTelemetry.Extensions.Hosting OpenTelemetry.Instrumentation.AspNetCore \
  OpenTelemetry.Instrumentation.Http OpenTelemetry.Instrumentation.Runtime \
  OpenTelemetry.Exporter.OpenTelemetryProtocol; do
  dotnet add src/Files.Api package "$p"
done

for p in \
  Serilog.Extensions.Hosting Serilog.Sinks.Console Serilog.Formatting.Compact Serilog.Sinks.OpenTelemetry \
  OpenTelemetry.Extensions.Hosting OpenTelemetry.Instrumentation.Runtime \
  OpenTelemetry.Exporter.OpenTelemetryProtocol; do
  dotnet add src/Files.Worker package "$p"
done

dotnet add tests/Files.UnitTests package FluentAssertions
dotnet add tests/Files.UnitTests package NSubstitute
dotnet add tests/Files.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Files.IntegrationTests package FluentAssertions
dotnet add tests/Files.IntegrationTests package NSubstitute

echo "== Angular workspace =="
if [[ "${SKIP_WEB:-0}" != "1" ]]; then
  npx --yes @angular/cli@20 new web \
    --routing --style=scss --ssr=false --skip-git \
    --package-manager=npm --defaults
  (cd web && npm install \
    bootstrap@^5.3 @popperjs/core @azure/msal-angular @azure/msal-browser @angular/localize@^20)
fi

echo
echo "Scaffold complete. Next:"
echo "  1. Add Directory.Build.props / global.json (see docs/02-architecture/backend-design.md)."
echo "  2. Implement /health/live and /health/ready, options validation, OTel."
echo "  3. Add EF Core DbContext and repositories in S2; run against docker compose PostgreSQL."
echo "  4. Update AGENTS.md and docs/09-delivery/roadmap.md progress."
