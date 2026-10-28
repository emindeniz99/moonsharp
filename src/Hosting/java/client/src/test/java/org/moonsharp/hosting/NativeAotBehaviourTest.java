package org.moonsharp.hosting;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

/** In-process behaviour with the NativeAOT build of MoonSharp. */
class NativeAotBehaviourTest extends InProcessBehaviourTest {
    @Override
    InProcessMoonSharp open() {
        return InProcessMoonSharp.nativeAot(Fixtures.jniLibrary(), Fixtures.aotLibrary());
    }

    // One .NET runtime per process: asking for the other backend must fail clearly, not load a second runtime.
    @Test
    void theOtherBackendCannotBeLoadedToo() {
        IllegalStateException e = assertThrows(IllegalStateException.class,
            () -> InProcessMoonSharp.coreClr(Fixtures.jniLibrary(), Fixtures.aotLibrary(), null));
        assertTrue(e.getMessage().contains("already loaded in this process as nativeaot"), e.getMessage());
    }
}
