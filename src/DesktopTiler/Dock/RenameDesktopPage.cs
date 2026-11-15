using System;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "Rename desktop": a <see cref="NameForm"/> prefilled with the desktop's current name. An empty
/// name removes the name. Built for a fixed desktop (a "Manage desktops" row) or for whichever
/// desktop is current when the page opens (the top-level "Rename current desktop" command).
/// </summary>
internal sealed partial class RenameDesktopPage : ContentPage
{
    private static readonly IconInfo RenameIcon = new(""); // Rename

    private readonly DesktopManager _manager;
    private readonly Guid? _desktopId;
    private readonly bool _keepOpen;

    /// <param name="desktopId">The desktop to rename; null for the current one.</param>
    /// <param name="keepOpen">True when opened from "Manage desktops": saving goes back to that
    /// list instead of closing the palette.</param>
    public RenameDesktopPage(DesktopManager manager, Guid? desktopId, bool keepOpen)
    {
        _manager = manager;
        _desktopId = desktopId;
        _keepOpen = keepOpen;

        Name = "Rename desktop";
        Title = "Rename desktop";
        Icon = RenameIcon;
    }

    public override IContent[] GetContent()
    {
        // Called directly by the host: must never throw.
        try
        {
            var (desktop, _) = _manager.Find(_desktopId);
            var id = desktop.Id;
            return
            [
                new NameForm(
                    "DesktopTiler.desktop.rename",
                    $"Rename Desktop {desktop.Index + 1}",
                    desktop.Name ?? string.Empty,
                    "Leave empty to remove the name",
                    DesktopManagementRules.MaxNameLength,
                    name =>
                    {
                        _manager.Rename(id, name);
                        return _keepOpen ? CommandResult.GoBack() : CommandResult.Dismiss();
                    }),
            ];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"RenameDesktopPage.GetContent failed: {ex.Message}");
            return [new MarkdownContent($"Can't rename this desktop: {ex.Message}")];
        }
    }
}
