# Phase 4 — Core hardware layer and first battery integration

This branch starts Phase 4 with a small, testable core library and a read-only Windows HID battery provider.

## Included

- Normalized identity parsing for Apple Bluetooth/USB HID IDs used by this project:
  - Magic Mouse 2015: product ID 0269
  - Magic Trackpad 2: product ID 0265
  - Magic Keyboard: product ID 0267
- A validated parser for battery input report 0x90, reading the percentage from byte 2.
- Explicit battery result states; unsupported/missing reports do not become 0%.
- A provider contract and ordered fallback router.
- A Windows HID provider that enumerates current HID interfaces dynamically and tries descriptor-indicated read operations:
  - input report 0x90 on the Apple vendor battery collection;
  - descriptor-declared Generic Device / Battery Strength feature reads as a fallback.
- Tray menu entries for Mouse, Trackpad and Keyboard battery readings, refreshed every 30 seconds.
- A no-dependency smoke-test executable and a Windows GitHub Actions build.

## Safety boundaries

- This code performs no driver installation, registry write, output report write, feature report write or IOCTL call. HidD_GetFeature is a read operation and is used only when the current HID descriptor declares a battery-strength feature.
- Mouse touch-report bytes are not decoded here. The current MagicMouseWindows reader's fixed PDO path and assumed byte offsets are not proven for Magic Mouse 2015 (PID 0269).
- Trackpad custom battery IOCTL is not called here. It will be introduced only in a separately reviewed Windows provider with an explicit access model and detection of the compatible driver.
- Battery health and capacity values remain null unless a provider can actually supply them.
- This is an incremental battery implementation, not yet the completed unified app. Device connection lifecycle, replacement of the old Mouse reader, Trackpad gesture service and UI settings integration remain.
- No driver installation or device-stack change is performed by the application.

## Build and verify on Windows

From the repository root:

    dotnet build src/MagicMouseApp/MagicMouseApp.csproj -c Release
    dotnet run --project tests/OwnMagicUtilities.Core.SmokeTests/OwnMagicUtilities.Core.SmokeTests.csproj -c Release

The smoke tests verify ID parsing, report validation, null capacity semantics and provider fallback. The Windows CI build validates compilation. Device-level battery reads and scrolling still require testing on the target PC.
