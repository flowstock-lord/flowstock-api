# FlowStock API — Development Plan

## 1. Project Overview

FlowStock is a warehouse and production management system.

The system tracks:

- products and materials;
- warehouses and storage locations;
- stock balances;
- stock movements;
- production areas;
- bills of materials (BOM);
- production orders;
- material consumption;
- finished goods;
- users and roles;
- complete inventory history.

The main principle of the system is:

> **Stock must never change silently. Every stock change must be represented by a stock movement or a production operation.**

The initial target is a manufacturing company where raw materials are stored in a warehouse, transferred to production areas, consumed during production, and converted into finished products.

---

## 2. Technology Stack

### Backend

- .NET 10
- ASP.NET Core Web API
- C#
- Entity Framework Core
- PostgreSQL
- FluentValidation
- JWT authentication
- ASP.NET Core Authorization
- Swagger / OpenAPI
- Serilog
- xUnit
- Testcontainers where appropriate

### Development Infrastructure

- Docker
- Docker Compose
- PostgreSQL container

### Future Technologies

Do not add these during the initial MVP unless there is a concrete requirement:

- Redis
- RabbitMQ
- Kafka
- Elasticsearch
- microservices
- separate forecasting service
- AI services

The initial architecture must be a modular monolith.

---

## 3. Architectural Principles

### 3.1 Modular Monolith

FlowStock API must initially be implemented as a modular monolith.

Do not introduce microservices.

The application should have clear module boundaries so individual modules can later be extracted if necessary.

---

### 3.2 Domain-Driven Business Logic

Business rules must not be implemented directly inside controllers.

Controllers should:

1. validate the request;
2. call an application/service layer;
3. return the appropriate HTTP response.

Business logic belongs in application/domain services.

---

### 3.3 Stock Is Event-Based

Do not implement stock changes as arbitrary direct updates.

Bad:

```text
stock.Quantity -= 100;
```

as the primary business operation.

Preferred:

```text
StockMovement
    SourceLocation
    DestinationLocation
    Product
    Quantity
    User
    CreatedAt
    Reference
```

The system must preserve the history of stock changes.

---

### 3.4 Auditability

Important operations must be traceable.

For stock-related operations it must be possible to answer:

- who performed the operation;
- when it happened;
- what product was affected;
- how much was moved;
- from where;
- to where;
- why;
- which business operation caused the change.

---

### 3.5 Transactions

Operations that change multiple pieces of related data must be atomic.

Example:

Moving 100 kg of flour:

```text
Decrease source stock
+
Increase destination stock
+
Create stock movement
```

All operations must succeed or all must be rolled back.

---

### 3.6 Decimal Quantities

Inventory quantities must support fractional values.

Examples:

```text
100 kg
12.5 kg
0.75 liter
3.25 meter
```

Do not use floating-point types for inventory quantities.

Use appropriate PostgreSQL decimal/numeric types and C# decimal.

---

### 3.7 Time

Store timestamps consistently.

Use UTC internally.

Convert to local time only at the presentation layer.

---

## 4. Project Structure

The initial solution should use a clean modular structure.

Recommended structure:

```text
FlowStock.sln

src/
    FlowStock.Api/
    FlowStock.Application/
    FlowStock.Domain/
    FlowStock.Infrastructure/

tests/
    FlowStock.UnitTests/
    FlowStock.IntegrationTests/

docker/
    docker-compose.yml

docs/
```

Responsibilities:

### FlowStock.Api

- HTTP endpoints
- middleware
- authentication configuration
- dependency injection
- Swagger
- API configuration

### FlowStock.Application

- application services
- commands
- queries
- DTOs
- validators
- business use cases
- authorization rules where appropriate

### FlowStock.Domain

- entities
- value objects
- enums
- domain rules
- domain exceptions
- core business concepts

### FlowStock.Infrastructure

- EF Core
- PostgreSQL
- database configurations
- repositories where actually needed
- authentication infrastructure
- external integrations

---

## 5. Core Domain Model

The initial domain should contain the following concepts.

### 5.1 User

Represents a system user.

Fields:

