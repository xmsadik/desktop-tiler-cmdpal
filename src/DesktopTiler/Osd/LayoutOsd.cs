using System;
using System.Runtime.InteropServices;
using System.Threading;
using DesktopTiler.Core.Layouts;

namespace DesktopTiler;

/// <summary>
/// A brief on-screen display ("Grid" / "Tiled 4 windows") centered on the monitor that was just
/// tiled, like the system volume flyout: topmost, never activated, click-through, and hidden again
/// after <see cref="VisibleFor"/>. Exists because Command Palette's own toast is not topmost, so
/// the windows a tiling pass just moved end up covering it.
///
/// This process has no UI framework (it's a COM server), so this is a bare Win32 popup: a
/// dedicated thread, started on first <see cref="Show"/>, owns the window and pumps its messages;
/// <see cref="Show"/> just stores the text and posts that thread a message, so it can be called
/// from any thread and never blocks on UI work.
/// </summary>
internal sealed partial class LayoutOsd : IDisposable
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(2);

    private const string ClassName = "DesktopTiler.LayoutOsd";

    // Layout, in pixels at 96 DPI; scaled by the target monitor's DPI.
    private const int TitleFontPx = 30;
    private const int DetailFontPx = 15;
    private const int PaddingX = 28;
    private const int PaddingY = 16;
    private const int LineGap = 2;
    private const int MinWidth = 220;

    private const uint BackgroundColor = 0x00202020; // COLORREF 0x00BBGGRR
    private const uint TitleColor = 0x00FFFFFF;
    private const uint DetailColor = 0x00C8C8C8;
    private const byte Opacity = 235;

    // The window procedure is a static [UnmanagedCallersOnly] method (trim/AOT-safe); it only
    // ever runs on the OSD thread, so a [ThreadStatic] routes it back to the instance.
    [ThreadStatic]
    private static LayoutOsd? t_current;

    private readonly object _gate = new();
    private Thread? _thread;
    private uint _threadId;
    private nint _hwnd;
    private bool _disposed;

    // Pending text/position, written by Show() under _gate and read by the OSD thread.
    private string _title = string.Empty;
    private string _detail = string.Empty;
    private Rect _workArea;

    // OSD-thread-only state.
    private nint _titleFont;
    private nint _detailFont;
    private uint _fontDpi;
    private nint _background;
    private RectNative _titleRect;
    private RectNative _detailRect;

    /// <summary>Shows (or, if already visible, updates in place and re-arms the hide timer) the
    /// OSD centered on <paramref name="workArea"/>. Never throws - a failed OSD must not turn a
    /// successful tiling pass into an error - but returns false if the OSD couldn't be shown, so
    /// the caller can fall back to a toast rather than leave the user with no feedback at all.</summary>
    public bool Show(string title, string detail, Rect workArea)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return false;
                }

                _title = title;
                _detail = detail;
                _workArea = workArea;
                return EnsureStarted() && _hwnd != 0 && PostMessageW(_hwnd, WmApp, 0, 0);
            }
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"LayoutOsd.Show failed: {ex}");
            return false;
        }
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
            if (thread is not null)
            {
                PostThreadMessageW(_threadId, WmQuit, 0, 0);
            }
        }

        thread?.Join();
    }

    /// <summary>Starts the OSD thread and waits (bounded - the caller holds _gate, which
    /// Dispose() needs too) until its window exists. False if the thread didn't report ready in
    /// time; it then stays unusable for the rest of the session rather than being retried.</summary>
    private bool EnsureStarted()
    {
        if (_thread is not null)
        {
            return _threadId != 0;
        }

        // Not disposed with a using: on timeout the thread may still Set() it later.
        var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "DesktopTiler.LayoutOsd" };
        _thread.Start();
        if (!ready.Wait(StartTimeout))
        {
            SpikeLog.WriteLine("LayoutOsd: OSD thread did not start in time");
            return false;
        }

        ready.Dispose();
        return _threadId != 0;
    }

    private unsafe void Run(ManualResetEventSlim ready)
    {
        t_current = this;
        var hInstance = GetModuleHandleW(0);
        var className = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            try
            {
                var wc = new WndClassExW
                {
                    CbSize = (uint)sizeof(WndClassExW),
                    LpfnWndProc = &WndProc,
                    HInstance = hInstance,
                    LpszClassName = className,
                };
                RegisterClassExW(in wc);

                _background = CreateSolidBrush(BackgroundColor);
                _hwnd = CreateWindowExW(
                    WsExTopmost | WsExToolWindow | WsExNoActivate | WsExLayered | WsExTransparent,
                    className,
                    0,
                    WsPopup,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    hInstance,
                    0);

                if (_hwnd != 0)
                {
                    SetLayeredWindowAttributes(_hwnd, 0, Opacity, LwaAlpha);
                    var corner = DwmwcpRound;
                    _ = DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, &corner, sizeof(int));
                }
                else
                {
                    SpikeLog.WriteLine($"LayoutOsd: RegisterClassEx/CreateWindowEx failed ({Marshal.GetLastPInvokeError()})");
                }

                _threadId = GetCurrentThreadId();
            }
            finally
            {
                ready.Set();
            }

            // GetMessage returns 0 on WM_QUIT and -1 on error; stop on either.
            while (GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                TranslateMessage(in msg);
                DispatchMessageW(in msg);
            }
        }
        catch (Exception ex)
        {
            SpikeLog.WriteLine($"LayoutOsd thread failed: {ex}");
        }
        finally
        {
            if (_hwnd != 0)
            {
                DestroyWindow(_hwnd);
            }

            DeleteFonts();
            if (_background != 0)
            {
                DeleteObject(_background);
            }

            UnregisterClassW(className, hInstance);
            Marshal.FreeHGlobal(className);
            t_current = null;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            var self = t_current;
            if (self is not null)
            {
                switch (msg)
                {
                    case WmApp:
                        self.Present(hwnd);
                        return 0;
                    case WmTimer:
                        KillTimer(hwnd, HideTimerId);
                        ShowWindow(hwnd, SwHide);
                        return 0;
                    case WmPaint:
                        self.Paint(hwnd);
                        return 0;
                    case WmEraseBkgnd:
                        return 1; // WM_PAINT fills the whole client area itself.
                    case WmNcHitTest:
                        return HtTransparent;
                    case WmMouseActivate:
                        return MaNoActivate;
                }
            }
        }
        catch (Exception ex)
        {
            // An exception must never unwind out of an [UnmanagedCallersOnly] callback (it would
            // terminate the process); a missed OSD frame is harmless, so just log it.
            SpikeLog.WriteLine($"LayoutOsd.WndProc({msg:X4}) failed: {ex}");
        }

        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Sizes the window to the pending text, centers it on the pending work area, shows it
    /// without activating it, and (re)arms the hide timer.</summary>
    private void Present(nint hwnd)
    {
        string title;
        string detail;
        Rect area;
        lock (_gate)
        {
            title = _title;
            detail = _detail;
            area = _workArea;
        }

        var areaNative = new RectNative { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
        var monitor = MonitorFromRect(in areaNative, MonitorDefaultToNearest);
        var dpi = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 ? dpiX : 96u;
        EnsureFonts(dpi);

        int Scale(int px) => (int)Math.Round(px * dpi / 96.0);

        var titleSize = Measure(hwnd, _titleFont, title);
        var detailSize = Measure(hwnd, _detailFont, detail);
        var width = Math.Min(
            area.Width,
            Math.Max(Scale(MinWidth), Math.Max(titleSize.Width, detailSize.Width) + (2 * Scale(PaddingX))));
        var height = titleSize.Height + Scale(LineGap) + detailSize.Height + (2 * Scale(PaddingY));

        var titleTop = Scale(PaddingY);
        _titleRect = new RectNative { Left = 0, Top = titleTop, Right = width, Bottom = titleTop + titleSize.Height };
        var detailTop = _titleRect.Bottom + Scale(LineGap);
        _detailRect = new RectNative { Left = 0, Top = detailTop, Right = width, Bottom = detailTop + detailSize.Height };

        var x = area.Left + ((area.Width - width) / 2);
        var y = area.Top + ((area.Height - height) / 2);
        SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow);
        InvalidateRect(hwnd, 0, false);
        SetTimer(hwnd, HideTimerId, (uint)VisibleFor.TotalMilliseconds, 0);
    }

    private void Paint(nint hwnd)
    {
        string title;
        string detail;
        lock (_gate)
        {
            title = _title;
            detail = _detail;
        }

        var hdc = BeginPaint(hwnd, out var ps);
        try
        {
            GetClientRect(hwnd, out var client);
            FillRect(hdc, in client, _background);
            _ = SetBkMode(hdc, Transparent);

            var old = SelectObject(hdc, _titleFont);
            _ = SetTextColor(hdc, TitleColor);
            var r = _titleRect;
            DrawTextW(hdc, title, -1, ref r, DtCenter | DtSingleLine | DtNoPrefix | DtEndEllipsis);

            SelectObject(hdc, _detailFont);
            _ = SetTextColor(hdc, DetailColor);
            r = _detailRect;
            DrawTextW(hdc, detail, -1, ref r, DtCenter | DtSingleLine | DtNoPrefix | DtEndEllipsis);

            SelectObject(hdc, old);
        }
        finally
        {
            EndPaint(hwnd, in ps);
        }
    }

    private static (int Width, int Height) Measure(nint hwnd, nint font, string text)
    {
        var hdc = GetDC(hwnd);
        try
        {
            var old = SelectObject(hdc, font);
            var r = default(RectNative);
            DrawTextW(hdc, text.Length == 0 ? " " : text, -1, ref r, DtCalcRect | DtSingleLine | DtNoPrefix);
            SelectObject(hdc, old);
            return (r.Right - r.Left, r.Bottom - r.Top);
        }
        finally
        {
            _ = ReleaseDC(hwnd, hdc);
        }
    }

    private void EnsureFonts(uint dpi)
    {
        if (_titleFont != 0 && _fontDpi == dpi)
        {
            return;
        }

        DeleteFonts();
        _titleFont = CreateFont(TitleFontPx, FwSemibold, dpi);
        _detailFont = CreateFont(DetailFontPx, FwNormal, dpi);
        _fontDpi = dpi;
    }

    private static nint CreateFont(int px, int weight, uint dpi) =>
        CreateFontW(-(int)Math.Round(px * dpi / 96.0), 0, 0, 0, weight, 0, 0, 0, DefaultCharset, 0, 0, ClearTypeQuality, 0, "Segoe UI");

    private void DeleteFonts()
    {
        if (_titleFont != 0)
        {
            DeleteObject(_titleFont);
            _titleFont = 0;
        }

        if (_detailFont != 0)
        {
            DeleteObject(_detailFont);
            _detailFont = 0;
        }
    }

    // --- Win32 -----------------------------------------------------------------------------

    private const uint WmApp = 0x8000;
    private const uint WmQuit = 0x0012;
    private const uint WmTimer = 0x0113;
    private const uint WmPaint = 0x000F;
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseActivate = 0x0021;
    private const nint HtTransparent = -1;
    private const nint MaNoActivate = 3;
    private const nuint HideTimerId = 1;

    private const uint WsPopup = 0x80000000;
    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint LwaAlpha = 0x2;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const nint HwndTopmost = -1;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;
    private const int Transparent = 1;
    private const uint DtCenter = 0x0001;
    private const uint DtSingleLine = 0x0020;
    private const uint DtCalcRect = 0x0400;
    private const uint DtNoPrefix = 0x0800;
    private const uint DtEndEllipsis = 0x8000;
    private const int FwNormal = 400;
    private const int FwSemibold = 600;
    private const uint DefaultCharset = 1;
    private const uint ClearTypeQuality = 5;

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private unsafe struct WndClassExW
    {
        public uint CbSize;
        public uint Style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public nint LpszMenuName;
        public nint LpszClassName;
        public nint HIconSm;
    }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
        public uint LPrivate;
    }

    [StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private unsafe struct PaintStruct
    {
        public nint Hdc;
        public int FErase;
        public RectNative RcPaint;
        public int FRestore;
        public int FIncUpdate;
        public fixed byte RgbReserved[32];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial nint GetModuleHandleW(nint lpModuleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(in WndClassExW wc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(nint lpClassName, nint hInstance);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint CreateWindowExW(uint dwExStyle, nint lpClassName, nint lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in Msg lpMsg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in Msg lpMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [LibraryImport("dwmapi.dll")]
    private static unsafe partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute, void* pvAttribute, int cbAttribute);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromRect(in RectNative lprc, uint dwFlags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    private static partial nuint SetTimer(nint hWnd, nuint nIdEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(nint hWnd, nuint uIdEvent);

    [LibraryImport("user32.dll")]
    private static partial nint BeginPaint(nint hWnd, out PaintStruct lpPaint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EndPaint(nint hWnd, in PaintStruct lpPaint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RectNative lpRect);

    [LibraryImport("user32.dll")]
    private static partial int FillRect(nint hDC, in RectNative lprc, nint hbr);

    [LibraryImport("user32.dll")]
    private static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DrawTextW(nint hdc, string lpchText, int cchText, ref RectNative lprc, uint format);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(nint hdc, uint color);
}
