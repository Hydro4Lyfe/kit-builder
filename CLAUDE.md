# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

PT Kit Builder: an Avalonia (.NET 10, C#) Windows desktop app that edits the deployment kit
(`../windows/deployment-kit`: an unattended Windows 11 install driven by `settings.json`,
`Scripts/<phase>/` and `Drivers/`). It is a sibling of PT Script Builder (`../script-generator`)
and deliberately shares its stack, palette and conventions.

## Commands

```bash
dotnet build KitBuilder.sln
dotnet test  KitBuilder.sln
dotnet test tests/KitBuilder.Core.Tests --filter "FullyQualifiedName~<Name>"
dotnet run --project src/KitBuilder.App
bash build/publish-win.sh
```

## The load-bearing rule

**The kit's files are the source of truth; the app keeps no state of its own** (only the
last-opened kit path in `%APPDATA%\PT Kit Builder\prefs.json`). Consequences:

- Never reimplement checking or building in C#. Call the kit's `tools/Test-Kit.ps1` and `tools/Build-Kit.ps1` via `KitTools`. If a check is missing, add it to `Test-Kit.ps1`.
- Every edit must be something a person could do by hand in Explorer, and the README of the kit must describe it.
- JSON is edited as `JsonNode` (`JsonDocumentFile`) so unknown keys survive. Saves write CRLF, 2-space indent, no BOM, relaxed escaping. Opening and saving an unchanged kit must produce byte-identical files (tested).
- Turning a script off moves it into `<phase>/_disabled/`. A leading `_` in a file name means "helper file", not "off".
- Reordering swaps two files' number prefixes rather than renumbering a phase, so references between scripts stay valid.

## Kit contract

`KitFolder.SupportedSchemaVersion` must match `$supportedSchemaVersion` in the kit's
`tools/Test-Kit.ps1`. `tests/KitBuilder.Core.Tests/Fixtures/kit/tools/` holds copies of the
kit's tools; refresh them when the kit's tools change.

## Conventions

- Same as PT Script Builder:
  - MVVM with CommunityToolkit.Mvvm;
  - `ViewModels/` and `Views/` mirror each other;
  - dialogs and shell access behind `Services/I*Service` for fakes;
  - compiled bindings;
  - `<summary>` comments explaining why.
- Avalonia is pinned to 11.2.3 to match PT Script Builder: no `Grid.RowSpacing`/`ColumnSpacing` (11.3+); use margins or the `Grid.form` style.
- Error messages are written for a technician, not a developer: what happened, and what to do.
- App tests are serialized (`TestAppBuilder.cs`). Headless tests must close the windows they show.
- Alert the operator before adding packages.
- No `Co-Authored-By: Claude` / "Generated with Claude Code" trailers (operator global instruction).
