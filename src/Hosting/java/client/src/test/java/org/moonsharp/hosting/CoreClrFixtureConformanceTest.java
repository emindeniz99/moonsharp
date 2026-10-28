package org.moonsharp.hosting;

/** The shared fixtures through CoreCLR hosted inside this JVM (hostfxr + JNI). */
class CoreClrFixtureConformanceTest extends FixtureConformanceTest {
    @Override
    MoonSharpHost open() {
        return InProcessMoonSharp.coreClr(Fixtures.jniLibrary(), Fixtures.nativeAssembly(), null);
    }
}
