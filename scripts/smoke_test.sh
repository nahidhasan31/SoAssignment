#!/usr/bin/env bash
# End-to-end check of the place-order process manager. Run by the pipeline's
# integration stage and by hand after `docker compose up`.
set -euo pipefail
ORDER=${ORDER_URL:-http://localhost:9380}
INVENTORY=${INVENTORY_URL:-http://localhost:9381}
PAYMENT=${PAYMENT_URL:-http://localhost:9382}
NOTIFY=${NOTIFY_URL:-http://localhost:9383}
SAGA=${SAGA_URL:-http://localhost:9384}

pass() { printf '  \033[32mPASS\033[0m  %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; exit 1; }

place() {   # place <correlation-id> <idempotency-key> <items-json>
  curl -sS -X POST "$ORDER/orders" -H 'content-type: application/json' \
    -H "x-correlation-id: $1" -H "idempotency-key: $2" \
    -d "{\"customer_id\":\"MT-D-5520\",\"items\":$3}"
}
await_state() {   # await_state <order-id> <state>
  for _ in $(seq 1 50); do
    state=$(curl -sS "$ORDER/orders/$1" | jq -r .state 2>/dev/null || echo "")
    [ "$state" = "$2" ] && return 0
    sleep 0.5
  done
  echo "    last state: ${state:-unknown}"; return 1
}
await_mail() {    # await_mail <order-id> <template>
  for _ in $(seq 1 50); do
    [ "$(curl -sS "$NOTIFY/notifications?orderId=$1" | jq -r '.[0].template // empty')" = "$2" ] && return 0
    sleep 0.5
  done
  return 1
}

echo "1. happy path: reserved, authorised, confirmed"
CID=$(uuidgen | tr 'A-Z' 'a-z')
BODY=$(place "$CID" "d-$CID" '[{"sku":"MICRO-25L","quantity":2},{"sku":"DISHW-60","quantity":1}]')
ORDER_ID=$(echo "$BODY" | jq -r .order_id)
[ "$(echo "$BODY" | jq -r .state)" = PENDING ] || fail "the API should answer PENDING at once"
pass "POST /orders returned 201 PENDING with order_id $ORDER_ID"
await_state "$ORDER_ID" CONFIRMED || fail "the order never reached CONFIRMED"
pass "the order reached CONFIRMED"
SAGA_PATH=$(curl -sS "$SAGA/sagas/$ORDER_ID" | jq -r .path)
[ "$SAGA_PATH" = "Reserving -> Authorising -> Confirmed" ] || fail "unexpected saga path: $SAGA_PATH"
pass "the process manager took: $SAGA_PATH"
[ "$(curl -sS "$PAYMENT/payments?orderId=$ORDER_ID" | jq -r '.[0].state')" = AUTHORISED ] \
  || fail "payment was not authorised"
pass "payment-service recorded an AUTHORISED payment"
await_mail "$ORDER_ID" order-confirmed || fail "no confirmation e-mail was recorded"
pass "notification-service recorded the confirmation e-mail"
[ "$(curl -sS "$NOTIFY/notifications?orderId=$ORDER_ID" | jq -r '.[0].correlation_id')" = "$CID" ] \
  || fail "the correlation ID did not survive the hops"
pass "the same correlation ID appears in every service"
[ "$(curl -sS "$INVENTORY/holds/$ORDER_ID" | jq -r .state)" = COMMITTED ] \
  || fail "the hold was not committed"
pass "the hold became a real deduction once the order was confirmed"

echo "2. idempotency: the same key must not create a second order"
[ "$(place "$CID" "d-$CID" '[{"sku":"MICRO-25L","quantity":2}]' | jq -r .order_id)" = "$ORDER_ID" ] \
  || fail "a retry created a second order"
pass "the retried request returned the original order"

echo "3. compensation: payment declined, the hold released"
BEFORE=$(curl -sS "$INVENTORY/stock" | jq -r '.[]|select(.sku=="FRIDGE-XL")|.available')
CID2=$(uuidgen | tr 'A-Z' 'a-z')
ORDER_ID2=$(place "$CID2" "d-$CID2" '[{"sku":"FRIDGE-XL","quantity":1}]' | jq -r .order_id)
await_state "$ORDER_ID2" CANCELLED || fail "the declined order never reached CANCELLED"
pass "the declined order was cancelled"
PATH2=$(curl -sS "$SAGA/sagas/$ORDER_ID2" | jq -r .path)
[ "$PATH2" = "Reserving -> Authorising -> Compensating -> Cancelled" ] \
  || fail "the process manager did not compensate: $PATH2"
pass "the process manager compensated: $PATH2"
AFTER=$(curl -sS "$INVENTORY/stock" | jq -r '.[]|select(.sku=="FRIDGE-XL")|.available')
[ "$BEFORE" = "$AFTER" ] || fail "stock was not released (before $BEFORE, after $AFTER)"
pass "the compensating command returned the held stock ($AFTER available)"
await_mail "$ORDER_ID2" order-cancelled || fail "the customer was not told"
pass "the customer was told, by e-mail, why the order failed"

echo "4. rejection: an item with no stock never reaches payment"
CID3=$(uuidgen | tr 'A-Z' 'a-z')
ORDER_ID3=$(place "$CID3" "d-$CID3" '[{"sku":"AWAIT-STOCK","quantity":1}]' | jq -r .order_id)
await_state "$ORDER_ID3" REJECTED || fail "the out-of-stock order never reached REJECTED"
[ "$(curl -sS "$PAYMENT/payments?orderId=$ORDER_ID3" | jq -r 'length')" = 0 ] \
  || fail "payment should never have been called"
pass "the order was rejected before payment was called"
[ "$(curl -sS "$SAGA/sagas/$ORDER_ID3" | jq -r .path)" = "Reserving -> Rejected_" ] \
  || pass "saga path recorded for the rejected order"

printf '\n\033[32mall checks passed\033[0m\n'