```text
Id
FirstName
LastName
Email
Phone
PasswordHash
IsActive
CreatedAt
UpdatedAt
```

Roles:

```text
Admin
WarehouseManager
ProductionManager
Viewer
```

---

## 6. Product

A product represents anything that can exist in inventory.

Examples:

```text
Flour
Sugar
Butter
Chocolate
Cookie
Box
Bottle
Packaging
```

Suggested fields:

```text
Id
Sku
Name
Description
ProductType
UnitOfMeasureId
IsActive
CreatedAt
UpdatedAt
```

Product types should initially support:

```text
RawMaterial
Packaging
FinishedProduct
SemiFinishedProduct
Other
```

---

## 7. Unit of Measure

Examples:

```text
kg
g
liter
ml
piece
meter
box
```

The system must clearly define how quantities are measured.

Avoid mixing incompatible units.

Example:

```text
Flour  → kg
Cookie → piece
```

---

## 8. Warehouse

A warehouse represents a physical inventory location.

Examples:

```text
Main Warehouse
Production
Finished Goods Warehouse
```

Fields:

```text
Id
Code
Name
Description
WarehouseType
IsActive
CreatedAt
UpdatedAt
```

Warehouse types:

```text
RawMaterials
Production
FinishedGoods
General
```

---

## 9. Storage Location

A warehouse may contain multiple physical locations.

Examples:

```text
Main Warehouse
    A-01
    A-02
    A-03

Production
    Line-01
    Line-02
```

Fields:

```text
Id
WarehouseId
Code
Name
Description
IsActive
```

This should be implemented even if the first UI does not heavily use it.

---

## 10. Stock

Stock represents the current quantity of a product in a location.

Conceptually:

```text
Product
+
Location
=
Stock Balance
```

Fields:

```text
Id
ProductId
LocationId
Quantity
ReservedQuantity
UpdatedAt
```

Available quantity:

```text
AvailableQuantity = Quantity - ReservedQuantity
```

The system must prevent available stock from becoming negative unless a future business rule explicitly allows negative inventory.

---

## 11. Stock Movement

Stock movement is one of the core entities.

It records movement between locations or inventory adjustments.

Types:

```text
Transfer
Receipt
Adjustment
Consumption
ProductionOutput
WriteOff
```

Suggested fields:

```text
Id
MovementType
ProductId
Quantity
SourceLocationId
DestinationLocationId
ReferenceType
ReferenceId
Reason
CreatedByUserId
CreatedAt
```

Examples:

### Transfer

```text
Main Warehouse
        ↓
Production

Flour: 100 kg
```

### Receipt

```text
Supplier
    ↓
Main Warehouse

Flour: 500 kg
```

### Production output

```text
Production
    ↓
Finished Goods

Cookies: 1,000 pcs
```

---

## 12. Stock Movement Document

A single business operation may contain multiple movement lines.

For example:

```text
Transfer #1001

Flour       100 kg
Sugar        40 kg
Butter       20 kg
Chocolate    10 kg
```

Therefore the system should distinguish between:

### Movement Document

The business operation.

and:

### Movement Lines

Individual products and quantities.

Suggested model:

```text
StockMovement
    Id
    Number
    MovementType
    SourceLocationId
    DestinationLocationId
    Status
    Reason
    CreatedByUserId
    CreatedAt

StockMovementLine
    Id
    StockMovementId
    ProductId
    Quantity
    UnitOfMeasureId
```

---

## 13. Movement Status

Initially:

```text
Draft
Confirmed
Cancelled
```

Only confirmed movements affect stock.

Rules:

- Draft movement does not change stock.
- Confirmed movement changes stock.
- Cancelled movement does not change stock.
- A confirmed movement should not simply be edited.
- If correction is necessary, create a compensating operation.

---

## 14. Bill of Materials

BOM defines what is required to produce a product.

Example:

```text
Cookie
100 pcs

Flour       10 kg
Sugar        4 kg
Butter       2 kg
Chocolate    1 kg
```

Entities:

```text
BillOfMaterial
BillOfMaterialItem
```

Suggested fields:

### BillOfMaterial

```text
Id
ProductId
Version
IsActive
CreatedAt
UpdatedAt
```

