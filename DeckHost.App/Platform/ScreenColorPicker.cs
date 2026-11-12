using System;
using System.Runtime.InteropServices;

namespace DeckHost.App.Platform;

/// <summary>What the eyedropper sees on one poll: the color under the pointer and the buttons/keys.</summary>
/// <param name="Color">0xRRGGBB, or null when it couldn't be read.</param>
/// <param name="Pick">The left mouse button, Space or Enter is down.</param>
/// <param name="Cancel">Escape is down.</param>
internal readonly record struct ScreenSample(uint? Color, bool Pick, bool Cancel);

/// <summary>
/// Reads the color under the pointer anywhere on the screen, for the color picker's eyedropper:
/// Windows through GDI, macOS through CoreGraphics (needs the Screen Recording permission, else
/// only the desktop picture is seen), Linux through Xlib on X11. Wayland gives apps no way to read
/// the screen, so there it isn't available. A session is polled on a timer (see ColorPickerPanel).
/// </summary>
internal static partial class ScreenColorPicker
{
    /// <summary>Why the eyedropper isn't available here, or null when it is.</summary>
    public static string? UnsupportedReason { get; } = DetectUnsupportedReason();

    public static bool IsSupported => UnsupportedReason is null;

    /// <summary>Starts reading the screen; null when it can't (the reason is then <see cref="UnsupportedReason"/> or a failed open).</summary>
    public static IScreenSampler? Open()
    {
        if (!IsSupported)
        {
            return null;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return new WindowsSampler();
            }

            if (OperatingSystem.IsMacOS())
            {
                return new MacSampler();
            }

            return X11Sampler.TryOpen();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static string? DetectUnsupportedReason()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return null;
        }

