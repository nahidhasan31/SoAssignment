#!/usr/bin/env bash
# Waits for Argo CD to report the application Synced and Healthy, then for Argo
# Rollouts to finish the blue-green promotion of the Payment service.
set -euo pipefail
APP=$1
argocd app wait "$APP" --sync --health --timeout 600
# The preview service is verified before any customer reaches it; promotion is a
# single atomic switch of the active service's selector, and abort is the same
# switch reversed.
kubectl argo rollouts status payment-service -n maltashop-production --timeout 10m
echo "$APP is synced, healthy and promoted"
