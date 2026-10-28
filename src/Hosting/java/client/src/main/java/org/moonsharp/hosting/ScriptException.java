package org.moonsharp.hosting;

/** Thrown by {@link HostResult#valueOrThrow()} when a script failed. */
public final class ScriptException extends RuntimeException {
    private final HostResult.HostError error;

    public ScriptException(HostResult.HostError error) {
        super(error.kind() + " error: " + error.message());
        this.error = error;
    }

    public HostResult.HostError error() {
        return error;
    }
}
