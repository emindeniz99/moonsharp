package org.moonsharp.hosting;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.BlockingQueue;
import java.util.function.Function;
import java.util.function.Supplier;

/**
 * A fixed set of {@link MoonSharpWorker} processes for parallel script calls. Every worker holds the
 * same modules: {@link #load} sends the module to all of them. A worker that dies is replaced and
 * gets the modules again.
 */
public final class MoonSharpPool implements AutoCloseable {
    public static final String CONTEXT = "pool";

    private final Supplier<MoonSharpWorker> factory;
    private final ContextOptions options;
    private final BlockingQueue<MoonSharpWorker> idle;
    private final List<MoonSharpWorker> all = Collections.synchronizedList(new ArrayList<>());
    private final Map<String, String> modules = Collections.synchronizedMap(new LinkedHashMap<>());

    public MoonSharpPool(int size, Supplier<MoonSharpWorker> factory) {
        this(size, factory, ContextOptions.DEFAULT);
    }

    /** A pool whose workers create their context with the given options, for example an instruction or time limit. */
    public MoonSharpPool(int size, Supplier<MoonSharpWorker> factory, ContextOptions options) {
        this.factory = factory;
        this.options = options;
        this.idle = new ArrayBlockingQueue<>(size);
        for (int i = 0; i < size; i++) {
            MoonSharpWorker w = start();
            all.add(w);
            idle.add(w);
        }
    }

    /** Loads or replaces a module in every worker. Returns the first error, if any worker reports one. */
    public synchronized HostResult load(String moduleId, String source) {
        modules.put(moduleId, source);
        List<MoonSharpWorker> workers = borrowAll();
        HostResult first = null;

        try {
            for (MoonSharpWorker w : workers) {
                HostResult r = w.load(CONTEXT, moduleId, source);
                if (first == null || (first.ok() && !r.ok())) first = r;
            }
        } finally {
            idle.addAll(workers);
        }

        return first;
    }

    public HostResult invoke(String moduleId, String function, Object... args) {
        return withWorker(w -> w.invoke(CONTEXT, moduleId, function, args));
    }

    public HostResult invokeJson(String moduleId, String function, String argsJson) {
        return withWorker(w -> w.invokeJson(CONTEXT, moduleId, function, argsJson));
    }

    public HostResult validate(String chunk, String source) {
        return withWorker(w -> w.validate(chunk, source));
    }

    private HostResult withWorker(Function<MoonSharpWorker, HostResult> call) {
        MoonSharpWorker w = take();
        try {
            return call.apply(w);
        } catch (WorkerException e) {
            w = replace(w);
            throw e;
        } finally {
            idle.add(w);
        }
    }

    private MoonSharpWorker take() {
        try {
            return idle.take();
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new WorkerException("interrupted while waiting for a MoonSharp worker", e);
        }
    }

    private List<MoonSharpWorker> borrowAll() {
        List<MoonSharpWorker> workers = new ArrayList<>();
        for (int i = 0; i < all.size(); i++) workers.add(take());
        return workers;
    }

    // Not synchronized on the pool: load() holds that lock while it waits for this worker to come back.
    private MoonSharpWorker replace(MoonSharpWorker dead) {
        dead.close();
        MoonSharpWorker fresh = start();
        Map<String, String> snapshot;

        synchronized (modules) {
            snapshot = new LinkedHashMap<>(modules);
        }

        snapshot.forEach((id, source) -> fresh.load(CONTEXT, id, source));

        synchronized (all) {
            all.set(all.indexOf(dead), fresh);
        }

        return fresh;
    }

    private MoonSharpWorker start() {
        MoonSharpWorker w = factory.get();
        w.create(CONTEXT, options);
        return w;
    }

    @Override
    public void close() {
        synchronized (all) {
            all.forEach(MoonSharpWorker::close);
        }
    }
}
