# Order Processing API

ASP.NET Core 8 backend for reliable asynchronous order processing with OTP login, JWT auth, RBAC permissions, and background workers.

## Submission documents

- `DESIGN.md`: schema, lifecycle, consistency, idempotency, retry approach.
- `EXPLANATION.md`: key design decisions, trade-offs, challenges, and future improvements.
- `ASSUMPTIONS.md`: assumptions and intentionally added scope.

## Quick start (one command with Docker)

From solution root (`OrderProcessing/`):

```bash
docker compose up --build
```

What this does:

- starts SQL Server container
- starts API container
- creates schema on startup (`EnsureCreatedAsync`)
- seeds required baseline data:
  - roles (`Admin`, `Customer`)
  - permissions and role-permission links
  - admin user from `Bootstrap:AdminEmail`
  - 4 sample products

API base URL: `http://localhost:8080`  
Swagger UI: `http://localhost:8080/swagger`

## Local run without Docker

Set a valid SQL Server connection string in `appsettings.json`, then:

```bash
cd OrderProcessing
dotnet run
```

Schema creation + seeding still run at startup.

## Run tests

From solution root:

```bash
dotnet test
```

## Seeded baseline data

At startup, the app seeds only when missing:

- roles and permissions (`Roles`, `Permissions`, `RolePermissions`)
- admin user and admin role assignment (`Users`, `UserRoles`)
- sample products (`Products`)

Other tables (`Orders`, `OrderItems`, `InventoryLedger`, `PaymentAttempts`, etc.) are populated by normal API + worker flow.

## Minimum API surface

- Product management: `GET /api/products`, `POST /api/products`, `PUT /api/products/{id}`
- Order creation: `POST /api/orders`
- Order details: `GET /api/orders/{id}`
- Order status: `GET /api/orders/{id}/status`
- Order listing: `GET /api/orders`

## Notes for evaluator

- Order creation is asynchronous; processing is done by `OrderProcessingWorker`.
- Idempotency is enforced via idempotency keys and unique constraints.
- Retry behavior is implemented through `OrderProcessingJobs`.
