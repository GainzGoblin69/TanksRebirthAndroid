# Tanks! Rebirth on Android

An unofficial Android port with touch controls.

## Controls

| Where | What it does |
| --- | --- |
| Left side of the screen | Touch and drag anywhere: a joystick appears under your thumb and drives the tank |
| Right side of the screen | Tap a spot: the turret aims there and fires |
| **MINE** button (bottom right) | Lay a mine |
| **II** button (top left) or the Android back button | Pause / go back |
| Menus | Tap buttons like you would click them |

A Bluetooth controller or keyboard still works the way it does on PC.

## Getting the APK (no tools needed)

The repo includes a GitHub Actions workflow that builds the APK on GitHub's servers.

1. Push this branch to your fork on GitHub (Actions must be enabled on the fork — check the **Actions** tab).
2. Every push runs **Android APK**. You can also start it by hand: **Actions → Android APK → Run workflow**.
3. When it finishes (about 10–15 minutes), open the run and download **TanksRebirth-android** under *Artifacts*. Unzip it to get `TanksRebirth.apk`.
4. Copy the APK to your phone and open it. Android will ask you to allow installing apps from that source.

The APK is signed with a throwaway debug key that changes between builds, so to install a newer build you may
need to uninstall the old one first. That also deletes your saves unless you back up the folder below.

## Where things are on the phone

Saves, settings, logs and campaigns live in
`Android/data/com.tanksrebirth.unofficial/files/Tanks Rebirth/`.
Put extra `.campaign` files in its `Campaigns` folder. The official campaign is bundled, and the game will also
download updates to it when online.

## Building locally (Windows)

Requires the .NET 8 SDK (plus the .NET 6 runtime for the content builder), the Android workload (`dotnet workload install android`), and JDK 17.

```powershell
dotnet tool install --global dotnet-mgcb --version 3.8.1.303

# compile models + shaders for Android (shaders must be compiled on Windows)
(Get-Content Content/Content.mgcb -Raw) -replace 'TextureFormat=Compressed','TextureFormat=Color' | Set-Content Content/Content.Android.mgcb
cd Content
mgcb /@:Content.Android.mgcb /platform:Android /outputDir:../Android/ContentBuilt /intermediateDir:../Android/obj/content /workingDir:.
cd ..

dotnet publish Android/TanksRebirth.Android.csproj -c Release -f net8.0-android
# → Android/bin/Release/net8.0-android/publish/com.tanksrebirth.unofficial-Signed.apk
```

## How the port works

* `Android/TanksRebirth.Android.csproj` compiles the same game source as the desktop build, against MonoGame's
  Android backend. The desktop project ignores the `Android/` folder.
* Textures, sounds, fonts and translations are packed into the APK and copied to app storage on first launch
  (`Android/AssetExtractor.cs`), because the game opens them as plain files.
* `Internals/Common/Framework/Input/TouchControls.cs` turns touches into joystick movement and a virtual mouse.
  It is switched off on desktop.
* Desktop-only features are skipped on Android: Discord presence, hardware detection, Steam, mod compiling.

## Known limitations

* Typing (chat, multiplayer names, level editor text) needs a hardware keyboard; there is no on-screen keyboard yet.
* The level editor's open/save file dialogs don't exist on Android.
* Scrolling to pick a starting mission in the campaign menu needs a mouse wheel.
* Lantern mode and the menu blur rely on shaders that may look different, or not work, on some older GPUs.