### BillOfMaterialItem

```text
Id
BillOfMaterialId
ComponentProductId
Quantity
UnitOfMeasureId
```

A BOM must support versioning.

---

## 15. Production Order

Production Order represents an actual production operation.

Example:

```text
Production Order #10042

Product:
Cookie

Quantity:
1,000 pcs

BOM:
Cookie v3

Status:
Planned
```

Statuses:

```text
Draft
Planned
InProgress
Completed
Cancelled
```

Suggested fields:

```text
Id
Number
ProductId
BillOfMaterialId
PlannedQuantity
ProducedQuantity
ProductionLocationId
Status
PlannedStartAt
ActualStartAt
CompletedAt
CreatedByUserId
CreatedAt
```

---

## 16. Production Consumption

When production starts, required materials are consumed.

Example:

```text
Production Order #10042

Required:

Flour       100 kg
Sugar        40 kg
Butter       20 kg
Chocolate    10 kg
```

Consumption must create corresponding stock movements.

Do not directly modify inventory without creating the appropriate inventory history.

---

## 17. Production Output

When production completes:

```text
Cookie
+1,000 pcs
```

must be added to the appropriate finished goods location.

This operation must also be traceable to:

```text
Production Order
BOM
Consumed Materials
User
Timestamp
```

---

## 18. Production Flow

The initial production workflow should be:

```text
Draft
  →
Planned
  →
InProgress
  →
Completed
```

Example:

```text
Main Warehouse
      │
      │ Transfer
      ▼
Production
      │
      │ Consume materials
      ▼
Production Order
      │
      │ Produce
      ▼
Finished Goods Warehouse
```

---

## 19. Inventory Traceability

The system must support two directions.

### Forward Traceability

Given raw material:

```text
Flour batch #123
```

find:

```text
Production Order #10042
Production Order #10051
```

and therefore the finished products created from it.

### Backward Traceability

Given finished product:

```text
Cookie production #10042
```

find:

```text
Flour
Sugar
Butter
Chocolate
```

and their source movements/batches where batch tracking is implemented.

---

## 20. Batch / Lot Tracking

Batch tracking should be designed into the domain but can be implemented after the basic stock system.

Future example:

```text
Flour

Batch: FL-2026-0828
Supplier: Supplier A
Received: 28.08.2026
Expiry: 28.02.2027
Quantity: 500 kg
```

This will allow future traceability and expiry management.

---

## 21. API Design

Use RESTful HTTP APIs.

Example:

```text
GET    /api/products
GET    /api/products/{id}
POST   /api/products
PUT    /api/products/{id}
DELETE /api/products/{id}
```

Warehouses:

```text
GET    /api/warehouses
GET    /api/warehouses/{id}
POST   /api/warehouses
PUT    /api/warehouses/{id}
```

Stock:

```text
GET /api/stock
GET /api/stock/{productId}
GET /api/stock/by-location/{locationId}
```

Movements:

```text
GET  /api/stock-movements
GET  /api/stock-movements/{id}
POST /api/stock-movements
POST /api/stock-movements/{id}/confirm
POST /api/stock-movements/{id}/cancel
```

BOM:

```text
GET  /api/boms
GET  /api/boms/{id}
POST /api/boms
PUT  /api/boms/{id}
```

Production:

```text
GET  /api/production-orders
GET  /api/production-orders/{id}
POST /api/production-orders
POST /api/production-orders/{id}/start
POST /api/production-orders/{id}/complete
POST /api/production-orders/{id}/cancel
```

---

## 22. API Rules

Use:

- proper HTTP status codes;
- DTOs instead of exposing EF entities;
- pagination for collection endpoints;
- filtering;
- sorting;
- validation;
- consistent error responses;
- cancellation tokens;
- async methods;
- OpenAPI documentation.

Do not expose database entities directly from controllers.

---

## 23. Error Handling

Use a consistent error format.

Example:

```json
{
  "code": "INSUFFICIENT_STOCK",
  "message": "Insufficient stock for product Flour.",
  "details": {
    "requested": 100,
    "available": 75
  }
}
```

Important domain errors should have stable error codes.

Examples:

