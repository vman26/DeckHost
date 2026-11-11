#include "MiraBoxBridge.h"
#include "DeviceManager/devicemanager.h"
#include "OpenCVImageEncoder.h"

#include <atomic>
#include <cstring>
#include <memory>

namespace {
    std::atomic<MiraBoxCallback> g_CSharpCallback = nullptr;
    std::atomic_bool g_IsInitialized = false;

    constexpr RegisterEvent g_ForwardedEvents[] = {
        RegisterEvent::KeyPress,
        RegisterEvent::KeyRelease,
        RegisterEvent::KnobLeft,
        RegisterEvent::KnobRight,
        RegisterEvent::KnobPress,
        RegisterEvent::KnobRelease,
        RegisterEvent::SwipeLeft,
        RegisterEvent::SwipeRight,
        RegisterEvent::ToggleUp,
        RegisterEvent::ToggleDown,
        RegisterEvent::DIPLeft,
        RegisterEvent::DIPLeftEnd,
        RegisterEvent::DIPRight,
        RegisterEvent::DIPRightEnd,
        RegisterEvent::DIPPress,
        RegisterEvent::DIPRelease
    };

    void ForwardEvent(RegisterEvent eventType, uint8_t controlId)
    {
        const auto callback = g_CSharpCallback.load();
        if (callback != nullptr) {
            callback(static_cast<int32_t>(eventType), static_cast<int32_t>(controlId), 0, 0);
        }
    }

    void RegisterDeviceCallbacks(const std::shared_ptr<StreamDock>& device)
    {
        if (device == nullptr) {
            return;
        }

        auto* reader = device->reader();
        if (reader == nullptr) {
            return;
        }

        for (uint16_t controlId = 0; controlId <= UINT8_MAX; ++controlId) {
            const auto logicalControlId = static_cast<uint8_t>(controlId);
            for (const auto eventType : g_ForwardedEvents) {
                reader->registerReadCallback(logicalControlId, [logicalControlId, eventType]() {
                    ForwardEvent(eventType, logicalControlId);
                }, eventType);
            }
        }

        reader->startReadLoop();
    }

    void RegisterConnectedDeviceCallbacks()
    {
        auto& streamDocks = DeviceManager::instance().getStreamDocks();
        for (const auto& [_, device] : streamDocks) {
            RegisterDeviceCallbacks(device);
        }
    }

    std::shared_ptr<StreamDock> GetDeviceByIndex(int32_t deviceId)
    {
        if (deviceId < 0) {
            return nullptr;
        }

        auto& streamDocks = DeviceManager::instance().getStreamDocks();
        size_t currentIndex = 0;
        for (const auto& [_, device] : streamDocks) {
            if (currentIndex == static_cast<size_t>(deviceId)) {
                return device;
            }

            ++currentIndex;
        }

        return nullptr;
    }
}

