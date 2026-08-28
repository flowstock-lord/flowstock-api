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

**Phase 3 — done. Phase 4 (inventory core) is next.**

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
  `BOM_INVALID`, `PRODUCTION_ORDER_INVALID`, `PRODUCTION_ORDER_ALREADY_COMPLETED`. Already in use:
  `USER_NOT_FOUND`, `EMAIL_ALREADY_EXISTS`, `ROLE_NOT_FOUND`, `SKU_ALREADY_EXISTS`,
  `UNIT_OF_MEASURE_NOT_FOUND`, `UNIT_OF_MEASURE_CODE_EXISTS`, `UNIT_OF_MEASURE_INACTIVE`,
  `WAREHOUSE_NOT_FOUND`, `WAREHOUSE_CODE_EXISTS`, `WAREHOUSE_INACTIVE`, `LOCATION_CODE_EXISTS`.
  Add new codes rather than reusing an ill-fitting one; never rename an existing code silently.
- Enums cross the wire by name (`JsonStringEnumConverter`), never as numbers.
- Model binding failures (malformed body, bad query type) go through `ModelBindingErrors` so they
  return the same envelope as FluentValidation, and never leak CLR type names.
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

Seed data and seed credentials are for local development only. `Jwt:Key` is empty in
`appsettings.json` on purpose — never commit a signing key; supply it per environment
(`Jwt__Key`). Development seeds four users (`admin@flowstock.local` and friends) whose passwords
live in `appsettings.Development.json`.

## Authentication (Phase 1)

- `POST /api/auth/login` → JWT; `GET /api/auth/me` → the caller. Access tokens only, no refresh.
- Roles live in the `Roles` table and are named by `RoleNames` in
  `src/FlowStock.Domain/Users/Role.cs` — never hardcode the strings.
- Protect endpoints with the policies in `src/FlowStock.Api/Authorization/Policies.cs`
  (`Admin`, `Warehouse`, `Production`, `AnyAuthenticated`), not with raw role strings.
- Login failures return 401 from the controller; they are not `DomainException`s, which the
  middleware maps to 400.
- Request DTOs are validated by `ValidationFilter`; adding an `AbstractValidator<T>` is enough,
  registration is automatic.

## Catalog (Phase 2)

- `Product` and `UnitOfMeasure` live in `src/FlowStock.Domain/Catalog/`. A product is bound to
  exactly one unit; quantities are never mixed across units.
- Master data is read by any authenticated user and written by `Admin` only — section 25 of the
  plan gives full access to `Admin` alone, and the catalogue is not a warehouse operation.
- `Sku` is normalized upper-case and unique; `UnitOfMeasure.Code` is normalized lower-case and
  unique. Both are immutable after creation — inventory history refers to them.
- `ProductType` is persisted by name, so reordering the enum can never reinterpret existing rows.
- Nothing in the catalogue is ever deleted: products and units are deactivated. An inactive unit
  cannot be attached to a product.
- Collections take `page`, `pageSize`, filters, and (for products) `sort`
  (`sku`, `name`, `type`, `createdAt`, `-` prefix for descending).

## Warehouses and locations (Phase 3)

- `Warehouse` and `StorageLocation` live in `src/FlowStock.Domain/Warehouses/`. Stock never sits
  on a warehouse — it sits in a storage location, which belongs to exactly one warehouse and
  never moves to another (docs/PLAN.md, section 27).
- Master data, same rule as the catalogue: read by any authenticated user, written by `Admin`.
- `Warehouse.Code` is unique globally; `StorageLocation.Code` is unique **within its warehouse**
  (`A-01` may exist in several). Both are normalized upper-case and immutable after creation.
- A deactivated warehouse accepts no new locations. Nothing is deleted, only deactivated.
- Locations are addressed flatly by id (`/api/storage-locations`, filter with `?warehouseId=`),
  because stock and movements will address them by id from Phase 4 on.
