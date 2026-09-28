#!/bin/bash
# Installs and launches a SwiftDotNet watch .app on a watch simulator (or a paired device via devicectl).
#
#   watch-launch.sh --app Path.app --bundle-id id [--target simulator|device] [--sim "name or UDID"]
#                      [--device "name or UDID"] [--console] [--wait-for-debugger] [--pid-file file] [--trace]
set -euo pipefail

APP=""; BUNDLE_ID=""; TARGET="simulator"; SIM=""; DEVICE=""; CONSOLE=0; WAIT=0; PID_FILE=""
while [ $# -gt 0 ]; do
  case "$1" in
    --app) APP="$2"; shift 2 ;;
    --bundle-id) BUNDLE_ID="$2"; shift 2 ;;
    --target) TARGET="$2"; shift 2 ;;
    --sim) SIM="$2"; shift 2 ;;
    --device) DEVICE="$2"; shift 2 ;;
    --console) CONSOLE=1; shift ;;
    --wait-for-debugger) WAIT=1; shift ;;
    --pid-file) PID_FILE="$2"; shift 2 ;;
    --trace) export SIMCTL_CHILD_SWIFTDOTNET_WATCH_TRACE=1; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ -d "$APP" ] || { echo "error: app bundle not found: $APP (build first)" >&2; exit 1; }

if [ "$TARGET" = "device" ]; then
  [ -n "$DEVICE" ] || DEVICE=$(xcrun devicectl list devices 2>/dev/null | awk '/Watch/ && /connected|available/ {print $3; exit}')
  [ -n "$DEVICE" ] || { echo "error: no Apple Watch found by devicectl; pass --device <UDID>" >&2; exit 1; }
  echo "Installing on device $DEVICE ..."
  xcrun devicectl device install app --device "$DEVICE" "$APP"
  ARGS=(device process launch --device "$DEVICE" --terminate-existing)
  [ $CONSOLE -eq 1 ] && ARGS+=(--console)
  [ $WAIT -eq 1 ] && ARGS+=(--start-stopped)
  exec xcrun devicectl "${ARGS[@]}" "$BUNDLE_ID"
fi

# ---- simulator ----
resolve_sim() {
  local want="$1"
  if [ -n "$want" ]; then
    # UDID or exact name, available watchOS runtimes only
    xcrun simctl list devices available -j | python3 -c '
import json, sys
want = sys.argv[1]
data = json.load(sys.stdin)["devices"]
hits = [d for rt, ds in data.items() if "watchOS" in rt for d in ds if d["udid"] == want or d["name"] == want]
hits.sort(key=lambda d: d["state"] != "Booted")
print(hits[0]["udid"] if hits else "")' "$want"
  else
    # prefer a booted watch, else the newest runtime's first watch
    xcrun simctl list devices available -j | python3 -c '
import json, sys
data = json.load(sys.stdin)["devices"]
watch = [(rt, d) for rt, ds in data.items() if "watchOS" in rt for d in ds]
booted = [d for rt, d in watch if d["state"] == "Booted"]
if booted: print(booted[0]["udid"])
elif watch:
    watch.sort(key=lambda x: x[0], reverse=True)
    print(watch[0][1]["udid"])
else: print("")'
  fi
}

UDID=$(resolve_sim "$SIM")
[ -n "$UDID" ] || { echo "error: no watchOS simulator found${SIM:+ matching '$SIM'} (install one in Xcode > Settings > Components)" >&2; exit 1; }

STATE=$(xcrun simctl list devices -j | python3 -c 'import json,sys; u=sys.argv[1]; print(next((d["state"] for ds in json.load(sys.stdin)["devices"].values() for d in ds if d["udid"]==u), ""))' "$UDID")
if [ "$STATE" != "Booted" ]; then
  echo "Booting simulator $UDID ..."
  xcrun simctl boot "$UDID"
fi
open -a Simulator --args -CurrentDeviceUDID "$UDID" >/dev/null 2>&1 || true
xcrun simctl bootstatus "$UDID" -b >/dev/null

echo "Installing $(basename "$APP") ..."
xcrun simctl install "$UDID" "$APP"

ARGS=(launch --terminate-running-process)
[ $WAIT -eq 1 ] && ARGS+=(--wait-for-debugger)
if [ $CONSOLE -eq 1 ] && [ $WAIT -eq 0 ]; then
  echo "Launching $BUNDLE_ID (Ctrl+C to stop) ..."
  exec xcrun simctl "${ARGS[@]}" --console-pty "$UDID" "$BUNDLE_ID"
fi

OUT=$(xcrun simctl "${ARGS[@]}" "$UDID" "$BUNDLE_ID")
PID="${OUT##*: }"
echo "Launched $BUNDLE_ID (pid $PID)"
[ -n "$PID_FILE" ] && echo "$PID" > "$PID_FILE"
exit 0
