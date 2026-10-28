package org.moonsharp.hosting;

/** The shared fixtures through the worker process ({@code moonsharp-host worker}). */
class WorkerFixtureConformanceTest extends FixtureConformanceTest {
    @Override
    MoonSharpHost open() {
        return MoonSharpWorker.startDll(Fixtures.hostDll());
    }

    @Override
    String hostName() {
        return "java-worker";
    }
}
