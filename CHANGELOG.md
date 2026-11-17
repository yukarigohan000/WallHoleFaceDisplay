# CHANGELOG


## 0.2.78 — 2026-11-18
- Added compatibility with Mad Island v0.6.0.4 beta, Steam Build 26719145 (beta_open).
- Replaced removed RandomCharacter.GetBloodSlots() with the verified native GetLayeredSlots("blood") API. It includes all previous 16 blood slots plus HairFront_blood, preserving NPC ID44 state-dependent blood rendering.
- Retained facility identity, appearance, crafting, embedded defaults, editor controls, playback and save format.
- Final Release build against the updated game assemblies completed with zero warnings/errors; its DLL is byte-identical to the in-game-tested candidate.
- Developer in-game verification completed for startup, crafting/placement, NPC IDs 15/16/17/44/90, restraint playback, normal/fast/finish transitions, expressions/poses, independent monitors, settings editor and save/load. No issues were reported in that environment; other environments are not guaranteed.
- Added a public C# source snapshot. Game assemblies/assets, local settings, saves, diagnostics and deployment history are excluded.

## 0.2.77
- Removed the developer game-assembly SHA256 startup gate; retained actual API/patch-shape/asset/ID validation.
- Kept BepInEx 5 / Unity Mono as the intended runtime without enforcing an exact patch version or install location.
- Isolated optional debug initialization and diagnostic-file output failures from registration and normal settings.
- Replaced the development-only rig-report path with BepInEx's configured directory.
- Made startup config binding read-only and initial cfg creation best-effort; existing cfg files are not rewritten.
- Added field-level numeric/type recovery and embedded document fallback for damaged profiles, preserving original files and valid values.
- Added missing user animation-name fallback to bundled mappings where the actual skeleton supports them; existing validation remains.
- Retained v0.2.76 item identity/crafting/appearance/legacy retirement, all 25 embedded resources and standard profiles.
- Public ZIP contains the plugin DLL, bilingual README and this changelog only. No developer config/log/save/source/scripts.
- Release and limited static/configuration checks completed. Unity in-game verification for 0.2.77 is pending.

## 0.2.76 baseline
- Retired new crafting/placement of whfd_asswall_00 while preserving saved legacy references.
