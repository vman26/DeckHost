using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace DeckHost.Device.MiraBox;

public static partial class MiraBoxNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MiraBoxEventCallback(int eventType, int keyId, int x, int y);

    [LibraryImport("MiraBoxBridge", EntryPoint = "MiraBox_Initialize")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Initialize();

    [LibraryImport("MiraBoxBridge", EntryPoint = "MiraBox_SetCallback")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SetCallback(MiraBoxEventCallback callback);

    [LibraryImport("MiraBoxBridge", EntryPoint = "MiraBox_Shutdown")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Shutdown();

    [LibraryImport("MiraBoxBridge", EntryPoint = "MiraBox_SendDisplayBuffer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int SendDisplayBuffer(int deviceId, in byte buffer, int bufferLength, int width, int height);
}
