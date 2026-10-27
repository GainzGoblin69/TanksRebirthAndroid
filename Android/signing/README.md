Signing key for the Android APK. Every build is signed with this same key so a new APK installs
over the previous one (Android refuses updates signed with a different key).

The key and its password are public on purpose (personal, unofficial build). Anyone could sign an
APK that installs over this app, so only install APKs from this repo's releases.

- keystore: tanksrebirth.keystore
- alias: tanksrebirth
- store/key password: 14c05ab702fad9b34d266349d4ebab92
