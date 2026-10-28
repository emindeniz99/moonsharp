package org.moonsharp.hosting;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** In-process behaviour with CoreCLR hosted through hostfxr. */
class CoreClrBehaviourTest extends InProcessBehaviourTest {
    @Override
    InProcessMoonSharp open() {
        return InProcessMoonSharp.coreClr(Fixtures.jniLibrary(), Fixtures.nativeAssembly(), null);
    }

    // One .NET runtime per process: asking for the other backend must fail clearly, not load a second runtime.
    @Test
    void theOtherBackendCannotBeLoadedToo() {
        IllegalStateException e = assertThrows(IllegalStateException.class,
            () -> InProcessMoonSharp.nativeAot(Fixtures.jniLibrary(), Fixtures.nativeAssembly()));
        assertTrue(e.getMessage().contains("already loaded in this process as coreclr"), e.getMessage());
    }
}
