# Phase 4 — Read-only battery preview

This branch is an incremental milestone, not the finished Magic Utilities replacement.

## Included
- .NET 10 Windows tray preview.
- Runtime HID interface enumeration using SetupAPI.
- Device identity handling for Mouse PID 0269, Trackpad PID 0265, and Keyboard PID 0267.
- Read-only attempts to query Apple battery input report 0x90 and descriptor-declared Generic Device / Battery Strength features.
- Explicit result states instead of reporting a query failure as 0%.
- Tests for ID parsing, battery-report validation, unknown capacity/health, provider fallback and malformed provider results.
- CI build, smoke tests and a self-contained Windows preview artifact.

## Not included yet
- Magic Mouse 2015 scrolling.
- Trackpad gesture translation.
- Any driver installation, filter installation or registry changes.
- The old MagicMouseWindows reader is deliberately not started in this preview: its fixed raw-PDO paths and assumed report bytes are not established for PID 0269 and it requests read/write access.

## Safety
- No output-report writes, feature-report writes, driver installs, registry edits, LowerFilters edits or custom IOCTLs.
- HidD_GetFeature is used only as a read when a currently exposed HID descriptor declares a Generic Device / Battery Strength feature.
- Health and capacity stay N/A unless an actual provider supplies those values.

## Build and verify on Windows
From the repository root:

    dotnet build src/MagicMouseApp/MagicMouseApp.csproj -c Release
    dotnet run --project tests/OwnMagicUtilities.Core.SmokeTests/OwnMagicUtilities.Core.SmokeTests.csproj -c Release

Device-level battery success remains dependent on the target PC's current HID stack and requires manual testing. A successful build does not prove battery access, scrolling or gestures.
