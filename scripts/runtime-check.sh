#!/usr/bin/env bash
set -euo pipefail
# Only isolated disposable containers. Never points at a user's Emby server.
image="$1"
root="$(cd "$(dirname "$0")/.." && pwd)"
config="$(mktemp -d)"
name="fernsehserien-check-${RANDOM}"
cleanup() { docker rm -f "$name" >/dev/null 2>&1 || true; }
trap cleanup EXIT
mkdir -p "$config/plugins"
cp "$root/src/bin/Release/netstandard2.0/Emby.Plugin.Fernsehserien.dll" "$config/plugins/"
cp "$root/integration/bin/Release/netstandard2.0/Fernsehserien.RuntimeChecks.dll" "$config/plugins/"
chmod -R a+rwX "$config"
docker run -d --name "$name" --env FERNSEHSERIEN_CI=1 --volume "$config:/config" "$image" >/dev/null
for i in $(seq 1 120); do
  if [ -f "$config/fernsehserien-result.txt" ]; then
    cat "$config/fernsehserien-result.txt"
    mkdir -p "$root/artifacts"
    cp "$config/fernsehserien-result.txt" "$root/artifacts/runtime-result.txt"
    grep -q '^PASS:' "$config/fernsehserien-result.txt"
    exit $?
  fi
  sleep 5
done
docker logs "$name" --tail 100
exit 1
