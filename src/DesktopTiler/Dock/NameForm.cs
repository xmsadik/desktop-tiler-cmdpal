using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DesktopTiler;

/// <summary>
/// A one-field form (an Adaptive Card in the palette): a heading, a text box prefilled with the
/// current value, and a Save button that hands the typed text to <c>submit</c>. Used to rename
/// desktops and workspaces, and to set a workspace app's arguments.
/// </summary>
internal sealed partial class NameForm : FormContent
{
    private readonly string _commandId;
    private readonly Func<string?, CommandResult> _submit;

    public NameForm(string commandId, string heading, string currentValue, string placeholder, int maxLength, Func<string?, CommandResult> submit, string label = "Name")
    {
        _commandId = commandId;
        _submit = submit;
        TemplateJson = Template(heading, currentValue, placeholder, maxLength, label);
    }

    public override CommandResult SubmitForm(string payload)
    {
        try
        {
            return _submit(JsonNode.Parse(payload)?["name"]?.GetValue<string>());
        }
        catch (Exception ex)
        {
            return DesktopManager.ToToast(_commandId, ex);
        }
    }

    /// <summary>Strings go in through <see cref="JsonEncodedText"/> so any text (quotes,
    /// non-ASCII) stays valid JSON.</summary>
    private static string Template(string heading, string currentValue, string placeholder, int maxLength, string label)
    {
        var l = JsonEncodedText.Encode(label).ToString();
        var h = JsonEncodedText.Encode(heading).ToString();
        var v = JsonEncodedText.Encode(currentValue).ToString();
        var p = JsonEncodedText.Encode(placeholder).ToString();
        return $$"""
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard",
              "version": "1.5",
              "body": [
                { "type": "TextBlock", "text": "{{h}}", "weight": "Bolder", "size": "Medium", "wrap": true },
                { "type": "Input.Text", "id": "name", "label": "{{l}}", "value": "{{v}}", "placeholder": "{{p}}", "maxLength": {{maxLength}} }
              ],
              "actions": [
                { "type": "Action.Submit", "title": "Save" }
              ]
            }
            """;
    }
}
