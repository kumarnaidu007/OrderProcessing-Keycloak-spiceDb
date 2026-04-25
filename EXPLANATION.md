# Explanation (Design Decisions and Trade-offs)

## Key decisions

### 1) Asynchronous processing with a worker

Order creation only persists request data and queues a processing job. Inventory and payment simulation are handled by a background worker. This keeps API latency predictable and meets the requirement that order processing must not be synchronous.

### 2) Explicit processing job table

`OrderProcessingJobs` acts as a durable queue inside the database. It tracks retries, next retry time, and lock metadata. This avoids in-memory queue loss and allows recovery after process restarts.

### 3) Idempotency first

Idempotency keys and uniqueness constraints are used to protect against duplicate submissions and duplicate side effects. This is important for retry behavior and network/client replays.

The create-order path also handles uniqueness races deterministically: if two concurrent requests use the same idempotency key, the second request returns the already-created order instead of failing unexpectedly.

Idempotency guarantees are keyed (header-based) rather than payload-hash based. If the header is omitted, the server generates a new key and treats the request as a new order.

### 4) Event/history-oriented observability

Status history, domain events, and audit logs provide a trace of each order lifecycle. This simplifies debugging and validation during failure scenarios.

### 5) Explicit transition and compensation rules

Order transitions are validated against an allowed transition matrix to prevent illegal state moves. Payment failures are treated as transient until max payment attempts are reached; when final failure occurs, inventory is compensated using idempotent restore ledger entries.

### 5) Bootstrapped seed data

Startup seeding creates minimum required access control and demo catalog data:

- roles
- permissions
- admin user
- sample products

This allows a fresh clone to be usable quickly.

## Trade-offs

- Using a DB-backed queue is simpler than external brokers, but can be less scalable than dedicated messaging systems.
- Worker and API in one service reduce deployment complexity for this assignment, but production systems may split these concerns.
- `EnsureCreated` is convenient for assignment bootstrap; migrations are preferable for long-term schema evolution.

## Challenges

- Preventing duplicate side effects under retries/replays.
- Coordinating order status transitions with side-effect writes.
- Keeping inventory consistent when payment ultimately fails.
- Keeping the evaluator setup simple while still demonstrating reliability patterns.

## Improvements with more time

- switch bootstrap from `EnsureCreated` to versioned EF Core migrations
- add dead-letter handling and operational dashboards for stuck jobs
- separate worker into independent service for scale and fault isolation
- add richer integration tests for retry/idempotency edge cases
- externalize secrets and use environment-specific configuration profiles
