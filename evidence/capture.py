#!/usr/bin/env python3
"""Runs the demonstration behind Sections 3 and 6.3 and saves the evidence.

    python3 evidence/capture.py

Every screenshot renders the real output of the command printed above it, and the
same text is kept in evidence/transcripts/. Needs Docker, curl, jq and Playwright.
"""
import html
import json
import subprocess
import time
import uuid
from pathlib import Path

from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent.parent
TRANSCRIPTS = ROOT / "evidence" / "transcripts"
SHOTS = ROOT / "evidence" / "screenshots"
ORDER, INVENTORY, PAYMENT, NOTIFY, SAGA = (
    "http://localhost:9380", "http://localhost:9381", "http://localhost:9382",
    "http://localhost:9383", "http://localhost:9384")
BASKET = ('{"customer_id":"MT-D-5520","items":[{"sku":"MICRO-25L","quantity":2},'
          '{"sku":"DISHW-60","quantity":1}]}')
BIG = '{"customer_id":"MT-D-5520","items":[{"sku":"FRIDGE-XL","quantity":1}]}'
RABBIT = "docker compose exec -T rabbitmq rabbitmqctl"

# A classic green-on-black terminal, so this submission's figures are its own.
TERMINAL = """<!doctype html><html><head><meta charset="utf-8"><style>
 body {{ margin:0; padding:20px; background:#ffffff; }}
 .window {{ width:{width}px; border-radius:5px; overflow:hidden; background:#06130a;
            border:1px solid #9bb3a2; box-shadow:0 2px 7px rgba(0,30,10,.25); }}
 .bar {{ height:30px; background:#0f2416; display:flex; align-items:center; padding:0 13px;
         border-bottom:1px solid #1d3c27; }}
 .dot {{ width:10px; height:10px; border-radius:50%; margin-right:8px; }}
 .title {{ color:#8fc9a3; font:12.5px/1 -apple-system, Helvetica, sans-serif; margin-left:12px; }}
 pre {{ margin:0; padding:15px 18px 18px; color:#b8f2c8; font:13.5px/1.5 Menlo, monospace;
        white-space:pre-wrap; word-break:break-all; }}
 .cmd {{ color:#7fe3a0; font-weight:600; }} .hl {{ color:#ffe066; font-weight:bold; }}
 .ok {{ color:#e8ffe8; }}
</style></head><body><div class="window"><div class="bar">
 <span class="dot" style="background:#3f7d53"></span><span class="dot" style="background:#5a9c6e"></span>
 <span class="dot" style="background:#7fe3a0"></span><span class="title">{title}</span></div>
 <pre>{body}</pre></div></body></html>"""


def sh(command, check=True):
    result = subprocess.run(["bash", "-c", command], cwd=ROOT, capture_output=True, text=True)
    if check and result.returncode != 0:
        raise SystemExit(f"command failed: {command}\n{result.stdout}{result.stderr}")
    return (result.stdout + result.stderr).rstrip("\n")


class Terminal:
    def __init__(self, browser):
        self.browser = browser

    def capture(self, name, title, commands, highlight=None, width=1020):
        lines = []
        for command in commands:
            lines.append(f"$ {command}")
            output = sh(command)
            if output:
                lines.append(output)
            lines.append("")
        text = "\n".join(lines).rstrip("\n")
        (TRANSCRIPTS / f"{name}.txt").write_text(text + "\n", encoding="utf-8")
        body = "\n".join(
            f'<span class="cmd">{html.escape(row)}</span>' if row.startswith("$ ")
            else f'<span class="ok">{html.escape(row)}</span>' if row.strip().startswith("PASS")
            else html.escape(row) for row in text.splitlines())
        for mark in ([highlight] if isinstance(highlight, str) else (highlight or [])):
            body = body.replace(html.escape(mark), f'<span class="hl">{html.escape(mark)}</span>')
        page = self.browser.new_page(device_scale_factor=2, viewport={"width": width + 44, "height": 400})
        page.set_content(TERMINAL.format(width=width, title=html.escape(title), body=body))
        page.locator(".window").screenshot(path=str(SHOTS / f"{name}.png"))
        page.close()
        print(f"  saved {name}")
        return text


def wait_state(order_id, state, tries=70):
    for _ in range(tries):
        if sh(f"curl -sS {ORDER}/orders/{order_id} | jq -r .state") == state:
            return
        time.sleep(0.5)
    raise SystemExit(f"{order_id} never reached {state}")


