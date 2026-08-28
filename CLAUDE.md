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

**Phase 8 — done. Phase 9 (reporting) is next.**

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

- Stable domain error codes in use: `USER_NOT_FOUND`, `EMAIL_ALREADY_EXISTS`, `ROLE_NOT_FOUND`,
  `PRODUCT_NOT_FOUND`, `SKU_ALREADY_EXISTS`, `UNIT_OF_MEASURE_NOT_FOUND`,
  `UNIT_OF_MEASURE_CODE_EXISTS`, `UNIT_OF_MEASURE_INACTIVE`, `WAREHOUSE_NOT_FOUND`,
  `WAREHOUSE_CODE_EXISTS`, `WAREHOUSE_INACTIVE`, `LOCATION_NOT_FOUND`, `LOCATION_CODE_EXISTS`,
  `LOCATION_INACTIVE`, `MOVEMENT_NOT_FOUND`, `INSUFFICIENT_STOCK`, `INVALID_MOVEMENT`,
  `MOVEMENT_ALREADY_CONFIRMED`, `MOVEMENT_ALREADY_CANCELLED`, `BOM_NOT_FOUND`, `BOM_INVALID`,
  `PRODUCTION_ORDER_NOT_FOUND`, `PRODUCTION_ORDER_INVALID`, `PRODUCTION_ORDER_ALREADY_COMPLETED`,
  `BATCH_NOT_FOUND`, `BATCH_NUMBER_EXISTS`, `BATCH_REQUIRED`, `BATCH_NOT_ALLOWED`, `BATCH_INVALID`.
  Add new codes rather than reusing an ill-fitting one; never rename an existing code silently.
- Enums cross the wire by name (`JsonStringEnumConverter`), never as numbers.
- Model binding failures (malformed body, bad query type) go through `ModelBindingErrors` so they
  return the same envelope as FluentValidation, and never leak CLR type names.
- Swagger/OpenAPI stays accurate. `decimal` is mapped explicitly to `format: decimal`, because
  Swashbuckle's default (`double`) would tell clients to use the one type quantities must not use.
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

## Inventory core (Phase 4)

- `Stock`, `StockMovement` and `StockMovementLine` live in `src/FlowStock.Domain/Inventory/`.
  All the writing happens in `StockMovementService`; `StockService` only reads.
- A balance (`Stock`) is a **derived** value — one row per (product, location), created on demand
  by the first movement that touches it. It is never the source of truth and is never written
  from anywhere but `StockMovementService.ConfirmAsync`.
- A movement is a document plus lines. Line quantities are always positive; direction comes from
  the document's endpoints. `Receipt` has only a destination, `Transfer` has both (and they must
  differ), `Adjustment` has exactly one — destination for a surplus, source for a shortage, and
  it must state a reason. `Consumption`, `ProductionOutput` and `WriteOff` exist in the enum but
  are rejected by the create validator until the phase that owns them.
- Status flow is `Draft → Confirmed` or `Draft → Cancelled`, nothing else. There is no update and
  no delete endpoint: a confirmed movement is corrected by a compensating movement.
- **Confirmation is the transaction.** `IFlowStockDbContext.BeginTransactionAsync` wraps it, and
  `LockStockAsync` loads every balance the document touches with `SELECT ... FOR UPDATE` in a
  fixed order, so a competing confirmation waits and then reads the updated quantity. Missing
  balances are inserted with `ON CONFLICT DO NOTHING` and re-locked. The lock is what makes the
  guarantee; `StockLockingTests` proves it by holding a balance and timing the second reader.
- Postgres check constraints (`Quantity >= 0`, `ReservedQuantity <= Quantity`, line
  `Quantity > 0`) are the last line of defence behind the application rule, not a substitute.
- `ReservedQuantity` is on `Stock` and always zero for now; reservations arrive with production
  orders. `AvailableQuantity = Quantity - ReservedQuantity` is computed, never a column.
