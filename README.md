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

---

## Countries API (SCRUM-6)

This repository contains a small read-only Countries API added for SCRUM-6. The feature exposes two public endpoints for listing countries and retrieving a single country by ISO code. The data source is an embedded JSON resource located at `OrderProcessing/Resources/countries.json` and is loaded into a thread-safe singleton in-memory store (ICountriesStore / CountriesStore).

Jira ticket: [SCRUM-6](https://jira.company.local/browse/SCRUM-6)

### Summary of behavior

- Endpoints are public (anonymous access).
- Uses an in-memory singleton store that loads `OrderProcessing/Resources/countries.json` (object with root key `countries`, array items with `code` and `name`).
- Deterministic ordering: results are ordered alphabetically by `name` using InvariantCultureIgnoreCase.
- Paging for the list endpoint (page=1 default, size=50 default, size max 500).
- Validation errors and server errors are returned as RFC7807 ProblemDetails.
  - ProblemDetails.extensions includes a correlationId (propagated from `X-Correlation-Id` header or generated per request).
- DTO: CountryResponseMinimal (properties Code, Name) serialized with System.Text.Json preserving PascalCase via JsonPropertyName attributes.
- Structured logging (ILogger<T>) for load success/failure, validation failures, lookup misses, and unhandled exceptions.

### Endpoints

1) List Countries

- Route: GET /api/v1/countries
- Query parameters:
  - page (int, optional) — 1-based page number; default = 1; must be >= 1
  - size (int, optional) — page size; default = 50; must be >= 1 and <= 500
- Success (200): paging envelope
  - JSON shape:

```json
{
  "items": [ { "code": "US", "name": "United States" }, ... ],
  "page": 1,
  "size": 10,
  "totalItems": 249,
  "totalPages": 25
}
```

- Error (400): invalid page/size → ProblemDetails (RFC7807) with correlationId in extensions.
- Error (500): server error → ProblemDetails.

Notes: no server-side caching headers are set by the API; clients may cache responses.

2) Get Country By Code

- Route: GET /api/v1/countries/{code}
- Path parameter: code (string) — trimmed, alphabetic only, length 2 or 3 (pattern: ^[A-Za-z]{2,3}$). Lookup is case-insensitive.
- Success (200): returns CountryResponseMinimal

Example success:

```json
{ "code": "US", "name": "United States" }
```

- Error (400): invalid code format → ProblemDetails (RFC7807).
- Error (404): well-formed code but not found → ProblemDetails (RFC7807).
- Error (500): server error → ProblemDetails.

### DI and hosting notes (developer checklist)

- The store is registered in DI as a singleton. Register exactly once:

  services.AddSingleton<ICountriesStore, CountriesStore>();

- There is an optional hosted service to eager-load the store at startup. If used, it should call EnsureLoadedAsync on startup.
- OrderProcessing/Resources/countries.json must be loaded as an object with root key "countries" (not a bare array). Each item has `code` and `name` fields.
- The controller `OrderProcessing/Controllers/CountriesController.cs` injects ICountriesStore and calls its async methods directly (no reflection).
- Country validation uses a regex: ^[A-Za-z]{2,3}$ and normalization is to uppercase for responses/lookup.

### How to run locally to test the Countries API

Option A — Full stack (recommended for integration with Keycloak/SpiceDB):

1. From repo root run:

```bash
docker compose up --build
```

2. Wait for the API to be available at http://localhost:8080.

3. Test the endpoints:

```bash
# List first 10 countries
curl -s "http://localhost:8080/api/v1/countries?page=1&size=10" | jq .

# Get a country by code
curl -s "http://localhost:8080/api/v1/countries/US" | jq .
```

Option B — Run API project only (fast local dev; Keycloak/SpiceDB not required for these public endpoints):

1. Change into the OrderProcessing project folder (where the ASP.NET project lives). Example:

```bash
cd OrderProcessing
```

2. Run the API locally on port 8080:

```bash
dotnet run --urls "http://localhost:8080"
```

3. Test with curl as above.

Notes: make sure OrderProcessing/Resources/countries.json is present in the build output (the project is set to embed or copy this file). If you modify the JSON resource, rebuild the project.

### Example error (ProblemDetails) — invalid code

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "Invalid country code format.",
  "instance": "/api/v1/countries/1",
  "extensions": {
    "correlationId": "a1b2c3d4-...",
    "errors": {
      "code": ["Code must be 2 or 3 alphabetic characters."]
    }
  }
}
```

### PR / QA requirements

- Branch from `main` and open a PR targeting `main`. Suggested branch name: `feature/SCRUM-6-create-countries-api` or `feature/SCRUM-6-<short-desc>`.
- Include a link to Jira ticket SCRUM-6 in the PR description (see: https://jira.company.local/browse/SCRUM-6).
- QA pipeline must run and pass before merge.
- Keep PR small and focused; include notes about the DI registration and the embedded JSON resource.

### Logging & error handling

- The system uses Microsoft.Extensions.Logging (ILogger<T>) for structured logs. The following events should be logged:
  - Startup: JSON load success or failure (include counts on success).
  - Validation failures (include parameter names/values but do not log PII).
  - Lookup misses (country code not found).
  - Unhandled exceptions (include stack trace and correlation id).
- All errors returned to clients use RFC7807 ProblemDetails with correlationId added to ProblemDetails.extensions.

---

CRITICAL: Match interface names, method signatures, and JSON shapes from technical contracts and already-generated files exactly.
Do NOT use reflection in controllers. Implement interfaces explicitly.
