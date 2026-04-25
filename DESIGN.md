# Design and Data Model

## Why this structure

The design separates request-time writes from background processing to satisfy asynchronous order handling and reliability requirements:

- request path creates `Orders`, `OrderItems`, and a queued `OrderProcessingJobs` record
- worker path reads pending jobs and performs inventory + payment side effects
- explicit history/event tables make order lifecycle traceable

## Core schema

### Catalog

- `Products`
  - `ProductId` (PK)
  - `Name`, `Price`
  - `AvailableQuantity`, `ReservedQuantity`
  - `IsActive`, timestamps
  - `RowVersion` for optimistic concurrency

### Orders

- `Orders`
  - `OrderId` (PK)
  - `CustomerId`
  - `Status` (`Pending`, `Processing`, `Completed`, `Failed`, `Cancelled`)
  - `TotalAmount`, `Currency`
  - `IdempotencyKey` (unique)
  - timestamps and failure metadata
- `OrderItems`
  - `OrderItemId` (PK)
  - `OrderId` (FK), `ProductId` (FK)
  - `Quantity`, `UnitPrice`, `LineTotal`
- `OrderShippingSnapshots`
  - one row per order, frozen delivery fields used for tracking/history

### Processing and side effects

- `OrderProcessingJobs`
  - `JobId` (PK), `OrderId` (unique active job filter)
  - `JobStatus`, `RetryCount`, `MaxRetries`, `NextRetryAtUtc`
  - lock metadata for safe worker pickup
- `InventoryLedger`
  - immutable record of inventory movement
  - `IdempotencyKey` unique to prevent duplicate deductions
- `PaymentAttempts`
  - payment simulation attempts
  - unique idempotency constraint for no duplicate external side effects

### Auditing and lifecycle traceability

- `OrderStatusHistory` for state transitions over time
- `OrderDomainEvents` for significant domain events
- `AuditLog` for security-relevant actions

### Identity and access

- `Users`, `Customers`
- `Roles`, `Permissions`, `UserRoles`, `RolePermissions`
- `UserLoginOtps`, `UserSessions`

## Order lifecycle support

1. API accepts order and stores it as `Pending`.
2. API enqueues `OrderProcessingJobs` row.
3. Worker loads queued job and transitions to `Processing`.
4. Worker validates stock, updates inventory, simulates payment.
5. Worker finalizes order into `Completed` or `Failed`.
6. Status/history/events are written for visibility and debugging.

## Consistency guarantees

- uniqueness constraints (`Orders.IdempotencyKey`, ledger/payment idempotency keys) prevent duplicate effects
- worker processing writes are coordinated with order status updates
- inventory changes are represented in ledger for recoverability/auditability
- state transitions are constrained by application flow and persisted history

## Assumptions

- SQL Server is the persistence engine for this submission.
- Payment provider is simulated.
- Background worker runs in the same process for assignment simplicity.