- Document numbers (`MOV-000001`) come from the `StockMovementNumbers` PostgreSQL sequence.
- Reading stock and movement history is open to any authenticated user — it is the audit trail.
  Creating, confirming and cancelling need `Policies.Warehouse` (docs/PLAN.md, section 25).

## Bills of materials (Phase 5)

- `BillOfMaterial` and `BillOfMaterialItem` live in `src/FlowStock.Domain/Production/`, which will
  also hold production orders. `BillOfMaterialService` is the only thing that touches them, and it
  never touches stock.
- A recipe produces `OutputQuantity` of its product — the "Cookie / 100 pcs" of docs/PLAN.md,
  section 14. This field is not in the plan's suggested field list, but section 14's own example
  is unreadable without it: item quantities need a scale to be read against.
- **A published version is immutable.** `PUT /api/boms/{id}` changes only `Name` and
  `Description`; components, output quantity and version never change. A different recipe is a new
  version, so an order built from an older one can still show what it used — the same reasoning as
  rule 2 for confirmed movements.
- Versions are numbered per product (`1, 2, 3...`), assigned by the service, and `(ProductId,
  Version)` is unique. Publishing makes the new version active and stands the previous one down.
- At most one active version per product, enforced by a **filtered unique index**
  (`IX_BillsOfMaterial_ProductId_Active`, `WHERE "IsActive"`). Because of it, deactivating the old
  version is saved *before* inserting the new one, inside a transaction: EF may order an insert
  ahead of an update within one save, which would trip the index on the intermediate state.
- Item units are copied from the component product, exactly as on a stock movement line.
- A component may appear once per recipe, and a product can never be a component of itself.
  Both raise `BOM_INVALID`. Multi-level explosion (a component that has its own recipe) is not
  Phase 5 — requirement calculation is single-level.
- `GET /api/boms/{id}/requirements?quantity=N` scales the recipe: `item.Quantity * N /
  OutputQuantity`, rounded to 4 decimals (the storage scale) away from zero. It is a pure
  calculation — it does not look at stock. Checking availability belongs to Phase 6.
- Reads are open to any authenticated user; writes need `Policies.Production`
  (docs/PLAN.md, section 25).

## Production orders (Phase 6)

- `ProductionOrder` and `ProductionOrderMaterial` live in `src/FlowStock.Domain/Production/`, next
  to the recipes they are built from. `ProductionOrderService` runs the workflow;
  `StockMovementService` still owns every write to stock.
- The workflow is `Draft → Planned → InProgress → Completed`, plus `Cancelled` from `Draft` or
  `Planned` only. A started run has confirmed movements behind it, so it is corrected with
  compensating movements, never by cancelling it (rule 2).
- **The order never touches stock itself.** Starting posts a `Consumption` movement out of the
  production location, completing posts a `ProductionOutput` movement into the output location,
  both created and confirmed through `IStockMovementService.PostForProductionOrderAsync` and
  stamped with `StockMovement.ProductionOrderId` — which is what makes traceability work in both
  directions (docs/PLAN.md, section 19). Those two types still cannot be posted by hand: the
  create validator rejects them, and the order is the only author.
- Materials are a **snapshot**, scaled from the recipe when the order is created, not a view over
  it. A recipe may be superseded while a run is open, and the run must keep saying what it
  undertook to consume. The order also records `BillOfMaterialId`, so it can show the exact
  version it used.
- `ProductionLocationId` is where materials are reserved and consumed; `OutputLocationId` is where
  finished goods land. The second field is not in section 15's suggested list, but section 17
  requires the output to land in a named location, and it is rarely the shop floor.
- **Reservations are what planning buys.** Planning locks the balances the same way a confirmation
  does and raises `ReservedQuantity`, so `AvailableQuantity` falls and a competing transfer is
  refused. Starting releases the reservation and saves it *before* confirming the consumption —
  otherwise the run's own reservation would make its own materials unavailable.
- A production operation is one transaction. `IFlowStockDbContext.BeginTransactionAsync` joins an
  already-open transaction instead of nesting, so the movements posted inside an order commit or
  roll back with it.
