# Garmin 255 heart rate overlay

Approved scope: Windows x64, .NET 10/WPF, BLE direct from Forerunner 255,
borderless games, a 280x160 DIP dark card with 80% background opacity, BPM
and a 5 minute time-based trend. No server/account/phone bridge, disk heart
rate history, alarms, OBS output, or exclusive-fullscreen injection.

Tasks:
1. Core: BLE measurement parsing, 5 minute buffer, freshness, serialized
   connection/retry controller with stale session rejection; executable tests.
2. Windows BLE: device discovery/adapter diagnostics, GATT 180D/2A37,
   notification lifecycle and proper disposal.
3. WPF: custom trend drawing, nonactivating topmost overlay, locked click-through,
   tray/settings, hotkeys Ctrl+Alt+H/L, persisted settings and monitor recovery.
4. Verification: protocol/lifecycle tests, simulated UI smoke checks, release
   build, self-contained win-x64 ZIP, Chinese user guide and real-device checklist.

Interfaces: HeartRateContracts.cs is shared by the controller, simulated
transport and Windows BLE transport. Transport creates inactive connections;
controller subscribes before StartAsync. Core has no Windows dependencies.

Rulings: This is an empty non-git directory, so implement in place without
creating a worktree or manufacturing commit history. Use a dependency-free
executable test harness (nonzero exit on failures) for core behavioral tests.
Hardware and actual game acceptance must be reported separately from simulation.

Progress: all software delivery tasks complete. Core 19/19 tests and WPF 14/14
checks pass. Full Release solution build has zero warnings and zero errors.
Visual previews inspected. Review's cold OS BLE discovery cache reconnect
finding was fixed and the scoped re-review found no important regression.
Self-contained win-x64 package published and its executable startup verified:
coreclr.dll is loaded from the release directory. ZIP contents validated.
See verification.md for exact evidence and remaining physical acceptance.

Hardware verification: sandbox BLE probe returned no adapter. An elevated
Get-PnpDevice -Class Bluetooth -PresentOnly query likewise found no Bluetooth
devices. Real 255/game/mixed-DPI acceptance remains unverified on this machine.

## v1.1.0 user-requested follow-up

The user reported missing window resizing and requested more heart-rate elements.
The fixed-size v1.0 layout is superseded by an unlocked resizable card with
persisted dimensions (default 320×240, minimum 280×216 DIP), responsive typography,
BPM-derived heart animation with a setting, and five-minute average/min/max.
The existing BLE protocol and reconnection behavior remain as implemented.

The user also requested repository creation and a GitHub push after each completed
iteration. Git was initialized in place; remote is private
https://github.com/neepoo/heartBeatDisplayer, branch main. Repo-local author is
neepoo <neepoowzk@gmail.com>. Repository-local HTTPS uses gh's authenticated
credential helper because the local SSH signing agent could not sign. See
AGENTS.md for the ongoing completion/commit/push convention.
