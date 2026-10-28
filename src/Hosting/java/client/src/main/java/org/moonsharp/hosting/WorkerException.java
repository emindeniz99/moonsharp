package org.moonsharp.hosting;

/** The worker process failed: it exited, or its reply could not be read. Script errors are not reported this way. */
public final class WorkerException extends RuntimeException {
    public WorkerException(String message, Throwable cause) {
        super(message, cause);
    }
}
