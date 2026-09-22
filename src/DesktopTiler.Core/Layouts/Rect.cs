namespace DesktopTiler.Core.Layouts;

/// <summary>
/// A simple axis-aligned rectangle in screen coordinates, using the same Left/Top/Right/Bottom
/// convention as the Win32 RECT struct (Right/Bottom are exclusive edges, not width/height).
/// Pure value type shared by layout math and window-frame math so both sides speak the same
/// geometry without depending on any P/Invoke struct.
/// </summary>
public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}
