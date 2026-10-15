# Session Resume

Session Resume preserves the player's working context inside each Derail Valley save and shows a compact **Where you left off** panel after loading.

Version 1.0.0 is the initial public release for Derail Valley Build 99.7.

## Features

- **Save-Data Safety**:
  - Embedded data schema versioning with automatic migration of older save data.
  - Graceful recovery from missing, damaged, or partially corrupt JSON payloads without ever blocking the game save from loading.
  - In-game and UMM options for "Reset this save's context" and "Reset all Session Resume data" (with safe confirmations).
- **Honest Automatic Detection**:
  - Transparent labeling for inferred state: "Last detected locomotive", "Recently detached", and "Likely parked".
  - Pin/Confirm button (`[Park]` / `[Pin]`) to turn an inferred recently detached wagon or detected locomotive into an explicitly confirmed parked vehicle.
  - Pinned vehicles remain parked and are protected from automatic cleanup.
- **Focused Display Settings**:
  - Context is shown only on the loading screen and in-game; Derail Valley's Continue and Sessions screens remain untouched.
  - Independent toggles for the loading-screen section and in-world startup summary.
  - Startup display duration slider and "Keep startup summary visible until dismissed" mode with dismiss button (`✕`).
  - Adjustable UI scale (75%–135%) and panel opacity (50%–100%).
  - Separate limits for maximum displayed jobs and vehicles.
  - "Hide empty sections" option to suppress empty jobs, equipment, and note headers.
  - Fully rebindable dashboard hotkey (default **F8**).
- **Resolution & Input Safety**:
  - Tested across 1920×1080, 2560×1440, 3840×2160, and Ultrawide (21:9 / 32:9) aspect ratios.
  - Clamped window geometries that never extend beyond the screen or cover native buttons.
  - Raycast-safe overlays with zero input consumption while hidden.
  - Full keyboard, mouse, and gamepad/controller navigation (including **Escape** and controller Cancel to dismiss the dashboard).
- **Diagnostics & Verbose Logging**:
  - One-click "Copy diagnostic report" button copying mod/game build, resolution, UI scale, active menu/loading hook status, context item counts, and recent errors.
  - Optional verbose-logging toggle for detailed troubleshooting in the Unity Mod Manager log.

## Installation

Extract the `SessionResume` folder into `Derail Valley\Mods`, then enable the mod in Unity Mod Manager.

## Use

Play normally and the context refreshes automatically once per second. Press **F8** (or your custom bound key) to open the dashboard, pin or unpin equipment, or supply a next-action note that overrides the generated suggestion.

## Building

Build `DV_SessionResume.sln` in either Debug or Release. Every project build creates an installable archive in `artifacts` with this structure:

```text
SessionResume-v<VERSION>-<CONFIGURATION>.zip
└── SessionResume/
    ├── DV.SessionResume.dll
    ├── Info.json
    └── README.md
```

Each package also includes `CHANGELOG.md` with the public release history.

`Info.json` is the single source of truth for the mod ID, version, and assembly filename.
