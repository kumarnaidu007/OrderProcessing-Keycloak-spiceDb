# Order Processing API

ASP.NET Core 8 backend for asynchronous order processing with **Keycloak** (OpenID Connect JWTs, realm roles) and optional **SpiceDB** checks for order-level permissions. A hosted **OrderProcessingWorker** drives the pipeline.

## Submission documents

- `DESIGN.md`: schema, lifecycle, consistency, idempotency, retry approach.
- `EXPLANATION.md`: key design decisions, trade-offs, challenges, and future improvements.
- `ASSUMPTIONS.md`: assumptions and intentionally added scope.

## Quick start with Docker (recommended)

Run from the **repository root** (parent of this folder — where `docker-compose.yml` lives):

```bash
docker compose up --build
```

What starts:

- **SQL Server** — persistence; API waits for DB health before starting.
- **Keycloak** — realm `order-processing` imported from `keycloak/import/`; JWT validation uses metadata from inside the compose network and issuers for `localhost`.
- **Postgres + SpiceDB** — migrate, serve, then `zed schema write` for `spicedb-schema.zed`.
- **API** — `EnsureCreatedAsync` creates the EF schema; **DataSeeder** adds sample products if the catalog is empty.

URLs: API `http://localhost:8080`, Swagger `http://localhost:8080/swagger`, Keycloak `http://localhost:8081`.

### Authentication (Swagger / API)

Obtain an access token (password grant — development only):

```http
POST http://localhost:8081/realms/order-processing/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&client_id=order-api&username=testuser&password=password
```

Use `access_token` as Bearer token. Dev users are defined in `keycloak/import/order-processing-realm.json` (for example `testuser` / `password`, `adminuser` / `admin`).

Users are **provisioned in the app database** on first authenticated use (`ApplicationUserResolver`) when they call the API with a valid Keycloak token.

## Local run without Docker

You need a reachable SQL Server, Keycloak configured like `appsettings.json` / dev settings, and optionally SpiceDB if you set `SpiceDb:Enabled` to `true`.

1. Set `ConnectionStrings:DefaultConnection` in `appsettings.Development.json` (or user secrets).
2. Run Keycloak (or point `Keycloak:*` at your instance) with realm `order-processing`, client `order-api`, and matching issuer URLs.
3. From this project directory:

```bash
dotnet run
```

Schema creation and product seeding still run at startup.

## Run tests

From the repository root (solution folder):

```bash
dotnet test
```

## Seeded baseline data

On startup, **sample products** are inserted only when no products exist yet. Roles for authorization come from **Keycloak** JWTs (`realm_access.roles`), not from seeding the SQL `Roles` table for normal Docker flow.

Other tables are filled by API usage and the background worker.

## Minimum API surface

- Product management: `GET /api/products`, `POST /api/products`, `PUT /api/products/{id}`
- Order creation: `POST /api/orders`
- Order details: `GET /api/orders/{id}`
- Order status: `GET /api/orders/{id}/status`
- Order cancellation: `POST /api/orders/{id}/cancel` (allowed while order is `Pending`)
- Order listing: `GET /api/orders`

## Notes for evaluator

- Order creation is asynchronous; processing is done by `OrderProcessingWorker`.
- Idempotency is enforced via idempotency keys and unique constraints.
- Duplicate protection applies when the same `Idempotency-Key` is reused.
- If `Idempotency-Key` is omitted, identical payload re-submissions may create separate orders.
- Retry behavior uses `OrderProcessingJobs`.
- Payment declines are retried up to `OrderProcessing:MaxPaymentAttempts` (default `2`).
- Failed payments after retries trigger idempotent inventory compensation (`SaleRestore`).
- Order status changes are guarded by `OrderStatusTransitions`.
- With SpiceDB enabled (Docker default), order view/cancel can require a relationship written when the order is created; see `SpiceDbAuthorizationService`.
