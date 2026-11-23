# SetArch – notes for Claude

Windows WPF app that creates a new architecture project from the user's OWN standards: builds a
configurable folder tree, copies their template files (title block, cover sheet …) into it, and
replaces placeholders like `[[PROJECT_NAME]]` in folder names, file names and inside DWGs.
The app must not generate drawing content (title blocks, layouts, layers); the user explicitly asked
for it only to change text in their own standards.

## Architecture
- `SetArch.Core` (net8.0, cross-platform) has all logic:
  - `Models/StandardsProfile.cs`: questions (`FieldDefinition`), folder tree (`FolderNode`), `TemplateFile`s; saved as JSON by `StandardsStore`.
  - `Planning/ProjectPlanner.Plan` resolves everything to absolute paths with errors and warnings, and touches nothing.
    `ProjectCreator.Create` executes it and never overwrites.
  - `Output/FillScript.cs`: core-AutoLISP-only (no `vla-*`) placeholder filler; runs in accoreconsole and AutoCAD LT 2024+.
    `Output/AutoCadConsole.cs` runs `accoreconsole.exe /i <dwg> /s <scr>` and parses the `SETARCH-RESULT n` marker.
- `SetArch.App`: one window with "New project" and "My standards" tabs; `ViewModels/MainViewModel.cs`.

## Building and testing
- Linux containers: `dotnet test tests/SetArch.Core.Tests` works. The WPF app does NOT build on the Ubuntu
  source-built SDK (no WindowsDesktop SDK); `.github/workflows/build.yml` builds it on windows-latest.
  Check that run after changing `src/SetArch.App`.
- Neither the fill script nor accoreconsole can be run against real AutoCAD here; say so when reporting.

## Conventions
- Never overwrite user files or modify their template sources.
- LISP strings go through `FillScript.Str`; values are made ASCII with `\U+XXXX` (`FillScript.Unicode`)
  and MTEXT-escaped (`FillScript.MText`). The console script must contain no blank lines.
