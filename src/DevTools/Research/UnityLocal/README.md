# Unity's compiler and Unity's Mono without a Unity licence

Two local checks from [register-vm-spike.md](../../../../docs/research/register-vm-spike.md), "How to
measure on IL2CPP" ("Checked without Unity"):

| File | What it does |
|---|---|
| `unity-compile.sh` | Compiles the Unity test bed (`src/Unity/UnityTestBed`) as Unity does: Unity's own Roslyn (`DotNetSdkRoslyn/csc.dll`), C# 9, `netstandard` 2.1 with Unity's shims, Unity's engine assemblies, editor or player defines, the source generator as an analyzer. Output: `MoonSharp.Interpreter.dll`, `MoonSharp.Interpreter.Tests.dll`, `Assembly-CSharp.dll` (and `Assembly-CSharp-Editor.dll` in editor mode) |
| `mono/` | `bench.exe`: the `workloads-tsv` runner of `src/DevTools/Benchmarks` (the same three files the test bed compiles) as a net48 executable, to run on Unity's Mono |
| `rounds.sh` | Interleaved rounds of `workloads-tsv` on .NET and Unity's Mono for two builds plus an A/A copy; `src/Unity/UnityTestBed/bench-summary.sh` summarizes the files |
| `registry-manifest.sh` | Prints a `unityci/editor` image manifest from Docker Hub, to find the layers when no Docker daemon is available |

This does not replace the Unity workflow: it shows the code compiles with each editor's compiler and
references, and runs on Unity's Mono. It says nothing about IL2CPP code generation, stripping or players.
The project is standalone: no solution, test runner or Dockerfile builds it.

## Inputs

- Unity's editor data for each version: `/opt/unity/Editor/Data/{Managed,NetStandard,DotNetSdkRoslyn}` from
  the images `.github/workflows/unity.yml` pins (`unityci/editor:ubuntu-2021.3.45f2-linux-il2cpp-3.2.2`,
  `ubuntu-2022.3.62f3-...`, `ubuntu-6000.0.85f1-...`, with their digests). With Docker:

  ```sh
  id=$(docker create unityci/editor:ubuntu-2021.3.45f2-linux-il2cpp-3.2.2@sha256:903ced48a1d4f415b597280b76c4f01f1bd38f66c654805fb3ffd16874d7d2ee)
  mkdir -p /tmp/unity-2021.3 && for d in Managed NetStandard DotNetSdkRoslyn; do docker cp $id:/opt/unity/Editor/Data/$d /tmp/unity-2021.3/; done
  docker rm $id
  ```

  Without Docker: `registry-manifest.sh <digest>` lists the image's platform manifests; run it again on the
  linux/amd64 one to get its layers. Download the layer that holds `opt/unity/Editor/Data` from
  `https://registry-1.docker.io/v2/unityci/editor/blobs/<layer digest>` (with the same bearer token, `curl -L`)
  and extract only those three folders, for example
  `curl ... | tar -xz -C /tmp/unity-2021.3 opt/unity/Editor/Data/Managed opt/unity/Editor/Data/NetStandard opt/unity/Editor/Data/DotNetSdkRoslyn`.
  Each version's three folders take about 100 MB; the layer download is several GB. 2022.3 was not
  extracted for the note.
- The staged package and the synced test bed: `tools/upm/stage-local-package.sh` (writes `.upm-staging/`)
  and `src/Unity/UnityTestBed/sync.sh`.
- Unity's Mono for `bench.exe`: `unity-mono-linux-2026-02-24.3.tar.gz` from
  <https://github.com/Benjamin-Dobell/unity-mono>, the archive the `Dockerfile` pins, checked against the
  SHA-256 there and unpacked into a private prefix.

## Build and run

From the repository root:

```sh
u=src/DevTools/Research/UnityLocal
mise exec -- tools/upm/stage-local-package.sh && src/Unity/UnityTestBed/sync.sh
mise exec -- $u/unity-compile.sh /tmp/unity-2021.3 "UNITY_2021_3;UNITY_2021_3_OR_NEWER" player /tmp/uc/2021.3-player
mise exec -- $u/unity-compile.sh /tmp/unity-2021.3 "UNITY_2021_3;UNITY_2021_3_OR_NEWER" editor /tmp/uc/2021.3-editor

# Unity's Mono against .NET, this branch against a baseline worktree (both with their Benchmarks and net45 interpreter built)
mise exec -- dotnet build -c Release -f net45 src/MoonSharp.Interpreter
mise exec -- dotnet build -c Release $u/mono -o /tmp/uc/mono-this
mise exec -- dotnet build -c Release $u/mono -p:MoonSharpDll=<baseline>/src/MoonSharp.Interpreter/bin/Release/net45/MoonSharp.Interpreter.dll -o /tmp/uc/mono-base
MONO=<unity-mono prefix>/bin/mono mise exec -- $u/rounds.sh /tmp/uc/rounds 5 \
  src/DevTools/Benchmarks/bin/Release/net8.0/Benchmarks.dll <baseline>/src/DevTools/Benchmarks/bin/Release/net8.0/Benchmarks.dll \
  /tmp/uc/mono-this/bench.exe /tmp/uc/mono-base/bench.exe
```

`unity-compile.sh` needs the staged package's `Runtime/Analyzers` (the source generator). The scripts print
their usage when called without arguments; `rounds.sh` takes the shared benchmark lock (`BENCH_LOCK`,
default `/tmp/moonsharp-bench.lock`). The two `mono/` builds share `obj/`, so run them one after the other.
