package org.moonsharp.hosting;

import com.fasterxml.jackson.core.JsonProcessingException;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Runs MoonSharp inside the JVM: CoreCLR loaded through hostfxr ({@link #coreClr}) or a NativeAOT
 * library ({@link #nativeAot}), both reached through JNI (src/Hosting/native). Linux x64 only.
 *
 * <p>Thread safety: any thread may call any method. Each context has a lock on the .NET side, so calls
 * on one context run one at a time; for parallel calls give each thread its own context.
 *
 * <p>What the process boundary of {@link MoonSharpWorker} protected against now reaches the JVM: a .NET
 * stack overflow or a fatal runtime error ends the JVM, and a script that never returns cannot be
 * stopped (interrupting the Java thread has no effect while it runs .NET code). See docs/hosting.md.
 *
 * <p>The .NET runtime cannot be unloaded: {@link #close()} drops this instance's contexts, and the
 * runtime stays loaded until the JVM exits. Only one backend can be loaded per process.
 */
public final class InProcessMoonSharp implements MoonSharpHost {
    private final ConcurrentHashMap<String, Long> contexts = new ConcurrentHashMap<>();

    private InProcessMoonSharp() { }

    /**
     * Starts CoreCLR in this process (once) and loads MoonSharp.Hosting.Native.dll.
     *
     * @param jniLibrary path of libmoonsharp_jni.so
     * @param assembly path of MoonSharp.Hosting.Native.dll; its .runtimeconfig.json and the MoonSharp
     *                 assemblies must sit next to it
     * @param dotnetRoot .NET install directory, or null to use DOTNET_ROOT or the default locations
     */
    public static InProcessMoonSharp coreClr(Path jniLibrary, Path assembly, String dotnetRoot) {
        String dll = assembly.toAbsolutePath().toString();
        String config = dll.replaceFirst("\\.dll$", "") + ".runtimeconfig.json";
        NativeBridge.open(jniLibrary, "coreclr", () -> NativeBridge.openCoreClr(config, dll, dotnetRoot));
        return new InProcessMoonSharp();
    }

    /**
     * Loads the NativeAOT build of MoonSharp (once). No .NET installation is needed.
     *
     * @param jniLibrary path of libmoonsharp_jni.so
     * @param aotLibrary path of MoonSharp.Hosting.Native.so from {@code dotnet publish -r linux-x64 -p:NativeAot=true}
     */
    public static InProcessMoonSharp nativeAot(Path jniLibrary, Path aotLibrary) {
        String lib = aotLibrary.toAbsolutePath().toString();
        NativeBridge.open(jniLibrary, "nativeaot", () -> NativeBridge.openAot(lib));
        return new InProcessMoonSharp();
    }

    @Override
    public String version() {
        return call(0, NativeBridge.OP_VERSION, null, null, null).value().asText();
    }

    @Override
    public HostResult load(String context, String moduleId, String source) {
        return call(context(context), NativeBridge.OP_LOAD, utf8(moduleId), utf8(source), null);
    }

    @Override
    public boolean unload(String context, String moduleId) {
        return call(context(context), NativeBridge.OP_UNLOAD, utf8(moduleId), null, null).value().asBoolean();
    }

    @Override
    public HostResult invoke(String context, String moduleId, String function, Object... args) {
        byte[] argsJson;
        try {
            argsJson = Json.MAPPER.writeValueAsBytes(args);
        } catch (JsonProcessingException e) {
            throw new IllegalArgumentException("arguments cannot be written as JSON", e);
        }
        return call(context(context), NativeBridge.OP_INVOKE, utf8(moduleId), utf8(function), argsJson);
    }

    @Override
    public HostResult invokeJson(String context, String moduleId, String function, String argsJson) {
        return call(context(context), NativeBridge.OP_INVOKE, utf8(moduleId), utf8(function), utf8(argsJson));
    }

    @Override
    public HostResult run(String context, String chunk, String source, String inputJson) {
        return call(context(context), NativeBridge.OP_RUN, utf8(chunk), utf8(source), utf8(inputJson));
    }

    @Override
    public HostResult validate(String chunk, String source) {
        return call(context("__validate"), NativeBridge.OP_VALIDATE, utf8(chunk), utf8(source), null);
    }

    @Override
    public boolean dispose(String context) {
        Long id = contexts.remove(context);
        return id != null && NativeBridge.contextDispose(id);
    }

    @Override
    public void close() {
        for (String name : contexts.keySet())
            dispose(name);
    }

    /** The native id of a context, created on first use. Package-private for the benchmark. */
    long context(String name) {
        return contexts.computeIfAbsent(name, n -> {
            long id = NativeBridge.contextCreate();
            if (id == 0) throw new IllegalStateException("MoonSharp could not create a script context");
            return id;
        });
    }

    private static HostResult call(long context, int op, byte[] a, byte[] b, byte[] c) {
        byte[] reply = NativeBridge.call(context, op, a, b, c);
        try {
            return HostResult.from(Json.MAPPER.readTree(reply));
        } catch (IOException e) {
            throw new IllegalStateException("MoonSharp returned invalid JSON: " + new String(reply, StandardCharsets.UTF_8), e);
        }
    }

    private static byte[] utf8(String s) {
        return s == null ? null : s.getBytes(StandardCharsets.UTF_8);
    }
}
