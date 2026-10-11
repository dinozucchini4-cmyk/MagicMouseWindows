# Phase 4 — Core hardware layer

This branch starts Phase 4 with a small, testable core library.

## Included

- Normalized identity parsing for Apple Bluetooth/USB HID IDs used by this project:
  - Magic Mouse 2015: product ID 0269
  - Magic Trackpad 2: product ID 0265
  - Magic Keyboard: product ID 0267
- A validated parser for battery input report 0x90, reading the percentage from byte 2.
- Explicit battery result states; unsupported/missing reports do not become 0%.
- A provider contract and ordered fallback router.
- A no-dependency smoke-test executable.
- A Windows GitHub Actions build for the existing app and the core smoke tests.

## Safety boundaries

- This code performs no driver installation, registry write, output report, feature report or IOCTL call.
- Mouse touch-report bytes are not decoded here. The current MagicMouseWindows reader's fixed PDO path and assumed byte offsets are not proven for Magic Mouse 2015 (PID 0269).
- Trackpad custom battery IOCTL is not called here. It will be introduced only in a separately reviewed Windows provider with an explicit access model and detection of the compatible driver.
- Battery health and capacity values remain null unless a provider can actually supply them.
- This is the core library slice, not yet the completed unified app. HID enumeration, concrete providers and UI wiring are subsequent Phase 4 steps.

## Build and verify on Windows

From the repository root:

```powershell
dotnet build src/MagicMouseApp/MagicMouseApp.csproj -c Release
dotnet run --project tests/OwnMagicUtilities.Core.SmokeTests/OwnMagicUtilities.Core.SmokeTests.csproj -c Release
```

The smoke tests verify ID parsing, report validation, null capacity semantics and provider fallback. A successful build does not prove device-level battery or scrolling behavior; those require testing on the target PC.
