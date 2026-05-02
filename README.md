# Order Processing (Keycloak + SpiceDB)

ASP.NET Core 8 API for asynchronous order processing, with **Keycloak** (JWT / realm roles) and **SpiceDB** (fine-grained order permissions). Background work runs in `OrderProcessingWorker`.

Detailed API and behavior notes: `OrderProcessing/README.md`.

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) with Compose
- Enough RAM/CPU for SQL Server, Keycloak, Postgres, SpiceDB, and the API (first cold start can take several minutes)

## Run everything (one command)

From this repository root (the folder that contains `docker-compose.yml`):

```bash
docker compose up --build
```

### Services and ports

| Service    | URL / port |
|-----------|------------|
| API       | http://localhost:8080 |
| Swagger   | http://localhost:8080/swagger |
| Keycloak  | http://localhost:8081 |
| SQL Server| host port `14333` → container `1433` |
| SpiceDB HTTP | http://localhost:8443 |
| SpiceDB gRPC | localhost:50051 |

Realm and users are imported from `keycloak/import/` on Keycloak startup (`--import-realm`). SpiceDB schema is applied by `spicedb-init` from `spicedb-schema.zed`.

### Get a JWT for Swagger or curl

Token endpoint (password grant; dev-only):

```http
POST http://localhost:8081/realms/order-processing/protocol/openid-connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=password&client_id=order-api&username=testuser&password=password
```

Example users from the imported realm (`keycloak/import/order-processing-realm.json`):

| Username   | Password  | Realm roles   |
|-----------|-----------|----------------|
| testuser  | password  | Customer, Admin |
| adminuser | admin     | Admin           |

Use the `access_token` from the JSON response as `Authorization: Bearer <token>` in Swagger (Authorize button) or HTTP clients.

Keycloak admin console: http://localhost:8081 — `admin` / `admin` (see `docker-compose.yml`).

### First boot

Keycloak may still be importing the realm when the API container starts. If token requests fail, wait a minute and retry.

## Additional docs

- `OrderProcessing/README.md` — API surface, tests, local run without Docker
- `DESIGN.md`, `EXPLANATION.md`, `ASSUMPTIONS.md` — design and assumptions