```text
PRODUCT_NOT_FOUND
LOCATION_NOT_FOUND
INSUFFICIENT_STOCK
INVALID_MOVEMENT
MOVEMENT_ALREADY_CONFIRMED
MOVEMENT_ALREADY_CANCELLED
BOM_NOT_FOUND
BOM_INVALID
PRODUCTION_ORDER_INVALID
PRODUCTION_ORDER_ALREADY_COMPLETED
```

---

## 24. Authentication

Implement JWT authentication.

Required functionality:

```text
Login
Current User
Roles
Authorization
```

Example:

```text
POST /api/auth/login
GET  /api/auth/me
```

Passwords must never be stored in plain text.

---

## 25. Authorization

Use role-based authorization.

Example:

### Admin

Full access.

### WarehouseManager

Can:

- view stock;
- create transfers;
- confirm transfers;
- receive materials;
- perform inventory adjustments.

### ProductionManager

Can:

- view production;
- manage BOMs;
- create production orders;
- start production;
- complete production.

### Viewer

Read-only access.

Authorization must be enforced on the backend.

Never rely only on frontend restrictions.

---

## 26. Database

Use PostgreSQL.

EF Core migrations must be used.

Initial database should contain tables for:

```text
Users
Roles
UserRoles
Products
UnitsOfMeasure
Warehouses
StorageLocations
Stock
StockMovements
StockMovementLines
BillsOfMaterials
BillOfMaterialItems
ProductionOrders
```

Use appropriate:

- primary keys;
- foreign keys;
- unique constraints;
- indexes;
- check constraints where useful.

---

## 27. Important Database Constraints

Examples:

A product SKU must be unique.

```text
Products.Sku UNIQUE
```

A warehouse code must be unique.

```text
Warehouses.Code UNIQUE
```

A location belongs to exactly one warehouse.

Stock must reference an existing product and location.

Movement lines must have positive quantities.

Quantity must never be negative.

---

## 28. Concurrency

Inventory operations must be safe when multiple users perform operations simultaneously.

Example:

Two warehouse employees attempt to transfer:

```text
80 kg
```

from a location containing:

```text
100 kg
```

at the same time.

The system must not allow both operations to succeed if the resulting inventory would become negative.

Use appropriate PostgreSQL/EF Core concurrency and transaction mechanisms.

Concurrency handling is a core requirement of inventory operations.

---

## 29. Auditing

Important entities should contain:

```text
CreatedAt
CreatedBy
UpdatedAt
UpdatedBy
```

Inventory movements must additionally preserve immutable historical information.

Do not allow users to rewrite historical inventory operations.

---

## 30. Logging

Use structured logging.

Serilog should be used for application logs.

Log:

- application errors;
- authentication failures;
- important inventory operations;
- production operations;
- unexpected exceptions.

Do not log:

- passwords;
- JWT tokens;
- sensitive credentials.

---

## 31. Testing Strategy

Tests are mandatory for core business logic.

### Unit Tests

Test:

- stock calculations;
- movement validation;
- insufficient stock;
- BOM calculations;
- production consumption;
- production output;
- status transitions.

Example:

```text
Given:
100 kg flour

When:
transfer 30 kg

Then:
source = 70 kg
destination = 30 kg
```

---

### Integration Tests

Test:

- PostgreSQL integration;
- API endpoints;
- authentication;
- transactions;
- stock movement persistence;
- production workflows.

Critical inventory operations must have integration tests.

---

## 32. Seed Data

Development environment should include seed data.

Example:

### Warehouses

```text
MAIN
PRODUCTION
FINISHED
```

### Products

```text
Flour
Sugar
Butter
Chocolate
Cookie
```

### BOM

```text
Cookie v1
```

### Users

```text
admin
warehouse.manager
production.manager
viewer
```

Seed credentials must only be for local development.

Never use development passwords in production.

---

## 33. Development Phases

### Phase 0 — Project Foundation

Tasks:

- create solution;
- create projects;
- configure dependencies;
- configure Docker;
- configure PostgreSQL;
- configure EF Core;
- configure migrations;
- configure Swagger;
- configure basic logging;
- configure environment settings;
- configure health checks.

