#!/usr/bin/env bash
# Compiles tools/sourcegen/Sample.cs with the Roslyn versions Unity ships, with
# MoonSharp.SourceGenerator as an analyzer: 3.8.0 (the version the Unity 2021.3 and 2022.3
# manuals require) and 4.3.1 (what 2021.3.45f2 to Unity 6000.3 ship). Sample.cs calls
# GeneratedUserData.RegisterAll(), which exists only if the generator loaded and ran, so a
# successful compile is the check. The compiler packages are pinned by SHA-256.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="${1:-$REPO_ROOT/.sourcegen-check}"
export DOTNET_ROLL_FORWARD=Major

dotnet build "$REPO_ROOT/src/MoonSharp.Interpreter/MoonSharp.Interpreter.csproj" -c Release -f netstandard2.0 -v quiet -nologo >/dev/null
dotnet build "$REPO_ROOT/src/MoonSharp.SourceGenerator/MoonSharp.SourceGenerator.csproj" -c Release -v quiet -nologo >/dev/null
dotnet restore "$REPO_ROOT/src/MoonSharp.SourceGenerator/MoonSharp.SourceGenerator.csproj" -v quiet >/dev/null

INTERPRETER="$REPO_ROOT/src/MoonSharp.Interpreter/bin/Release/netstandard2.0/MoonSharp.Interpreter.dll"
GENERATOR="$REPO_ROOT/src/MoonSharp.SourceGenerator/bin/Release/netstandard2.0/MoonSharp.SourceGenerator.dll"
NETSTANDARD="$(find "${NUGET_PACKAGES:-$HOME/.nuget/packages}/netstandard.library/2.0.3" -name netstandard.dll -path '*ref*' | head -n 1)"

check() {
  local version="$1" sha="$2"
  local dir="$WORK/roslyn-$version"

  if [ ! -d "$dir" ]; then
    mkdir -p "$dir"
    curl -sSfL -o "$dir.nupkg" "https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/$version/microsoft.net.compilers.toolset.$version.nupkg"
    echo "$sha  $dir.nupkg" | sha256sum -c - >/dev/null
    (cd "$dir" && unzip -q "$dir.nupkg")
  fi

  local csc
  csc="$(find "$dir/tasks" -path '*bincore/csc.dll' | grep -v net472 | head -n 1)"
  dotnet "$csc" -nologo -t:library -langversion:9 -nostdlib -out:"$WORK/Sample-$version.dll" \
    -r:"$NETSTANDARD" -r:"$INTERPRETER" -analyzer:"$GENERATOR" -nowarn:MSUD001 "$REPO_ROOT/tools/sourcegen/Sample.cs"
  echo "Roslyn $version: generated and compiled"
}

check 3.8.0 8b889bc0cd497cea6827a8e189965821a90fc553e275d643f15e1e6b8a3f22de
check 4.3.1 ccbb7f75ba7271f5fad020e8b1b4eeffe5c56da5dd3a17797c2943aacf29ef78
