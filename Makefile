SHELL := /bin/bash
.DEFAULT_GOAL := help

.PHONY: help up down reset logs migrate db-verify validate check-docs lint-openapi fmt-tf tf-validate compose-config bootstrap

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

validate: check-docs lint-openapi fmt-tf compose-config ## Validate docs, contract, IaC, compose

check-docs: ## Check markdown links, fences, and requirement/story IDs
	python3 scripts/check_docs.py

lint-openapi: ## Lint the OpenAPI contract
	npx --yes @redocly/cli@latest lint docs/04-api/openapi/files-api.v1.yaml

fmt-tf: ## Check Terraform formatting
	terraform fmt -check -recursive infra/terraform

compose-config: ## Validate docker-compose.yml
	docker compose config -q

tf-validate: ## terraform init (no backend) + validate
	cd infra/terraform && terraform init -backend=false -input=false >/dev/null && terraform validate

bootstrap: ## Scaffold the .NET solution and Angular workspace (run once)
	./scripts/bootstrap-solution.sh
