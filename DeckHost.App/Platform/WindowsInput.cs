using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DeckHost.App.Platform;

/// <summary>Synthesizes keyboard input with SendInput (Windows only).</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsInput
{
    public const ushort VkMediaNextTrack = VirtualKeys.MediaNextTrack;
    public const ushort VkMediaPrevTrack = VirtualKeys.MediaPrevTrack;
    public const ushort VkMediaStop = VirtualKeys.MediaStop;
    public const ushort VkMediaPlayPause = VirtualKeys.MediaPlayPause;
    public const ushort VkVolumeMute = VirtualKeys.VolumeMute;
    public const ushort VkVolumeDown = VirtualKeys.VolumeDown;
    public const ushort VkVolumeUp = VirtualKeys.VolumeUp;

    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    /// <summary>Keys that need the extended-key flag (navigation block, media and volume keys, Windows keys).</summary>
    private static readonly HashSet<ushort> ExtendedKeys =
    [
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D,
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,
    ];

    public static void TapKey(ushort virtualKey, int times = 1)
    {
        var inputs = new List<Input>();
        for (var i = 0; i < times; i++)
        {
            inputs.Add(KeyInput(virtualKey, keyUp: false));
            inputs.Add(KeyInput(virtualKey, keyUp: true));
        }

        Send(inputs);
    }

    /// <summary>Presses the modifiers, taps the key, then releases the modifiers in reverse order.</summary>
    public static void SendChord(IReadOnlyList<ushort> modifiers, ushort key)
    {
        var inputs = new List<Input>();
        foreach (var modifier in modifiers)
        {
            inputs.Add(KeyInput(modifier, keyUp: false));
        }

        inputs.Add(KeyInput(key, keyUp: false));
        inputs.Add(KeyInput(key, keyUp: true));

        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            inputs.Add(KeyInput(modifiers[i], keyUp: true));
        }

        Send(inputs);
    }

    /// <summary>Types text as Unicode characters, independent of the keyboard layout.</summary>
    public static void TypeText(string text)
    {
        var inputs = new List<Input>();
        foreach (var character in text.Replace("\r\n", "\n", StringComparison.Ordinal))
        {
            if (character == '\n')
            {
                inputs.Add(KeyInput(0x0D, keyUp: false));
                inputs.Add(KeyInput(0x0D, keyUp: true));
                continue;
            }

            inputs.Add(UnicodeInput(character, keyUp: false));
            inputs.Add(UnicodeInput(character, keyUp: true));
        }

        Send(inputs);
    }

    public static void LockWorkstation()
    {
        if (!LockWorkStationNative())
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static Input KeyInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = (keyUp ? KeyEventKeyUp : 0) | (ExtendedKeys.Contains(virtualKey) ? KeyEventExtendedKey : 0),
            },
        },
    };

    private static Input UnicodeInput(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Union = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                ScanCode = character,
                Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
            },
        },
    };

    private static void Send(List<Input> inputs)
    {
        if (inputs.Count == 0)
        {
            return;
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            // Usually UIPI: the foreground window belongs to an elevated process.
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked the simulated input (is the target app running as administrator?).");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int size);

    [DllImport("user32.dll", EntryPoint = "LockWorkStation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStationNative();

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    // MouseInput is the largest member; it must be present so the union (and INPUT) has the right size.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
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
}
