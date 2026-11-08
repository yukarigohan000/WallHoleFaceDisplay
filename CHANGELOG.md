# CHANGELOG

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
