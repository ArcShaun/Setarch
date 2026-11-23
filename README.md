# SetArch

A Windows app that sets up architectural drawing sheets in AutoCAD. It asks a short set of
questions, then **Create** builds every sheet for you.

## What it asks

1. **Project** – name, number, client, site address, firm, drawn/checked by, date, revision, status
2. **Paper & plotting** – ISO A0–A4, ARCH C–E1 or ANSI B–D; orientation; plotter (PC3); plot style table
3. **Title block** – a generated one (vertical strip on the right or along the bottom), or your own DWG
4. **Which sheets** – cover, general notes, site plan, basement and floor plans, roof plan, RCPs,
   elevations, sections, details, schedules; numbering by drawing type (G-001, A-101, A-201 …) or sequential
5. **Sheet list** – edit numbers, titles, sheet types and viewport scales in a table
6. **Drawing standards** – AIA/NCS-style architectural layers, text styles, view titles, locked viewports
7. **Review & create** – live preview of each sheet, then create

## What it creates

For every sheet: a layout tab with a saved page setup (plotter, paper, 1:1, plot style),
a border and filled-in title block, a viewport at the right scale (on a non-plotting layer),
and a view title. The cover sheet gets the project title and a sheet index. The drawing also
gets units, layers, linetypes and text styles. Existing layouts, layers and styles are never
changed. If a layout with the same name already exists, that sheet is skipped.

### Two ways to create

| | Create in AutoCAD now | Save an AutoLISP file |
|---|---|---|
| Works with | Full AutoCAD 2018+ (incl. AutoCAD Architecture) | Full AutoCAD, **AutoCAD LT 2024+** |
| How | Attaches to (or starts) AutoCAD over COM and builds the sheets | Writes a `.lsp`; in AutoCAD run `APPLOAD`, then `SETARCH` |

### Using your own title block

Pick a `.dwg` drawn at 1:1 in paper units with its lower-left corner at 0,0. Attributes are filled
by tag (case, spaces, `-` and `_` are ignored): `PROJECT_NAME`, `PROJECT_NO`, `CLIENT`, `ADDRESS`,
`FIRM`, `SHEET_TITLE`, `SHEET_NO`, `SHEET_OF`, `SCALE`, `DRAWN`, `CHECKED`, `DATE`, `REV`, `STATUS`,
plus common aliases (`DWG_NO`, `DRAWN_BY`, `TITLE`, `REVISION`, …; see
`src/SetArch.Core/Planning/TitleBlockFields.cs`). Enter the margins your title block uses so the
viewport fits in the free area.

Answers can be saved to a `.setarch.json` file and reopened for the next project. The last answers are
remembered automatically.

## Download / build

Every push builds `SetArch.exe` (self-contained, no .NET install needed) in GitHub Actions:
open the latest **build** run and download the `SetArch-win-x64` artifact.

To build yourself on Windows with the .NET 8 SDK:

```
dotnet test tests/SetArch.Core.Tests
dotnet run --project src/SetArch.App
```

## Project layout

- `src/SetArch.Core` – cross-platform logic: answers model, standards (paper sizes, scales, layers),
  sheet list generator, plan builder (all geometry), AutoLISP exporter, SVG preview
- `src/SetArch.App` – WPF wizard and the AutoCAD COM writer (Windows only)
- `tests/SetArch.Core.Tests` – xUnit tests for Core

## Notes and limits

- SetArch must run at the same privilege level as AutoCAD (don't run one of them as administrator).
- The page setup uses the paper size names of `DWG To PDF.pc3`. For other plotters it looks for a
  matching size; if none is found it tells you and you can set it with `PAGESETUP`.
- Viewports are created empty and centred on 0,0 in model space; pan them onto your drawing
  (or unlock, pan, re-lock).
