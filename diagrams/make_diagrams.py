#!/usr/bin/env python3
"""Draws every figure in the report as an SVG in diagrams/src/."""
from svgkit import (OUT, svg, text, text_lines, rect, box, arrow, polyline, line, cylinder, panel,
                    INK, GREY, LIGHT, WHITE, ACCENT, AMBER, AMBER_FILL, BLUE, BLUE_FILL,
                    GREEN, GREEN_FILL, GOLD, GOLD_FILL)

W = 880


def write(name, width, height, body):
    (OUT / f"{name}.svg").write_text(svg(width, height, body))
    print(f"  {name}.svg  {width}x{height}")


def band(y, title, hh=24, x=24, w=832, fill=INK):
    return "\n".join([f'<rect x="{x}" y="{y}" width="{w}" height="{hh}" fill="{fill}"/>',
                      text(x + w / 2, y + hh - 7, title, size=12.5, bold=True, colour=WHITE)])


# ------------------------------------------------------------------ Figure 1
def architecture():
    h = 672
    p = [rect(0, 0, W, h, WHITE, WHITE, 0)]

    for i, (name, sub) in enumerate((("Web storefront", "browser"), ("Mobile app", "iOS / Android"),
                                     ("30 partner retailers", "server to server"))):
        x = 24 + i * 282
        p.append(panel(x, 14, 264, 56, name, [sub], stroke=INK, header=22, title_size=12.5, size=11))
        p.append(arrow(x + 132, 70, 150 + i * 290, 96, "ink", width=1.6))

    p.append(panel(24, 96, 832, 60, "Ingress — NGINX gateway inside the mesh",
                   ["TLS at the edge · OAuth2 for customers · one client credential and quota per retailer "
                    "· every hop beyond this point carries mutual TLS"],
                   stroke=BLUE, header_fill=BLUE, header=24, title_size=13.5, size=11))

    # the seven services, in two rows
    p.append(band(176, "Bounded contexts — seven services, seven databases, one owning team each", fill=INK))
    for i, (name, store, critical) in enumerate((("Order", "orders", True), ("Orchestration", "saga state", True),
                                                 ("Inventory", "stock · holds", True), ("Payment", "payments", True))):
        x = 24 + i * 210
        stroke = ACCENT if critical else INK
        p.append(panel(x, 208, 194, 86, name, [], stroke=stroke, header_fill=stroke, header=24, title_size=13))
        p.append(cylinder(x + 16, 244, colour=stroke))
        p.append(text(x + 110, 261, store, size=11.5, anchor="start"))
        p.append(text(x + 97, 284, "PostgreSQL Flexible Server", size=9.5, colour=GREY))
    for i, (name, store) in enumerate((("Catalogue", "products"), ("Customer", "profiles (GDPR)"),
                                       ("Notification", "message log"))):
        x = 24 + i * 210
        p.append(panel(x, 306, 194, 76, name, [], stroke=INK, header=22, title_size=12.5))
        p.append(cylinder(x + 16, 338, colour=INK))
        p.append(text(x + 110, 355, store, size=11.5, anchor="start"))
    p.append(panel(654, 306, 202, 76, "Recommendation", ["Python; reads the CDC feed,", "never the checkout path"],
                   stroke=GREEN, header_fill=GREEN, header=22, title_size=12.5, size=10.5))
    p.append(arrow(440, 156, 440, 174, "ink", width=1.6))

    # every service above publishes to and subscribes from the bus
    p.append(arrow(286, 384, 286, 406, "gold", both=True, width=1.8))
    p.append(text(300, 400, "every service publishes and subscribes", size=10, italic=True,
                  colour=GREY, anchor="start"))

    p.append(panel(24, 408, 520, 84, "Azure Service Bus",
                   ["queues for commands, named for the endpoint that handles them",
                    "topics with one subscription per service for events",
                    "duplicate detection (10 min) · dead-letter per subscription · sessions off"],
                   stroke=GOLD, header_fill=GOLD, header=24, title_size=13.5, size=10.5))

    p.append(panel(568, 408, 288, 84, "Debezium change data capture",
                   ["reads the PostgreSQL write-ahead log,", "not the application, so the analytics",
                    "and AI feed costs the services nothing"],
                   stroke=BLUE, header_fill=BLUE, header=24, title_size=13, size=10))
    p.append(polyline([(856, 350), (868, 350), (868, 450), (854, 450)], "blue", dashed=True, width=1.3))

    # platform
    p.append(band(516, "Platform — Azure Kubernetes Service, from month one", fill=BLUE))
    for i, (name, rows) in enumerate((
            ("Linkerd mesh", ["mutual TLS, retries, timeouts,", "circuit breaking, traffic split"]),
            ("Argo CD", ["the repository is the desired", "state; drift is reconciled"]),
            ("Argo Rollouts", ["blue-green with an automated", "analysis gate (Figure 2)"]),
            ("Dynatrace", ["OneAgent operator; topology", "discovered, not configured"]))):
        x = 24 + i * 210
        p.append(rect(x, 548, 194, 70, WHITE, BLUE, 1.4))
        p.append(text(x + 97, 570, name, size=12.5, bold=True, colour=BLUE))
        p.append(text_lines(x + 97, 588, rows, size=10, gap=13))

    p.append(text(440, 646, "Plain arrow = synchronous call inside the mesh   ·   double arrow = publish "
                            "and subscribe   ·   every service is a Deployment", size=10.5,
                  italic=True, colour=GREY))
    write("architecture", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 2
def pipeline():
    h = 500
    p = [rect(0, 0, W, h, WHITE, WHITE, 0)]
    p.append(band(16, "Azure DevOps — the pipeline writes desired state; it never touches the cluster"))

    stages = [("Verify", ["dotnet format", "saga unit tests", "coverage gate"]),
              ("Build", ["five images, one file", "pushed by digest", "SBOM + provenance"]),
              ("Integration", ["compose up --wait", "both failure paths"]),
              ("Scan", ["Trivy HIGH/CRITICAL", "vulnerable NuGet"]),
              ("Promote", ["digests written to the", "environment repository"])]
    for i, (name, rows) in enumerate(stages):
        x = 24 + i * 170
        stroke = ACCENT if name == "Scan" else INK
        p.append(panel(x, 56, 152, 82, name, rows, stroke=stroke, header_fill=stroke,
                       header=22, title_size=12.5, size=10))
        if i:
            p.append(arrow(x - 18, 97, x - 2, 97, "ink", width=1.4))

    p.append(text(440, 162, "the five images built once are the only ones ever deployed; "
                            "every later stage names them by digest", size=11, italic=True, colour=ACCENT))

    p.append(arrow(780, 138, 780, 180, "ink", width=1.4))
    p.append(panel(600, 182, 256, 62, "Argo CD", ["sees the new digests and reconciles",
                                                  "the cluster to match the repository"],
                   stroke=BLUE, header_fill=BLUE, header=22, title_size=12.5, size=10.5))

    p.append(band(262, "Blue-green for Payment, with an automated analysis gate", fill=ACCENT))

    p.append(rect(24, 300, 832, 126, LIGHT, INK, 1.4))
    p.append(panel(48, 318, 150, 54, "active", ["version N"], stroke=BLUE, header_fill=BLUE,
                   header=20, title_size=12, size=10.5))
    p.append(panel(238, 318, 150, 54, "preview", ["version N+1"], stroke=GREEN, header_fill=GREEN,
                   header=20, title_size=12, size=10.5))
    p.append(arrow(200, 345, 234, 345, "ink", width=1.4))
    p.append(panel(428, 318, 190, 90, "AnalysisRun", ["30 synthetic orders against",
                                                      "the preview service; p99 and",
                                                      "error rate from Dynatrace"],
                   stroke=INK, header=20, title_size=12, size=10))
    p.append(arrow(390, 345, 424, 345, "ink", width=1.4))
    p.append(panel(658, 318, 174, 44, "promote", ["one atomic switch"], stroke=GREEN,
                   header_fill=GREEN, header=20, title_size=12, size=10.5))
    p.append(panel(658, 372, 174, 44, "abort", ["the same switch, reversed"], stroke=ACCENT,
                   header_fill=ACCENT, header=20, title_size=12, size=10))
    p.append(arrow(620, 336, 654, 336, "green", width=1.4))
    p.append(arrow(620, 390, 654, 390, "accent", width=1.4))

    p.append(text(440, 450, "Canary was rejected for Payment: at roughly 40 payments an hour a five per cent "
                            "slice is three orders, which proves nothing.", size=11, italic=True, colour=GREY))
    p.append(text(440, 468, "Continuous delivery, not deployment: the analysis gate decides, and a release "
                            "manager approves the environment.", size=11, italic=True, colour=GREY))
    p.append(text(440, 490, "Change failure rate 31% → under 15%   ·   mean time to restore 112 min → under 20 min",
                  size=12, bold=True))
    write("pipeline", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 3
def observability():
    h = 470
    p = [rect(0, 0, W, h, WHITE, WHITE, 0)]
    p.append(band(16, "Telemetry — discovered by the platform, not configured by the team"))

    for i, name in enumerate(("Order", "Orchestration", "Inventory", "Payment", "Notification")):
        x = 24 + i * 170
        p.append(panel(x, 56, 152, 50, name, ["OpenTelemetry spans"], stroke=INK, header=20,
                       title_size=12, size=10))
        p.append(arrow(x + 76, 106, 440, 142, "ink", width=1.2))

    p.append(panel(176, 144, 528, 56, "Dynatrace OneAgent operator",
                   ["injected into every pod; discovers the service map from the mesh and the kubelet,",
                    "so the dependency graph is observed rather than drawn"],
                   stroke=BLUE, header_fill=BLUE, header=22, title_size=13, size=10))

    p.append(panel(176, 212, 528, 58, "Davis — causal analysis",
                   ["one problem with a probable cause, not four dashboards to correlate by hand"],
                   stroke=GOLD, header_fill=GOLD, header=22, title_size=13, size=10.5))
    p.append(arrow(440, 200, 440, 210, "ink", width=1.2))

    p.append(band(286, "Two objectives, because availability and speed fail differently", fill=ACCENT))

    p.append(panel(24, 326, 404, 122, "SLO 1 — checkout availability",
                   ["99.9% of POST /orders answer 2xx, over 90 days",
                    "error budget: 2 h 9 min per quarter",
                    "",
                    "burn 14.4× over 5 min → severity 1, paged",
                    "burn 6× over 1 h → severity 2, same day"],
                   stroke=ACCENT, header_fill=ACCENT, header=24, title_size=13, size=10.5))

    p.append(panel(452, 326, 404, 122, "SLO 2 — checkout latency",
                   ["p99 of POST /orders ≤ 2,000 ms, over 90 days",
                    "measured at the ingress, not in the service",
                    "",
                    "breach for 15 min → severity 2",
                    "first runbook step: compare p99 by version"],
                   stroke=BLUE, header_fill=BLUE, header=24, title_size=13, size=10.5))
    write("observability", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 4
def saga():
    h = 560
    p = [rect(0, 0, W, h, WHITE, WHITE, 0)]
    p.append(band(16, "The place-order process manager, as declared in OrderStateMachine.cs"))

    def node(x, y, w, name, rows, stroke=INK, fill=WHITE):
        return panel(x, y, w, 58, name, rows, stroke=stroke, header_fill=stroke, fill=fill,
                     header=22, title_size=12.5, size=10)

    def hop(x1, x2, y, lines, colour="ink"):
        mid = (x1 + x2) / 2
        tint = {"ink": GREY, "accent": ACCENT}[colour]
        return "\n".join([arrow(x1, y, x2, y, colour, width=1.4),
                           text_lines(mid, y - 18, lines, size=8.5, gap=10, colour=tint)])

    p.append(node(24, 68, 150, "Initially", ["OrderSubmitted"], stroke=GREY))
    p.append(node(236, 68, 150, "Reserving", ["send ReserveStock"], stroke=ACCENT))
    p.append(node(448, 68, 150, "Authorising", ["send AuthorisePayment"], stroke=ACCENT))
    p.append(node(660, 68, 196, "Confirmed", ["publish OrderConfirmed"], stroke=GREEN))
    p.append(hop(176, 234, 97, ["Order", "Submitted"]))
    p.append(hop(388, 446, 97, ["Stock", "Reserved"]))
    p.append(hop(600, 658, 97, ["Payment", "Authorised"]))

    p.append(node(448, 176, 150, "Compensating", ["send ReleaseStock"], stroke=ACCENT, fill="#fbeef0"))
    p.append(arrow(523, 126, 523, 174, "accent", width=1.4))
    p.append(text(533, 154, "PaymentDeclined", size=9, colour=ACCENT, anchor="start"))

    p.append(node(660, 176, 196, "Cancelled", ["publish OrderCancelled"], stroke=ACCENT))
    p.append(hop(600, 658, 205, ["Stock", "Released"], "accent"))

    p.append(node(236, 176, 150, "Rejected", ["publish OrderCancelled"], stroke=ACCENT))
    p.append(arrow(311, 126, 311, 174, "accent", width=1.4))
    p.append(text(321, 154, "StockRejected", size=9, colour=ACCENT, anchor="start"))

    p.append(band(252, "What the framework supplies, so that the file above holds only the sequence",
                  fill=GOLD))
    rows = [("Correlation", "every message carries the order id; MassTransit finds the one instance, or none"),
            ("Concurrency", "the instance row is locked while a message is handled, so two cannot interleave"),
            ("Persistence", "Entity Framework saga repository on the orchestration database"),
            ("Resilience", "retry (3 × 2 s), delayed redelivery (10 s, 30 s), then the error queue"),
            ("Commands vs events", "Send names one endpoint; Publish fans out to every subscription"),
            ("Testing", "an in-memory harness runs every branch of this diagram in about two seconds")]
    for i, (name, detail) in enumerate(rows):
        y = 292 + i * 30
        p.append(rect(24, y, 180, 26, LIGHT, INK, 1.0))
        p.append(text(114, y + 18, name, size=11, bold=True))
        p.append(rect(204, y, 652, 26, WHITE, INK, 1.0))
        p.append(text(216, y + 18, detail, size=10.5, anchor="start"))

    p.append(text(440, 500, "Nothing is rolled back. ReleaseStock is an ordinary command whose handler is "
                            "safe to run twice and safe to run", size=11, italic=True, colour=GREY))
    p.append(text(440, 518, "when nothing was ever held — which is what makes the compensating path "
                            "as reliable as the happy one.", size=11, italic=True, colour=GREY))
    p.append(text(440, 544, "Finished instances are kept, so support can read the whole path from one row.",
                  size=11, italic=True, colour=GREY))
    write("saga", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 5
def prototype():
    h = 452
    p = [rect(0, 0, W, h, WHITE, WHITE, 0)]
    p.append(band(16, "docker compose up --build -d --wait   ·   eleven containers"))

    for i, (name, port, critical) in enumerate((("order", ":9380", True), ("orchestration", ":9384", True),
                                                ("inventory", ":9381", True), ("payment", ":9382", True),
                                                ("notification", ":9383", False))):
        x = 24 + i * 170
        stroke = ACCENT if critical else INK
        p.append(panel(x, 56, 152, 58, name, [f"{port} · .NET 8", "non-root, aspnet-alpine"],
                       stroke=stroke, header_fill=stroke, header=22, title_size=12, size=9.5))
        p.append(arrow(x + 52, 114, x + 52, 142, "ink", both=True, width=1.2))
        p.append(rect(x + 6, 142, 108, 34, LIGHT, INK, 1.0))
        p.append(cylinder(x + 14, 149))
        p.append(text(x + 76, 164, "PostgreSQL", size=10))
        p.append(polyline([(x + 124, 114), (x + 124, 196)], "gold", end_arrow=True, width=1.3))
        p.append(polyline([(x + 132, 196), (x + 132, 114)], "gold", end_arrow=True, width=1.3))

    p.append(panel(24, 196, 832, 58, "RabbitMQ 3.13 — the transport, locally",
                   ["six endpoint queues, one error queue each, and a fanout exchange per message type",
                    "Azure Service Bus in production: the same code, one configuration line"],
                   stroke=GOLD, header_fill=GOLD, header=22, title_size=13, size=10.5))

    p.append(panel(24, 278, 404, 142, "what the prototype proves",
                   ["checkout answers in ~70 ms and keeps answering while",
                    "the notification service is stopped",
                    "the saga's row shows the exact path an order took",
                    "a declined card sends ReleaseStock and the hold returns",
                    "one correlation ID appears in all five services",
                    "the hold becomes a deduction only once the order is confirmed"],
                   stroke=GREEN, header_fill=GREEN, header=24, title_size=13, size=10))

    p.append(panel(452, 278, 404, 142, "what it does not prove",
                   ["no scheduled timeout: a worker that never answers leaves",
                    "the saga open, which production fixes with Schedule",
                    "RabbitMQ locally, Azure Service Bus in production",
                    "no mesh, no ingress, no mutual TLS, no Argo CD",
                    "no Debezium, so the CDC feed is designed but untested",
                    "no load test: 70 ms is one request on an idle machine"],
                   stroke=ACCENT, header_fill=ACCENT, header=24, title_size=13, size=10))
    write("prototype", W, h, "\n".join(p))


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    architecture()
    pipeline()
    observability()
    saga()
    prototype()
