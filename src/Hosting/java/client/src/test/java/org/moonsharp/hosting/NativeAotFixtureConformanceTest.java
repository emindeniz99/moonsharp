package org.moonsharp.hosting;

/** The shared fixtures through the NativeAOT build of MoonSharp loaded into this JVM (JNI). */
class NativeAotFixtureConformanceTest extends FixtureConformanceTest {
    @Override
    MoonSharpHost open() {
        return InProcessMoonSharp.nativeAot(Fixtures.jniLibrary(), Fixtures.aotLibrary());
    }
}