- Order numbers (`PRD-000001`) come from the `ProductionOrderNumbers` PostgreSQL sequence.
- Reading orders is open to any authenticated user — production history is part of the audit
  trail. Running one needs `Policies.Production` (docs/PLAN.md, section 25).

## Traceability (Phase 7)

- `TraceabilityService` lives in `src/FlowStock.Application/Traceability/` and owns no entities and
  no schema: Phase 7 adds no table and no migration. Everything it answers is derived from
  confirmed movements and production orders, which is exactly the rule that reports may never
  become another way to change stock.
- Three questions, three endpoints under `/api/traceability`, all `Policies.AnyAuthenticated`:
  `products/{id}/history` (where a product came from and went, in or out, who and when),
  `products/{id}/usage` (forward: which runs consumed a material and what they produced), and
  `production-orders/{id}` (backward: what a run was made of).
- **Only confirmed movements are history.** A draft has not happened and a cancelled one never
  did, so neither appears.
- The person is resolved to a name and email, not left as an id — "who moved this material" is one
  of the questions the module exists to answer. A movement is attributed to whoever confirmed it,
  because confirmation is the act that changed stock.
- `MaterialSource` lists the movements that *could* have supplied a consumed material — the
  confirmed inbound documents into the production location up to the moment of consumption, newest
  first. Without batches (Phase 8) stock in a location is fungible, so naming the exact kilograms
  is not something the data supports; section 19 asks for source movements "where batch tracking is
  implemented", and this is the honest answer until then.
- `StockFlow` is a presentation concept, not a domain rule, so it lives in the Application layer:
  asked about a product it reads In / Out / Transfer, asked about a location it reads relative to
  that location.

## Batches and lots (Phase 8)

- `Batch` lives in `src/FlowStock.Domain/Inventory/`, not in the catalogue: a lot is goods that
  arrived or were made, not a definition. `BatchService` registers and reads lots and never touches
  stock — how much of a lot is left is a `Stock` balance like any other.
- **Tracking is opt-in per product.** `Product.IsBatchTracked` is set when the product is created
  and immutable afterwards, for the same reason the SKU is: balances and history are recorded
  either with a lot or without one, and flipping the flag would make them unreadable.
- **The warehouse names the lot; the system never picks one.** A movement line carries `BatchId`,
  required for a tracked product and rejected for any other. One line moves one lot — taking from
  two lots is two lines, and that is no longer a duplicate: the "same product twice" rule is now
  per (product, lot). There is deliberately no automatic FEFO allocation.
- **A balance is per (product, location, lot).** The unique index behind `LockStockAsync` is
  `(ProductId, LocationId, BatchId)` declared **`NULLS NOT DISTINCT`**: without it Postgres treats
  every null batch as a different key, and an untracked product would open a new anonymous balance
  on every receipt. The locking SQL matches lots with `IS NOT DISTINCT FROM` for the same reason.
- A production order names the lot of every tracked component when it is created, so the
  reservation holds the exact goods the run counted on. Completing a run of a tracked product
  creates the output lot — numbered after the order unless the request names one — and links it
  back with `Batch.ProductionOrderId`.
- Lot numbers are normalized upper-case and unique per product, and immutable after creation.
  Supplier is free text: there is no purchasing module in any phase of docs/PLAN.md.
- Nothing blocks issuing an expired lot. Expiry is reported (`IsExpired`, `?expiringBefore=`,
  soonest-expiry-first ordering) and acting on it is Phase 10's notification, not a hidden rule
  invented here.
- `GET /api/traceability/batches/{id}` is where lot traceability lands: what the lot is, where it
  is now, every movement that touched it, and the runs it ended up in. With a lot in hand the chain
  is exact, so `MaterialSource` stops being a list of candidates for tracked products.
- Reading lots is open to any authenticated user; registering one needs `Policies.Warehouse` —
  a lot appears when goods arrive, which is a warehouse operation, not catalogue maintenance.
