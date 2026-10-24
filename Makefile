SHELL := /bin/bash
.DEFAULT_GOAL := help

.PHONY: help up down reset logs migrate db-verify validate check-docs lint-openapi fmt-tf tf-validate compose-config compose-prod-config bootstrap build test web-build docker-build

help: ## List available targets
	@grep -hE '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-16s\033[0m %s\n", $$1, $$2}'

up: ## Start local dependencies (PostgreSQL, LocalStack S3, Mailpit, OTel)
	docker compose up -d

down: ## Stop local dependencies (keep volumes)
	docker compose down

reset: ## Stop and delete local volumes (destructive, dev only)
	docker compose down -v

logs: ## Follow local dependency logs
	docker compose logs -f

migrate: ## Apply Flyway migrations to the local database
	docker compose --profile tools run --rm flyway migrate

db-verify: ## Apply migrations to a throwaway PostgreSQL 18 and assert the schema
	./scripts/db-verify.sh

build: ## Build the .NET solution with warnings as errors
	dotnet build -warnaserror

test: ## Run the .NET test suite
	dotnet test

web-build: ## Install and build the Angular portal
	cd web && npm ci && npm run build

docker-build: ## Build the API, worker, and web Docker images
	docker build -f src/Files.Api/Dockerfile -t files-api:local .
	docker build -f src/Files.Worker/Dockerfile -t files-worker:local .
	docker build -f web/Dockerfile -t files-web:local web

validate: check-docs lint-openapi fmt-tf compose-config compose-prod-config build test web-build ## Validate docs, contract, IaC, compose, and code

check-docs: ## Check markdown links, fences, and requirement/story IDs
	python3 scripts/check_docs.py

lint-openapi: ## Lint the OpenAPI contract
	npx --yes @redocly/cli@latest lint docs/04-api/openapi/files-api.v1.yaml

fmt-tf: ## Check Terraform formatting
	terraform fmt -check -recursive infra/terraform

compose-config: ## Validate docker-compose.yml
	docker compose config -q

compose-prod-config: ## Validate deploy/docker-compose.prod.yml with the example env
	FILES_ENV_FILE=env.prod.example docker compose --env-file deploy/env.prod.example -f deploy/docker-compose.prod.yml config -q

tf-validate: ## terraform init (no backend) + validate
	cd infra/terraform && terraform init -backend=false -input=false >/dev/null && terraform validate

bootstrap: ## Scaffold the .NET solution and Angular workspace (run once)
	./scripts/bootstrap-solution.sh
