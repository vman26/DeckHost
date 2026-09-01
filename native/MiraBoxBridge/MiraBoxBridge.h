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

extern "C" {
    // Define the signature for your callback function
    typedef void (MIRABOX_CALL *MiraBoxCallback)(int32_t eventType, int32_t keyId, int32_t x, int32_t y);

    // Exported bridge functions
    EXPORT int32_t MIRABOX_CALL MiraBox_Initialize();
    EXPORT void MIRABOX_CALL MiraBox_SetCallback(MiraBoxCallback callback);
    EXPORT int32_t MIRABOX_CALL MiraBox_SendDisplayBuffer(int32_t deviceId, const uint8_t* buffer, int32_t bufferLength, int32_t width, int32_t height);
    EXPORT void MIRABOX_CALL MiraBox_Shutdown();
}
