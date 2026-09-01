#!/usr/bin/env bash
set -euo pipefail

hours="${1:-72}"
if ! [[ "$hours" =~ ^[0-9]+$ ]] || [ "$hours" -lt 1 ]; then
  echo "usage: $0 <positive soak hours>" >&2
  exit 2
fi

deadline=$(( $(date +%s) + hours * 3600 ))
cycles=0
while [ "$(date +%s)" -lt "$deadline" ]; do
  cargo test -p aurora-opcua --features native --test native_hil --locked -- --ignored --exact native_browse_read_write_and_monitored_item_round_trip
  cargo test -p aurora-comm-runtime --test mixed_fifty_devices --locked
  cycles=$((cycles + 1))
  echo "Aurora 2.0 HIL cycle $cycles passed at $(date --iso-8601=seconds)"
  sleep 30
done

echo "Aurora 2.0 HIL soak completed: $cycles cycles across $hours hours"
