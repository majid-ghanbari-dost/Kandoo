#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "usage: Invoke-C50AndroidSmoke.sh <apk> <evidence-directory>" >&2
  exit 2
fi

apk=$1
evidence=$2
mkdir -p "$evidence"

capture_logcat() {
  adb logcat -d -v threadtime > "$evidence/logcat.txt" 2>&1 || true
}
trap capture_logcat EXIT

adb wait-for-device
adb shell input keyevent 82 || true
adb logcat -c
adb install -r "$apk" > "$evidence/install.txt" 2>&1
adb shell monkey -p ir.kandoo.app -c android.intent.category.LAUNCHER 1 > "$evidence/launch.txt" 2>&1
sleep 15

if pid="$(adb shell pidof -s ir.kandoo.app 2>/dev/null | tr -d '\r')" && [[ -n "$pid" ]]; then
  printf '%s\n' "$pid" > "$evidence/process-alive.txt"
  echo "C50_EMULATOR PASS package=ir.kandoo.app alive_after_seconds=15 pid=$pid"
else
  echo "C50_EMULATOR FAIL package=ir.kandoo.app process_not_alive_after_seconds=15" | tee "$evidence/process-dead.txt"
  exit 1
fi

grep -q 'Events injected: 1' "$evidence/launch.txt"
