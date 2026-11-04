#!/usr/bin/env bash
# Writes the digests this commit produced into the environment repository.
# The pipeline has no credentials for the cluster at all: Argo CD pulls.
set -euo pipefail
ENVIRONMENT=$1
TAG=$2
REPO=${ENV_REPO:-git@github.com:maltashop/platform-environments.git}
WORK=$(mktemp -d); trap 'rm -rf "$WORK"' EXIT

git clone --depth 1 "$REPO" "$WORK"
cd "$WORK/overlays/$ENVIRONMENT"
for project in MaltaShop.Order MaltaShop.Orchestration MaltaShop.Inventory \
               MaltaShop.Payment MaltaShop.Notification; do
  digest=$(crane digest "maltashopacr.azurecr.io/maltashop/$project:$TAG")
  kustomize edit set image "$project=maltashopacr.azurecr.io/maltashop/$project@$digest"
  echo "  $project -> $digest"
done
git -c user.email=pipeline@maltashop.mt -c user.name="release pipeline" \
    commit -am "promote $TAG into $ENVIRONMENT"
git push
echo "desired state updated; Argo CD will reconcile"
