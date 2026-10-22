#!/usr/bin/env bash
# Installs the APK on the CI emulator, launches it twice, and collects screenshots + logs into ./diag
set -x
PKG=com.tanksrebirth.unofficial
EXT=/sdcard/Android/data/$PKG/files/Tanks\ Rebirth
mkdir -p diag
adb root; sleep 3; adb wait-for-device  # emulator images allow root: needed to signal the app and read /data/anr
adb shell getprop ro.build.version.release > diag/device.txt
adb shell getprop ro.product.cpu.abi >> diag/device.txt
adb install -r -g apk/TanksRebirth.apk > diag/install.txt 2>&1
adb logcat -c

launch() {  # $1 = label, $2 = seconds to wait
  adb shell monkey -p $PKG -c android.intent.category.LAUNCHER 1 > /dev/null
  local elapsed=0
  for t in 10 25 45 $2; do
    sleep $((t - elapsed)); elapsed=$t
    adb exec-out screencap -p > "diag/$1-${t}s.png"
  done
  adb shell pidof $PKG > "diag/$1-pid.txt" 2>&1
  adb shell dumpsys activity activities | grep -iE "mResumedActivity|topResumedActivity" > "diag/$1-activity.txt" 2>&1
}

launch run1 90
# if it is stuck, ask Mono for a dump of every thread's stack (lands in logcat)
P=$(adb shell pidof $PKG | tr -d '\r')
[ -n "$P" ] && adb shell kill -3 $P && sleep 5
adb logcat -d -v threadtime > diag/logcat-run1.txt
adb shell 'cat /data/anr/* 2>/dev/null' > diag/anr-traces.txt
adb shell am force-stop $PKG
sleep 3
launch run2 60

adb logcat -d -v threadtime > diag/logcat-full.txt
grep -iE "mono-rt|Thread|at |TanksRebirth|DOTNET|mono|monodroid|AndroidRuntime|FATAL|Exception|libc|DEBUG|OpenGL|EGL|Adreno|MonoGame|$PKG" diag/logcat-full.txt > diag/logcat-filtered.txt
adb shell "ls -laR '$EXT'" > diag/files.txt 2>&1
adb shell "cat '$EXT/startup.txt'" > diag/startup.txt 2>&1
adb shell "cat '$EXT/crash.txt'" > diag/crash.txt 2>&1
adb shell "cat '$EXT'/Logs/*.log" > diag/gamelogs.txt 2>&1
for f in diag/*.txt; do echo >> "$f"; done  # no empty files (release uploads reject them)
ls -la diag
exit 0
