#pragma once
#include <stdint.h>

// Handle export directives for different operating systems
#if defined(_WIN32)
#define EXPORT __declspec(dllexport)
#define MIRABOX_CALL __cdecl
#else
#define EXPORT __attribute__((visibility("default")))
#define MIRABOX_CALL
#endif

// Every function below must have a matching [LibraryImport] with the exact same
// EntryPoint in DeckHost.Device.MiraBox/MiraBoxNative.cs.
//
// Devices are addressed by their device key (the HID path the SDK uses to track
// the device), not by an index, because the SDK's device map is unordered and
// changes on hot-plug.
//
// Return codes: >= 0 success, negative = MiraBoxResult error code.

extern "C" {
    enum MiraBoxResult : int32_t {
        MIRABOX_OK = 0,
        MIRABOX_INVALID_ARGUMENT = -1,
        MIRABOX_BUFFER_SIZE_MISMATCH = -2,
        MIRABOX_DEVICE_NOT_FOUND = -3,
        MIRABOX_NOT_SUPPORTED = -4,
        MIRABOX_BUFFER_TOO_SMALL = -5,
        MIRABOX_ENCODE_FAILED = -6,
        MIRABOX_EXCEPTION = -7,
    };

    // Synthetic event type (outside the SDK's RegisterEvent range) raised when the
    // set of connected devices may have changed. controlId and value are 0.
    enum { MIRABOX_EVENT_DEVICES_CHANGED = 0x80 };

    // Called on an SDK thread. deviceKey is only valid for the duration of the call.
    typedef void (MIRABOX_CALL *MiraBoxCallback)(const char* deviceKey, int32_t eventType, int32_t controlId, int32_t value);

    EXPORT int32_t MIRABOX_CALL MiraBox_Initialize();
    EXPORT void    MIRABOX_CALL MiraBox_SetCallback(MiraBoxCallback callback);
    EXPORT void    MIRABOX_CALL MiraBox_Shutdown();

    EXPORT int32_t MIRABOX_CALL MiraBox_GetDeviceCount();
    // Writes the UTF-8 device key (NUL-terminated) and returns its length.
    EXPORT int32_t MIRABOX_CALL MiraBox_GetDeviceKey(int32_t index, char* outBuffer, int32_t bufferSize);
    // Writes a UTF-8 JSON object describing the device and returns its length.
    EXPORT int32_t MIRABOX_CALL MiraBox_GetDeviceInfo(const char* deviceKey, char* outBuffer, int32_t bufferSize);

    // RGBA8888, width * height * 4 bytes. keyId is the SDK key id (main keys or second-screen keys).
    EXPORT int32_t MIRABOX_CALL MiraBox_SendKeyImage(const char* deviceKey, int32_t keyId, const uint8_t* rgba, int32_t bufferLength, int32_t width, int32_t height);
    // RGBA8888 full background image, stored on the device (see MiraBox_SendBackgroundFile).
    EXPORT int32_t MIRABOX_CALL MiraBox_SendBackground(const char* deviceKey, const uint8_t* rgba, int32_t bufferLength, int32_t width, int32_t height);
    // Background from an image file (PNG/JPG/...), scaled to the screen. The N4 Pro stores it and
    // keeps showing it as its idle/start-up screen, also behind the keys.
    EXPORT int32_t MIRABOX_CALL MiraBox_SendBackgroundFile(const char* deviceKey, const char* imagePath);
    // RGBA8888 background that is only drawn, not stored: it covers the stored background behind
    // and between the keys until it is cleared or the device loses power. Any size; it is scaled
    // to the screen. Send the key images again afterwards.
    EXPORT int32_t MIRABOX_CALL MiraBox_SetTemporaryBackground(const char* deviceKey, const uint8_t* rgba, int32_t bufferLength, int32_t width, int32_t height);
    // Removes the temporary background, so the stored one shows again.
    EXPORT int32_t MIRABOX_CALL MiraBox_ClearTemporaryBackground(const char* deviceKey);
    EXPORT int32_t MIRABOX_CALL MiraBox_ClearAllKeys(const char* deviceKey);
    EXPORT int32_t MIRABOX_CALL MiraBox_SetKeyBrightness(const char* deviceKey, uint8_t brightness);

    // rgb holds ledCount * 3 bytes (R, G, B per LED). Sends the whole LED state at once.
    EXPORT int32_t MIRABOX_CALL MiraBox_SetLedColors(const char* deviceKey, const uint8_t* rgb, int32_t ledCount);
    EXPORT int32_t MIRABOX_CALL MiraBox_SetLedBrightness(const char* deviceKey, uint8_t brightness);

    // Device configuration (Configer). states holds one byte per config item in the device's
    // order (N4 Pro: LedFollowKeyLight, KeyLightOnDisconnect, CheckUsbPower, EnableVibration,
    // ResetUsbReport, EnableBootVideo); values 0x11 = on, 0xFF = off, 0x1F = follow default.
    EXPORT int32_t MIRABOX_CALL MiraBox_SetDeviceConfig(const char* deviceKey, const uint8_t* states, int32_t count);
}
