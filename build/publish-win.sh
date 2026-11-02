#!/usr/bin/env bash
# Build a self-contained, single-file Windows x64 executable of PT Kit Builder.
# Output: build/publish-win/PT Kit Builder.exe
# The target machine needs NO .NET runtime installed. x64 builds also run on
# Windows on ARM laptops through emulation.
#
# Note: the produced .exe is unsigned. SmartScreen/Defender may warn on first run.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet publish src/KitBuilder.App/KitBuilder.App.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o build/publish-win

EXE="build/publish-win/PT Kit Builder.exe"
VERSION=$(dotnet msbuild src/KitBuilder.App/KitBuilder.App.csproj -getProperty:Version)
echo
echo "Published: $EXE  (v$VERSION, $(stat -c%s "$EXE") bytes)"
echo "Copy it anywhere; it asks for the deployment-kit folder on first run."
