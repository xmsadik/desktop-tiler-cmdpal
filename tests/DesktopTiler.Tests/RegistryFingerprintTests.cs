using System;
using DesktopTiler.Core.VirtualDesktops;
using Xunit;

namespace DesktopTiler.Tests;

public class RegistryFingerprintTests
{
    private static readonly byte[] TwoIds = [.. Guid.NewGuid().ToByteArray(), .. Guid.NewGuid().ToByteArray()];

    [Fact]
    public void SameInputs_SameFingerprint()
    {
        var a = RegistryDesktopReader.Fingerprint(TwoIds, [1, 2], ["Work", null]);
        var b = RegistryDesktopReader.Fingerprint((byte[])TwoIds.Clone(), [1, 2], ["Work", null]);

        Assert.Equal(a, b);
    }

    [Fact]
    public void CurrentDesktopChange_ChangesFingerprint()
    {
        Assert.NotEqual(
            RegistryDesktopReader.Fingerprint(TwoIds, [1], ["Work", null]),
            RegistryDesktopReader.Fingerprint(TwoIds, [2], ["Work", null]));
    }

    [Fact]
    public void Rename_ChangesFingerprint()
    {
        Assert.NotEqual(
            RegistryDesktopReader.Fingerprint(TwoIds, [1], ["Work", null]),
            RegistryDesktopReader.Fingerprint(TwoIds, [1], ["Work", "Play"]));
    }

    [Fact]
    public void AddedDesktop_ChangesFingerprint()
    {
        byte[] threeIds = [.. TwoIds, .. Guid.NewGuid().ToByteArray()];

        Assert.NotEqual(
            RegistryDesktopReader.Fingerprint(TwoIds, [1], [null, null]),
            RegistryDesktopReader.Fingerprint(threeIds, [1], [null, null, null]));
    }

    [Fact]
    public void NamesCannotShiftBetweenDesktops()
    {
        Assert.NotEqual(
            RegistryDesktopReader.Fingerprint(TwoIds, [1], ["a", "bc"]),
            RegistryDesktopReader.Fingerprint(TwoIds, [1], ["ab", "c"]));
    }

    [Fact]
    public void NullInputs_AreEmpty()
    {
        Assert.Equal("||", RegistryDesktopReader.Fingerprint(null, null, []));
    }
}
