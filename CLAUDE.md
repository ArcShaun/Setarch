# SetArch – notes for Claude

Windows WPF wizard that sets up architectural sheets (layouts, page setups, title blocks, viewports,
layers) in AutoCAD.

## Architecture
- `SetArch.Core` (net8.0, cross-platform) holds ALL layout logic. `SheetPlanBuilder.Build(answers)` resolves
  answers into a `SheetPlan` of concrete paper-space primitives (lines, polylines, text, viewports, block inserts).
- Two writers consume the same `SheetPlan`, so keep them in step when adding an entity type or setting:
  - `SetArch.App/Services/AutoCadComWriter.cs` – late-bound COM (`dynamic`) for full AutoCAD.
  - `SetArch.Core/Output/LispGenerator.cs` – core AutoLISP only (entmake/command, no `vla-*`) so it runs in AutoCAD LT 2024+.
- Geometry in `SheetGeometry` is designed in mm for an A1 sheet, scaled by paper size, converted to inches for imperial.

## Building and testing
- Linux containers: `dotnet test tests/SetArch.Core.Tests` works; the WPF app does NOT build on the Ubuntu
  source-built SDK (no WindowsDesktop SDK). The app is compiled by `.github/workflows/build.yml` on windows-latest —
  check that run after changing anything in `src/SetArch.App`.
- Visual check of layouts: `SETARCH_PREVIEW_DIR=<dir> dotnet test` writes SVG previews and sample .lsp files.
- Neither writer can be run against real AutoCAD here; say so when reporting changes to them.

## Conventions
- Never modify the user's existing layers, styles or layouts; skip and log instead.
- LISP output: numbers via `LispGenerator.N` (invariant culture), strings via `LispGenerator.Str`.
