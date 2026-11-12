#include "MiraBoxBridge.h"
#include "DeviceManager/devicemanager.h"
#include "OpenCVImageEncoder.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace {
    std::atomic<MiraBoxCallback> g_CSharpCallback = nullptr;
    std::atomic_bool g_IsInitialized = false;

    // Devices whose read callbacks are already registered. Registering again would
    // only replace the same entries, but it also restarted the read loop every time.
    std::mutex g_RegisteredMutex;
    std::vector<std::weak_ptr<StreamDock>> g_RegisteredDevices;

    // The SDK keys its callback map by logical control id. Ids above this are not
    // used by any device this bridge supports (the N4 Pro uses 1..29).
    constexpr uint8_t MaxLogicalControlId = 63;

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

    void Forward(const std::string& deviceKey, int32_t eventType, int32_t controlId, int32_t value)
    {
        const auto callback = g_CSharpCallback.load();
        if (callback != nullptr) {
            callback(deviceKey.c_str(), eventType, controlId, value);
        }
    }

    std::string FindDeviceKey(const StreamDock* device)
    {
        for (const auto& [key, candidate] : DeviceManager::instance().getStreamDocks()) {
            if (candidate.get() == device) {
                return key;
            }
        }

        return {};
    }

    bool MarkRegistered(const std::shared_ptr<StreamDock>& device)
    {
        std::lock_guard lock(g_RegisteredMutex);
        g_RegisteredDevices.erase(
            std::remove_if(g_RegisteredDevices.begin(), g_RegisteredDevices.end(),
                [](const std::weak_ptr<StreamDock>& entry) { return entry.expired(); }),
            g_RegisteredDevices.end());

        for (const auto& entry : g_RegisteredDevices) {
            if (entry.lock() == device) {
                return false;
            }
        }

        g_RegisteredDevices.push_back(device);
        return true;
    }

    void RegisterDeviceCallbacks(const std::shared_ptr<StreamDock>& device)
    {
        if (device == nullptr || device->reader() == nullptr || !MarkRegistered(device)) {
            return;
        }

        const auto deviceKey = FindDeviceKey(device.get());
        auto* reader = device->reader();
        for (uint16_t controlId = 0; controlId <= MaxLogicalControlId; ++controlId) {
            const auto logicalControlId = static_cast<uint8_t>(controlId);
            for (const auto eventType : g_ForwardedEvents) {
                reader->registerReadCallback(logicalControlId, [deviceKey, logicalControlId, eventType]() {
                    Forward(deviceKey, static_cast<int32_t>(eventType), static_cast<int32_t>(logicalControlId), 0);
                }, eventType);
            }
        }

        // setFrameBackgroundStream (the temporary background) encodes through the device's encoder.
        device->setEncoder(std::make_shared<OpenCVImageEncoder>());

        reader->startReadLoop();

        // Same start-up as the SDK's own N4 Pro sample. Without the heartbeat the device treats
        // the host as gone: it keeps its own standby behaviour (a key press blanks the screen or
        // brings the last images back) instead of reporting presses to us.
        if (auto* heartbeat = device->heartbeater()) {
            heartbeat->startHeartBeatLoop();
        }

        device->wakeupScreen();
        device->refresh();
    }

    void RegisterConnectedDeviceCallbacks()
    {
        for (const auto& [_, device] : DeviceManager::instance().getStreamDocks()) {
            RegisterDeviceCallbacks(device);
        }
    }

    std::shared_ptr<StreamDock> GetDevice(const char* deviceKey)
    {
        if (deviceKey == nullptr) {
            return nullptr;
        }

        auto& streamDocks = DeviceManager::instance().getStreamDocks();
        const auto it = streamDocks.find(deviceKey);
        return it == streamDocks.end() ? nullptr : it->second;
    }

    int32_t WriteString(const std::string& value, char* outBuffer, int32_t bufferSize)
    {
        if (outBuffer == nullptr || bufferSize <= 0) {
            return MIRABOX_INVALID_ARGUMENT;
        }

        if (static_cast<size_t>(bufferSize) < value.size() + 1) {
            return MIRABOX_BUFFER_TOO_SMALL;
        }

        std::memcpy(outBuffer, value.c_str(), value.size() + 1);
        return static_cast<int32_t>(value.size());
    }

    std::string JsonEscape(const std::string& value)
    {
        std::string escaped;
        escaped.reserve(value.size() + 2);
        for (const unsigned char c : value) {
            switch (c) {
            case '"': escaped += "\\\""; break;
            case '\\': escaped += "\\\\"; break;
            case '\n': escaped += "\\n"; break;
            case '\r': escaped += "\\r"; break;
            case '\t': escaped += "\\t"; break;
            default:
                if (c < 0x20) {
                    char buffer[8];
                    std::snprintf(buffer, sizeof(buffer), "\\u%04x", c);
                    escaped += buffer;
                } else {
                    escaped += static_cast<char>(c);
                }
            }
        }

        return escaped;
    }

    // Serial numbers are ASCII in practice; anything else is replaced.
    std::string Narrow(const std::wstring& value)
    {
        std::string narrow;
        narrow.reserve(value.size());
        for (const auto c : value) {
            narrow += (c > 0 && c < 0x80) ? static_cast<char>(c) : '?';
        }

        return narrow;
    }

    int32_t EncodeRgba(const uint8_t* rgba, int32_t bufferLength, int32_t width, int32_t height, int quality, const ImgHelper& helper, std::string& encoded)
    {
        if (rgba == nullptr || bufferLength <= 0 || width <= 0 || height <= 0) {
            return MIRABOX_INVALID_ARGUMENT;
        }

        const auto expectedLength = static_cast<size_t>(width) * static_cast<size_t>(height) * 4u;
        if (expectedLength != static_cast<size_t>(bufferLength)) {
            return MIRABOX_BUFFER_SIZE_MISMATCH;
        }

        // The host renders R, G, B, A; OpenCV (and so the SDK's canvas) expects B, G, R, A.
        // Copying the bytes as they are showed red as blue and blue as red on the device.
        RawCanvas canvas(width, height, true);
        auto* destination = canvas.pixels.data();
        for (size_t offset = 0; offset < expectedLength; offset += 4) {
            destination[offset] = rgba[offset + 2];
            destination[offset + 1] = rgba[offset + 1];
            destination[offset + 2] = rgba[offset];
            destination[offset + 3] = rgba[offset + 3];
        }

        OpenCVImageEncoder encoder;
        std::vector<uint8_t> encodedBuffer;
        if (!encoder.encodeToMemory(encodedBuffer, canvas, quality, helper)) {
            return MIRABOX_ENCODE_FAILED;
        }

        // The SDK's stream APIs take std::string, so one copy is unavoidable here.
        encoded.assign(reinterpret_cast<const char*>(encodedBuffer.data()), encodedBuffer.size());
        return MIRABOX_OK;
    }
}

