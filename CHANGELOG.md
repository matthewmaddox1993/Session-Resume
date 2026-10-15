# Changelog

All notable changes to Session Resume are documented in this file.

## [1.0.0] - 2026-10-14

Initial public release for Derail Valley Build 99.7.

### Added

- Per-save working context stored directly inside each Derail Valley save.
- Automatic tracking of the last detected locomotive and accepted jobs.
- Recorded job routes and last known progress.
- Detection of recently detached wagons and likely parked equipment.
- Manual pinning of parked locomotives and wagons so automatic cleanup does not remove them.
- A player-written next-action note with an automatically generated fallback suggestion.
- A safe loading-screen summary that does not modify Derail Valley's loading UI hierarchy.
- An in-game dashboard opened with a configurable hotkey, defaulting to F8.
- Configurable startup-summary duration, UI scale, opacity, item limits, and empty-section hiding.
- Save-data schema migration and recovery from missing, damaged, or partially corrupt mod data.
- Reset controls for the current save or all Session Resume data.
- Diagnostic reporting and optional verbose logging.
- Automated Debug and Release packaging using the version from `Info.json`.

### Changed during pre-release development

- Removed the Continue and Sessions screen panels so Derail Valley's main menu remains untouched.
- Replaced direct loading-canvas modification with an independent, crash-resistant overlay.
- Improved loading-summary layout to display multiple active jobs and more recorded context.

### Compatibility

- Derail Valley Build 99.7.
- Unity Mod Manager 0.27.5 or newer.
