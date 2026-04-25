# Assumptions and Added Scope

## Assignment assumptions

- A SQL database is available (local SQL Server or Docker SQL Server).
- Order status model includes at least `Pending`, `Processing`, `Completed`, `Failed`, `Cancelled`.
- Payment is simulated, not integrated with a real gateway.
- Background processing can be implemented in-process for this assignment.

## Additional scope intentionally added

These items go beyond strict minimum API requirements but improve realism:

- OTP-based authentication flow
- JWT access tokens and refresh sessions
- role/permission based authorization
- customer address book
- order shipping snapshot table
- security-focused audit logs
- domain events and status history tables

## Data bootstrap assumptions

- Baseline seed data is required for a usable first run:
  - `Admin` + `Customer` roles
  - permission catalog + role mappings
  - default admin user
  - 4 sample products

- Operational transactional tables are not seeded:
  - orders and related processing rows are created by normal API/worker usage

## Docker assumptions

- Evaluator can run `docker compose up --build`.
- Port `8080` is available for API and `14333` for SQL Server host mapping.
- Demo password in compose is for assignment only and should be replaced in real deployments.
