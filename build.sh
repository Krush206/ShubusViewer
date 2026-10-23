#!/bin/sh
# Builds Shubus Viewer for Linux with Mono.
#
# Requirements: mono-complete (mcs + xbuild), System.Windows.Forms support
# (shipped with mono-complete).
#
# Usage: ./build.sh          -> builds bin/Release/ShubusViewer.exe
#        ./build.sh clean    -> removes build output

set -e
cd "$(dirname "$0")"

if [ "$1" = "clean" ]; then
    rm -rf ShubusViewer/bin ShubusViewer/obj
    echo "Cleaned."
    exit 0
fi

if ! command -v xbuild >/dev/null 2>&1 && ! command -v msbuild >/dev/null 2>&1; then
    echo "ERROR: xbuild or msbuild not found. Install mono-complete first." >&2
    exit 1
fi

if command -v msbuild >/dev/null 2>&1; then
    cd ShubusViewer && msbuild "Shubus Viewer.csproj" -t:Build -p:Configuration=Release && cd ..
else
    cd ShubusViewer && xbuild "Shubus Viewer.csproj" /t:Build /p:Configuration=Release && cd ..
fi

cp README-LINUX.md ShubusViewer/bin/Release/ 2>/dev/null || true

echo
echo "Build OK: ShubusViewer/bin/Release/ShubusViewer.exe"
echo "Run with: mono ShubusViewer/bin/Release/ShubusViewer.exe [file]"
