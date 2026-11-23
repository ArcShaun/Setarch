# SetArch

A Windows app that sets up a new architecture project from **your office's own standards**.
You answer a few questions, pick where the project goes, and press **Create project**. SetArch then:

1. creates your project folder tree (configurable, e.g. `2026-014 - Smith Residence/03 CAD/Sheets`),
2. copies your template files (title block, cover sheet, sheets, spreadsheets …) into the right folders,
   renamed the way you want (e.g. `2026-014 A-000 Cover.dwg`),
3. fills in the project details inside the drawings.

It doesn't draw anything itself. Your title block, cover sheet and other standards are used exactly
as you made them; SetArch only changes their text.

## The questions

By default: **Project name, Project #, Address, Project type, Date, Occupancy type, Area**.
You can rename, remove or add questions under *My standards → Questions*. Each has a key, e.g. `PROJECT_NAME`.

## Placeholders

Put placeholders wherever a project detail should go:

| Where | Example |
|---|---|
| Folder names | `[[PROJECT_NUMBER]] - [[PROJECT_NAME]]` |
| Save-as file names | `[[PROJECT_NUMBER]] A-000 Cover.dwg` |
| Text / MTEXT in a drawing | `PROJECT: [[PROJECT_NAME]]`, `OCCUPANCY: [[OCCUPANCY_TYPE]]` |
| Attributes in a title block | a value of `[[AREA]]`, **or** an attribute tagged `AREA` (tag matching ignores case, `_` and `-`) |

Placeholders are found in model space, on every layout, and inside block definitions, so a title
block that is a block updates on every sheet. The `[[` `]]` markers can be changed under *Questions*.

## Title block

The default title block (edit it under *My standards → Title block*):

| Tag / placeholder | Gets | Example |
|---|---|---|
| `LINE1` | project type | Single-Family Residential |
| `LINE2` | project name (line 1) | The Smith Family Lake |
| `LINE3` | rest of a long project name, else empty | House Renovation |
| `LINE4` | street address (before the first comma) | 1234 Lakeshore Drive |
| `LINE5` | city, state ZIP (after the first comma) | Springfield, IL 62704 |
| `JOB_NO` | project # | 2026-014 |
| `DATE` | date, written mm/dd/yyyy | 03/07/2026 |
| `COPYRIGHT` | year of the project date | 2026 |

In your title block drawing, either name the attributes with those tags, or type the placeholder
(e.g. `[[LINE2]]`) where the text goes. A long name is split between words so line 2 holds at most
25 characters (changeable). Type the address as `street, city, state ZIP`.

## Setting up your standards (*My standards* tab)

- **Folder structure:** build the tree, or **Import from a folder…** to copy the tree (and, optionally,
  the files) of an existing blank project folder.
- **Template files:** each file has the folder it goes in, a save-as name, whether to fill in its
  text, and optionally a project type it applies to (e.g. a commercial code sheet only for *Commercial*).
- **Export / Import standards** share the setup with the rest of the office as a `.setarch.json` file.

Existing folders and files are never overwritten. Running Create again on the same project only adds what's missing.

## Filling in drawings

DWG files can only be edited by AutoCAD. SetArch uses **AutoCAD's Core Console** (`accoreconsole.exe`),
which comes with full AutoCAD and its verticals (Architecture, MEP …). It edits the drawings in the
background without opening the AutoCAD window. SetArch finds it automatically, or you can set the path under
*My standards → AutoCAD*.

**AutoCAD LT** doesn't include the Core Console. Tick *Also save a SetArch Fill.lsp*; then in each
drawing run `APPLOAD` on that file, type `SETARCHFILL`, and save (LT 2024 or newer).

Not filled: text inside tables, multileaders and dimensions, and xrefs (fill the xref'd file itself).

## Download / build

Every push builds `SetArch.exe` (self-contained, no .NET install needed) in GitHub Actions:
open the latest **build** run and download the `SetArch-win-x64` artifact.

To build yourself on Windows with the .NET 8 SDK:

```
dotnet test tests/SetArch.Core.Tests
dotnet run --project src/SetArch.App
```

## Project layout

- `src/SetArch.Core`: standards model and JSON storage, placeholder resolution, project planner
  (works out every folder and file first), creator (folders and copies), folder import,
  AutoLISP fill script, and the Core Console runner
- `src/SetArch.App`: the WPF window (Windows only)
- `tests/SetArch.Core.Tests`: xUnit tests for Core
