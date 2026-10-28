package org.moonsharp.hosting.bench;

import org.moonsharp.hosting.HostResult;
import org.moonsharp.hosting.MoonSharpPool;
import org.moonsharp.hosting.MoonSharpWorker;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

/**
 * Measures the Java -> worker -> MoonSharp path. Not a unit test: run it by hand.
 *   java -cp <test classpath> org.moonsharp.hosting.bench.WorkerBenchmark <moonsharp-host.dll> <fixtures dir>
 * Reports cold start, per-call latency percentiles (with the script's own run time from the envelope,
 * so the rest is process I/O and JSON), and throughput for several pool sizes.
 */
public final class WorkerBenchmark {
    static final String ARGS = "[{\"level\":12,\"country\":\"DE\",\"flags\":{\"beta\":true}},\"offers\"]";

    public static void main(String[] a) throws Exception {
        String dll = a[0];
        String source = Files.readString(Path.of(a[1], "scripts", "eligibility.lua"));
        System.out.println("java " + System.getProperty("java.version") + ", " + Runtime.getRuntime().availableProcessors() + " cores");

        // Cold: start a process, load the module, make the first call.
        long[] cold = new long[5];
        for (int i = 0; i < cold.length; i++) {
            long t0 = System.nanoTime();
            try (MoonSharpWorker w = MoonSharpWorker.startDll(dll)) {
                w.load("c", "eligibility", source).valueOrThrow();
                w.invokeJson("c", "eligibility", "eligible", ARGS).valueOrThrow();
                cold[i] = System.nanoTime() - t0;
            }
        }
        Arrays.sort(cold);
        System.out.printf("cold start + load + first call: median %.1f ms (min %.1f, max %.1f, n=%d)%n",
            cold[2] / 1e6, cold[0] / 1e6, cold[4] / 1e6, cold.length);

        // Hot: one worker, sequential calls.
        try (MoonSharpWorker w = MoonSharpWorker.startDll(dll)) {
            w.load("c", "eligibility", source).valueOrThrow();
            for (int i = 0; i < 20000; i++) w.invokeJson("c", "eligibility", "eligible", ARGS);

            int n = 50000;
            long[] lat = new long[n];
            double[] script = new double[n];
            for (int i = 0; i < n; i++) {
                long t0 = System.nanoTime();
                HostResult r = w.invokeJson("c", "eligibility", "eligible", ARGS);
                lat[i] = System.nanoTime() - t0;
                script[i] = r.timing().path("runMs").asDouble() * 1000.0;
            }
            Arrays.sort(lat);
            Arrays.sort(script);
            System.out.printf("hot call, 1 worker, sequential (n=%d): p50 %.1f us, p95 %.1f us, p99 %.1f us, max %.1f us%n",
                n, lat[n / 2] / 1e3, lat[n * 95 / 100] / 1e3, lat[n * 99 / 100] / 1e3, lat[n - 1] / 1e3);
            System.out.printf("  of which inside ScriptHost.Invoke (parse args and run): p50 %.1f us, p99 %.1f us%n",
                script[n / 2], script[n * 99 / 100]);
        }

        // Throughput: pools of 1, 2 and 4 workers, 8 client threads.
        for (int size : new int[] { 1, 2, 4 }) {
            try (MoonSharpPool pool = new MoonSharpPool(size, () -> MoonSharpWorker.startDll(dll))) {
                pool.load("eligibility", source).valueOrThrow();
                ExecutorService exec = Executors.newFixedThreadPool(8);
                run(pool, exec, 20000);
                long t0 = System.nanoTime();
                int calls = 100000;
                run(pool, exec, calls);
                double secs = (System.nanoTime() - t0) / 1e9;
                System.out.printf("throughput, pool of %d, 8 client threads: %,.0f calls/s%n", size, calls / secs);
                exec.shutdown();
            }
        }
    }

    static void run(MoonSharpPool pool, ExecutorService exec, int calls) throws Exception {
        List<Future<?>> fs = new ArrayList<>();
        int perTask = calls / 8;
        for (int t = 0; t < 8; t++)
            fs.add(exec.submit(() -> {
                for (int i = 0; i < perTask; i++) pool.invokeJson("eligibility", "eligible", ARGS).valueOrThrow();
                return null;
            }));
        for (Future<?> f : fs) f.get();
    }
}
