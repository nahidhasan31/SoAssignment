# ADR-0002: Dynatrace as the observability platform

* Status: accepted
* Date: 2026-05-20
* Deciders: Lead Solutions Architect, Operations lead, Finance

## Context

The platform being built is the most capable of the options MaltaShop considered
and also the one with the most moving parts: a Kubernetes cluster, a service mesh,
GitOps reconciliation, a managed broker, five databases and seven services. That
is a great deal of surface for two operations staff who today have no dashboards at
all and learn about incidents from customers. The constraint is not the tooling
budget - this plan already accepts a larger one - it is attention.

## Decision

Adopt **Dynatrace**, deployed as the OneAgent operator on the cluster, as the single
platform for metrics, logs, traces and topology. Services additionally emit
OpenTelemetry spans, which Dynatrace ingests, so the instrumentation in the code
remains vendor-neutral even though the backend is not.

## Alternatives rejected

| Option | Why it was rejected |
|---|---|
| **Grafana Cloud (Loki, Mimir, Tempo)** | Open at every layer and the cheapest credible option, and the one to revisit if the Dynatrace invoice outgrows its value. Rejected because correlation across the four signals is assembled by the engineer, and the whole premise of this decision is to spend money to save the two operations staff that assembly work. |
| **Datadog** | Very close on capability and slightly cheaper at this size. Rejected narrowly: its Kubernetes and mesh topology mapping needed more configuration in the evaluation, and automatic root-cause analysis was the specific capability being bought. |
| **Self-hosted OpenTelemetry plus Prometheus and Jaeger** | No licence at all. Rejected for the same reason the broker is managed: it adds three components to operate in the year the team is already absorbing Kubernetes, Argo CD and Linkerd. |

## Criteria used

Automatic discovery of service topology and dependencies (weight 3), root-cause
analysis rather than dashboards to read (3), one platform for all four signals (3),
effort for two operations staff (3), neutral instrumentation (2), licence cost (1 -
deliberately the lowest weight in this plan).

## Consequences

**Good.** The service map draws itself from the mesh and the agent, so the "which
component is at fault?" question that made the March incident long is answered by
the product rather than by an engineer. Alerts arrive as problems with a probable
cause attached rather than as a metric crossing a line. OpenTelemetry spans mean the
backend can be replaced without touching service code.

**Bad.** It is by a distance the largest line in the tooling budget, and it is priced
per host-hour, so the autoscaling that protects checkout during a sale also raises
the invoice. The team becomes fluent in one vendor's query language and its notion
of a "problem", which is a skill that does not transfer.

**Debt accepted.** Log ingestion is capped to the seven services; the legacy
reporting jobs are left on their existing files until phase three. Davis's automatic
baselines need roughly four weeks of traffic before its anomaly detection is
trustworthy, so for the first month the team runs explicit threshold alerts
alongside it and must remember to retire them.
