#!/usr/bin/env python3
"""Draws the report's figures as engineering drawings.

The line carries the meaning: weight says how important the path is, pattern says
what travels along it. Fills appear only on things MaltaShop does not run.
"""
from svgkit import (OUT, svg, text, lines, box, unit, line, run, on_line, rule_h, rule_v,
                    store, title_block, legend, INK, RULE, QUIET, SIGNAL, FAULT, WASH, WHITE,
                    HEAVY, LIGHT, HAIR, ASYNC, OBSERVE)

W = 880


def write(name, width, height, body):
    (OUT / f"{name}.svg").write_text(svg(width, height, body))
    print(f"  {name}.svg  {width}x{height}")


# ------------------------------------------------------------------ Figure 1
def architecture():
    h = 612
    p = []

    for i, (name, who) in enumerate((("Web storefront", "customers"), ("Mobile app", "customers"),
                                     ("Partner API", "30 retailers"))):
        x = 110 + i * 240
        p.append(text(x, 24, name, size=12.5, weight="600", slab=True))
        p.append(text(x, 39, who, size=10, colour=QUIET))
        p.append(line(x, 48, x, 70, "rule", HAIR))

    p.append(unit(24, 74, 832, 44, "Ingress", (), stroke=INK, width=LIGHT))
    p.append(text(440, 106, "TLS, OAuth2 for customers, one client credential and quota per retailer",
                  size=10.5, colour=QUIET))
    p.append(text(866, 96, "mesh boundary", size=9, anchor="end", colour=SIGNAL))
    p.append(rule_h(24, 130, 856, SIGNAL, HAIR))
    p.append(line(440, 118, 440, 150, "ink", HEAVY))

    # the checkout path, drawn heavy
    checkout = [("Order", "orders", 24), ("Orchestration", "saga state", 232),
                ("Inventory", "stock, holds", 440), ("Payment", "payments", 648)]
    for name, held, x in checkout:
        p.append(unit(x, 156, 192, 72, name, (), stroke=INK, width=HEAVY))
        p.append(store(x + 16, 196, held))
    p.append(line(216, 178, 230, 178, "ink", HEAVY))
    p.append(text(440, 244, "the checkout path, drawn heavy", size=9.5, colour=QUIET, italic=True))

    supporting = [("Catalogue", "products", 24), ("Customer", "profiles", 232),
                  ("Notification", "message log", 440)]
    for name, held, x in supporting:
        p.append(unit(x, 258, 192, 64, name, (), stroke=INK, width=LIGHT))
        p.append(store(x + 16, 294, held))
    p.append(unit(648, 258, 208, 64, "Recommendation", ("reads the capture feed",),
                  stroke=INK, width=LIGHT, fill=WASH))

    # the broker: the one signal colour in the drawing. Both runs sit in the
    # gutters between units, so no line crosses a box it does not touch.
    p.append(line(224, 228, 224, 356, "signal", LIGHT, ASYNC))
    p.append(line(640, 228, 640, 356, "signal", LIGHT, ASYNC))
    p.append(text(232, 344, "every service publishes and subscribes",
                  size=9, anchor="start", colour=QUIET, italic=True))
    p.append(unit(24, 360, 520, 64, "Azure Service Bus",
                  ("queues for commands, topics with one subscription per service for events",
                   "duplicate detection over ten minutes, dead-letter per subscription"),
                  stroke=SIGNAL, width=LIGHT))

    p.append(unit(576, 360, 280, 64, "Debezium",
                  ("reads the write-ahead log, not the service,",
                   "so the analytics feed costs checkout nothing"),
                  stroke=INK, width=LIGHT, fill=WASH))
    p.append(line(576, 392, 548, 392, "ink", HAIR, OBSERVE))
    p.append(text(544, 396, "from the Order and Catalogue write-ahead logs",
                  size=9, anchor="end", colour=QUIET))
    p.append(line(752, 360, 752, 326, "ink", HAIR, OBSERVE))

    p.append(rule_h(24, 452, 856))
    p.append(text(24, 474, "Azure Kubernetes Service, from month one", size=12,
                  anchor="start", weight="600", slab=True))
    for i, (name, detail) in enumerate((("Linkerd", "mutual TLS on every hop inside the boundary"),
                                        ("Argo CD", "the repository is the desired state"),
                                        ("Argo Rollouts", "blue-green, gated by analysis"),
                                        ("Dynatrace", "topology discovered, not configured"))):
        x = 24 + i * 212
        p.append(text(x, 496, name, size=11, anchor="start", weight="600"))
        p.append(text(x, 511, detail, size=9.5, anchor="start", colour=QUIET))

    p.append(legend(24, 540, [
        (HEAVY, None, "ink", "the checkout path"),
        (LIGHT, None, "ink", "a synchronous call"),
        (LIGHT, ASYNC, "signal", "a message on the broker"),
        (HAIR, OBSERVE, "ink", "an observation; the service is not involved"),
    ]))
    p.append(title_block(560, 540, 296, "1", "Platform after twelve months", "C"))
    write("architecture", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 2
def pipeline():
    h = 500
    p = []

    stages = [("Verify", ("format, analysers", "saga unit tests")),
              ("Build", ("five images, one file", "pushed by digest")),
              ("Integration", ("compose up", "both failure paths")),
              ("Scan", ("images, packages", "blocks the release")),
              ("Promote", ("digests written to the", "environment repository"))]
    for i, (name, rows) in enumerate(stages):
        x = 24 + i * 170
        stroke = FAULT if name == "Scan" else INK
        p.append(unit(x, 24, 152, 74, name, rows, stroke=stroke, width=LIGHT))
        if i:
            p.append(line(x - 18, 61, x - 4, 61, "ink", LIGHT))

    p.append(line(780, 98, 780, 128, "ink", LIGHT))
    p.append(unit(600, 132, 256, 56, "Argo CD",
                  ("reconciles the cluster to the repository",), stroke=INK, width=LIGHT))
    p.append(text(588, 160, "the pipeline holds no cluster credentials at all",
                  size=10, anchor="end", colour=QUIET, italic=True))

    p.append(rule_h(24, 212, 856))
    p.append(text(24, 238, "Releasing Payment", size=13, anchor="start", weight="600", slab=True))
    p.append(text(24, 256, "a complete preview, proved before any customer reaches it",
                  size=10.5, anchor="start", colour=QUIET))

    p.append(unit(24, 274, 168, 54, "active", ("version N, taking traffic",), stroke=INK, width=HEAVY))
    p.append(unit(222, 274, 168, 54, "preview", ("version N+1, taking none",), stroke=INK, width=LIGHT))
    p.append(text(207, 348, "two complete environments, side by side",
                  size=9.5, colour=QUIET, italic=True))
    p.append(line(390, 301, 416, 301, "ink", LIGHT))
    p.append(on_line(390, 301, 416, 301, "driven", QUIET, 8.5, -8))
    p.append(unit(420, 262, 196, 78, "AnalysisRun",
                  ("thirty synthetic orders,", "then p99 and error rate", "from Dynatrace"),
                  stroke=INK, width=LIGHT))

    p.append(line(616, 282, 646, 282, "ink", LIGHT))
    p.append(unit(650, 262, 206, 36, "promote", (), stroke=INK, width=HEAVY))
    p.append(on_line(616, 282, 646, 282, "pass", INK))
    p.append(line(616, 322, 646, 322, "fault", LIGHT))
    p.append(unit(650, 304, 206, 36, "abort", (), stroke=FAULT, width=LIGHT))
    p.append(on_line(616, 322, 646, 322, "fail", FAULT))
    p.append(text(856, 356, "one atomic switch, and the same switch reversed",
                  size=10, anchor="end", colour=QUIET))

    p.append(rule_h(24, 384, 856))
    p.append(lines(24, 408, [
        "Canary was rejected here: at about forty payments an hour a ten per cent slice is four orders,",
        "which is not a sample. Blue-green proves the new version against thirty orders instead.",
    ], size=11, anchor="start", colour=INK, gap=19))
    p.append(text(24, 458, "Change failure rate 31 per cent to under 15. Mean time to restore "
                           "112 minutes to under 20.", size=11, anchor="start", colour=QUIET))
    p.append(title_block(560, 452, 296, "2", "Delivery and the Payment release", "C"))
    write("pipeline", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 3
def observability():
    h = 470
    p = []

    for i, name in enumerate(("Order", "Orchestration", "Inventory", "Payment", "Notification")):
        x = 70 + i * 172
        p.append(text(x, 26, name, size=11.5, weight="600", slab=True))
        p.append(line(x, 36, 440, 78, "ink", HAIR, OBSERVE))
    p.append(text(24, 62, "OpenTelemetry spans, so the instrumentation outlives the vendor",
                  size=10, anchor="start", colour=QUIET, italic=True))

    p.append(unit(216, 82, 448, 50, "OneAgent operator",
                  ("injected into every pod; the service map is discovered from the mesh",),
                  stroke=INK, width=LIGHT))
    p.append(line(440, 132, 440, 154, "ink", LIGHT))
    p.append(unit(216, 158, 448, 50, "Davis",
                  ("one problem with a probable cause, not four dashboards to correlate",),
                  stroke=SIGNAL, width=LIGHT))

    p.append(rule_h(24, 240, 856))
    p.append(text(24, 266, "Two objectives, because availability and speed fail differently",
                  size=12.5, anchor="start", weight="600", slab=True))

    p.append(unit(24, 286, 404, 128, "Availability", (), stroke=INK, width=LIGHT))
    p.append(lines(226, 324, [
        "99.9 per cent of checkout answers 2xx, over ninety days",
        "an error budget of 2 hours 9 minutes a quarter",
        "",
        "burning it 14.4 times too fast for five minutes pages",
        "the on-call engineer; six times for an hour reaches Slack",
    ], size=10.5, colour=QUIET, gap=16))

    p.append(unit(452, 286, 404, 128, "Latency", (), stroke=INK, width=LIGHT))
    p.append(lines(654, 324, [
        "the 99th percentile answers in 2,000 ms or less",
        "measured at the ingress, not inside the service",
        "",
        "the case's own failure was latency, not availability:",
        "checkout queued long before any request actually failed",
    ], size=10.5, colour=QUIET, gap=16))

    p.append(title_block(560, 424, 296, "3", "Telemetry and objectives", "B"))
    write("observability", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 4
def saga():
    h = 540
    p = []

    p.append(text(24, 26, "The process manager, as OrderStateMachine.cs declares it",
                  size=13, anchor="start", weight="600", slab=True))

    p.append(unit(24, 44, 150, 50, "Initially", (), stroke=RULE, width=HAIR))
    p.append(unit(236, 44, 150, 50, "Reserving", ("sends ReserveStock",), stroke=INK, width=HEAVY))
    p.append(unit(448, 44, 150, 50, "Authorising", ("sends AuthorisePayment",), stroke=INK, width=HEAVY))
    p.append(unit(660, 44, 196, 50, "Confirmed", ("publishes OrderConfirmed",), stroke=INK, width=HEAVY))
    for x1, x2, label in ((174, 232, ("Order", "Submitted")), (386, 444, ("Stock", "Reserved")),
                          (598, 656, ("Payment", "Authorised"))):
        p.append(line(x1, 69, x2, 69, "ink", LIGHT, ASYNC))
        p.append(lines((x1 + x2) / 2, 52, label, size=8, gap=9.5, colour=QUIET))

    p.append(unit(448, 136, 150, 50, "Compensating", ("sends ReleaseStock",), stroke=FAULT, width=LIGHT))
    p.append(line(523, 94, 523, 132, "fault", LIGHT, ASYNC))
    p.append(text(533, 118, "PaymentDeclined", size=8.5, anchor="start", colour=FAULT))
    p.append(unit(660, 136, 196, 50, "Cancelled", ("publishes OrderCancelled",), stroke=FAULT, width=LIGHT))
    p.append(line(598, 161, 656, 161, "fault", LIGHT, ASYNC))
    p.append(text(627, 152, "StockReleased", size=8.5, colour=FAULT))

    p.append(unit(236, 136, 150, 50, "Rejected", ("publishes OrderCancelled",), stroke=FAULT, width=LIGHT))
    p.append(line(311, 94, 311, 132, "fault", LIGHT, ASYNC))
    p.append(text(321, 118, "StockRejected", size=8.5, anchor="start", colour=FAULT))

    p.append(rule_h(24, 216, 856))
    p.append(text(24, 240, "What the framework supplies, so the file above holds only the sequence",
                  size=12, anchor="start", weight="600", slab=True))

    rows = [("Correlation", "every message carries the order id, so MassTransit finds one instance or none"),
            ("Concurrency", "the instance row is locked while a message is handled; two cannot interleave"),
            ("Persistence", "an Entity Framework saga repository on the orchestration database"),
            ("Resilience", "retry three times at two seconds, redelivery at ten and thirty, then the error queue"),
            ("Commands and events", "Send names one endpoint; Publish reaches every subscription"),
            ("Testing", "an in-memory harness runs every branch above in about two seconds")]
    for i, (name, detail) in enumerate(rows):
        y = 262 + i * 28
        p.append(rule_h(24, y + 20, 856, RULE, HAIR))
        p.append(text(24, y + 14, name, size=10.5, anchor="start", weight="600"))
        p.append(text(224, y + 14, detail, size=10.5, anchor="start", colour=QUIET))

    p.append(text(24, 456, "Nothing is rolled back. ReleaseStock is an ordinary command whose handler is "
                           "safe to run twice,", size=10.5, anchor="start", colour=QUIET, italic=True))
    p.append(text(24, 472, "and safe when nothing was ever held, which is what makes the compensating "
                           "path as reliable as the happy one.",
                  size=10.5, anchor="start", colour=QUIET, italic=True))
    p.append(title_block(560, 492, 296, "4", "Place-order process manager", "D"))
    write("saga", W, h, "\n".join(p))


# ------------------------------------------------------------------ Figure 5
def prototype():
    h = 440
    p = []

    p.append(text(24, 26, "docker compose up, and eleven containers", size=13,
                  anchor="start", weight="600", slab=True))

    for i, (name, port, critical) in enumerate((("order", "9380", True), ("orchestration", "9384", True),
                                                ("inventory", "9381", True), ("payment", "9382", True),
                                                ("notification", "9383", False))):
        x = 24 + i * 172
        p.append(unit(x, 44, 152, 44, name, (), stroke=INK,
                      width=HEAVY if critical else LIGHT, size=11.5))
        p.append(text(x + 76, 102, f"port {port}", size=9.5, colour=QUIET))
        p.append(line(x + 50, 110, x + 50, 132, "ink", HAIR, both=True))
        p.append(box(x + 6, 132, 108, 30, INK, HAIR))
        p.append(store(x + 14, 140, "PostgreSQL"))
        p.append(line(x + 118, 88, x + 118, 186, "signal", HAIR, ASYNC))

    p.append(unit(24, 190, 832, 52, "RabbitMQ",
                  ("six endpoint queues, one error queue each, and a fanout exchange per message type",
                   "Azure Service Bus in production: the same code, one line of configuration"),
                  stroke=SIGNAL, width=LIGHT))

    p.append(rule_h(24, 272, 856))
    p.append(text(24, 296, "What it shows", size=11.5, anchor="start", weight="600", slab=True))
    p.append(lines(24, 318, [
        "checkout answers in about 70 ms, and keeps answering",
        "while the notification service is stopped",
        "the saga's own row shows the path an order took",
        "a declined card sends ReleaseStock and the hold returns",
        "one correlation identifier runs through all five services",
    ], size=10.5, anchor="start", colour=QUIET, gap=16))

    p.append(rule_v(440, 282, 400))
    p.append(text(464, 296, "What it does not", size=11.5, anchor="start", weight="600",
                  colour=FAULT, slab=True))
    p.append(lines(464, 318, [
        "no scheduled timeout, so a worker that never answers",
        "leaves the saga open; that is the first gap to close",
        "RabbitMQ locally, not Azure Service Bus",
        "no cluster, no mesh, no ingress, no Argo CD",
        "no load test; 70 ms is one request on an idle machine",
    ], size=10.5, anchor="start", colour=QUIET, gap=16))
    write("prototype", W, h, "\n".join(p))


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    architecture()
    pipeline()
    observability()
    saga()
    prototype()
