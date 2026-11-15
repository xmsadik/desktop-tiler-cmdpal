using System.Runtime.InteropServices;

namespace DesktopTiler.Core.Windows;

/// <summary>
/// Windows only lets a process move the foreground if it received the last input event (among a
/// few other cases). A command invoked from a Command Palette hotkey gets that right passed on by
/// the host; one picked from the Dock flyout does not, and then every SetForegroundWindow call is
/// refused - the target window just flashes in the taskbar. Synthesizing a harmless keystroke
/// with SendInput makes this process the one that produced the last input, which restores the
/// right. The keystroke is a "masked Alt": Alt down, the unassigned virtual key 0xE8 down/up,
/// Alt up - the key in between stops a lone Alt tap from activating a menu bar. Same technique
/// as the virtual-desktop-helper app; documented user32 calls only.
/// </summary>
internal static partial class ForegroundRight
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const ushort VkMenu = 0x12;
    private const ushort VkMask = 0xE8; // unassigned

    /// <summary>SetForegroundWindow, and if that's refused, once more after acquiring the right
    /// with a masked Alt tap. Returns whether <paramref name="window"/> ended up in the foreground.</summary>
    internal static bool SetForeground(nint window)
    {
        if (NativeMethods.SetForegroundWindow(window))
        {
            return true;
        }

        MaskedAltTap();
        return NativeMethods.SetForegroundWindow(window);
    }

    private static void MaskedAltTap()
    {
        Input[] inputs =
        [
            Key(VkMenu, up: false),
            Key(VkMask, up: false),
            Key(VkMask, up: true),
            Key(VkMenu, up: true),
        ];
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private static Input Key(ushort vk, bool up) => new()
    {
        Type = InputKeyboard,
        Keyboard = new KeyboardInput { VirtualKey = vk, Flags = up ? KeyEventFKeyUp : 0 },
    };

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    // INPUT: a type tag followed by a union whose largest member is MOUSEINPUT, which fixes the
    // struct size SendInput validates (40 bytes on 64-bit).
    [StructLayout(LayoutKind.Explicit)]
    private struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public KeyboardInput Keyboard;

        [FieldOffset(8)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
