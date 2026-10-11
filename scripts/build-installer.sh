#!/usr/bin/env bash
# Збирає інсталятор dist/SchoolBell-Setup-<версія>.exe на macOS або Linux.
#
# Потрібно:
#   .NET 10 SDK   macOS:  brew install --cask dotnet-sdk   (або https://dotnet.microsoft.com/download)
#   NSIS          macOS:  brew install makensis            Linux: sudo apt install nsis
#
# Запуск:  ./scripts/build-installer.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

red()   { printf '\033[31m%s\033[0m\n' "$*"; }
cyan()  { printf '\033[36m==> %s\033[0m\n' "$*"; }
green() { printf '\033[32m%s\033[0m\n' "$*"; }

if ! command -v dotnet >/dev/null 2>&1; then
  red "Не знайдено .NET SDK. macOS: brew install --cask dotnet-sdk  (потрібна версія 10)"
  exit 1
fi
if ! dotnet --list-sdks | grep -q '^10\.'; then
  red "Потрібен .NET 10 SDK. Встановлені: $(dotnet --list-sdks | cut -d' ' -f1 | tr '\n' ' ')"
  red "macOS: brew install --cask dotnet-sdk   або   https://dotnet.microsoft.com/download/dotnet/10.0"
  exit 1
fi
if ! command -v makensis >/dev/null 2>&1; then
  red "Не знайдено makensis. macOS: brew install makensis    Linux: sudo apt install nsis"
  exit 1
fi

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/SchoolBell.App/SchoolBell.App.csproj | head -n1)"
VERSION="${VERSION:-1.0.0}"
PUBLISH="$ROOT/publish"
DIST="$ROOT/dist"
OUT="$DIST/SchoolBell-Setup-$VERSION.exe"

cyan "Тести логіки розкладу і тривоги"
dotnet run --project tests/SchoolBell.Core.Tests -c Release

cyan "Збірка SchoolBell.exe для Windows x64 (версія $VERSION)"
rm -rf "$PUBLISH"
dotnet publish src/SchoolBell.App/SchoolBell.App.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:DebugType=none \
  -o "$PUBLISH"

if [[ ! -f "$PUBLISH/SchoolBell.exe" ]]; then
  red "Не знайдено $PUBLISH/SchoolBell.exe після публікації."
  exit 1
fi

cyan "Пакування інсталятора"
mkdir -p "$DIST"
makensis -V2 -INPUTCHARSET UTF8 \
  "-DVERSION=$VERSION" \
  "-DPUBLISH_DIR=$PUBLISH" \
  "-DOUT_FILE=$OUT" \
  installer/SchoolBell.nsi

echo
green "Готово: $OUT"
echo "Скопіюйте цей файл на шкільний комп'ютер і запустіть."
