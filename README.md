# Payment Gateway Challenge

This project is a lightweight .NET 8 payment gateway API that validates merchant payment requests, calls a simulated acquiring bank, stores the outcome, and exposes a retrieval endpoint for payment status and masked card details. It is designed as a clean demo of the core payment flow without introducing enterprise infrastructure overhead.

The codebase keeps a simple structure for a challenge project while still applying clear separation of concerns: interfaces live under the interfaces layer, validation is isolated in a dedicated validator, and the orchestration logic remains focused in the payment service.

## Run locally

1. Start the bank simulator from the repository root:

```powershell
docker-compose up -d
```

2. Start the API in a second terminal:

```powershell
dotnet run --project src/PaymentGateway.Api
```

3. Open Swagger in a browser:

```text
https://localhost:7092/swagger
```

If you are running with a different local profile, the launch settings file defines the exact port used by the project.

4. Run the test suite:

```powershell
dotnet test PaymentGateway.sln --nologo
```

## API contract

### Create payment

`POST /api/payments`

```json
{
  "cardNumber": "4111111111111111",
  "expiryMonth": 12,
  "expiryYear": 2099,
  "currency": "GBP",
  "amount": 150,
  "cvv": "123",
  "idempotencyKey": "client-generated-unique-key"
}
```

### Retrieve payment

`GET /api/payments/{id}`

The create response returns the payment identifier, which is then used to fetch the stored payment record.

## Key design choices and assumptions

### Architecture and simplicity

This solution intentionally stays compact and pragmatic:

- controller handles HTTP concerns
- interfaces declare the service and bank contracts
- validation is isolated in a dedicated request validator
- service handles idempotency checks and orchestration
- bank client owns outbound HTTP to the simulator
- repository stores payment state in memory

This keeps the code easy to reason about while still using a cleaner structure than a single monolithic service class. The result is a small but more maintainable design that remains aligned with the challenge scope.

### State storage

The payment repository is intentionally in-memory and uses `ConcurrentDictionary` for thread-safe access. This matches the exercise brief and keeps the project focused on the payment flow rather than adding a database layer or persistence framework. The repository is behind an interface so the implementation can be swapped cleanly later without changing the service contract.

### Data protection and masking

Security is treated as a practical concern, not a slogan:

- only the last four digits of the card are returned to the client
- CVV is never persisted or returned in retrieval payloads
- CVV is not logged or retained after the bank call
- sensitive values are minimized at every layer

This is aligned with the expected PCI-conscious handling for a simplified demo system.

### Bank simulator integration

The app treats the Mountebank simulator as the external acquiring bank. It validates request shape and then sends the required payload:

```json
{
  "card_number": "4111111111111111",
  "expiry_date": "12/2099",
  "currency": "GBP",
  "amount": 150,
  "cvv": "123"
}
```

The simulator contract is intentionally deterministic:

- odd last digit -> authorized
- even last digit -> declined
- last digit `0` -> bank failure (`503`)

The gateway maps upstream failures into meaningful HTTP responses without crashing the service. Validation errors are rejected before the bank call is made.

## Validation rules

The implementation enforces the required business checks:

- card number: required, 14–19 digits only
- expiry month: 1–12
- expiry year: required and must be valid for the chosen month
- currency: exactly 3 chars, case-insensitive, limited to supported ISO codes (`GBP`, `EUR`, `USD`)
- amount: positive integer in minor units
- CVV: required, 3–4 digits, keeps leading zeroes as string data

If validation fails, the request is rejected with `400 Bad Request` and no bank call is made.

## Idempotency

The service supports optional idempotency keys for safe retries:

- same key + same payload -> returns the original payment
- same key + different payload -> `409 Conflict`
- no key -> no replay protection is applied

This is sufficient for the challenge and keeps the logic explicit without introducing infrastructure-heavy distributed idempotency patterns.

## Future production enhancements

In a real production environment, the next steps would be:

- persistent storage with migrations and durable persistence for payments and idempotency records
- distributed idempotency protection for multi-instance deployments
- retries and circuit breakers using a library such as Polly
- telemetry, structured logs, and distributed tracing for downstream bank failures and operational diagnostics

## Scope and limitations

This is a challenge implementation, not a production payment platform. It intentionally uses in-memory state, a simulated bank dependency, and a compact service design. It does not include production-grade persistence, distributed state, or full payment reconciliation.
