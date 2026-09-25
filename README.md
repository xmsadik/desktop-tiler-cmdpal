# Desktop Tiler for Command Palette

A PowerToys Command Palette extension for switching virtual desktops and tiling windows, one shortcut at a time, from the **Dock**.

> Unofficial community project. Not affiliated with or endorsed by Microsoft.

- **In the Dock:** a *Desktops* band with one button per Windows virtual desktop (`1`, `2`, `3`...), the active one marked `●`, optionally subtitled with the desktop's name. Each button's icon shows whether that desktop has any app windows on it: a filled square if it does, an outlined one if it's empty (minimized windows count). Click a button to switch. Updates live as you switch, add, remove, or rename desktops, and as windows open, close, or move between desktops.
- **Switch desktops:** `Desktop 1`...`Desktop 9`, `Next desktop`, `Previous desktop` - bind hotkeys to them in Command Palette settings.
- **Tile windows, once:** `Tile: Master-stack`, `Tile: Columns`, `Tile: Grid`, `Tile: Monocle`, `Tile: Center-master` arrange the tileable windows on the focused monitor into that layout. `Tile: Next layout` cycles through them in that order. `Tile: Retile` re-applies whatever you last tiled with, keeping window order - if you clicked a different window first, that window becomes master. `Tile: Rotate` shifts the remembered window order by one step, so the next window in line becomes master; press it repeatedly until the one you want is in the master (or center) slot. After each pass, the layout's name and the result (e.g. **Grid** · *Tiled 4 windows*) flash briefly in the middle of that monitor, on top of the windows just moved; it doesn't take focus and clicks pass through it.

Tiling is **one-shot, not continuous**: nothing is watched or re-tiled automatically. Open or close a window, then press the tile command again. Each pass only touches the monitor of the currently focused window, and only the current virtual desktop; window order is remembered per desktop+monitor for as long as the extension keeps running.

## Requirements

