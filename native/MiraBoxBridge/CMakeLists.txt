cmake_minimum_required(VERSION 3.21)

project(MiraBoxBridge LANGUAGES CXX)

# CMakeList.txt : CMake project for MiraBoxBridge, include source and define
# project specific logic here.
#

set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")

foreach(config Debug Release RelWithDebInfo MinSizeRel)
    string(TOUPPER "${config}" config_upper)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${config_upper} "${CMAKE_BINARY_DIR}/bin/${config}")
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY_${config_upper} "${CMAKE_BINARY_DIR}/bin/${config}")
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY_${config_upper} "${CMAKE_BINARY_DIR}/lib/${config}")
endforeach()

# The SDK's static libraries end up inside this shared library, so they must be position
# independent (Linux and macOS; it changes nothing for MSVC).
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# The SDK looks for its bundled hidapi archive relative to the top-level project, which is this
# folder and not the SDK's. Declaring it first (the first declaration wins) makes the SDK use its
# own copy instead of downloading whatever hidapi's master branch is at build time.
set(MIRABOX_BUNDLED_HIDAPI "${CMAKE_CURRENT_SOURCE_DIR}/../StreamDock-SDK/CPP-SDK/third_party/hidapi-master.zip")
if(EXISTS "${MIRABOX_BUNDLED_HIDAPI}")
    include(FetchContent)
    FetchContent_Declare(hidapi URL "${MIRABOX_BUNDLED_HIDAPI}" DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
endif()

# 1. Add the vendor's SDK folder. 
# This runs the CMakeLists.txt shown in your image.
# (The second argument is just a temporary build folder name for their stuff)
add_subdirectory("../StreamDock-SDK/CPP-SDK" "build_sdk")

# The SDK's opencv.zip stores the macOS dylib symlinks (libopencv_core.dylib, ...412.dylib) as small
# text files that name their target, so the linker rejects them ("unknown file type"). The SDK has
# extracted the archive by now: turn those stubs into the symlinks they stand for.
if(APPLE)
    file(GLOB MIRABOX_OPENCV_STUBS "${CMAKE_CURRENT_SOURCE_DIR}/../StreamDock-SDK/CPP-SDK/ImgProcesser/third_party/opencv/mac/lib/libopencv_*.dylib")
    foreach(stub IN LISTS MIRABOX_OPENCV_STUBS)
        if(NOT IS_SYMLINK "${stub}")
            file(SIZE "${stub}" stub_size)
            if(stub_size LESS 256)
                file(READ "${stub}" stub_target)
                string(STRIP "${stub_target}" stub_target)
                file(REMOVE "${stub}")
                file(CREATE_LINK "${stub_target}" "${stub}" SYMBOLIC)
            endif()
        endif()
    endforeach()
endif()

if(WIN32 AND TARGET TransportDLL)
    set_target_properties(TransportDLL PROPERTIES
        MAP_IMPORTED_CONFIG_RELWITHDEBINFO Release
        MAP_IMPORTED_CONFIG_MINSIZEREL Release
    )
endif()

# 2. Replicate their source file collection (ignoring their main.cpp)
file(GLOB_RECURSE HOTSPOTDEVICE
    "../StreamDock-SDK/CPP-SDK/src/HotspotDevice/**/*.cpp"
    "../StreamDock-SDK/CPP-SDK/src/HotspotDevice/**/*.h"
)

set(VENDOR_SOURCES
    ${HOTSPOTDEVICE}
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo/streamdock.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo/streamdockfactory.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo/Feature/RGBController/rgbcontroller.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo/Feature/GifController/gifcontroller.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo/Feature/ReadController/readcontroller.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo/Feature/Configer/configer.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo/Feature/HeartBeat/heartbeat.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceManager/DeviceEnumerator/deviceenumerator.cpp"
    "../StreamDock-SDK/CPP-SDK/src/DeviceManager/devicemanager.cpp"
)

if(WIN32)
    list(APPEND VENDOR_SOURCES "../StreamDock-SDK/CPP-SDK/src/DeviceManager/devicemanager_win.cpp")
elseif(APPLE)
    list(APPEND VENDOR_SOURCES "../StreamDock-SDK/CPP-SDK/src/DeviceManager/devicemanager_mac.cpp")
elseif(UNIX)
    list(APPEND VENDOR_SOURCES "../StreamDock-SDK/CPP-SDK/src/DeviceManager/devicemanager_linux.cpp")
endif()

# 3. Create your Shared Library using your code PLUS their source code
add_library(MiraBoxBridge SHARED 
    "MiraBoxBridge.cpp" 
    "MiraBoxBridge.h"
    ${VENDOR_SOURCES}
)

set_target_properties(MiraBoxBridge PROPERTIES
    CXX_STANDARD 20
    CXX_STANDARD_REQUIRED ON
)

# 4. Include the same directories they do
target_include_directories(MiraBoxBridge PRIVATE
    "../StreamDock-SDK/CPP-SDK/src"
    "../StreamDock-SDK/CPP-SDK/src/DeviceManager/DeviceEnumerator"
    "../StreamDock-SDK/CPP-SDK/src/DeviceInfo"
    "../StreamDock-SDK/CPP-SDK/ImgProcesser/src/Gif2ImgFrame"
    "../StreamDock-SDK/CPP-SDK/ImgProcesser/src/ImgHelper"
    "../StreamDock-SDK/CPP-SDK/ImgProcesser/src/OpenCVImageEncoder"
    "../StreamDock-SDK/CPP-SDK/src/Transport"
    "../StreamDock-SDK/CPP-SDK/src/ToolKit"
)

# 5. Link against their internal libraries
target_link_libraries(MiraBoxBridge PRIVATE TransportCWrapper ImgProcesser TransportDLL)

# 6. Apply platform-specific links (copied from their file)
if(WIN32)
    target_link_libraries(MiraBoxBridge PRIVATE setupapi)
elseif(APPLE)
    find_library(COREFOUNDATION_FRAMEWORK CoreFoundation)
    find_library(IOKIT_FRAMEWORK IOKit)
    find_library(APPLICATIONSERVICES_FRAMEWORK ApplicationServices)
    target_link_libraries(MiraBoxBridge PRIVATE ${COREFOUNDATION_FRAMEWORK} ${IOKIT_FRAMEWORK} ${APPLICATIONSERVICES_FRAMEWORK})
elseif(UNIX AND NOT APPLE)
    target_link_libraries(MiraBoxBridge PRIVATE udev pthread)
endif()

if(WIN32)
    add_custom_command(TARGET MiraBoxBridge POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different $<TARGET_RUNTIME_DLLS:MiraBoxBridge> $<TARGET_FILE_DIR:MiraBoxBridge>
        COMMAND_EXPAND_LISTS
    )
else()
    # Linux and macOS: the bridge finds its libraries next to itself (the app's plugin folder),
    # not at the absolute paths of this build machine. Everything it loads is copied there.
    if(APPLE)
        set(MIRABOX_RPATH "@loader_path")
    else()
        set(MIRABOX_RPATH "$ORIGIN")
    endif()
    set_target_properties(MiraBoxBridge PROPERTIES
        BUILD_WITH_INSTALL_RPATH ON
        INSTALL_RPATH "${MIRABOX_RPATH}"
    )

    set(MIRABOX_RUNTIME_LIBS "$<TARGET_FILE:TransportDLL>")
    if(APPLE)
        # The SDK links the OpenCV dylibs it ships for macOS; they go along (by their versioned name).
        file(GLOB MIRABOX_OPENCV_DYLIBS "${CMAKE_CURRENT_SOURCE_DIR}/../StreamDock-SDK/CPP-SDK/ImgProcesser/third_party/opencv/mac/lib/libopencv_*.*.dylib")
        list(APPEND MIRABOX_RUNTIME_LIBS ${MIRABOX_OPENCV_DYLIBS})
    endif()

    add_custom_command(TARGET MiraBoxBridge POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different ${MIRABOX_RUNTIME_LIBS} $<TARGET_FILE_DIR:MiraBoxBridge>
        COMMAND_EXPAND_LISTS
    )
endif()

install(TARGETS MiraBoxBridge
    RUNTIME DESTINATION .
    LIBRARY DESTINATION .
    ARCHIVE DESTINATION lib
)

if(WIN32)
    install(FILES $<TARGET_RUNTIME_DLLS:MiraBoxBridge>
        DESTINATION .
    )
endif()
