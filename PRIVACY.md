# Privacy Policy

_Last updated: 2026-09-25_

Desktop Tiler is a Command Palette extension that switches Windows virtual desktops and arranges windows on your screen. This policy explains what it does with your information.

## Summary

Desktop Tiler does **not** collect, store, transmit, sell, or share any personal information. It has no network access, no telemetry, no analytics, no advertising, and no accounts.

## What the extension reads on your device

To do its job, the extension reads the following on your own computer while it runs. It uses them only in memory and never sends them anywhere:

- **Your virtual desktops:** their list, order, names, and which one is active, read from Windows (the `VirtualDesktops` registry key and the Windows virtual desktop interfaces).
- **Your open windows:** their position, size, state (for example minimized), which virtual desktop and monitor they are on, and the name of the program that owns them. The extension uses this to decide which windows to arrange, and to show whether each desktop has any windows on it.
- **Your monitors:** their size and usable work area.

Window titles are not read or stored, except to check whether a window has one.

## What the extension stores

- **Settings:** the options you choose (default layout, master ratio, gap, switch animation, show desktop names) are saved in a `settings.json` file inside the extension's own local app data folder on your device.
- **Window order memory:** the order of tiled windows is kept in memory only, and is gone when the extension stops.

Nothing else is saved, and nothing leaves your device.

## Changes to this policy

If this policy changes, the updated version will be published at this address with a new "Last updated" date.

## Contact

For questions about this policy, open an issue at <https://github.com/xmsadik/desktop-tiler-cmdpal/issues>.
