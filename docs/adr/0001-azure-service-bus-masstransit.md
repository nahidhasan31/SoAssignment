# ADR-0001: Azure Service Bus, behind MassTransit

* Status: accepted
* Date: 2026-05-06
* Deciders: Lead Solutions Architect, Platform lead, Operations lead

## Context

Seven services need commands delivered to one handler and events delivered to
every interested subscriber, with the saga's state surviving a restart and two
messages for one order never interleaving. The platform is being built on Azure
Kubernetes Service, the team's language is C#, and the business wants the migration
to be reversible: whatever is chosen must not be the reason a later decision is
impossible.

## Decision

Use **Azure Service Bus** as the production transport — topics with per-service
subscriptions for events, queues for commands, sessions disabled, duplicate
detection on a ten-minute window — accessed exclusively through **MassTransit**,
which provides the saga state machine, the Entity Framework saga repository, the
transactional outbox, retry and redelivery policies, and the error and dead-letter
topology. The prototype runs the same code against **RabbitMQ**, because the
transport is a configuration line.

## Alternatives rejected

| Option | Why it was rejected |
|---|---|
| **Azure Event Hubs (Kafka-compatible)** | The right shape for the browsing stream and for replay, and it will be adopted for exactly that in phase two. Rejected as the primary transport because partitioned log semantics make competing consumers, per-message retry and dead-lettering - which the saga needs on every hop - something the application has to build. |
| **RabbitMQ, self-managed on the cluster** | What the prototype runs, and the cheapest licence. Rejected for production because a quorum-queue cluster, its upgrades and its disk alarms become the platform team's problem on the same day they take on Kubernetes, Argo CD and a mesh. |
| **Raw Azure Service Bus SDK, no framework** | One fewer dependency and complete control. Rejected because it means writing the saga repository, the outbox, the retry policy and the dead-letter handling by hand - roughly the four things most likely to be got subtly wrong, and the four that MassTransit has already had tested by everyone else. |
| **Dapr pub/sub** | Attractive on Kubernetes and genuinely portable, but the saga would still be hand-written, and a sidecar per pod is a second runtime to reason about alongside the mesh's. |

## Criteria used

Durable saga state with concurrency control (weight 3), commands and events in one
model (3), dead-lettering and redelivery without writing them (3), managed rather
than operated (3), replaceability of the transport (2), the team's existing skills (2).

## Consequences

**Good.** The place-order process is a readable state machine rather than a switch
statement, and every branch of it - including the compensating one - is unit tested
in milliseconds against an in-memory harness. The saga row is queryable, so support
can answer "where did this order stop?" from one place. Because MassTransit
abstracts the transport, the prototype's RabbitMQ and production's Service Bus run
identical code, and a move to a third transport is a configuration change.

**Bad.** MassTransit is a substantial dependency with opinions, and its conventions
(endpoint naming, exchange topology, message-type routing) must be learned before
anything subtle can be debugged. Service Bus Standard has a per-operation cost and
a 256 KB message limit, which rules out carrying anything large on the bus.

**Debt accepted.** The prototype has no scheduled timeout: a worker that never
answers leaves the saga in `Reserving` or `Authorising` for ever. Production uses
Service Bus scheduled messages through MassTransit's `Schedule`, and that is the
first thing to add. The prototype also uses pessimistic row locking for saga
concurrency rather than optimistic concurrency, which is simpler but serialises
updates to one order.