def main():
    TRANSCRIPTS.mkdir(parents=True, exist_ok=True)
    SHOTS.mkdir(parents=True, exist_ok=True)
    print("starting a fresh platform ...")
    sh("docker compose down -v --remove-orphans", check=False)
    sh("docker compose up -d --build --wait")
    time.sleep(5)

    correlation = str(uuid.uuid4())
    key = f"d-{correlation[:8]}"

    with sync_playwright() as playwright:
        browser = playwright.chromium.launch()
        term = Terminal(browser)

        term.capture("01_compose_up", "docker compose up: five services, five databases, one broker",
                     ["docker compose ps --format 'table {{.Service}}\\t{{.Status}}' | cut -c1-96"],
                     width=940)

        term.capture("02_order_request", "POST /orders: the one synchronous step in checkout", [
            f"curl -sS -D /tmp/h.txt -o /tmp/b.json -X POST {ORDER}/orders "
            f"-H 'content-type: application/json' -H 'x-correlation-id: {correlation}' "
            f"-H 'idempotency-key: {key}' -d '{BASKET}'; "
            "grep -Ei '^HTTP|^location|^x-correlation' /tmp/h.txt; jq . /tmp/b.json"],
            highlight=correlation, width=1060)
        order_id = json.loads(sh("cat /tmp/b.json"))["order_id"]
        wait_state(order_id, "CONFIRMED")

        term.capture("03_saga_state", "the process manager's own row: where the order went", [
            f"curl -sS {SAGA}/sagas/{order_id} | jq .",
            f"curl -sS {INVENTORY}/holds/{order_id} | jq ."],
            highlight=order_id)

        term.capture("04_event_consumed", "notification-service: the OrderConfirmed event, consumed", [
            f"curl -sS '{NOTIFY}/notifications?orderId={order_id}' "
            "| jq '.[] | {template, subject, body, status, correlation_id}'"],
            highlight=[correlation, order_id])

        term.capture("05_correlation_logs", "one correlation ID across five services", [
            f"docker compose logs --no-log-prefix 2>/dev/null | grep {correlation} "
            "| jq -r 'select(.Event) | [.\"@t\"[11:23], .service, .Event] | @tsv' "
            "| sort | column -t -s $'\\t'"], highlight=correlation)

        term.capture("06_broker_topology", "the topology MassTransit built: a queue per endpoint", [
            f"{RABBIT} list_queues name messages consumers --quiet "
            "| grep -E 'maltashop' | sort | column -t",
            f"{RABBIT} list_exchanges name type --quiet | grep -E 'MaltaShop.Contracts' "
            "| sort | head -8 | column -t"], width=1020)

        before = sh(f"curl -sS {INVENTORY}/stock | jq -r '.[]|select(.sku==\"FRIDGE-XL\")|.available'")
        cid2 = str(uuid.uuid4())
        declined = json.loads(sh(f"curl -sS -X POST {ORDER}/orders -H 'content-type: application/json' "
                                 f"-H 'x-correlation-id: {cid2}' -H 'idempotency-key: dec-{cid2[:8]}' "
                                 f"-d '{BIG}'"))
        wait_state(declined["order_id"], "CANCELLED")
        term.capture("07_compensation", "payment declined: the process manager compensates", [
            f"echo 'available before the order: {before}'",
            f"curl -sS {SAGA}/sagas/{declined['order_id']} | jq '{{state, path, failure_reason}}'",
            f"curl -sS {INVENTORY}/holds/{declined['order_id']} | jq '{{state}}'",
            f"curl -sS {INVENTORY}/stock | jq -r '.[]|select(.sku==\"FRIDGE-XL\")"
            "|\"available after compensation: \\(.available)\"'",
            f"curl -sS '{NOTIFY}/notifications?orderId={declined['order_id']}' | jq -r '.[].subject'"],
            highlight=declined["order_id"], width=1060)

        print("fault isolation ...")
        cid3 = str(uuid.uuid4())
        out = term.capture("08_fault_isolation", "Notification is down: checkout is unaffected", [
            "docker compose stop notification-service 2>&1 | tail -1",
            f"curl -sS -o /dev/null -w 'checkout answered HTTP %{{http_code}} in %{{time_total}}s\\n' "
            f"-X POST {ORDER}/orders -H 'content-type: application/json' "
            f"-H 'x-correlation-id: {cid3}' -H 'idempotency-key: out-{cid3[:8]}' -d '{BASKET}'",
            f"sleep 8; {RABBIT} list_queues name messages --quiet "
            "| grep -E 'maltashop-notification|maltashop-order-outcomes' | column -t"], width=1060)
        assert "HTTP 201" in out, out

        term.capture("09_recovery", "Notification restarts and drains its queue", [
            "docker compose start notification-service 2>&1 | tail -1",
            "until curl -fsS http://localhost:9383/health/ready >/dev/null 2>&1; do sleep 1; done; "
            "for _ in $(seq 1 40); do "
            "  n=$(curl -sS http://localhost:9383/notifications | jq length); "
            "  [ \"$n\" -ge 3 ] && break; sleep 2; done; "
            f"{RABBIT} list_queues name messages --quiet "
            "| grep -E 'maltashop-notification|maltashop-order-outcomes' | column -t",
            f"curl -sS {NOTIFY}/notifications | jq -r '.[] | [.template, .subject] | @tsv' "
            "| head -4 | column -t -s $'\\t'"], width=1060)

        term.capture("10_pipeline_run", "the pipeline's verify, build and integration stages",
                     ["./scripts/run_pipeline.sh"], width=1040)
        browser.close()

    (ROOT / "evidence" / "demo.json").write_text(json.dumps(
        {"order_id": order_id, "correlation_id": correlation,
         "cancelled_order_id": declined["order_id"],
         "captured_at": time.strftime("%Y-%m-%dT%H:%M:%S%z")}, indent=2) + "\n")
    print("done")


if __name__ == "__main__":
    main()
