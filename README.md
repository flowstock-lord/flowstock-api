# FlowStock API

Backend for **FlowStock** — a warehouse and production management system.

FlowStock tracks products and materials, warehouses and storage locations, stock balances and
stock movements, bills of materials, production orders, material consumption and finished goods,
together with a complete inventory history.

Core principle:

> Stock never changes silently. Every stock change is represented by a stock movement or a
> production operation.

**Status: Phase 0 (project foundation) — done. Next: Phase 1 — authentication and users.**

## Stack

.NET 10 · ASP.NET Core Web API · Entity Framework Core · PostgreSQL · FluentValidation ·
JWT authentication · Swagger/OpenAPI · Serilog · xUnit · Testcontainers · Docker Compose.

The architecture is a modular monolith. No microservices, message brokers or caches until a
concrete requirement justifies them.

## Layout

```text
FlowStock.slnx
src/
    FlowStock.Api/             HTTP endpoints, middleware, auth, DI, Swagger
    FlowStock.Application/     use cases, DTOs, validators, application services
    FlowStock.Domain/          entities, value objects, domain rules
    FlowStock.Infrastructure/  EF Core, PostgreSQL, auth infrastructure
tests/
    FlowStock.UnitTests/
    FlowStock.IntegrationTests/
docker/
docs/
```

## Running locally

Requires the .NET 10 SDK, Docker and `dotnet tool install --global dotnet-ef`.

```bash
# 1. database
docker compose -f docker/docker-compose.yml up -d postgres

# 2. migrations (also applied on startup in Development)
dotnet ef database update -p src/FlowStock.Infrastructure -s src/FlowStock.Api

# 3. API
dotnet run --project src/FlowStock.Api
```

Then:

- Swagger UI — <http://localhost:5112/swagger> (Development only)
- Liveness — <http://localhost:5112/health/live>
- Readiness (checks PostgreSQL) — <http://localhost:5112/health/ready>

Run everything, API included, in containers:

```bash
docker compose -f docker/docker-compose.yml --profile full up -d --build
```

Tests:

```bash
dotnet test FlowStock.slnx
```

Configuration: the connection string comes from `ConnectionStrings:FlowStockDb`
(env var `ConnectionStrings__FlowStockDb`); `Database:MigrateOnStartup` controls whether
migrations run at startup. Development defaults are in `appsettings.Development.json` and are
for local use only.

## Documentation

- [CLAUDE.md](CLAUDE.md) — architecture rules and conventions all contributors (human or agent) follow.
- [docs/PLAN.md](docs/PLAN.md) — the full development plan and phase roadmap.
- [docs/README.md](docs/README.md) — documentation index.
