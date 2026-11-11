# PT Kit Builder

A Windows desktop app for maintaining the **deployment kit** (the unattended Windows 11
install in `premier-tech/windows/deployment-kit`) without working through its README.

- **Settings:** edition, time zone, language, Wi-Fi, passwords, apps removed from Windows, Start pins.
- **Scripts:** each install phase's scripts, with on/off switches, run order, add a file, new `.reg`.
- **Drivers:** per-model driver folders and their `match.txt`, importing driver packs.
- **Build:** put the kit on a USB stick, or build an ISO for a VM.

Built with Avalonia on .NET 10, the same stack and look as PT Script Builder.

## How it works: the kit's files are the source of truth

PT Kit Builder stores nothing of its own except which kit folder was open last
(`%APPDATA%\PT Kit Builder\prefs.json`). Every action is an ordinary edit to the kit's
files:

| In the app | On disk |
| --- | --- |
| Save settings | rewrites `settings.json` and `secrets.json`, keeping keys the app does not know about |
| Switch a script off / on | moves it into / out of `Scripts\<phase>\_disabled\` |
| Move a script up / down | swaps the number prefixes of the two files |
| Add model | creates `Drivers\<model>\match.txt` |
| Overview check | runs the kit's `tools\Test-Kit.ps1 -Format Json` |
| Build | runs the kit's `tools\Build-Kit.ps1 -UsbDrive X:` or `-Iso` |

So the kit still works without the app, anything the app does can be done by hand, and
checking/building has exactly one implementation (the kit's PowerShell tools). On Windows
the tools run in Windows PowerShell 5.1, the same as a technician running them by hand.

## Installation

Grab `PT Kit Builder.exe` from the latest [GitHub Release](../../releases/latest) and run
it. It is fully self-contained (no .NET runtime needed). There is no auto-update: to
update, download the newer exe and replace the old one.

Requirements: Windows 10/11 x64 (also runs on Windows on ARM through emulation). The
`.exe` is **unsigned**, so SmartScreen/Defender may warn on first run.

## Using it

Run `PT Kit Builder.exe` and open the `deployment-kit` folder. The app finds the kit on
its own if the exe sits inside or next to that folder. The Overview tab shows what
`Test-Kit.ps1` reports and updates after every change.

## Development

```bash
dotnet build KitBuilder.sln
dotnet test  KitBuilder.sln             # Core + headless UI tests, all on Linux
dotnet run --project src/KitBuilder.App # needs a graphical session (WSLg)
bash build/publish-win.sh               # -> build/publish-win/PT Kit Builder.exe (self-contained)
```

Tests that run the real kit tools need `pwsh` on PATH (and `xorriso` for the ISO test);
they are skipped when it is missing.

Version: `<Version>` in `Directory.Build.props`.

### CI and releases

Two GitHub Actions workflows ([`.github/workflows/`](.github/workflows)):

- **`ci.yml`**: on every push/PR to `main`, restore, build and run the full test suite on Linux.
- **`release-app.yml`**: runs on pushes to `main`. When `<Version>` in [`Directory.Build.props`](Directory.Build.props) has no `v{VERSION}` tag yet, it runs the tests, publishes the Windows `.exe` and creates the tag and GitHub Release with the exe attached.

To ship a release: bump `<Version>`, commit, push.

### Layout

- `src/KitBuilder.Core/`: no UI.
  - `Kit/` holds the kit folder model (`KitFolder`), JSON editing that preserves unknown keys (`JsonDocumentFile`, `KitSettings`), `ScriptInventory` and `DriverLibrary`.
  - `Tools/` runs the kit's PowerShell tools (`PowerShellRunner`, `KitTools`) and lists drives.
- `src/KitBuilder.App/`: Avalonia MVVM. `ViewModels/` and `Views/` mirror each other. Dialogs and shell access are behind `Services/` interfaces, so tests use fakes.
- `tests/KitBuilder.Core.Tests/Fixtures/kit/`: a small kit used by both test projects. Each test works on a temp copy.

### Keeping in step with the kit

The app depends on three things in the kit:

1. **`schemaVersion` in `settings.json`.** `KitFolder.SupportedSchemaVersion` must match `$supportedSchemaVersion` in the kit's `tools/Test-Kit.ps1`. Bump both together on an incompatible layout change. The app refuses kits newer than it understands.
2. **`Test-Kit.ps1 -Format Json`** printing `{ "errors": [...], "warnings": [...] }`.
3. **`Build-Kit.ps1 -UsbDrive X:` / `-Iso -IsoPath <path>`**, with exit code 0 on success.

`Fixtures/kit/tools/` holds copies of the kit's two tool scripts, so `KitToolsTests`
doubles as a contract test. When the kit's tools change, copy them in again and run the
tests.

## Windows QA checklist

These can't be checked from WSL. Check them on Windows before relying on them:

- [ ] Build to a USB stick (a Media Creation Tool stick): the kit lands on the root, and `BitLockerKeys\` is left alone.
- [ ] The recovery-key warning appears for a stick with files in `BitLockerKeys\`.
- [ ] The drive list shows USB sticks, and fixed drives only when that option is ticked.
- [ ] Build an ISO on Windows (IMAPI path), then boot a Hyper-V VM with it.
- [ ] Import a `.cab` driver pack (`expand.exe`).
- [ ] Open / Show buttons open the editor and Explorer, with the file selected.
- [ ] Rendering at 125% / 150% display scaling.
