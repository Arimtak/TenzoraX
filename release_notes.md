## TenzoraX v1.0.2 - Fix default button layout, add reset button

### Fixed
- Default button positions now match the correct controller reference layout
- Button and stick positions restore correctly on startup and persist after restart
- Added "Reset button positions" button in edit mode
- Added debug logging for save/load diagnostics
- L3/R3 stick dragging no longer resets to center every frame
- App no longer crashes on startup (resource path error)
- Icons and images are now embedded as assembly resources

### Changed
- Corrected relative positions for all 16 buttons (A/B/X/Y diamond, L3/R3, D-Pad, etc.)
- Single-file EXE, no external assets folder
- Resource items instead of Content items for assets

### Usage
1. Start the app - buttons appear at default positions
2. Enable Edit mode > Buttons > drag to customize
3. Click "Reset button positions" to restore defaults (saved immediately)
