# Privacy Policy

_Last updated: 2026-11-16_

Desktop Tiler is a Command Palette extension that switches and manages Windows virtual desktops and moves and arranges windows on your screen. This policy explains what it does with your information.

## Summary

Desktop Tiler does **not** collect, transmit, sell, or share any personal information. It has no network access, no telemetry, no analytics, no advertising, and no accounts. Everything it reads stays on your device, and the little it saves is saved only in its own folder on your device.

## What the extension reads on your device

To do its job, the extension reads the following on your own computer while it runs:

- **Your virtual desktops:** their list, order, names, and which one is active, read from Windows (the `VirtualDesktops` registry key and the Windows virtual desktop interfaces).
- **Your open windows:** their title, position, size, state (for example minimized), which virtual desktop and monitor they are on, whether they are shown on all desktops, and the path and name of the program that owns them. Titles are shown in the extension's own window lists (for example **All windows**) so you can find a window; they are kept in memory only and never saved.
- **Your monitors:** their size and usable work area.
- **Command Palette's Dock layout:** whether the Dock currently shows button labels, read from Command Palette's own settings file, so the desktop buttons can show their window list only where it fits.

## What the extension saves

All of these files are in the extension's own local app data folder on your device, and only change when you change them:

- **Settings** (`settings.json`): the options you choose, such as the default layout, master ratio, gap, switch animation, and whether desktop names are shown.
- **Window rules** (`rules.json`), if you create any: for each rule, the program's name and path and the desktop its new windows go to.
- **Desktop workspaces** (`workspaces.json`), if you save any: each workspace's name and, for each app in it, the program's path, its desktop, and the command-line arguments you entered for it.
- **Start shortcuts** (`launch\*.lnk`): when a workspace app has command-line arguments, a Windows shortcut holding the program and those arguments is written here and used to start the app.

The order in which you used your desktops, and the order of tiled windows, are kept in memory only and are gone when the extension stops.

## What the extension does on your device

At your request, the extension switches, creates, renames, reorders and deletes virtual desktops; moves windows between desktops, arranges them on the screen, and asks them to close; shows windows or apps on all desktops; and, when you open a desktop workspace, starts the apps saved in it (through Windows Explorer). It does nothing of this on its own, except that windows matching a window rule you created are moved to the rule's desktop when they open.

Nothing leaves your device.

## Changes to this policy

If this policy changes, the updated version will be published at this address with a new "Last updated" date.

## Contact

For questions about this policy, open an issue at <https://github.com/xmsadik/desktop-tiler-cmdpal/issues>.
