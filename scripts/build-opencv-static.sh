#!/usr/bin/env bash
# Builds a minimal, static, position-independent OpenCV (core, imgproc, imgcodecs with its own
# zlib/libpng/libjpeg) for the native bridge on Linux, so the bridge doesn't depend on the
# distribution's OpenCV (which pulls in GDAL, GDCM, OpenEXR, ... at a distro-specific version).
#
#   scripts/build-opencv-static.sh <install-prefix> [opencv-source-dir]
#
# Without a source dir, OpenCV $OPENCV_VERSION (default 4.10.0) is downloaded from GitHub.
# Then build DeckHost with:  -p:MiraBoxCMakeArgs="-DOpenCV_DIR=<install-prefix>/lib/cmake/opencv4"
set -euo pipefail

prefix="${1:?usage: build-opencv-static.sh <install-prefix> [opencv-source-dir]}"
source_dir="${2:-}"
version="${OPENCV_VERSION:-4.10.0}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

if [[ -z "$source_dir" ]]; then
    curl -fsSL "https://github.com/opencv/opencv/archive/refs/tags/${version}.tar.gz" | tar xz -C "$work"
    source_dir="$work/opencv-${version}"
fi

cmake -S "$source_dir" -B "$work/build" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$prefix" \
    -DCMAKE_POSITION_INDEPENDENT_CODE=ON \
    -DBUILD_SHARED_LIBS=OFF \
    -DBUILD_LIST=core,imgproc,imgcodecs \
    -DBUILD_ZLIB=ON -DBUILD_PNG=ON -DBUILD_JPEG=ON \
    -DWITH_PNG=ON -DWITH_JPEG=ON \
    -DWITH_TIFF=OFF -DWITH_WEBP=OFF -DWITH_OPENJPEG=OFF -DWITH_JASPER=OFF -DWITH_OPENEXR=OFF \
    -DWITH_IMGCODEC_HDR=OFF -DWITH_IMGCODEC_SUNRASTER=OFF -DWITH_IMGCODEC_PXM=OFF -DWITH_IMGCODEC_PFM=OFF \
    -DWITH_GDAL=OFF -DWITH_GDCM=OFF -DWITH_AVIF=OFF -DWITH_SPNG=OFF \
    -DWITH_IPP=OFF -DWITH_ITT=OFF -DWITH_TBB=OFF -DWITH_OPENMP=OFF -DWITH_PTHREADS_PF=ON \
    -DWITH_OPENCL=OFF -DWITH_OPENGL=OFF -DWITH_LAPACK=OFF -DWITH_EIGEN=OFF -DWITH_VA=OFF -DWITH_VA_INTEL=OFF \
    -DWITH_GTK=OFF -DWITH_QT=OFF -DWITH_FFMPEG=OFF -DWITH_GSTREAMER=OFF -DWITH_V4L=OFF -DWITH_1394=OFF \
    -DWITH_PROTOBUF=OFF -DWITH_ADE=OFF -DWITH_QUIRC=OFF -DWITH_CAROTENE=OFF -DWITH_KLEIDICV=OFF \
    -DBUILD_TESTS=OFF -DBUILD_PERF_TESTS=OFF -DBUILD_EXAMPLES=OFF -DBUILD_DOCS=OFF -DBUILD_opencv_apps=OFF \
    -DBUILD_opencv_python3=OFF -DBUILD_JAVA=OFF -DOPENCV_GENERATE_PKGCONFIG=OFF \
    -DCV_TRACE=OFF

cmake --build "$work/build" --parallel "$(nproc 2>/dev/null || sysctl -n hw.ncpu)"
cmake --install "$work/build"
echo "OpenCV ${version} (static) installed to $prefix"
