# CLAUDE.md — FlowStock API working agreement

Read this before any work in this repository. Full detail lives in [docs/PLAN.md](docs/PLAN.md).

## Project

FlowStock is a warehouse and production management system. It tracks products, warehouses and
storage locations, stock balances, stock movements, bills of materials, production orders,
material consumption and finished goods, with a complete inventory history.

The source of truth is the inventory and production transaction history. Everything else
(reports, analytics, forecasting, AI) is built on top of it and must never become an
alternative way to change stock.

## Current phase

**Phase 0 — done. Phase 1 (authentication and users) is next.**

Keep this line current. Update it when a phase reaches its Definition of Done in
[docs/PLAN.md](docs/PLAN.md) (section 33). Do not implement work from a later phase before the
current one is done.

## Stack

.NET 10 · ASP.NET Core Web API · C# · EF Core · PostgreSQL · FluentValidation · JWT auth ·
ASP.NET Core Authorization · Swagger/OpenAPI · Serilog · xUnit · Testcontainers · Docker Compose.

Not now, and not without a concrete requirement discussed first: Redis, RabbitMQ, Kafka,
Elasticsearch, microservices, a separate forecasting service, AI services.

The architecture is a **modular monolith** with clear module boundaries so modules can be
extracted later if that ever becomes necessary.

## Structure

```text
FlowStock.slnx
src/
    FlowStock.Api/             HTTP endpoints, middleware, auth config, DI, Swagger
    FlowStock.Application/     use cases, commands/queries, DTOs, validators, app services
    FlowStock.Domain/          entities, value objects, enums, domain rules and exceptions
    FlowStock.Infrastructure/  EF Core, PostgreSQL, configurations, auth infrastructure
tests/
    FlowStock.UnitTests/
    FlowStock.IntegrationTests/
docker/docker-compose.yml
docs/
```

Dependency direction: `Api → Application → Domain`, `Infrastructure → Domain`.
`Domain` depends on nothing. Never reference `Infrastructure` from `Application` except through
abstractions declared in `Application`/`Domain`.

## Non-negotiable rules

1. **Stock never changes silently.** Every stock change is a confirmed stock movement line or a
   production operation. `stock.Quantity -= x` is never the business operation — it is only the
   effect of writing a movement inside the inventory application logic.
2. **Only `Confirmed` movements affect stock.** `Draft` and `Cancelled` do not. A confirmed
   movement is never edited or deleted — corrections are compensating operations.
3. **Atomic operations.** Source decrease + destination increase + movement record succeed
   together or roll back together, in one transaction.
4. **Decimal quantities.** C# `decimal`, PostgreSQL `numeric`. Never `float`/`double` for
   quantities, ever.
5. **UTC internally.** Store and compute in UTC; convert only at the presentation layer.
6. **No negative availability.** `AvailableQuantity = Quantity - ReservedQuantity` must not go
   below zero unless an explicit business rule allows it. Inventory writes must be safe under
   concurrent users — use row locking / concurrency tokens and let one operation fail rather
   than let both succeed. Concurrency is a core requirement, not a later refinement.
7. **Business logic is not in controllers.** A controller validates the request, calls an
   application service, and maps the result to an HTTP response. Nothing else.
8. **DTOs on the wire.** Never expose or accept EF entities in controllers.
9. **Schema changes come with an EF Core migration.** No manual DDL, no drift.
10. **Critical business rules are tested.** Stock math, insufficient stock, movement validation,
    status transitions, BOM calculation, consumption and output all have tests; critical
    inventory operations also have integration tests.
11. **No silent contract changes.** Changing an existing request/response shape or status code
    is a deliberate, called-out change.
12. **Stay in scope.** Do not implement future phases early, do not add technologies the current
    phase does not need, and do not rewrite working architecture without a concrete reason. If an
    architectural decision is ambiguous, stop and explain the alternatives instead of inventing a
    large implementation.
13. **Follow what is already there.** Inspect existing code and match its conventions before
    adding a feature.

## API conventions

- REST, `/api/...` (no version segment until a breaking change actually requires `/api/v1/...`).
- Collections support pagination, filtering and sorting.
- All I/O-bound methods are `async` and take a `CancellationToken`.
- Proper status codes; validation via FluentValidation.
- Consistent error envelope:

  ```json
  {
    "code": "INSUFFICIENT_STOCK",
    "message": "Insufficient stock for product Flour.",
    "details": { "requested": 100, "available": 75 }
  }
  ```

- Stable domain error codes: `PRODUCT_NOT_FOUND`, `LOCATION_NOT_FOUND`, `INSUFFICIENT_STOCK`,
  `INVALID_MOVEMENT`, `MOVEMENT_ALREADY_CONFIRMED`, `MOVEMENT_ALREADY_CANCELLED`, `BOM_NOT_FOUND`,
  `BOM_INVALID`, `PRODUCTION_ORDER_INVALID`, `PRODUCTION_ORDER_ALREADY_COMPLETED`. Add new codes
  rather than reusing an ill-fitting one; never rename an existing code silently.
- Swagger/OpenAPI stays accurate.
- Authorization is enforced on the backend (`Admin`, `WarehouseManager`, `ProductionManager`,
  `Viewer`), never assumed from the client.

## Auditing and logging

- Important entities carry `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`.
- Inventory movements are immutable history — users cannot rewrite them.
- Every stock operation must be able to answer: who, when, what product, how much, from where,
  to where, why, and which business operation caused it.
- Serilog structured logging for errors, authentication failures, inventory operations,
  production operations and unexpected exceptions.
- **Never log** passwords, password hashes, JWT tokens or any credentials.

## Commands

```bash
dotnet build
dotnet test
dotnet ef migrations add <Name> -p src/FlowStock.Infrastructure -s src/FlowStock.Api
dotnet ef database update -p src/FlowStock.Infrastructure -s src/FlowStock.Api
docker compose -f docker/docker-compose.yml up -d
```

Seed data and seed credentials are for local development only.
