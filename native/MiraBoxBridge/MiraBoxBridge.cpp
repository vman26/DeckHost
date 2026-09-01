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