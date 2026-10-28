package org.moonsharp.hosting;

/**
 * Options for {@link MoonSharpHost#create(String, ContextOptions)} (docs/hosting.md, "Limits").
 *
 * @param int64            adds the int64 and uint64 modules (docs/int64.md)
 * @param instructionLimit instructions one call may run before it stops with error kind {@code interrupted}; 0 for no limit
 * @param timeLimitMs      milliseconds one call may run before it stops with error kind {@code interrupted}; 0 for no limit
 */
public record ContextOptions(boolean int64, long instructionLimit, int timeLimitMs) {
    public static final ContextOptions DEFAULT = new ContextOptions(false, 0, 0);

    public ContextOptions {
        if (instructionLimit < 0 || timeLimitMs < 0) throw new IllegalArgumentException("limits must be 0 (none) or positive");
    }

    public ContextOptions withInt64(boolean int64) {
        return new ContextOptions(int64, instructionLimit, timeLimitMs);
    }

    public ContextOptions withInstructionLimit(long instructionLimit) {
        return new ContextOptions(int64, instructionLimit, timeLimitMs);
    }

    public ContextOptions withTimeLimitMs(int timeLimitMs) {
        return new ContextOptions(int64, instructionLimit, timeLimitMs);
    }
}
