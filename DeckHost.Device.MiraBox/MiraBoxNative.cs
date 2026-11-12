using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace DeckHost.Device.MiraBox;

/// <summary>
/// P/Invoke surface of native/MiraBoxBridge. Every EntryPoint here must match an export in
/// MiraBoxBridge.h exactly. Return values: >= 0 success, negative = <see cref="MiraBoxResult"/>.
/// </summary>
internal static unsafe partial class MiraBoxNative
{
    private const string LibraryName = "MiraBoxBridge";
    private const string PluginDirectoryName = "deckhost.device.mirabox";
    private const int StringBufferSize = 4096;
    private static readonly Lock NativeLoadSync = new();
    private static bool s_pluginDependenciesLoaded;

    static MiraBoxNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(MiraBoxNative).Assembly, ResolveLibraryImport);
    }

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_Initialize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Initialize();

    /// <summary>Callback: (deviceKey UTF-8, eventType, controlId, value). Runs on an SDK thread.</summary>
    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetCallback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SetCallback(delegate* unmanaged[Cdecl]<byte*, int, int, int, void> callback);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_Shutdown")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Shutdown();

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_GetDeviceCount")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int GetDeviceCount();

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_GetDeviceKey")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int GetDeviceKeyNative(int index, byte* outBuffer, int bufferSize);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_GetDeviceInfo", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int GetDeviceInfoNative(string deviceKey, byte* outBuffer, int bufferSize);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SendKeyImage", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SendKeyImageNative(string deviceKey, int keyId, byte* rgba, int bufferLength, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SendBackground", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SendBackgroundNative(string deviceKey, byte* rgba, int bufferLength, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_ClearAllKeys", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int ClearAllKeys(string deviceKey);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetKeyBrightness", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetKeyBrightness(string deviceKey, byte brightness);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetLedColors", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SetLedColorsNative(string deviceKey, byte* rgb, int ledCount);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetLedBrightness", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetLedBrightness(string deviceKey, byte brightness);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetDeviceConfig", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int SetDeviceConfigNative(string deviceKey, byte* states, int count);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SendBackgroundFile", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SendBackgroundFile(string deviceKey, string imagePath);

    public static int SetDeviceConfig(string deviceKey, ReadOnlySpan<byte> states)
    {
        fixed (byte* values = states)
        {
            return SetDeviceConfigNative(deviceKey, values, states.Length);
        }
    }

    public static string? GetDeviceKey(int index)
    {
        var buffer = stackalloc byte[StringBufferSize];
        var length = GetDeviceKeyNative(index, buffer, StringBufferSize);
        return length >= 0 ? Encoding.UTF8.GetString(buffer, length) : null;
    }

    public static string? GetDeviceInfoJson(string deviceKey, out int result)
    {
        var buffer = stackalloc byte[StringBufferSize];
        result = GetDeviceInfoNative(deviceKey, buffer, StringBufferSize);
        return result >= 0 ? Encoding.UTF8.GetString(buffer, result) : null;
    }

    public static int SendKeyImage(string deviceKey, int keyId, ReadOnlySpan<byte> rgba, int width, int height)
    {
        fixed (byte* pixels = rgba)
        {
            return SendKeyImageNative(deviceKey, keyId, pixels, rgba.Length, width, height);
        }
    }

    public static int SendBackground(string deviceKey, ReadOnlySpan<byte> rgba, int width, int height)
    {
        fixed (byte* pixels = rgba)
        {
            return SendBackgroundNative(deviceKey, pixels, rgba.Length, width, height);
        }
    }

    /// <param name="rgb">ledCount * 3 bytes.</param>
    public static int SetLedColors(string deviceKey, ReadOnlySpan<byte> rgb)
    {
        fixed (byte* colors = rgb)
        {
            return SetLedColorsNative(deviceKey, colors, rgb.Length / 3);
        }
    }

    public static string Describe(int result) => result switch
    {
        >= 0 => "OK",
        (int)MiraBoxResult.InvalidArgument => "invalid argument",
        (int)MiraBoxResult.BufferSizeMismatch => "buffer size does not match width x height",
        (int)MiraBoxResult.DeviceNotFound => "device not connected",
        (int)MiraBoxResult.NotSupported => "not supported by this device or key",
        (int)MiraBoxResult.BufferTooSmall => "buffer too small",
        (int)MiraBoxResult.EncodeFailed => "image encoding failed",
        (int)MiraBoxResult.Exception => "native exception",
        _ => $"native error {result}",
    };

    private static IntPtr ResolveLibraryImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        var pluginDirectory = GetPluginDirectory();
        if (!Directory.Exists(pluginDirectory))
        {
            return IntPtr.Zero;
        }

        EnsurePluginDependenciesLoaded(pluginDirectory);

        var preferredLibraryPath = Path.Combine(pluginDirectory, GetPlatformLibraryFileName());
        if (File.Exists(preferredLibraryPath) && NativeLibrary.TryLoad(preferredLibraryPath, out var handle))
        {
            return handle;
        }

        foreach (var candidatePath in Directory.GetFiles(pluginDirectory, GetPlatformLibrarySearchPattern(), SearchOption.TopDirectoryOnly))
        {
            if (NativeLibrary.TryLoad(candidatePath, out handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    private static string GetPluginDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "plugins", "device", PluginDirectoryName);

    private static void EnsurePluginDependenciesLoaded(string pluginDirectory)
    {
        lock (NativeLoadSync)
        {
            if (s_pluginDependenciesLoaded)
            {
                return;
            }

            var mainLibraryFileName = GetPlatformLibraryFileName();
            foreach (var dependencyPath in Directory
                         .EnumerateFiles(pluginDirectory, GetNativeBinarySearchPattern(), SearchOption.TopDirectoryOnly)
                         .Where(path => !string.Equals(Path.GetFileName(path), mainLibraryFileName, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(GetDependencyPriority)
                         .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                NativeLibrary.TryLoad(dependencyPath, out _);
            }

            s_pluginDependenciesLoaded = true;
        }
    }

    private static int GetDependencyPriority(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Contains("hidapi", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fileName.Contains("transport", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (fileName.Contains("opencv", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 10;
    }

    private static string GetPlatformLibraryFileName()
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{LibraryName}.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"lib{LibraryName}.dylib";
        }

        return $"lib{LibraryName}.so";
    }

    private static string GetPlatformLibrarySearchPattern()
    {
        if (OperatingSystem.IsWindows())
        {
            return $"{LibraryName}*.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return $"*{LibraryName}*.dylib";
        }

        return $"*{LibraryName}*.so";
    }

    private static string GetNativeBinarySearchPattern()
    {
        if (OperatingSystem.IsWindows())
        {
            return "*.dll";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "*.dylib";
        }

        return "*.so";
    }
}

internal enum MiraBoxResult
{
    Ok = 0,
    InvalidArgument = -1,
    BufferSizeMismatch = -2,
    DeviceNotFound = -3,
    NotSupported = -4,
    BufferTooSmall = -5,
    EncodeFailed = -6,
    Exception = -7,
}