extern "C" {
    EXPORT int32_t MIRABOX_CALL MiraBox_Initialize() {
        try {
            auto& deviceManager = DeviceManager::instance();
            deviceManager.enumerator();

            if (!g_IsInitialized.exchange(true)) {
                deviceManager.listen([](std::shared_ptr<StreamDock> device) {
                    RegisterDeviceCallbacks(device);
                });
            }

            if (g_CSharpCallback.load() != nullptr) {
                RegisterConnectedDeviceCallbacks();
            }

            return 0; // Return 0 for success
        }
        catch (...) {
            g_IsInitialized = false;
            // NEVER let C++ exceptions cross the C boundary into C#. It will crash.
            return -1;
        }
    }

    EXPORT void MIRABOX_CALL MiraBox_SetCallback(MiraBoxCallback callback) {
        g_CSharpCallback = callback;

        if (callback == nullptr || !g_IsInitialized.load()) {
            return;
        }

        try {
            RegisterConnectedDeviceCallbacks();
        }
        catch (...) {
            // NEVER let C++ exceptions cross the C boundary into C#. It will crash.
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SendDisplayBuffer(int32_t deviceId, const uint8_t* buffer, int32_t bufferLength, int32_t width, int32_t height) {
        try {
            if (buffer == nullptr || bufferLength <= 0 || width <= 0 || height <= 0) {
                return -1;
            }

            const auto expectedLength = static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
            if (expectedLength != static_cast<size_t>(bufferLength)) {
                return -2;
            }

            auto device = GetDeviceByIndex(deviceId);
            if (device == nullptr) {
                return -3;
            }

            auto imgHelper = device->getBgImgHelper();
            if (imgHelper == nullptr) {
                return -4;
            }

            ImgHelper outputHelper = *imgHelper;
            if (outputHelper._width == 0) {
                outputHelper._width = static_cast<uint32_t>(width);
            }

            if (outputHelper._height == 0) {
                outputHelper._height = static_cast<uint32_t>(height);
            }

            if (outputHelper._width == 0 || outputHelper._height == 0) {
                return -5;
            }

            RawCanvas canvas(width, height, true);
            std::memcpy(canvas.pixels.data(), buffer, expectedLength);

            OpenCVImageEncoder encoder;
            std::vector<uint8_t> encodedBuffer;
            if (!encoder.encodeToMemory(encodedBuffer, canvas, 85, outputHelper)) {
                return -6;
            }

            std::string displayBuffer(reinterpret_cast<const char*>(encodedBuffer.data()), encodedBuffer.size());
            device->setBackgroundImgStream(displayBuffer);
            return 0;
        }
        catch (...) {
            // NEVER let C++ exceptions cross the C boundary into C#. It will crash.
            return -7;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SendKeyImage(int32_t deviceId, uint8_t keyId, const uint8_t* buffer, int32_t bufferLength, int32_t width, int32_t height) {
         try {
             if (buffer == nullptr || bufferLength <= 0 || width <= 0 || height <= 0) {
                 return -1; // Invalid parameters
             }

             const auto expectedLength = static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
             if (expectedLength != static_cast<size_t>(bufferLength)) {
                 return -2; // Buffer size mismatch
             }

             auto device = GetDeviceByIndex(deviceId);
             if (device == nullptr) {
                 return -3; // Device not found
             }

             auto imgHelper = device->getKyImgHelper(keyId);
             if (imgHelper == nullptr) {
                 return -4; // Image helper not available
             }

             RawCanvas canvas(width, height, true);
             std::memcpy(canvas.pixels.data(), buffer, expectedLength);

             OpenCVImageEncoder encoder;
             std::vector<uint8_t> encodedBuffer;
             if (!encoder.encodeToMemory(encodedBuffer, canvas, 95, *imgHelper)) {
                 return -6; // Encoding failed
             }

             std::string displayBuffer(reinterpret_cast<const char*>(encodedBuffer.data()), encodedBuffer.size());
             device->setKeyImgFileStream(displayBuffer, keyId);
             return 0; // Success
         }
         catch (...) {
             return -7; // Exception
         }
     }

     EXPORT int32_t MIRABOX_CALL MiraBox_SendTouchDisplay(int32_t deviceId, const uint8_t* buffer, int32_t bufferLength, int32_t width, int32_t height) {
         try {
             if (buffer == nullptr || bufferLength <= 0 || width <= 0 || height <= 0) {
                 return -1;
             }

             const auto expectedLength = static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
             if (expectedLength != static_cast<size_t>(bufferLength)) {
                 return -2;
             }

             auto device = GetDeviceByIndex(deviceId);
             if (device == nullptr) {
                 return -3;
             }

             auto imgHelper = device->getBgImgHelper();
             if (imgHelper == nullptr) {
                 return -4;
             }

             RawCanvas canvas(width, height, true);
             std::memcpy(canvas.pixels.data(), buffer, expectedLength);

             OpenCVImageEncoder encoder;
             std::vector<uint8_t> encodedBuffer;
             if (!encoder.encodeToMemory(encodedBuffer, canvas, 85, *imgHelper)) {
                 return -6;
             }

             std::string displayBuffer(reinterpret_cast<const char*>(encodedBuffer.data()), encodedBuffer.size());
             device->setBackgroundImgStream(displayBuffer);
             return 0;
         }
         catch (...) {
             return -7;
         }
     }

     EXPORT int32_t MIRABOX_CALL MiraBox_SetKeyRGB(int32_t deviceId, uint8_t keyId, uint8_t red, uint8_t green, uint8_t blue) {
         try {
             auto device = GetDeviceByIndex(deviceId);
             if (device == nullptr) {
                 return -3; // Device not found
             }

             auto rgbController = device->rgber();
             if (rgbController == nullptr) {
                 return -4; // RGB not supported
             }

             // Set single LED color
             std::vector<std::array<uint8_t, 3>> colors;
             // Ensure we have enough entries for the target keyId
             for (uint8_t i = 0; i <= keyId; ++i) {
                 if (i == keyId) {
                     colors.push_back({red, green, blue});
                 } else {
                     colors.push_back({0, 0, 0}); // Off for other LEDs
                 }
             }

             rgbController->setSingleLedColor(colors);
             return 0; // Success
         }
         catch (...) {
             return -7; // Exception
         }
     }

     EXPORT int32_t MIRABOX_CALL MiraBox_SetAllKeyRGB(int32_t deviceId, uint8_t red, uint8_t green, uint8_t blue) {
         try {
             auto device = GetDeviceByIndex(deviceId);
             if (device == nullptr) {
                 return -3;
             }

             auto rgbController = device->rgber();
             if (rgbController == nullptr) {
                 return -4;
             }

             rgbController->setLedColor(red, green, blue);
             return 0;
         }
         catch (...) {
             return -7;
         }
     }

     EXPORT int32_t MIRABOX_CALL MiraBox_QueryDeviceInfo(int32_t deviceId, char* outBuffer, int32_t bufferSize) {
         try {
             auto device = GetDeviceByIndex(deviceId);
             if (device == nullptr) {
                 return -3; // Device not found
             }

             auto info = device->info();
             if (info == nullptr) {
                 return -4;
             }

             // Build a simple JSON response with device info
             std::string json = "{\"deviceName\":\"MiraBox N4 Pro\","
                 "\"width\":" + std::to_string(info->width) + ","
                 "\"height\":" + std::to_string(info->height) + ","
                 "\"keyWidth\":" + std::to_string(info->keyWidth) + ","
                 "\"keyHeight\":" + std::to_string(info->keyHeight) + ","
                 "\"minKey\":" + std::to_string(info->minKey) + ","
                 "\"maxKey\":" + std::to_string(info->maxKey) + "}";

             if (static_cast<size_t>(bufferSize) < json.size() + 1) {
                 return -5; // Buffer too small
             }

             std::memcpy(outBuffer, json.c_str(), json.size() + 1);
             return static_cast<int32_t>(json.size());
         }
         catch (...) {
             return -7;
         }
     }

     EXPORT void MIRABOX_CALL MiraBox_Shutdown() {
         g_CSharpCallback = nullptr;

         if (!g_IsInitialized.exchange(false)) {
             return;
         }

         try {
             auto& deviceManager = DeviceManager::instance();
             auto& streamDocks = deviceManager.getStreamDocks();
             for (const auto& [_, device] : streamDocks) {
                 if (device != nullptr && device->reader() != nullptr) {
                     device->reader()->stopReadLoop();
                 }
             }

             deviceManager.stopListen();
         }
         catch (...) {
             // NEVER let C++ exceptions cross the C boundary into C#. It will crash.
         }
     }
}