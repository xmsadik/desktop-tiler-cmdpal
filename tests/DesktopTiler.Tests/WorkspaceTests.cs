using System;
using System.IO;
using DesktopTiler.Core.Rules;
using Xunit;

namespace DesktopTiler.Tests;

public class WorkspaceTests
{
    private static readonly Guid D1 = Guid.NewGuid();
    private static readonly Guid D2 = Guid.NewGuid();
    private static readonly Guid D3 = Guid.NewGuid();
    private static readonly Guid[] Desktops = [D1, D2, D3];

    [Fact]
    public void Snapshot_OneEntryPerApp_FrontmostWindowDecides_OrderedByDesktopThenZ()
    {
        SnapshotWindow[] windows =
        [
            new(@"C:\a\Code.exe", D3),     // front
            new(@"C:\b\chrome.exe", D1),
            new(@"C:\a\code.EXE", D1),     // same app, further back: ignored
            new(@"C:\c\Teams.exe", D1),
            new(@"C:\d\notepad.exe", D2),
        ];

        var entries = WorkspaceSnapshot.FromWindows(windows, Desktops, out var skipped);

        Assert.Equal(0, skipped);
        Assert.Equal(
            [new(@"C:\b\chrome.exe", D1), new(@"C:\c\Teams.exe", D1), new(@"C:\d\notepad.exe", D2), new(@"C:\a\Code.exe", D3)],
            entries);
    }

    [Fact]
    public void Snapshot_SkipsStoreAppsUnknownDesktopsAndUnreadablePaths()
    {
        SnapshotWindow[] windows =
        [
            new(@"C:\Windows\System32\ApplicationFrameHost.exe", D1),
            new(@"C:\Windows\System32\ApplicationFrameHost.exe", D2), // same host: counted once
            new(@"C:\x\pinned.exe", Guid.NewGuid()),                 // shown on all desktops
            new(string.Empty, D1),
            new(@"C:\x\ok.exe", D2),
        ];

        var entries = WorkspaceSnapshot.FromWindows(windows, Desktops, out var skipped);

        Assert.Equal(1, skipped);
        Assert.Equal(@"C:\x\ok.exe", Assert.Single(entries).ImagePath);
    }

    [Fact]
    public void DefaultName_TakesFirstFreeNumber()
    {
        Assert.Equal("Workspace 1", WorkspaceSnapshot.DefaultName([]));
        Assert.Equal("Workspace 2", WorkspaceSnapshot.DefaultName(["Workspace 1", "Coding", "workspace 3"]));
    }

    [Fact]
    public void Serialize_ThenParse_RoundTrips()
    {
        Workspace[] all =
        [
            new(Guid.NewGuid(), "Kodlama \"İş\"", [new(@"C:\a\Code.exe", D1), new(@"C:\Program Files\x\y.exe", D2)]),
            new(Guid.NewGuid(), "Empty", []),
        ];

        Assert.Equal(all, WorkspaceStore.Parse(WorkspaceStore.Serialize(all)));
    }

    [Fact]
    public void Parse_SkipsMalformedWorkspacesAndEntries()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            { "workspaces": [
              { "id": "{{id}}", "name": "Ok", "apps": [
                  { "path": "C:\\a.exe", "desktop": "{{D1}}" },
                  { "path": "", "desktop": "{{D1}}" },
                  { "path": "C:\\b.exe", "desktop": "nope" } ] },
              { "id": "not-a-guid", "name": "Bad" },
              { "id": "{{Guid.NewGuid()}}", "name": "  " },
              7
            ] }
            """;

        var ws = Assert.Single(WorkspaceStore.Parse(json));
        Assert.Equal(new Workspace(id, "Ok", [new(@"C:\a.exe", D1)]), ws);
    }

    [Fact]
    public void Store_EditsPersist()
    {
        var dir = Directory.CreateTempSubdirectory("dt-ws-");
        try
        {
            var path = Path.Combine(dir.FullName, "workspaces.json");
            var store = new WorkspaceStore(path);
            var id = Guid.NewGuid();
            store.Add(new Workspace(id, "A", [new(@"C:\a\Code.exe", D1), new(@"C:\b\chrome.exe", D1)]));

            store.Rename(id, "Coding");
            store.SetEntryDesktop(id, "code.exe", D3);
            store.RemoveEntry(id, "CHROME.EXE");

            var reloaded = new WorkspaceStore(path).Find(id);
            Assert.Equal(new Workspace(id, "Coding", [new(@"C:\a\Code.exe", D3)]), reloaded);

            store.Remove(id);
            Assert.Empty(new WorkspaceStore(path).Workspaces);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Placements_CoverEveryWindowUntilExpiry()
    {
        var p = new PendingPlacements();
        p.Add("Code.exe", D2, nowTicks: 1000);

        Assert.True(p.Any(1000));
        Assert.Equal(D2, p.TargetFor("code.exe", 1500));
        Assert.Equal(D2, p.TargetFor("CODE.EXE", 2000)); // not used up by the first window
        Assert.Null(p.TargetFor("chrome.exe", 2000));

        var expiry = 1000 + (long)PendingPlacements.Lifetime.TotalMilliseconds;
        Assert.Null(p.TargetFor("code.exe", expiry));
        Assert.False(p.Any(expiry));
    }

    [Fact]
    public void Placements_AddRenewsAndRetargets()
    {
        var p = new PendingPlacements();
        p.Add("a.exe", D1, 0);
        p.Add("a.exe", D3, 20_000);
        Assert.Equal(D3, p.TargetFor("a.exe", 45_000));
    }

    [Fact]
    public void MoveTarget_FollowsSameChecksAsRules()
    {
        Assert.Equal(D2, WindowRules.MoveTarget(D2, D1, Desktops));
        Assert.Null(WindowRules.MoveTarget(null, D1, Desktops));
        Assert.Null(WindowRules.MoveTarget(D1, D1, Desktops));
        Assert.Null(WindowRules.MoveTarget(Guid.NewGuid(), D1, Desktops));
        Assert.Null(WindowRules.MoveTarget(D2, Guid.NewGuid(), Desktops));
    }
}
