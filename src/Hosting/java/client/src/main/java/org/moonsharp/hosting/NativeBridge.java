package org.moonsharp.hosting;

import java.nio.file.Path;

/**
 * JNI functions of libmoonsharp_jni.so (src/Hosting/native). Strings travel as UTF-8 byte arrays.
 * MoonSharp is loaded once per process, through CoreCLR or as NativeAOT, and stays loaded.
 */
final class NativeBridge {
    static final int OP_ECHO = 0, OP_LOAD = 1, OP_INVOKE = 2, OP_RUN = 3, OP_VALIDATE = 4, OP_UNLOAD = 5, OP_VERSION = 6;

    private static String loadedLibrary;
    private static String openedAs;

    private NativeBridge() { }

    /** Loads the JNI library and opens MoonSharp. Opening the same backend again is a no-op. */
    static synchronized void open(Path jniLibrary, String backend, Opener opener) {
        String lib = jniLibrary.toAbsolutePath().toString();

        if (loadedLibrary == null) {
            System.load(lib);
            loadedLibrary = lib;
        } else if (!loadedLibrary.equals(lib)) {
            throw new IllegalStateException("libmoonsharp_jni is already loaded from " + loadedLibrary);
        }

        if (openedAs != null) {
            if (!openedAs.equals(backend))
                throw new IllegalStateException("MoonSharp is already loaded in this process as " + openedAs + ", not " + backend);
            return;
        }

        String error = opener.open();
        if (error != null)
            throw new IllegalStateException("cannot load MoonSharp (" + backend + "): " + error);
        openedAs = backend;
    }

    interface Opener {
        String open();
    }

    /** Returns null on success, or an error message. dotnetRoot may be null. */
    static native String openCoreClr(String runtimeConfig, String assembly, String dotnetRoot);

    /** Returns null on success, or an error message. */
    static native String openAot(String library);

    /** Option bit for {@link #contextCreate}: the int64 and uint64 modules. */
    static final int CONTEXT_INT64 = 1;

    /** Returns a context id, or 0 on failure. options is 0 or {@link #CONTEXT_INT64}; a limit of 0 means none. */
    static native long contextCreate(int options, long instructionLimit, int timeLimitMs);

    static native boolean contextDispose(long context);

    /** Returns the UTF-8 result envelope. a, b and c may be null. */
    static native byte[] call(long context, int op, byte[] a, byte[] b, byte[] c);
}
