package org.moonsharp.hosting;

/**
 * The script operations every Java transport offers (see docs/hosting.md, "The contract").
 * {@link MoonSharpWorker} runs them in a separate .NET process; {@link InProcessMoonSharp} runs them
 * inside the JVM. A context is one Lua state, named by the caller and created on first use.
 */
public interface MoonSharpHost extends AutoCloseable {
    String version();

    HostResult load(String context, String moduleId, String source);

    boolean unload(String context, String moduleId);

    /** Calls a module function. Arguments are serialized with {@link Json#MAPPER}; BigInteger and BigDecimal stay exact. */
    HostResult invoke(String context, String moduleId, String function, Object... args);

    /** Calls a module function with arguments given as JSON array text. */
    HostResult invokeJson(String context, String moduleId, String function, String argsJson);

    HostResult run(String context, String chunk, String source, String inputJson);

    HostResult validate(String chunk, String source);

    boolean dispose(String context);

    @Override
    void close();
}
