# syntax=docker/dockerfile:1
# Production image for Files.Worker. Build context is the repository root (see
# deploy/docker-compose.prod.yml). Multi-stage and non-root.

# --- build ------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer cache).
COPY global.json Directory.Build.props ./
COPY src/Files.Domain/Files.Domain.csproj src/Files.Domain/
COPY src/Files.Application/Files.Application.csproj src/Files.Application/
COPY src/Files.Infrastructure/Files.Infrastructure.csproj src/Files.Infrastructure/
COPY src/Files.Worker/Files.Worker.csproj src/Files.Worker/
RUN dotnet restore src/Files.Worker/Files.Worker.csproj

# Copy the remaining source and publish.
COPY . .
RUN dotnet publish src/Files.Worker/Files.Worker.csproj -c Release -o /app/publish --no-restore

# --- runtime ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

# The .NET runtime image ships a non-root `app` user (UID 1654).
COPY --from=build /app/publish ./
USER app

ENTRYPOINT ["dotnet", "Files.Worker.dll"]
