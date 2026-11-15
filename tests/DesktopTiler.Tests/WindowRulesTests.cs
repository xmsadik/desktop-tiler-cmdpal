using System;
using System.IO;
using DesktopTiler.Core.Rules;
using Xunit;

namespace DesktopTiler.Tests;

public class WindowRulesTests
{
    private static readonly Guid D1 = Guid.NewGuid();
    private static readonly Guid D2 = Guid.NewGuid();
    private static readonly Guid Gone = Guid.NewGuid();
    private static readonly Guid[] Desktops = [D1, D2];

    private static readonly WindowRule[] Rules = [new("OUTLOOK.EXE", D1, @"C:\x\OUTLOOK.EXE")];

    [Fact]
    public void TargetFor_MovesMatchingAppCaseInsensitively()
    {
        Assert.Equal(D1, WindowRules.TargetFor(Rules, @"C:\Program Files\Office\outlook.exe", D2, Desktops));
    }

    [Fact]
    public void TargetFor_NullWhenAlreadyOnTarget()
    {
        Assert.Null(WindowRules.TargetFor(Rules, @"C:\x\OUTLOOK.EXE", D1, Desktops));
    }

    [Fact]
    public void TargetFor_NullForOtherApps()
    {
        Assert.Null(WindowRules.TargetFor(Rules, @"C:\x\notepad.exe", D2, Desktops));
    }

    [Fact]
    public void TargetFor_NullWhenRuleDesktopRemoved()
    {
        WindowRule[] rules = [new("OUTLOOK.EXE", Gone, string.Empty)];
        Assert.Null(WindowRules.TargetFor(rules, @"C:\x\OUTLOOK.EXE", D2, Desktops));
    }

    [Fact]
    public void TargetFor_NullWhenWindowOnNoKnownDesktop()
    {
        // A window shown on all desktops reports an id that matches no desktop.
        Assert.Null(WindowRules.TargetFor(Rules, @"C:\x\OUTLOOK.EXE", Guid.NewGuid(), Desktops));
        Assert.Null(WindowRules.TargetFor(Rules, @"C:\x\OUTLOOK.EXE", Guid.Empty, Desktops));
    }

    [Fact]
    public void TargetFor_NullWhenPathUnknown()
    {
        Assert.Null(WindowRules.TargetFor(Rules, string.Empty, D2, Desktops));
    }

    [Theory]
    [InlineData(@"C:\x\OUTLOOK.EXE", true)]
    [InlineData(@"C:\Windows\System32\ApplicationFrameHost.exe", false)]
    [InlineData(@"C:\Windows\System32\applicationframehost.EXE", false)]
    [InlineData("", false)]
    public void CanHaveRule_ExcludesUwpHostAndUnknown(string path, bool expected)
    {
        Assert.Equal(expected, WindowRules.CanHaveRule(path));
    }

    [Fact]
    public void Serialize_ThenParse_RoundTrips()
    {
        WindowRule[] rules = [new("OUTLOOK.EXE", D1, @"C:\x\OUTLOOK.EXE"), new("Çağrı \"q\".exe", D2, string.Empty)];
        var parsed = WindowRuleStore.Parse(WindowRuleStore.Serialize(rules));
        Assert.Equal(rules, parsed);
    }

    [Fact]
    public void Parse_SkipsMalformedEntries()
    {
        var json = $$"""
            { "rules": [
              { "app": "a.exe", "desktop": "{{D1}}" },
              { "app": "b.exe", "desktop": "not-a-guid" },
              { "app": "", "desktop": "{{D1}}" },
              { "desktop": "{{D1}}" },
              42
            ] }
            """;
        var rule = Assert.Single(WindowRuleStore.Parse(json));
        Assert.Equal(new WindowRule("a.exe", D1, string.Empty), rule);
    }

    [Fact]
    public void Store_SetReplacesSameApp_RemoveDeletes_AndPersists()
    {
        var dir = Directory.CreateTempSubdirectory("dt-rules-");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.json");
            var store = new WindowRuleStore(path);
            Assert.Empty(store.Rules);

            store.Set(new WindowRule("OUTLOOK.EXE", D1, string.Empty));
            store.Set(new WindowRule("outlook.exe", D2, string.Empty));
            store.Set(new WindowRule("code.exe", D1, string.Empty));
            Assert.Equal(2, store.Rules.Count);
            Assert.Equal(D2, WindowRules.Find(new WindowRuleStore(path).Rules, @"C:\OUTLOOK.EXE")!.DesktopId);

            store.Remove("OUTLOOK.exe");
            Assert.Equal("code.exe", Assert.Single(new WindowRuleStore(path).Rules).App);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Store_UnreadableFile_MeansNoRules()
    {
        var dir = Directory.CreateTempSubdirectory("dt-rules-");
        try
        {
            var path = Path.Combine(dir.FullName, "rules.json");
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new WindowRuleStore(path).Rules);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Tracker_FirstLookReportsNothing_ThenOnlyNewWindows()
    {
        var t = new NewWindowTracker();
        Assert.Empty(t.Update([1, 2]));
        Assert.Equal([3], t.Update([1, 2, 3]));
        Assert.Empty(t.Update([1, 3]));
        Assert.Equal([2], t.Update([1, 2, 3])); // closed and reopened (or re-shown) counts as new
    }

    [Fact]
    public void Tracker_ResetStartsAfresh()
    {
        var t = new NewWindowTracker();
        t.Update([1]);
        t.Reset();
        Assert.Empty(t.Update([1, 2]));
    }
}
