# Unity

Two Unity projects:

- `UnityTestBed` runs the MoonSharp test suite in CI (`.github/workflows/unity.yml`), in the
  editor (Mono) and in a Linux IL2CPP player, on Unity 2021.3, 2022.3 and 6000.0. It uses the
  UPM package as users get it and compiles the test sources itself.
- `MoonSharp` is for trying things by hand: a test runner scene, a debugger scene and a custom
  test scene, with the interpreter, debugger and test sources copied in by `ResynchAssets.sh`.

## Running the test bed locally

```bash
tools/upm/stage-local-package.sh        # the package, into .upm-staging/
src/Unity/UnityTestBed/sync.sh          # the test sources and TestMore scripts
unity-editor -batchmode -nographics -quit -logFile - -projectPath src/Unity/UnityTestBed \
  -executeMethod MoonSharp.UnityTestBed.BatchRunner.RunTests -moonsharpResults /tmp/editor.tsv
```

`BatchRunner.BuildLinuxIl2cpp -moonsharpBuild <dir>/TestBed.x86_64` builds the player; run it
with `-batchmode -nographics -moonsharpResults <file>`. Each results line is `OK`, `FAIL` or
`SKIP`, the test name and a message. `compare-results.sh` checks a results file against the .NET
run of `src/TestRunners/EmbeddedTestRunner`, which compiles the tests the same way.
