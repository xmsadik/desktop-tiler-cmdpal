using DesktopTiler.Core.Layouts;
using DesktopTiler.Core.Settings;
using Xunit;

namespace DesktopTiler.Tests;

public class SettingsParsingTests
{
    [Theory]
    [InlineData("MasterStack", LayoutKind.MasterStack)]
    [InlineData("Columns", LayoutKind.Columns)]
    [InlineData("Grid", LayoutKind.Grid)]
    [InlineData("Monocle", LayoutKind.Monocle)]
    [InlineData("CenterMaster", LayoutKind.CenterMaster)]
    public void ParseLayoutKind_ValidName_ReturnsThatKind(string raw, LayoutKind expected)
    {
        Assert.Equal(expected, SettingsParsing.ParseLayoutKind(raw));
    }

    [Theory]
    [InlineData("masterstack")] // wrong case - not an exact match
    [InlineData("Master-stack")] // display name, not the stored enum name
    [InlineData("NotALayout")]
    [InlineData("2")] // numeric strings must not silently resolve to a member
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseLayoutKind_GarbageOrEmpty_ReturnsDefault(string? raw)
    {
        Assert.Equal(SettingsParsing.DefaultLayoutKind, SettingsParsing.ParseLayoutKind(raw));
    }

    [Theory]
    [InlineData("40", 40)]
    [InlineData("45", 45)]
    [InlineData("50", 50)]
    [InlineData("55", 55)]
    [InlineData("60", 60)]
    [InlineData("65", 65)]
    [InlineData("70", 70)]
    public void ParseMasterRatioPercent_ValidChoice_ReturnsThatPercent(string raw, int expected)
    {
        Assert.Equal(expected, SettingsParsing.ParseMasterRatioPercent(raw));
    }

    [Theory]
    [InlineData("10")] // numerically valid but outside the offered range
    [InlineData("200")]
    [InlineData("-40")]
    [InlineData("bogus")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseMasterRatioPercent_OutOfRangeOrGarbageOrEmpty_ReturnsDefault(string? raw)
    {
        Assert.Equal(SettingsParsing.DefaultMasterRatioPercent, SettingsParsing.ParseMasterRatioPercent(raw));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("4", 4)]
    [InlineData("8", 8)]
    [InlineData("12", 12)]
    [InlineData("16", 16)]
    public void ParseGap_ValidChoice_ReturnsThatGap(string raw, int expected)
    {
        Assert.Equal(expected, SettingsParsing.ParseGap(raw));
    }

    [Theory]
    [InlineData("1")] // numerically valid but not one of the offered choices
    [InlineData("100")]
    [InlineData("-4")]
    [InlineData("bogus")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseGap_OutOfRangeOrGarbageOrEmpty_ReturnsDefault(string? raw)
    {
        Assert.Equal(SettingsParsing.DefaultGap, SettingsParsing.ParseGap(raw));
    }
}