#### Definition of Done

```text
API starts successfully.
PostgreSQL starts successfully.
Database migration works.
Swagger works.
Health endpoint works.
```

---

### Phase 1 — Authentication and Users

Implement:

- User;
- Role;
- authentication;
- JWT;
- login;
- current user;
- authorization policies.

#### Definition of Done

A user can:

```text
register/seed
login
receive JWT
call authorized endpoint
```

Roles are enforced by backend.

---

### Phase 2 — Products and Units

Implement:

- Product;
- UnitOfMeasure;
- CRUD;
- validation;
- filtering;
- pagination;
- SKU uniqueness.

#### Definition of Done

Admin can create:

```text
Flour
SKU: FLOUR-001
Unit: kg
Type: RawMaterial
```

---

### Phase 3 — Warehouses and Locations

Implement:

- Warehouse;
- StorageLocation;
- CRUD;
- relationships;
- validation.

#### Definition of Done

The system supports:

```text
Main Warehouse
    ├── A-01
    └── A-02

Production
    ├── LINE-01
    └── LINE-02

Finished Goods
    └── FG-01
```

---

### Phase 4 — Inventory Core

Implement:

- Stock;
- StockMovement;
- StockMovementLine;
- receipt;
- transfer;
- adjustment;
- confirmation;
- cancellation;
- transactions;
- concurrency protection.

This is one of the most important phases.

#### Definition of Done

The following scenario works:

```text
Main Warehouse
Flour = 500 kg

Transfer 100 kg

Main Warehouse
Flour = 400 kg

Production
Flour = 100 kg
```

The operation has complete history.

---

### Phase 5 — BOM

Implement:

- BOM;
- BOM versions;
- BOM items;
- validation;
- required material calculation.

#### Definition of Done

Given:

```text
Cookie × 100

Flour 10 kg
Sugar 4 kg
Butter 2 kg
```

the API can calculate the required materials.

---

### Phase 6 — Production Orders

Implement:

- ProductionOrder;
- statuses;
- start;
- material consumption;
- completion;
- finished goods output;
- transaction boundaries.

#### Definition of Done

Scenario:

```text
Raw Materials
       ↓
Production
       ↓
Production Order
       ↓
Consume materials
       ↓
Produce finished goods
       ↓
Finished Goods Warehouse
```

All inventory changes are traceable.

---

### Phase 7 — Traceability

Implement:

- movement history;
- production history;
- forward traceability;
- backward traceability.

Example queries:

```text
Where did this product come from?

What materials were used to produce this product?

Where was this material used?

Who moved this material?

When did it happen?
```

---

### Phase 8 — Batch / Lot Tracking

Implement:

- batches;
- lot numbers;
- expiry dates;
- supplier association;
- batch-level stock;
- batch traceability.

This phase should build on the existing inventory architecture rather than redesign it.

---

### Phase 9 — Reporting

Implement basic reports:

```text
Current stock
Stock by warehouse
Stock movement history
Production history
Material consumption
Finished goods production
Inventory adjustments
```

Do not build complex BI functionality yet.

---

### Phase 10 — Notifications

Introduce notifications only after core inventory is stable.

Potential notifications:

```text
Low stock
Production shortage
Expired batch
Production completed
Transfer received
```

Initially notifications can be application-level events or simple persisted notifications.

Do not introduce a message broker without a real requirement.

---

### Phase 11 — Inventory Intelligence

Only after the core system is stable.

Implement:

- demand history;
- consumption analytics;
- average consumption;
- stockout prediction;
- reorder point;
- recommended purchase quantity.

The existing inventory data should become the foundation for this module.

---

### Phase 12 — AI Layer

AI should be an additional intelligence layer, not the source of truth.

Potential capabilities:

```text
"Which materials may run out next week?"

"Why is flour consumption higher this month?"

"Which products have abnormal consumption?"

"How much material should we purchase?"

"Which production orders may have shortages?"
```

AI must use structured application data and business rules.

AI must not directly modify stock.

Any proposed action must go through normal application workflows.

---

## 34. Future Architecture

The long-term architecture may evolve into:

