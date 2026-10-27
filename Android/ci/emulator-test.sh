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
# simulate a second screen (like the AYN Thor's lower display) for the second launch
adb shell settings put global overlay_display_devices "1240x1080/240"
sleep 4
launch run2 60
for id in $(adb shell dumpsys SurfaceFlinger --display-id | grep -oE 'Display [0-9]+' | awk '{print $2}'); do
  adb exec-out screencap -d $id -p > "diag/display-$id.png"
done

# remove the simulated second display (its overlay window would swallow the taps below)
adb shell settings put global overlay_display_devices '""'
sleep 4

# tap Play on the main menu (as a fraction of the screen size) and capture what comes next
SIZE=$(adb shell wm size | grep -oE '[0-9]+x[0-9]+' | tail -1)
W=${SIZE%x*}; H=${SIZE#*x}; [ $W -lt $H ] && { T=$W; W=$H; H=$T; }
# phone menus: one button column on the left (x = 384/1080 of the height); main menu = 4 slots, play menu = 6
CX=$((H * 384 / 1080))
adb exec-out screencap -p > diag/run2-mainmenu.png
tapy() { adb shell input tap $CX $((H * $1 / 1000)); sleep ${2:-3}; }
# Options and back: the main menu buttons must keep their size
tapy 455 4
adb exec-out screencap -p > diag/run2-options.png
adb shell input tap $((W * 494 / 1000)) $((H * 856 / 1000)); sleep 3
adb exec-out screencap -p > diag/run2-options-back.png
# Play -> play menu (5 slots: Single Player, Multiplayer, Tank Color, Level Editor, Back)
tapy 314 6
adb exec-out screencap -p > diag/run2-after-play.png
# Tank Color, then Back (slot 3 of 3)
tapy 525 4
adb exec-out screencap -p > diag/run2-tankcolor.png
tapy 666
# Multiplayer -> Same Device (screenshot) -> Back; Host on Wi-Fi (lobby screenshot) -> Leave; Back
tapy 384 3
adb exec-out screencap -p > diag/run2-mp.png
tapy 666 3
adb exec-out screencap -p > diag/run2-mp-online.png
# Host Online: the lobby should show a 6-digit code from Firebase; then Leave and Back
tapy 384 10
adb exec-out screencap -p > diag/run2-online-lobby.png
tapy 736 4
tapy 666 3
tapy 244 3
adb exec-out screencap -p > diag/run2-mp-local.png
tapy 736
tapy 384 5
adb exec-out screencap -p > diag/run2-mp-lobby.png
tapy 736 4
tapy 806 3
# Single Player -> campaign screen (Campaign, Level Select, Difficulties, Update Campaign, Back)
tapy 244 5
adb exec-out screencap -p > diag/run2-singleplayer.png
# Difficulties: flip the first two toggles and Lantern Mode, then Back to the campaign screen
tapy 525 4
adb exec-out screencap -p > diag/run2-difficulties.png
adb shell input tap $((W * 263 / 1000)) $((H * 147 / 1000)); sleep 3
adb shell input tap $((W * 263 / 1000)) $((H * 214 / 1000)); sleep 3
adb exec-out screencap -p > diag/run2-difficulties-toggled.png
# turn on Double Enemies (3rd column, 7th row)
adb shell input tap $((W * 736 / 1000)) $((H * 547 / 1000)); sleep 2
adb shell input tap $((W * 494 / 1000)) $((H * 856 / 1000)); sleep 3
adb exec-out screencap -p > diag/run2-singleplayer-back.png

# Level Select (the button under "Campaign"): let previews render, then start mission 1
adb shell input tap $CX $((H * 384 / 1000)); sleep 15
adb exec-out screencap -p > diag/run2-levelselect-15s.png
sleep 60
adb exec-out screencap -p > diag/run2-levelselect-75s.png
# back to the campaign screen and start the Campaign: Double Enemies should give mission 1 two enemy tanks
adb shell input tap $((H * 143 / 1080)) $((H * 59 / 1000)); sleep 4
tapy 244 8
adb exec-out screencap -p > diag/run2-levelrun-banner.png; sleep 9
adb exec-out screencap -p > diag/run2-levelrun.png

adb logcat -d -v threadtime > diag/logcat-full.txt
grep -iE "mono-rt|Thread|at |TanksRebirth|DOTNET|mono|monodroid|AndroidRuntime|FATAL|Exception|libc|DEBUG|OpenGL|EGL|Adreno|MonoGame|$PKG" diag/logcat-full.txt > diag/logcat-filtered.txt
adb shell "ls -laR '$EXT'" > diag/files.txt 2>&1
adb shell "cat '$EXT/startup.txt'" > diag/startup.txt 2>&1
adb shell "cat '$EXT/crash.txt'" > diag/crash.txt 2>&1
adb shell "cat '$EXT'/Logs/*.log" > diag/gamelogs.txt 2>&1
for f in diag/*.txt; do echo >> "$f"; done  # no empty files (release uploads reject them)
ls -la diag
exit 0