- Windows 10 19041+ / Windows 11, PowerToys with Command Palette **0.9 or later** (Dock support)
- Windows 11 24H2 (build 26100) for virtual desktop switching and tiling - see [Limitations](#limitations)

## Installation

### 1. Turn on the Dock

1. Install or update [PowerToys](https://github.com/microsoft/PowerToys/releases) and make sure **Command Palette** is enabled in PowerToys Settings.
2. Open Command Palette (default <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd>) → **Settings** → **Dock (Preview)** → turn on **Enable Dock**.

### 2. Download

From the [Releases page](https://github.com/xmsadik/desktop-tiler-cmdpal/releases) (newest release at the top) download:
- `DesktopTilerDev.cer`
- the package for your CPU: `DesktopTiler_<version>_x64.msix` (Intel/AMD) or `DesktopTiler_<version>_arm64.msix` (Arm, e.g. Snapdragon). Not sure? Run `$env:PROCESSOR_ARCHITECTURE` in PowerShell: `AMD64` → x64, `ARM64` → arm64.

### 3. Trust the certificate (once per machine)

The package is signed with a self-signed certificate, so Windows has to be told to trust it. In **PowerShell as Administrator**, in the download folder:

```powershell
Import-Certificate .\DesktopTilerDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

### 4. Install

In a normal PowerShell window (or double-click the `.msix` and choose *Install*):

```powershell
Add-AppxPackage .\DesktopTiler_<version>_x64.msix
```

### 5. Show it in the Dock

1. Open Command Palette and run **Reload** so it picks up the new extension.
2. The *Desktops* band usually appears in the Dock by itself. If it doesn't, search for **Desktop Tiler** in Command Palette, open its context menu and run **Pin to Dock** (choose the side you like, e.g. *Right*).
3. Click the band to switch desktops. Bind hotkeys to the tiling and desktop-switching commands (see [Suggested hotkeys](#suggested-hotkeys)). Settings: search **Desktop Tiler** → *Settings*.

### Update

Download the newer `.msix` and run `Add-AppxPackage` again; the certificate step isn't needed again. Then **Reload** Command Palette.

### Uninstall

```powershell
Get-AppxPackage DesktopTiler | Remove-AppxPackage
# optional, as Administrator: remove the trusted certificate
Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=DesktopTilerDev' | Remove-Item
```

### Troubleshooting

| Symptom | Fix |
|---|---|
| `0x800B0109` / "the root certificate … is not trusted" on install | Step 3 was skipped or not run as Administrator. |
| `0x80073CFB` / "a package with the same identity is already installed" | A development build is registered: `Get-AppxPackage DesktopTiler \| Remove-AppxPackage`, then install again. |
| Band doesn't appear | Check **Enable Dock** is on, run **Reload**, then use **Pin to Dock** as in step 5. |
| Toast says "Unsupported Windows build for desktop switching" | Your Windows build's undocumented virtual-desktop COM layout doesn't match what this extension expects - see [Limitations](#limitations). Wait for an update, or check the project's issues. |
| A window is reported "skipped (admin)" | The window belongs to an elevated (Run as administrator) process; a non-elevated extension can't move it - see [Limitations](#limitations). |
| Clicking the band opens the palette instead of the flyout, or it stops updating | Command Palette host issues, see [Known host issues](#known-host-issues). **Reload** fixes both. |

## Commands

| Command | Id |
|---|---|
| Desktop 1 ... Desktop 9 | `DesktopTiler.desktop.1` ... `DesktopTiler.desktop.9` |
| Next desktop | `DesktopTiler.desktop.next` |
| Previous desktop | `DesktopTiler.desktop.prev` |
| Tile: Master-stack | `DesktopTiler.tile.masterstack` |
| Tile: Columns | `DesktopTiler.tile.columns` |
| Tile: Grid | `DesktopTiler.tile.grid` |
| Tile: Monocle | `DesktopTiler.tile.monocle` |
| Tile: Center-master | `DesktopTiler.tile.centermaster` |
| Tile: Next layout | `DesktopTiler.tile.next` |
| Tile: Retile | `DesktopTiler.tile.retile` |
| Tile: Rotate | `DesktopTiler.tile.rotate` |
| Desktop Tiler settings | (settings page) |

Ids are stable across releases, so a hotkey bound to one keeps working; `Desktop 3`, for example, always means "the 3rd desktop in display order right now", not a specific desktop that might later be removed.

### Suggested hotkeys

Bind these in Command Palette → *Settings* → *Commands*:

| Hotkey | Command |
|---|---|
| <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>1</kbd>...<kbd>9</kbd> | Desktop 1...9 |
| <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>Space</kbd> | Tile: Next layout |
| <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>R</kbd> | Tile: Retile |
| <kbd>Win</kbd>+<kbd>Alt</kbd>+<kbd>O</kbd> | Tile: Rotate |

Avoid <kbd>Alt</kbd>+*number* - it collides with browser tab-switching shortcuts globally - and avoid <kbd>Alt</kbd>+<kbd>Shift</kbd> combinations, which Windows uses to switch keyboard layout.

## Settings

Command Palette → *Desktop Tiler* → *Settings*:
- **Default layout**: the layout `Tile: Retile` and `Tile: Next layout` start from before anything has been tiled this session (default Master-stack).
- **Master ratio**: how much of the work area the master pane takes up, for Master-stack and Center-master, 40-70% (default 55%).
- **Gap**: space left between tiled windows and the work area's edge: 0, 4, 8, 12, or 16 px (default 8 px).
- **Switch animation**: animate when switching desktops (default on).
- **Show desktop names**: show desktop names as the Dock band's subtitles (default on).

## Limitations

- Desktop switching and tiling use undocumented Windows virtual-desktop COM interfaces, pinned to the layout observed on Windows 11 24H2 (build 26100). On other builds these interfaces may differ, and commands show an "Unsupported Windows build" toast until an update adds support for that build.
- An elevated (Run as administrator) window can't be moved from this extension's non-elevated process; it's reported as "skipped (admin)" instead of tiled.
- Each tile pass only arranges windows on a single monitor - the one the focused window is on.
- A window with a large minimum size may not fully fit its assigned cell and can overflow it.
- The package declares the restricted `unvirtualizedResources` capability so that registry change notifications for the virtual desktop list (which live outside the app's virtualized registry view) actually reach the extension.

## Build from source

Needs the .NET 10 SDK. A full Windows SDK / Visual Studio is **not** required.

```powershell
dotnet test tests\DesktopTiler.Tests -p:Platform=x64      # unit tests
.\scripts\dev-deploy.ps1                                  # build + register (Developer Mode on)
.\scripts\dev-deploy.ps1 -Remove                          # unregister
```

After deploying, run **Reload** in Command Palette. If the band does not appear by itself, use **Pin to Dock** (see [Installation](#5-show-it-in-the-dock)).

### MSIX package

```powershell
.\scripts\pack.ps1 -Sign        # dist\...\DesktopTiler_<ver>_x64.msix + dist\DesktopTilerDev.cer
```

`-Platform ARM64` builds the Arm package. The first `-Sign` run creates a self-signed `CN=DesktopTilerDev` code-signing certificate in `Cert:\CurrentUser\My` and reuses it afterwards. Install the result as described in [Installation](#installation).

## Layout

```
src/DesktopTiler/        Command Palette extension (Dock band, commands, settings, Tiler)
src/DesktopTiler.Core/   Plain .NET library: virtual desktop COM/registry, layouts, window order memory
tests/DesktopTiler.Tests xUnit tests for Core
scripts/                 dev-deploy.ps1, pack.ps1
```

## Known host issues

These are Command Palette bugs, not bugs in this extension:
- [#50367](https://github.com/microsoft/PowerToys/issues/50367): after the host releases an idle extension, clicking a band opens the palette instead of the flyout.
- [#49688](https://github.com/microsoft/PowerToys/issues/49688): bands stop repainting after roughly 41 hours of uptime.

Running **Reload** in Command Palette works around both.

## License

[MIT](LICENSE). Parts derived from the PowerToys extension template are © Microsoft, MIT; the virtual desktop COM interface definitions are based on Markus Scholtes' VirtualDesktop project, MIT; see [NOTICE](NOTICE).