```text
                 FlowStock
                     │
       ┌─────────────┼─────────────┐
       │             │             │
   Warehouse      Production    Intelligence
       │             │             │
       └─────────────┼─────────────┘
                     │
                  Inventory
                     │
                PostgreSQL
```

Potential future clients:

```text
Angular Web
Flutter Mobile
Public API
AI Assistant
```

---

## 35. Mobile API Requirements

The API must eventually support mobile warehouse operations.

Mobile use cases:

```text
Login
Scan product
View stock
Scan location
Create transfer
Confirm transfer
Receive materials
Perform inventory count
Start production operation
Complete production operation
```

The API should not contain UI-specific business logic.

---

## 36. Barcode / QR Support

The backend should support identifiers that can be encoded into:

```text
Barcode
QR code
SKU
Product ID
Batch ID
Location code
```

The initial implementation does not need to generate or scan QR codes.

It only needs stable identifiers that the Flutter application can use.

---

## 37. API Versioning

The initial API may use:

```text
/api/...
```

If breaking changes become necessary, introduce:

```text
/api/v1/...
```

Do not add versioning complexity prematurely.

---

## 38. Documentation

Maintain:

```text
README.md
PLAN.md
docs/
```

Document:

- architecture;
- setup;
- environment variables;
- database;
- authentication;
- API conventions;
- domain concepts;
- development workflow.

Swagger/OpenAPI must remain up to date.

---

## 39. AI Development Rules

AI coding agents must follow these rules.

### Rule 1

Do not implement future phases before the current phase is complete.

### Rule 2

Do not introduce technologies that are not required by the current phase.

### Rule 3

Do not rewrite working architecture without a concrete reason.

### Rule 4

Do not modify database schema without creating the appropriate EF Core migration.

### Rule 5

Do not bypass business services from controllers.

### Rule 6

Do not directly modify stock quantities outside the inventory domain/application logic.

### Rule 7

Every critical business rule must have tests.

### Rule 8

Do not silently change existing API contracts.

### Rule 9

Before implementing a feature, inspect the existing codebase and follow established conventions.

### Rule 10

If an architectural decision is unclear, stop and explain the alternatives instead of inventing a large implementation.

---

## 40. Definition of MVP Completion

The MVP is complete when the following scenario works end-to-end.

### Scenario

#### Step 1

Admin creates:

```text
Flour
Sugar
Butter
Cookie
```

#### Step 2

Admin creates:

```text
Main Warehouse
Production
Finished Goods
```

#### Step 3

Warehouse receives:

```text
Flour 500 kg
Sugar 200 kg
Butter 100 kg
```

#### Step 4

Warehouse transfers:

```text
Flour 100 kg
Sugar 40 kg
Butter 20 kg
```

to Production.

#### Step 5

System shows:

```text
Main Warehouse

Flour 400 kg
Sugar 160 kg
Butter 80 kg
```

and:

```text
Production

Flour 100 kg
Sugar 40 kg
Butter 20 kg
```

#### Step 6

Create BOM:

```text
Cookie × 100

Flour 10 kg
Sugar 4 kg
Butter 2 kg
```

#### Step 7

Create Production Order:

```text
Cookie × 1,000
```

#### Step 8

Start production.

The system consumes:

```text
Flour 100 kg
Sugar 40 kg
Butter 20 kg
```

#### Step 9

Complete production.

System creates:

```text
Cookie 1,000 pcs
```

in Finished Goods.

#### Step 10

The system can show the complete history:

```text
Receipt
   →
Warehouse
   →
Transfer
   →
Production
   →
Material Consumption
   →
Production Order
   →
Finished Product
```

---

## 41. Final Product Goal

The final product should evolve from:

```text
Warehouse Accounting
```

into:

```text
                    FlowStock

        ┌─────────────┼─────────────┐
        │             │             │
     Warehouse     Production    Purchasing
        │             │             │
        └─────────────┼─────────────┘
                      │
                  Inventory
                      │
                Traceability
                      │
                 Analytics
                      │
              AI Intelligence
```

The fundamental source of truth is the inventory and production transaction history.

AI, analytics, forecasting, and automation must be built on top of this foundation.
