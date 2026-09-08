# Verification — 2026-09-08

## v1.1.0 — resizing and heart-rate elements

- Before the fix, regression checks failed for native corner resize hit testing,
  persisted width/height, heart animation, and summary statistics.
- Core: **21/21 passed**. Added valid-window statistics, unavailable values and
  rounding fixtures with independently specified expectations.
- WPF application: **24/24 passed**. Added resize hit targets, locked resize
  exclusion, size persistence, summary labels, motion toggle/staleness, compact
  layout, invalid dimensions and migration from v1.0 settings.
- `dotnet build HeartBeat.slnx -c Release --no-restore`: **0 warnings, 0 errors**.
- Inspected 420×280 and 280×216 DIP rendered previews in
  `artifacts/v1.1-check/`. Full source review found no actionable important issue.
- Native resize tests exercise WM_NCHITTEST and WM_EXITSIZEMOVE against the real
  WPF window. Actual mouse dragging and mixed-DPI monitor behavior still require
  interactive testing on the target desktop.
- Release output is versioned to avoid overwriting the user's running v1.0.
- Published `HeartBeat-1.1.0-win-x64/HeartBeat.exe` was started in a separate
  test session and verified to load its bundled runtime. Its ZIP contains 477
  entries, including the program, runtime, Chinese guide and demo launcher.
  Only the test process was stopped; the user's running v1.0 process was preserved.

## v1.0 baseline

## Completed

- `dotnet run --project tests/HeartBeat.Tests -c Release`: **19/19 passed**.
  Includes 8/16-bit BLE packets, optional fields, malformed packets, no contact,
  stale data, second buckets, five-minute eviction, gaps, old session callbacks,
  serialized device switching, retry cancellation, startup disconnect, and
  disposal while a native connect is pending.
- `dotnet build HeartBeat.slnx -c Release --no-restore`: **0 warnings, 0 errors**.
- `dotnet run --project tests/HeartBeat.App.Tests -c Release --no-build -- --demo --settings-dir artifacts/smoke-release`:
  **14/14 passed** against the actual WPF application. Result file:
  `artifacts/smoke-release/smoke-test.txt`.
- Rendered and visually inspected the card and settings window using synthetic
  samples. PNG previews in `artifacts/smoke-release/`.
- Independent source review found a cold Windows discovery-cache reconnect
  issue. Fixed by exact-address/type discovery before retrying a null lookup;
  scoped re-review found no important regression.
- `scripts/publish.ps1` completed; published executable was launched with a
  separate test settings directory. Verified it remained running and loaded
  `coreclr.dll` from its own release folder, then stopped only that test process.
  This is a startup test; graceful application exit was exercised by WPF tests.
- ZIP entry check verified EXE, application DLL, .NET runtime, WPF framework,
  Chinese guide and demo launcher. **477 entries, 82,631,051 bytes**.

## Hardware observations and remaining acceptance

The BLE API probe returned no adapter. An elevated
`Get-PnpDevice -Class Bluetooth -PresentOnly` check also found no present Bluetooth
devices. Therefore **no real Garmin connection, real heart-rate latency, or game
compatibility result is claimed**.

The target computer still needs the checks in `ble-validation.md`: live 255
notifications, radio/broadcast recovery, startup after discovery-cache reset,
30-minute borderless-game use, mixed-DPI monitor movement/removal, and notification
to-display latency below the 500 ms target. The automated check covers native
click-through flags and nonactivation when showing the window, not a particular
game's handling of overlays.
