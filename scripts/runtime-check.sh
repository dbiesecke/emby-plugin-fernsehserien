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
touch "$config/fernsehserien-ci"
chmod -R a+rwX "$config"
docker run -d --name "$name" --volume "$config:/config" "$image" >/dev/null
for i in $(seq 1 72); do
  if docker exec "$name" /bin/sh -c '[ -f /config/fernsehserien-result.txt ]'; then
    mkdir -p "$root/artifacts"
    docker exec "$name" cat /config/fernsehserien-result.txt > "$root/artifacts/runtime-result.txt"
    cat "$root/artifacts/runtime-result.txt"
    if grep -q '^PASS:' "$root/artifacts/runtime-result.txt"; then exit 0; fi
    docker logs "$name" --tail 350
    exit 1
  fi
  if [ "$(docker inspect --format '{{.State.Running}}' "$name")" != "true" ]; then
    docker logs "$name" --tail 100
    exit 1
  fi
  if [ "$i" = "24" ]; then
    docker exec "$name" ls -ld /config /config/fernsehserien-ci
    docker exec "$name" cat /config/fernsehserien-progress.txt || true
  fi
  sleep 5
done
docker logs "$name" --tail 100
docker exec "$name" ls -la /config
exit 1
