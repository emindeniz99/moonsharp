#!/usr/bin/env bash
# Installs the pinned lua-language-server release into a folder and prints the
# path of its executable. Used to check generated LuaCATS definitions
# (src/Tools/MoonSharp.LuaCats.Tests); not shipped.
#
#   tools/luals/install.sh <dir>
#
# 3.19.1 was tagged on 2026-08-14. The SHA-256 values are the asset digests
# the GitHub release lists; the four archives were downloaded and matched on
# 2026-10-28. 3.19.2 (2026-10-13) is newer; move to it once its digests have
# been read from the release.
set -euo pipefail

VERSION=3.19.1

if [ $# -ne 1 ]; then
  echo "usage: $0 <dir>" >&2
  exit 2
fi

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) PLATFORM=linux-x64; SHA256=e9235d2d72ef55bc41cf8c99cda2ed64777682024b4bb81f5dea425060c5cbb8 ;;
  Linux-aarch64) PLATFORM=linux-arm64; SHA256=abd2572e8fc929dc838a81ffb8473c5bce0bf39bfe8edb4b120b3b623176ce83 ;;
  Darwin-x86_64) PLATFORM=darwin-x64; SHA256=eb373c159cbe556711d7cd316315de2dce969bfd54b31edb7eb9cab2937f2cca ;;
  Darwin-arm64) PLATFORM=darwin-arm64; SHA256=0bc077f4447f076b4c92c14e9fd303f5b569eda2ec74b4dca2b55f75fae2e90c ;;
  *) echo "no pinned lua-language-server for $(uname -s) $(uname -m)" >&2; exit 1 ;;
esac

DIR="$1"
ARCHIVE="$DIR/lua-language-server-$VERSION-$PLATFORM.tar.gz"
mkdir -p "$DIR"

if [ ! -f "$ARCHIVE" ]; then
  curl -fsSL -o "$ARCHIVE.part" "https://github.com/LuaLS/lua-language-server/releases/download/$VERSION/lua-language-server-$VERSION-$PLATFORM.tar.gz"
  mv "$ARCHIVE.part" "$ARCHIVE"
fi

if command -v sha256sum >/dev/null 2>&1; then
  ACTUAL="$(sha256sum "$ARCHIVE" | cut -d' ' -f1)"
else
  ACTUAL="$(shasum -a 256 "$ARCHIVE" | cut -d' ' -f1)"
fi

if [ "$ACTUAL" != "$SHA256" ]; then
  rm -f "$ARCHIVE"
  echo "unexpected SHA-256 for $ARCHIVE: $ACTUAL (expected $SHA256)" >&2
  exit 1
fi

rm -rf "$DIR/$VERSION"
mkdir -p "$DIR/$VERSION"
tar -xzf "$ARCHIVE" -C "$DIR/$VERSION"
echo "$DIR/$VERSION/bin/lua-language-server"
