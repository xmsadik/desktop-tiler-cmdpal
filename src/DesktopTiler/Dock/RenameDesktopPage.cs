using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using DesktopTiler.Core.VirtualDesktops;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// "Rename desktop": a one-field form (an Adaptive Card in the palette) prefilled with the
/// desktop's current name. An empty name removes the name. Built for a fixed desktop (a Dock
/// button or "Manage desktops" row) or for whichever desktop is current when the page opens
/// (the top-level "Rename current desktop" command).
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
            return [new RenameForm(_manager, desktop, _keepOpen)];
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"RenameDesktopPage.GetContent failed: {ex.Message}");
            return [new MarkdownContent($"Can't rename this desktop: {ex.Message}")];
        }
    }

    private sealed partial class RenameForm : FormContent
    {
        private readonly DesktopManager _manager;
        private readonly Guid _desktopId;
        private readonly bool _keepOpen;

        public RenameForm(DesktopManager manager, DesktopInfo desktop, bool keepOpen)
        {
            _manager = manager;
            _desktopId = desktop.Id;
            _keepOpen = keepOpen;
            TemplateJson = Template($"Desktop {desktop.Index + 1}", desktop.Name ?? string.Empty);
        }

        public override CommandResult SubmitForm(string payload)
        {
            const string commandId = "DesktopTiler.desktop.rename";
            try
            {
                var name = JsonNode.Parse(payload)?["name"]?.GetValue<string>();
                _manager.Rename(_desktopId, name);
                return _keepOpen ? CommandResult.GoBack() : CommandResult.Dismiss();
            }
            catch (Exception ex)
            {
                return DesktopManager.ToToast(commandId, ex);
            }
        }

        /// <summary>The card: a heading, the name box, a Save button. Strings go in through
        /// <see cref="JsonEncodedText"/> so any name (quotes, non-ASCII) stays valid JSON.</summary>
        private static string Template(string heading, string currentName)
        {
            var h = JsonEncodedText.Encode($"Rename {heading}").ToString();
            var v = JsonEncodedText.Encode(currentName).ToString();
            var max = DesktopManagementRules.MaxNameLength;
            return $$"""
                {
                  "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
                  "type": "AdaptiveCard",
                  "version": "1.5",
                  "body": [
                    { "type": "TextBlock", "text": "{{h}}", "weight": "Bolder", "size": "Medium", "wrap": true },
                    { "type": "Input.Text", "id": "name", "label": "Name", "value": "{{v}}", "placeholder": "Leave empty to remove the name", "maxLength": {{max}} }
                  ],
                  "actions": [
                    { "type": "Action.Submit", "title": "Save" }
                  ]
                }
                """;
        }
    }
}
