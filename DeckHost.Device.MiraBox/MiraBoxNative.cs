using System.Runtime.CompilerServices;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DeckHost.Device.MiraBox;

public static partial class MiraBoxNative
{
    private const string LibraryName = "MiraBoxBridge";
    private const string PluginDirectoryName = "deckhost.device.mirabox";
    private static readonly Lock NativeLoadSync = new();
    private static bool s_pluginDependenciesLoaded;

    static MiraBoxNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(MiraBoxNative).Assembly, ResolveLibraryImport);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MiraBoxEventCallback(int eventType, int keyId, int x, int y);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_Initialize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Initialize();

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetCallback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SetCallback(MiraBoxEventCallback callback);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_Shutdown")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Shutdown();

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SendDisplayBuffer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SendDisplayBuffer(int deviceId, in byte buffer, int bufferLength, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SendKeyImage")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SendKeyImage(int deviceId, byte keyId, in byte buffer, int bufferLength, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SendTouchDisplay")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SendTouchDisplay(int deviceId, in byte buffer, int bufferLength, int width, int height);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetKeyRGB")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetKeyRGB(int deviceId, byte keyId, byte red, byte green, byte blue);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_SetAllKeyRGB")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SetAllKeyRGB(int deviceId, byte red, byte green, byte blue);

    [LibraryImport(LibraryName, EntryPoint = "MiraBox_GetDeviceInfo")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int GetDeviceInfoNative(int deviceId, byte[] outBuffer, int bufferSize);

    /// <summary>
    /// Query device information from the connected MiraBox device.
    /// Returns a JSON string with device capabilities.
    /// </summary>
    public static string? QueryDeviceInfo(int deviceId)
    {
        const int bufferSize = 1024;
        var buffer = new byte[bufferSize];
        var result = GetDeviceInfoNative(deviceId, buffer, bufferSize);

        if (result > 0)
        {
            return System.Text.Encoding.UTF8.GetString(buffer, 0, result);
        }

        return null;
    }

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
