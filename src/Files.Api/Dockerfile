# syntax=docker/dockerfile:1
# Production image for Files.Api. Build context is the repository root (see
# deploy/docker-compose.prod.yml). Multi-stage, non-root, with a liveness healthcheck.

# --- build ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer cache). Only project files + shared props are needed.
COPY global.json Directory.Build.props ./
COPY src/Files.Domain/Files.Domain.csproj src/Files.Domain/
COPY src/Files.Application/Files.Application.csproj src/Files.Application/
COPY src/Files.Infrastructure/Files.Infrastructure.csproj src/Files.Infrastructure/
COPY src/Files.Api/Files.Api.csproj src/Files.Api/
RUN dotnet restore src/Files.Api/Files.Api.csproj

# Copy the remaining source and publish.
COPY . .
RUN dotnet publish src/Files.Api/Files.Api.csproj -c Release -o /app/publish --no-restore

# --- runtime ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is only used by the healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./
USER app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --retries=3 --start-period=40s \
  CMD curl -fsS http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Files.Api.dll"]
