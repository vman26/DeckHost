using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DeckHost.App.Platform;

/// <summary>
/// The few macOS system calls DeckHost needs, through the Objective-C runtime and Mach:
/// media/volume keys (as system-defined key events, like the keyboard sends), the frontmost app,
/// the running apps, and CPU and memory readings.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class MacNative
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";

    // NSEventTypeSystemDefined, its "aux control button" subtype, and the NX_KEYTYPE_* codes.
    private const ulong SystemDefinedEvent = 14;
    private const short AuxControlButtons = 8;
    private const int KeyDownFlags = 0xA00;
    private const int KeyUpFlags = 0xB00;
    private const uint HidEventTap = 0;

    private static readonly Lazy<bool> AppKitLoaded = new(static () => NativeLibrary.TryLoad(AppKit, out _));

    /// <summary>The NX_KEYTYPE code of a media or volume key.</summary>
    public static int KeyType(MediaKey key) => key switch
    {
        MediaKey.VolumeUp => 0,
        MediaKey.VolumeDown => 1,
        MediaKey.Mute => 7,
        MediaKey.PlayPause => 16,
        MediaKey.Next => 17,
        MediaKey.Previous => 18,
        _ => -1,
    };

    /// <summary>Presses and releases a media key the way the keyboard does (the system shows its volume HUD).</summary>
    public static void PostMediaKey(MediaKey key)
    {
        var keyType = KeyType(key);
        if (keyType < 0)
        {
            throw new PlatformNotSupportedException($"macOS has no {key} key.");
        }

        RequireAppKit();
        var pool = objc_autoreleasePoolPush();
        try
        {
            foreach (var down in new[] { true, false })
            {
                var flags = down ? KeyDownFlags : KeyUpFlags;
                var data1 = ((long)keyType << 16) | ((long)(down ? 0xA : 0xB) << 8);
                var nsEvent = SendEvent(
                    Class("NSEvent"),
                    Selector("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:"),
                    SystemDefinedEvent, default, (ulong)flags, 0, 0, IntPtr.Zero, AuxControlButtons, data1, -1);
                var cgEvent = nsEvent == IntPtr.Zero ? IntPtr.Zero : Send(nsEvent, Selector("CGEvent"));
                if (cgEvent == IntPtr.Zero)
                {
                    throw new InvalidOperationException("macOS did not create the key event.");
                }

                CGEventPost(HidEventTap, cgEvent);
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>(process id, executable name) of the app that has focus.</summary>
    public static (int ProcessId, string Name)? FrontmostApp()
    {
        if (!AppKitLoaded.Value)
        {
            return null;
        }

        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = Send(Class("NSWorkspace"), Selector("sharedWorkspace"));
            var app = Send(workspace, Selector("frontmostApplication"));
            return app == IntPtr.Zero ? null : (SendInt(app, Selector("processIdentifier")), AppName(app));
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>Names of the running apps that appear in the Dock.</summary>
    public static IReadOnlyList<string> RunningApps()
    {
        var names = new List<string>();
        if (!AppKitLoaded.Value)
        {
            return names;
        }

        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = Send(Class("NSWorkspace"), Selector("sharedWorkspace"));
            var apps = Send(workspace, Selector("runningApplications"));
            var count = SendUInt(apps, Selector("count"));
            for (ulong index = 0; index < count; index++)
            {
                var app = SendAt(apps, Selector("objectAtIndex:"), index);

                // NSApplicationActivationPolicyRegular: ordinary apps with a Dock icon.
                if (app != IntPtr.Zero && SendLong(app, Selector("activationPolicy")) == 0 && AppName(app) is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }

        return names;
    }

    // NSActivityUserInitiatedAllowingIdleSystemSleep: NSActivityUserInitiated (0x00FFFFFF) without
    // NSActivityIdleSystemSleepDisabled (1 << 20).
    private const ulong UserInitiatedAllowingIdleSystemSleep = 0x00FFFFFFUL & ~(1UL << 20);

    private static IntPtr _activity;

    /// <summary>
    /// Keeps App Nap away for as long as DeckHost runs. Without a window in front, macOS otherwise slows
    /// DeckHost's timers and threads down, and a key press can take seconds to answer. The Mac may still
    /// go to sleep; only the throttling is off, as for any app that drives hardware.
    /// </summary>
    public static bool KeepResponsive()
    {
        if (_activity != IntPtr.Zero)
        {
            return true;
        }

        var pool = objc_autoreleasePoolPush();
        try
        {
            var processInfo = Send(Class("NSProcessInfo"), Selector("processInfo"));
            var reason = SendString(Class("NSString"), Selector("stringWithUTF8String:"), "DeckHost answers its devices' keys and knobs.");
            if (processInfo == IntPtr.Zero || reason == IntPtr.Zero)
            {
                return false;
            }

            var activity = SendActivity(processInfo, Selector("beginActivityWithOptions:reason:"), UserInitiatedAllowingIdleSystemSleep, reason);

            // Kept (retained) until DeckHost exits: ending the activity would let App Nap back in.
            _activity = activity == IntPtr.Zero ? IntPtr.Zero : Send(activity, Selector("retain"));
            return _activity != IntPtr.Zero;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    /// <summary>Ticks spent idle and in total by all CPUs since start-up.</summary>
    public static (ulong Idle, ulong Total)? CpuTicks()
    {
        // HOST_CPU_LOAD_INFO: user, system, idle, nice.
        Span<uint> ticks = stackalloc uint[4];
        var count = 4u;
        if (host_statistics(mach_host_self(), 3, ref MemoryMarshal.GetReference(ticks), ref count) != 0)
        {
            return null;
        }

        return (ticks[2], (ulong)ticks[0] + ticks[1] + ticks[2] + ticks[3]);
    }

    /// <summary>(used, total) physical memory in bytes, "used" as Activity Monitor counts it (app, wired, compressed).</summary>
    public static (ulong Used, ulong Total)? Memory()
    {
        if (SysctlUInt64("hw.memsize") is not { } total || SysctlUInt64("hw.pagesize") is not { } pageSize)
        {
            return null;
        }

        // HOST_VM_INFO64 fills a vm_statistics64 (38 integers). Offsets in bytes:
        // active_count 4, wire_count 12, purgeable_count 88, compressor_page_count 128, internal_page_count 140.
        Span<int> statistics = stackalloc int[38];
        var count = 38u;
        if (host_statistics64(mach_host_self(), 4, ref MemoryMarshal.GetReference(statistics), ref count) != 0)
        {
            return null;
        }

        // Every field used is a 32-bit page count at a 4-byte offset.
        ulong internalPages = (uint)statistics[140 / 4];
        ulong purgeable = (uint)statistics[88 / 4];
        ulong wired = (uint)statistics[12 / 4];
        ulong compressed = (uint)statistics[128 / 4];
        var appPages = internalPages > purgeable ? internalPages - purgeable : 0;
        var used = (appPages + wired + compressed) * pageSize;
        return (Math.Min(used, total), total);
    }

    private static ulong? SysctlUInt64(string name)
    {
        ulong value = 0;
        var size = (nuint)sizeof(ulong);
        return sysctlbyname(name, ref value, ref size, IntPtr.Zero, 0) == 0 ? value : null;
    }

    private static string AppName(IntPtr app)
    {
        // The executable's name ("Google Chrome", "Code"), which is what users type as the app.
        var url = Send(app, Selector("executableURL"));
        var name = url == IntPtr.Zero ? IntPtr.Zero : Send(url, Selector("lastPathComponent"));
        if (name == IntPtr.Zero)
        {
            name = Send(app, Selector("localizedName"));
        }

        var utf8 = name == IntPtr.Zero ? IntPtr.Zero : Send(name, Selector("UTF8String"));
        return utf8 == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(utf8) ?? string.Empty;
    }

    private static void RequireAppKit()
    {
        if (!AppKitLoaded.Value)
        {
            throw new PlatformNotSupportedException("AppKit could not be loaded.");
        }
    }

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjC)]
    private static partial IntPtr objc_autoreleasePoolPush();

    [LibraryImport(ObjC)]
    private static partial void objc_autoreleasePoolPop(IntPtr pool);

    // objc_msgSend must be called with the exact signature of each method.
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendAt(IntPtr receiver, IntPtr selector, ulong index);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial ulong SendUInt(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial long SendLong(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial int SendInt(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr SendString(IntPtr receiver, IntPtr selector, string value);

    // -[NSProcessInfo beginActivityWithOptions:reason:]
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendActivity(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

    // +[NSEvent otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:]
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendEvent(IntPtr receiver, IntPtr selector, ulong type, CGPoint location, ulong modifierFlags,
        double timestamp, long windowNumber, IntPtr context, short subtype, long data1, long data2);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventPost(uint tap, IntPtr cgEvent);

    [LibraryImport(LibSystem)]
    private static partial uint mach_host_self();

    [LibraryImport(LibSystem)]
    private static partial int host_statistics(uint host, int flavor, ref uint info, ref uint count);

    [LibraryImport(LibSystem)]
    private static partial int host_statistics64(uint host, int flavor, ref int info, ref uint count);

    [LibraryImport(LibSystem, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int sysctlbyname(string name, ref ulong value, ref nuint size, IntPtr newValue, nuint newSize);
}
