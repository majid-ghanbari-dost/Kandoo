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
  echo "::notice::C50_EMULATOR PASS package=ir.kandoo.app alive_after_seconds=15 pid=$pid"
else
  echo "C50_EMULATOR FAIL package=ir.kandoo.app process_not_alive_after_seconds=15" | tee "$evidence/process-dead.txt"
  exit 1
fi

grep -q 'Events injected: 1' "$evidence/launch.txt"
adb shell uiautomator dump /sdcard/kandoo-c58-ui.xml > "$evidence/ui-dump.txt" 2>&1
adb shell cat /sdcard/kandoo-c58-ui.xml > "$evidence/ui.xml"
for role_text in 'فروشگاه نمونه' 'مشتری نمونه' 'فروشگاه و مشتری نمونه'; do
  if ! grep -q "$role_text" "$evidence/ui.xml"; then
    echo "::error::C58_UI missing_text=$role_text"
    exit 1
  fi
done
echo "::notice::C58_UI PASS first_run_roles=store,customer,both"
capture_logcat
trap - EXIT
crash_markers=$(grep -c 'KANDOO_CRASH' "$evidence/logcat.txt" || true)
echo "::notice::C50_LOGCAT lines=$(wc -l < "$evidence/logcat.txt") crash_markers=$crash_markers artifact=kandoo-app-c50-emulator-evidence"
if [[ "$crash_markers" -gt 0 ]]; then
  grep -A 12 -B 2 -E 'KANDOO_CRASH|KandooCrash' "$evidence/logcat.txt" | head -60 > "$evidence/crash-context.txt" || true
  while IFS= read -r line; do
    printf '::warning::C50_CRASH_DETAIL %s\n' "$line"
  done < "$evidence/crash-context.txt"
  echo "::error::C50_CRASH markers=$crash_markers"
  exit 1
fi
