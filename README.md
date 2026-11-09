# MaltaShop domain platform

A running slice of the platform-engineered MaltaShop described in the Unit 5
report: the **place-an-order** flow, coordinated by a **durable process manager**
written as a state machine, over a **message broker**, in **.NET 8**.

What distinguishes it:

1. **The saga is a persisted state machine, not procedural code.** MassTransit
   holds one row per order, correlates every message to it by order id, and locks
   that row while a message is handled, so two messages for one order cannot
   interleave.
2. **The framework owns the hard parts.** Retry, delayed redelivery, the error and
   dead-letter topology, the saga repository and the outbox are configuration, not
   code. What is left in `OrderStateMachine.cs` is the business sequence.
3. **The saga is queryable.** `GET /sagas/{orderId}` answers "where did this order
   stop, and what was undone?" from one row — the practical advantage this design
   claims over a choreography.

---

## What is in the system

| Container | Port (host) | Database | Responsibility |
|---|---|---|---|
| `order-service` | 9380 | `order-db` | REST entry point; owns the order; publishes `OrderSubmitted` and consumes the outcome |
| `orchestration-service` | 9384 | `saga-db` | The process manager. Owns the sequence and the compensation; holds no business rules |
| `inventory-service` | 9381 | `inventory-db` | Holds, releases and commits stock |
| `payment-service` | 9382 | `payment-db` | The only caller of the card provider; stores a reference, never a card number |
| `notification-service` | 9383 | `notification-db` | Subscribes to outcomes; nothing in checkout waits for it |
| `rabbitmq` | 9385 (UI) | – | The broker. Azure Service Bus in production — the same code, one configuration line |

Five services, five databases. No service connects to another's database.

### The process

```
POST /orders ──► order-service ──publish OrderSubmitted──► orchestration-service
                                                                │  state: Reserving
                                    send ReserveStock ──────────┤
                                                                ▼
                                                         inventory-service
                                                                │ holds stock
                                        publish StockReserved ──┘
                                                                │  state: Authorising
                                 send AuthorisePayment ─────────┤
                                                                ▼
                                                          payment-service
                                           ┌────────────────────┴────────────────┐
                                  PaymentAuthorised                      PaymentDeclined
                                           │                                     │
                                  state: Confirmed                      state: Compensating
                                  publish OrderConfirmed                send ReleaseStock
                                           │                                     │
                      ┌────────────────────┼──────────────┐              StockReleased
                      ▼                    ▼              ▼                      │
                order-service      notification     inventory               state: Cancelled
                 (CONFIRMED)         (e-mail)      (commit hold)          publish OrderCancelled
```

**Commands** (`ReserveStock`, `AuthorisePayment`, `ReleaseStock`) are *sent* to one
named queue and name an action. **Events** (`StockReserved`, `PaymentDeclined`,
`OrderConfirmed`, …) are *published* and name something that happened; three
independent services subscribe to `OrderConfirmed` and none of them knows about the
others.

---

## Running it

Requirements: Docker with Compose v2, plus `curl`, `jq` and `uuidgen`. The first
build pulls the .NET SDK image and restores NuGet packages, so allow a few minutes.

```bash
git clone <this repository>
cd maltashop-domain-platform
cp .env.example .env          # optional; the defaults work
docker compose up --build -d --wait
./scripts/smoke_test.sh
```

Stop and remove everything:

```bash
docker compose down -v
```

### Try it by hand

```bash
curl -sS -X POST http://localhost:9380/orders \
  -H 'content-type: application/json' \
  -H 'x-correlation-id: 5a2d0000-1111-2222-3333-444455556666' \
  -H 'idempotency-key: demo-001' \
  -d '{"customer_id":"MT-D-5520","items":[{"sku":"MICRO-25L","quantity":2},
                                          {"sku":"DISHW-60","quantity":1}]}' | jq

curl -sS http://localhost:9380/orders/<order_id> | jq
curl -sS http://localhost:9384/sagas/<order_id> | jq      # where the process reached
curl -sS http://localhost:9381/stock | jq
curl -sS http://localhost:9381/holds/<order_id> | jq
curl -sS "http://localhost:9383/notifications?orderId=<order_id>" | jq
```

The broker's management UI is at <http://localhost:9385> (`maltashop` /
`maltashop_dev_pw`), where the queues, the exchanges MassTransit creates per
message type, and the error queues are all visible.