extern "C" {
    EXPORT int32_t MIRABOX_CALL MiraBox_Initialize() {
        try {
            auto& deviceManager = DeviceManager::instance();
            deviceManager.enumerator();

            if (!g_IsInitialized.exchange(true)) {
                deviceManager.listen([](std::shared_ptr<StreamDock> device) {
                    try {
                        RegisterDeviceCallbacks(device);
                        Forward(FindDeviceKey(device.get()), MIRABOX_EVENT_DEVICES_CHANGED, 0, 0);
                    }
                    catch (...) {
                    }
                });
            }

            RegisterConnectedDeviceCallbacks();
            return MIRABOX_OK;
        }
        catch (...) {
            g_IsInitialized = false;
            // NEVER let C++ exceptions cross the C boundary into C#. It will crash.
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT void MIRABOX_CALL MiraBox_SetCallback(MiraBoxCallback callback) {
        g_CSharpCallback = callback;
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_GetDeviceCount() {
        try {
            return static_cast<int32_t>(DeviceManager::instance().getStreamDocks().size());
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_GetDeviceKey(int32_t index, char* outBuffer, int32_t bufferSize) {
        try {
            if (index < 0) {
                return MIRABOX_INVALID_ARGUMENT;
            }

            int32_t current = 0;
            for (const auto& [key, _] : DeviceManager::instance().getStreamDocks()) {
                if (current++ == index) {
                    return WriteString(key, outBuffer, bufferSize);
                }
            }

            return MIRABOX_DEVICE_NOT_FOUND;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_GetDeviceInfo(const char* deviceKey, char* outBuffer, int32_t bufferSize) {
        try {
            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            const auto* info = device->info();
            const auto* feature = device->feature();
            if (info == nullptr || feature == nullptr) {
                return MIRABOX_NOT_SUPPORTED;
            }

            std::string json = "{";
            json += "\"deviceKey\":\"" + JsonEscape(deviceKey) + "\",";
            json += "\"originType\":" + std::to_string(static_cast<int>(info->originType)) + ",";
            json += "\"vendorId\":" + std::to_string(info->vendor_id) + ",";
            json += "\"productId\":" + std::to_string(info->product_id) + ",";
            json += "\"serialNumber\":\"" + JsonEscape(Narrow(info->serialNumber)) + "\",";
            json += "\"firmwareVersion\":\"" + JsonEscape(info->firmwareVersion) + "\",";
            json += "\"width\":" + std::to_string(info->width) + ",";
            json += "\"height\":" + std::to_string(info->height) + ",";
            json += "\"keyWidth\":" + std::to_string(info->keyWidth) + ",";
            json += "\"keyHeight\":" + std::to_string(info->keyHeight) + ",";
            json += "\"minKey\":" + std::to_string(info->minKey) + ",";
            json += "\"maxKey\":" + std::to_string(info->maxKey) + ",";
            json += std::string("\"hasSecondScreen\":") + (feature->hasSecondScreen ? "true" : "false") + ",";
            json += "\"minSecondScreenKey\":" + std::to_string(feature->min2rdScreenKey) + ",";
            json += "\"maxSecondScreenKey\":" + std::to_string(feature->max2rdScreenKey) + ",";
            json += "\"secondScreenWidth\":" + std::to_string(feature->_2rdScreenWidth) + ",";
            json += "\"secondScreenHeight\":" + std::to_string(feature->_2rdScreenHeights) + ",";
            json += std::string("\"hasRgbLed\":") + (feature->hasRGBLed ? "true" : "false") + ",";
            json += "\"ledCount\":" + std::to_string(feature->ledCounts);
            json += "}";

            return WriteString(json, outBuffer, bufferSize);
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SendKeyImage(const char* deviceKey, int32_t keyId, const uint8_t* rgba, int32_t bufferLength, int32_t width, int32_t height) {
        try {
            if (keyId < 0 || keyId > UINT8_MAX) {
                return MIRABOX_INVALID_ARGUMENT;
            }

            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            const auto sdkKey = static_cast<uint8_t>(keyId);
            if (device->outOfRange(sdkKey)) {
                return MIRABOX_NOT_SUPPORTED;
            }

            auto imgHelper = device->getKyImgHelper(sdkKey);
            if (imgHelper == nullptr) {
                return MIRABOX_NOT_SUPPORTED;
            }

            std::string encoded;
            const auto result = EncodeRgba(rgba, bufferLength, width, height, 95, *imgHelper, encoded);
            if (result != MIRABOX_OK) {
                return result;
            }

            device->setKeyImgFileStream(encoded, sdkKey);
            // The device only shows newly sent images after a refresh (as in the SDK samples).
            device->refresh();
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SendBackground(const char* deviceKey, const uint8_t* rgba, int32_t bufferLength, int32_t width, int32_t height) {
        try {
            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            auto imgHelper = device->getBgImgHelper();
            if (imgHelper == nullptr) {
                return MIRABOX_NOT_SUPPORTED;
            }

            ImgHelper outputHelper = *imgHelper;
            if (outputHelper._width == 0) {
                outputHelper._width = static_cast<uint32_t>(width);
            }

            if (outputHelper._height == 0) {
                outputHelper._height = static_cast<uint32_t>(height);
            }

            std::string encoded;
            const auto result = EncodeRgba(rgba, bufferLength, width, height, 85, outputHelper, encoded);
            if (result != MIRABOX_OK) {
                return result;
            }

            device->setBackgroundImgStream(encoded);
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SendBackgroundFile(const char* deviceKey, const char* imagePath) {
        try {
            if (imagePath == nullptr) {
                return MIRABOX_INVALID_ARGUMENT;
            }

            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            auto imgHelper = device->getBgImgHelper();
            if (imgHelper == nullptr) {
                return MIRABOX_NOT_SUPPORTED;
            }

            const auto fileData = StreamDock::readImgToString(imagePath);
            if (fileData.empty()) {
                return MIRABOX_INVALID_ARGUMENT;
            }

            std::vector<uint8_t> input(fileData.begin(), fileData.end());
            std::vector<uint8_t> encoded;
            OpenCVImageEncoder encoder;
            if (!encoder.encodeToMemory(encoded, input, 85, *imgHelper)) {
                return MIRABOX_ENCODE_FAILED;
            }

            device->setBackgroundImgStream(std::string(reinterpret_cast<const char*>(encoded.data()), encoded.size()));
            device->refresh();
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SetTemporaryBackground(const char* deviceKey, const uint8_t* rgba, int32_t bufferLength, int32_t width, int32_t height) {
        try {
            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            if (device->feature() == nullptr || !device->feature()->isDualDevice || !device->feature()->supportBackGroundGif) {
                return MIRABOX_NOT_SUPPORTED;
            }

            // setFrameBackgroundStream returns nothing and only prints when it can't send; check
            // what it needs here, so the host hears about it (and tries again on the next connect).
            if (!device->canTransportWrite()) {
                return MIRABOX_NOT_READY;
            }

            if (const auto helper = device->getBackgroundGifHelper(); helper == nullptr || helper->_width == 0 || helper->_height == 0) {
                return MIRABOX_NOT_SUPPORTED;
            }

            // Lossless and unrotated: the SDK scales it to the screen, rotates it and encodes the JPEG.
            const ImgHelper passThrough(static_cast<uint32_t>(std::max(width, 0)), static_cast<uint32_t>(std::max(height, 0)), 0.0,
                ResizeOption::Scale, false, false, ImgType::PNG);
            std::string encoded;
            const auto result = EncodeRgba(rgba, bufferLength, width, height, 95, passThrough, encoded);
            if (result != MIRABOX_OK) {
                return result;
            }

            device->setFrameBackgroundStream(encoded);
            device->refresh();
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_ClearTemporaryBackground(const char* deviceKey) {
        try {
            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            if (device->feature() == nullptr || !device->feature()->isDualDevice || !device->feature()->supportBackGroundGif) {
                return MIRABOX_NOT_SUPPORTED;
            }

            device->gifer()->clearBackgroundGifStream();
            device->refresh();
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_ClearAllKeys(const char* deviceKey) {
        try {
            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            device->clearAllKeys();
            device->refresh();
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SetKeyBrightness(const char* deviceKey, uint8_t brightness) {
        try {
            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            device->setKeyBrightness(brightness);
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SetLedColors(const char* deviceKey, const uint8_t* rgb, int32_t ledCount) {
        try {
            if (rgb == nullptr || ledCount <= 0) {
                return MIRABOX_INVALID_ARGUMENT;
            }

            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            auto* rgbController = device->rgber();
            if (rgbController == nullptr || device->feature() == nullptr || !device->feature()->hasRGBLed) {
                return MIRABOX_NOT_SUPPORTED;
            }

            std::vector<std::array<uint8_t, 3>> colors(static_cast<size_t>(ledCount));
            for (size_t i = 0; i < colors.size(); ++i) {
                colors[i] = { rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2] };
            }

            rgbController->setSingleLedColor(colors);
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SetLedBrightness(const char* deviceKey, uint8_t brightness) {
        try {
            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            auto* rgbController = device->rgber();
            if (rgbController == nullptr) {
                return MIRABOX_NOT_SUPPORTED;
            }

            rgbController->setLedBrightness(brightness);
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT int32_t MIRABOX_CALL MiraBox_SetDeviceConfig(const char* deviceKey, const uint8_t* states, int32_t count) {
        try {
            if (states == nullptr || count <= 0 || count > 64) {
                return MIRABOX_INVALID_ARGUMENT;
            }

            auto device = GetDevice(deviceKey);
            if (device == nullptr) {
                return MIRABOX_DEVICE_NOT_FOUND;
            }

            auto* configer = device->configer();
            if (configer == nullptr || device->feature() == nullptr || !device->feature()->supportConfig) {
                return MIRABOX_NOT_SUPPORTED;
            }

            configer->setDeviceConfig(std::vector<uint8_t>(states, states + count));
            return MIRABOX_OK;
        }
        catch (...) {
            return MIRABOX_EXCEPTION;
        }
    }

    EXPORT void MIRABOX_CALL MiraBox_Shutdown() {
        g_CSharpCallback = nullptr;

        if (!g_IsInitialized.exchange(false)) {
            return;
        }

        try {
            auto& deviceManager = DeviceManager::instance();
            for (const auto& [_, device] : deviceManager.getStreamDocks()) {
                if (device != nullptr && device->reader() != nullptr) {
                    device->reader()->stopReadLoop();
                }

                if (device != nullptr && device->heartbeater() != nullptr) {
                    device->heartbeater()->stopHeartBeatLoop();
                }
            }

            deviceManager.stopListen();

            {
                std::lock_guard lock(g_RegisteredMutex);
                g_RegisteredDevices.clear();
            }

            // Destroy the devices now, while every thread is still alive. Otherwise the SDK's
            // singletons are destroyed during DLL_PROCESS_DETACH, after Windows has already
            // terminated the read/heartbeat/GIF threads, and joining them there aborts the
            // process (0xC0000409 on exit).
            deviceManager.getStreamDocks().clear();
        }
        catch (...) {
            // NEVER let C++ exceptions cross the C boundary into C#. It will crash.
        }
    }
}