        if (!OperatingSystem.IsLinux())
        {
            return "Not available on this system.";
        }

        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return "Wayland doesn't let apps read the screen. Log in to an X11 session to use the eyedropper.";
        }

        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ? "No X11 display to read the screen from." : null;
    }

    internal interface IScreenSampler : IDisposable
    {
        ScreenSample Sample();
    }

    // ---------------------------------------------------------------- Windows

    private sealed partial class WindowsSampler : IScreenSampler
    {
        private const int LeftButton = 0x01;
        private const int Enter = 0x0D;
        private const int Escape = 0x1B;
        private const int Space = 0x20;
        private const uint InvalidColor = 0xFFFFFFFF;

        public ScreenSample Sample()
        {
            uint? color = null;
            if (GetCursorPos(out var point) != 0)
            {
                var dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    try
                    {
                        var pixel = GetPixel(dc, point.X, point.Y);
                        if (pixel != InvalidColor)
                        {
                            // COLORREF is 0x00BBGGRR.
                            color = ((pixel & 0xFF) << 16) | (pixel & 0xFF00) | ((pixel >> 16) & 0xFF);
                        }
                    }
                    finally
                    {
                        _ = ReleaseDC(IntPtr.Zero, dc);
                    }
                }
            }

            return new ScreenSample(color, IsDown(LeftButton) || IsDown(Space) || IsDown(Enter), IsDown(Escape));
        }

        public void Dispose()
        {
        }

        private static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [LibraryImport("user32.dll")]
        private static partial int GetCursorPos(out Point point);

        [LibraryImport("user32.dll")]
        private static partial IntPtr GetDC(IntPtr window);

        [LibraryImport("user32.dll")]
        private static partial int ReleaseDC(IntPtr window, IntPtr dc);

        [LibraryImport("gdi32.dll")]
        private static partial uint GetPixel(IntPtr dc, int x, int y);

        [LibraryImport("user32.dll")]
        private static partial short GetAsyncKeyState(int key);
    }

    // ---------------------------------------------------------------- macOS

    private sealed unsafe partial class MacSampler : IScreenSampler
    {
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const int CombinedSessionState = 0;
        private const uint LeftMouseButton = 0;
        private const ushort SpaceKey = 49;
        private const ushort ReturnKey = 36;
        private const ushort EscapeKey = 53;
        private const uint OnScreenOnly = 1;
        private const uint PremultipliedLast = 1;

        public ScreenSample Sample()
        {
            uint? color = null;
            var mouseEvent = CGEventCreate(IntPtr.Zero);
            if (mouseEvent != IntPtr.Zero)
            {
                var location = CGEventGetLocation(mouseEvent);
                CFRelease(mouseEvent);
                color = ColorAt(location);
            }

            var pick = CGEventSourceButtonState(CombinedSessionState, LeftMouseButton) != 0 ||
                       CGEventSourceKeyState(CombinedSessionState, SpaceKey) != 0 ||
                       CGEventSourceKeyState(CombinedSessionState, ReturnKey) != 0;
            return new ScreenSample(color, pick, CGEventSourceKeyState(CombinedSessionState, EscapeKey) != 0);
        }

        public void Dispose()
        {
        }

        private static uint? ColorAt(CGPoint location)
        {
            var image = CGWindowListCreateImage(new CGRect(location.X, location.Y, 1, 1), OnScreenOnly, 0, 0);
            if (image == IntPtr.Zero)
            {
                return null;
            }

            var colorSpace = CGColorSpaceCreateDeviceRGB();
            var pixel = stackalloc byte[4];
            var context = CGBitmapContextCreate(pixel, 1, 1, 8, 4, colorSpace, PremultipliedLast);
            try
            {
                if (context == IntPtr.Zero)
                {
                    return null;
                }

                // A Retina point is 2x2 pixels; drawn into one pixel they average out.
                CGContextDrawImage(context, new CGRect(0, 0, 1, 1), image);
                return ((uint)pixel[0] << 16) | ((uint)pixel[1] << 8) | pixel[2];
            }
            finally
            {
                if (context != IntPtr.Zero)
                {
                    CFRelease(context);
                }

                CFRelease(colorSpace);
                CFRelease(image);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double X;
            public double Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CGRect(double x, double y, double width, double height)
        {
            public readonly double X = x;
            public readonly double Y = y;
            public readonly double Width = width;
            public readonly double Height = height;
        }

        [LibraryImport(CoreGraphics)]
        private static partial IntPtr CGEventCreate(IntPtr source);

        [LibraryImport(CoreGraphics)]
        private static partial CGPoint CGEventGetLocation(IntPtr @event);

        [LibraryImport(CoreGraphics)]
        private static partial byte CGEventSourceButtonState(int state, uint button);

        [LibraryImport(CoreGraphics)]
        private static partial byte CGEventSourceKeyState(int state, ushort key);

        [LibraryImport(CoreGraphics)]
        private static partial IntPtr CGWindowListCreateImage(CGRect bounds, uint listOption, uint windowId, uint imageOption);

        [LibraryImport(CoreGraphics)]
        private static partial IntPtr CGColorSpaceCreateDeviceRGB();

        [LibraryImport(CoreGraphics)]
        private static partial IntPtr CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, IntPtr colorSpace, uint bitmapInfo);

        [LibraryImport(CoreGraphics)]
        private static partial void CGContextDrawImage(IntPtr context, CGRect rect, IntPtr image);

        [LibraryImport(CoreFoundation)]
        private static partial void CFRelease(IntPtr value);
    }

    // ---------------------------------------------------------------- Linux (X11)

    private sealed unsafe partial class X11Sampler : IScreenSampler
    {
        private const string LibX11 = "libX11.so.6";
        private const int ZPixmap = 2;
        private const uint Button1Mask = 1 << 8;
        private const ulong AllPlanes = ulong.MaxValue;
        private const ulong SpaceSym = 0x20;
        private const ulong ReturnSym = 0xff0d;
        private const ulong EscapeSym = 0xff1b;

        // Offsets of the channel masks in Xlib's XImage on 64-bit systems.
        private const int RedMaskOffset = 56;
        private const int GreenMaskOffset = 64;
        private const int BlueMaskOffset = 72;

        private readonly IntPtr _display;
        private readonly ulong _root;
        private readonly byte _space;
        private readonly byte _return;
        private readonly byte _escape;

        private X11Sampler(IntPtr display)
        {
            _display = display;
            _root = XDefaultRootWindow(display);
            _space = XKeysymToKeycode(display, SpaceSym);
            _return = XKeysymToKeycode(display, ReturnSym);
            _escape = XKeysymToKeycode(display, EscapeSym);
        }

        public static X11Sampler? TryOpen()
        {
            var display = XOpenDisplay(IntPtr.Zero);
            return display == IntPtr.Zero ? null : new X11Sampler(display);
        }

        public ScreenSample Sample()
        {
            if (XQueryPointer(_display, _root, out _, out _, out var x, out var y, out _, out _, out var mask) == 0)
            {
                return new ScreenSample(null, false, false);
            }

            var keys = stackalloc byte[32];
            XQueryKeymap(_display, keys);
            bool Down(byte keycode) => keycode != 0 && (keys[keycode / 8] & (1 << (keycode % 8))) != 0;

            return new ScreenSample(ColorAt(x, y), (mask & Button1Mask) != 0 || Down(_space) || Down(_return), Down(_escape));
        }

        public void Dispose() => XCloseDisplay(_display);

        private uint? ColorAt(int x, int y)
        {
            var image = XGetImage(_display, _root, x, y, 1, 1, AllPlanes, ZPixmap);
            if (image == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var pixel = XGetPixel(image, 0, 0);
                var bytes = (byte*)image;
                return (Channel(pixel, *(ulong*)(bytes + RedMaskOffset)) << 16) |
                       (Channel(pixel, *(ulong*)(bytes + GreenMaskOffset)) << 8) |
                       Channel(pixel, *(ulong*)(bytes + BlueMaskOffset));
            }
            finally
            {
                _ = XDestroyImage(image);
            }
        }

        /// <summary>One channel scaled to 0–255, from the pixel and the channel's mask.</summary>
        private static uint Channel(ulong pixel, ulong mask)
        {
            if (mask == 0)
            {
                return 0;
            }

            var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
            var max = mask >> shift;
            return (uint)(((pixel & mask) >> shift) * 255 / max);
        }

        [LibraryImport(LibX11)]
        private static partial IntPtr XOpenDisplay(IntPtr name);

        [LibraryImport(LibX11)]
        private static partial int XCloseDisplay(IntPtr display);

        [LibraryImport(LibX11)]
        private static partial ulong XDefaultRootWindow(IntPtr display);

        [LibraryImport(LibX11)]
        private static partial int XQueryPointer(IntPtr display, ulong window, out ulong root, out ulong child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);

        [LibraryImport(LibX11)]
        private static partial IntPtr XGetImage(IntPtr display, ulong drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

        [LibraryImport(LibX11)]
        private static partial ulong XGetPixel(IntPtr image, int x, int y);

        [LibraryImport(LibX11)]
        private static partial int XDestroyImage(IntPtr image);

        [LibraryImport(LibX11)]
        private static partial int XQueryKeymap(IntPtr display, byte* keys);

        [LibraryImport(LibX11)]
        private static partial byte XKeysymToKeycode(IntPtr display, ulong keysym);
    }
}