Two SKUs make the failure paths visible:

* **`FRIDGE-XL`** costs €1,299, above the stub provider's €1,000 ceiling, so
  payment is declined, `ReleaseStock` is sent and the order ends `CANCELLED`;
* **`AWAIT-STOCK`** has no stock, so the saga stops at `Rejected_` and payment is
  never asked.

### Endpoints

| Method | Path | Service |
|---|---|---|
| `POST` | `/orders` | order — accepts `Idempotency-Key` and `X-Correlation-Id`; answers `201` with `state: PENDING` |
| `GET` | `/orders/{id}` | order — the order and its outcome |
| `GET` | `/sagas/{orderId}` | orchestration — current state, the path taken, the failure reason |
| `GET` | `/sagas` | orchestration — the fifty most recently updated |
| `GET` | `/stock`, `/holds/{orderId}` | inventory |
| `GET` | `/payments?orderId=` | payment |
| `GET` | `/notifications?orderId=` | notification |
| `GET` | `/health/live`, `/health/ready` | all |

---

## Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `SERVICE_NAME` | per container | The `service` field on every log line |
| `SERVICE_VERSION` | `2.1.0` | Stamped on logs, so a bad release stands out |
| `ENVIRONMENT` | `local` | `local`, `staging` or `production` |
| `PORT` | 8080–8084 | Port inside the container |
| `DATABASE_URL` | per service | That service's own PostgreSQL instance |
| `BROKER_HOST` | `rabbitmq` | Azure Service Bus in staging and production |
| `BROKER_USER`, `BROKER_PASSWORD` | dev values | Key Vault secrets in production |
| `CARD_CEILING_EUR` | `1000` | The stub provider's limit, used to show compensation |

In staging and production every value comes from Azure Key Vault through the
External Secrets Operator. No secret is committed.

---

## How consistency is kept without a distributed transaction

| Problem | Mechanism | Where |
|---|---|---|
| Two messages for one order arriving at once | **Pessimistic row lock** on the saga instance (`ConcurrencyMode.Pessimistic`) | `Program.cs` (orchestration) |
| A message for an order that does not exist | **Correlation by order id**; no instance, no effect | `OrderStateMachine.cs` |
| An event arriving in the wrong state | The state machine simply has no transition for it | `OrderStateMachine.cs` |
| The customer presses "Pay" twice | **Unique index** on `IdempotencyKey` | `OrderDbContext.cs` |
| A command redelivered to Inventory or Payment | The handler **answers with the decision already taken** | `Consumers.cs`, `AuthorisePaymentConsumer.cs` |
| A second charge for one order | **Unique index** on `Payments.OrderId` | `PaymentDbContext.cs` |
| Stock held for an order that then fails | **`ReleaseStock`**, sent by the `Compensating` state | `OrderStateMachine.cs` |
| A transient broker or database failure | **Retry**, then **delayed redelivery**, then the error queue | each `ReceiveEndpoint` |

## Tests

```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet test tests/MaltaShop.Tests/MaltaShop.Tests.csproj
./scripts/smoke_test.sh        # needs the stack running
./scripts/run_pipeline.sh      # every pipeline stage, locally
```

* **Unit** — the whole process manager against MassTransit's in-memory test
  harness: the happy path, the compensating path, the rejection path, and a stray
  event for an unknown order. No broker, no database, about two seconds.
* **Integration** — `scripts/smoke_test.sh` drives all five services through the
  real broker and five real databases, including both failure paths.

## Layout

```
azure-pipelines.yml      verify, build, integration, scan, promote
Dockerfile               one file, five images, selected by --build-arg PROJECT
src/MaltaShop.Contracts  the messages: the contract between services
src/MaltaShop.Shared     logging, correlation, health probes
src/MaltaShop.Order      REST entry point and the order aggregate
src/MaltaShop.Orchestration  the process manager and its database
src/MaltaShop.Inventory  hold, release and commit
src/MaltaShop.Payment    the provider adapter
src/MaltaShop.Notification  the outcome subscriber
tests/MaltaShop.Tests    saga unit tests on the in-memory harness
scripts/                 smoke test, local pipeline, promotion and rollout
docs/adr/                architecture decision records
evidence/                screenshots and transcripts of the demonstration
diagrams/                the report's figures
report/                  the build pipeline for the Unit 5 report
```
