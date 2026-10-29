#!/usr/bin/env bash
# Compiles the UnityTestBed the way Unity does, with Unity's own Roslyn and reference assemblies
# taken from a unityci/editor image (README.md says how to extract them).
#
# Usage: unity-compile.sh UNITY_DATA_DIR VERSION_DEFINES MODE(editor|player) OUT
#   UNITY_DATA_DIR: the image's /opt/unity/Editor/Data (Managed, NetStandard, DotNetSdkRoslyn)
#   VERSION_DEFINES: Unity's version defines, e.g. "UNITY_2021_3;UNITY_2021_3_OR_NEWER"
#   REPO (optional): the checkout to compile, default this repository. It needs the staged package
#   (tools/upm/stage-local-package.sh, .upm-staging/) and the synced test bed (src/Unity/UnityTestBed/sync.sh).
set -euo pipefail
if [ $# -ne 4 ]; then
  awk 'NR > 1 && /^#/ { sub(/^# ?/, ""); print; next } NR > 1 { exit }' "$0"
  exit 2
fi
U="$1"; VERDEFS="$2"; MODE="$3"; OUT="$4"
REPO="${REPO:-$(cd "$(dirname "$0")/../../../.." && pwd)}"
BED="$REPO/src/Unity/UnityTestBed"
mkdir -p "$OUT"

COMMON="UNITY_5_3_OR_NEWER;$VERDEFS;UNITY_STANDALONE_LINUX;UNITY_STANDALONE;PLATFORM_STANDALONE_LINUX;PLATFORM_STANDALONE;UNITY_64;NET_STANDARD_2_0;NET_STANDARD;NETSTANDARD2_1;NETSTANDARD;CSHARP_7_3_OR_NEWER"
if [ "$MODE" = editor ]; then
  DEFS="$COMMON;UNITY_EDITOR;UNITY_EDITOR_64;UNITY_EDITOR_LINUX;ENABLE_MONO;DEBUG;TRACE"
  ENGINE_REFS=$(find "$U/Managed/UnityEngine" -name '*.dll' | sort | sed 's/^/-r:/')
else
  DEFS="$COMMON;ENABLE_IL2CPP"
  ENGINE_REFS=$(find "$U/Managed/UnityEngine" -name 'UnityEngine*.dll' | sort | sed 's/^/-r:/')
fi

SHIMS=$(find "$U/NetStandard/compat/2.1.0/shims" -name '*.dll' | sort | sed 's/^/-r:/')
csc() {
  # Unity 2021.3 to 6000.0 compile user code as C# 9.
  dotnet "$U/DotNetSdkRoslyn/csc.dll" -nologo -noconfig -nostdlib -target:library -langversion:9.0 \
    -deterministic -debug:portable -optimize+ -nowarn:1701,1702 \
    "-r:$U/NetStandard/ref/2.1.0/netstandard.dll" $SHIMS "-define:$DEFS" "$@"
}

csc -out:"$OUT/MoonSharp.Interpreter.dll" $ENGINE_REFS \
  $(find "$REPO/.upm-staging/org.moonsharp.moonsharp/Runtime" -name '*.cs' | sort)
GEN=$(find "$REPO/.upm-staging/org.moonsharp.moonsharp/Runtime/Analyzers" -name "*.dll" | sed "s/^/-analyzer:/")
csc -out:"$OUT/MoonSharp.Interpreter.Tests.dll" $GEN $ENGINE_REFS -r:"$OUT/MoonSharp.Interpreter.dll" \
  $(find "$BED/Assets/MoonSharp.Interpreter.Tests" -name '*.cs' | sort)
csc -out:"$OUT/Assembly-CSharp.dll" $GEN $ENGINE_REFS -r:"$OUT/MoonSharp.Interpreter.dll" -r:"$OUT/MoonSharp.Interpreter.Tests.dll" \
  $(find "$BED/Assets" -name '*.cs' -not -path "$BED/Assets/Editor/*" -not -path "$BED/Assets/MoonSharp.Interpreter.Tests/*" | sort)
if [ "$MODE" = editor ]; then
  csc -out:"$OUT/Assembly-CSharp-Editor.dll" $ENGINE_REFS -r:"$OUT/MoonSharp.Interpreter.dll" \
    -r:"$OUT/MoonSharp.Interpreter.Tests.dll" -r:"$OUT/Assembly-CSharp.dll" \
    $(find "$BED/Assets/Editor" -name '*.cs' | sort)
fi
echo "compiled $MODE into $OUT"
