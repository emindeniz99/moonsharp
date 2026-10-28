package org.moonsharp.hosting;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.CyclicBarrier;
import java.util.function.IntFunction;

/**
 * Compares the Java transports on the eligibility rule from the fixtures. Not a unit test: run it by hand,
 * one variant per JVM (a JVM can load MoonSharp in process only once):
 *
 * <pre>
 *   java -cp &lt;test classpath&gt; [-Dmoonsharp.*=...] org.moonsharp.hosting.HostBenchmark &lt;variant&gt; &lt;fixtures dir&gt; [cold|hot]
 * </pre>
 *
 * variant: {@code worker} (moonsharp.host.dll), {@code coreclr} (moonsharp.jni.lib, moonsharp.native.dll),
 * {@code aot} (moonsharp.jni.lib, moonsharp.aot.lib) or {@code none} (cold start of an empty JVM).
 *
 * <ul>
 *   <li>{@code cold}: starts 5 fresh JVMs and times each from process start to the first call's result.
 *   <li>{@code hot} (default): sequential latency over 50,000 calls after 20,000 warm-up calls, where the time
 *       goes, throughput with 1/2/4/8 threads (one context, or one worker process, per thread), and memory.
 *   <li>{@code memory}: resident memory for 1 and 8 contexts; run it with {@code -Xms64m -Xmx64m}.
 * </ul>
 * Same script, arguments and call counts as {@code bench.WorkerBenchmark}.
 */
public final class HostBenchmark {
    static final String ARGS = "[{\"level\":12,\"country\":\"DE\",\"flags\":{\"beta\":true}},\"offers\"]";
    static final int WARMUP = 20_000, CALLS = 50_000, THROUGHPUT_CALLS = 100_000;

    public static void main(String[] a) throws Exception {
        String variant = a[0];
        Path fixtures = Path.of(a[1]);
        String mode = a.length > 2 ? a[2] : "hot";

        switch (mode) {
            case "cold" -> cold(variant, fixtures);
            case "cold-child" -> coldChild(variant, fixtures);
            case "hot" -> hot(variant, fixtures);
            case "memory" -> memory(variant, fixtures);
            default -> throw new IllegalArgumentException("mode " + mode);
        }
    }

    // ------------------------------------------------------------------------------------------- cold

    static void cold(String variant, Path fixtures) throws Exception {
        List<String> command = new ArrayList<>();
        command.add(Path.of(System.getProperty("java.home"), "bin", "java").toString());
        command.add("-cp");
        command.add(System.getProperty("java.class.path"));
        for (String p : new String[] { "moonsharp.host.dll", "moonsharp.jni.lib", "moonsharp.native.dll", "moonsharp.aot.lib" })
            if (System.getProperty(p) != null) command.add("-D" + p + "=" + System.getProperty(p));
        command.add(HostBenchmark.class.getName());
        command.add(variant);
        command.add(fixtures.toString());
        command.add("cold-child");

        int runs = 5;
        double[] total = new double[runs];
        String[] detail = new String[runs];

        for (int i = 0; i < runs; i++) {
            long t0 = System.nanoTime();
            Process p = new ProcessBuilder(command).redirectError(ProcessBuilder.Redirect.INHERIT).start();
            try (BufferedReader r = new BufferedReader(new InputStreamReader(p.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = r.readLine()) != null && !line.startsWith("first-result")) { }
                total[i] = (System.nanoTime() - t0) / 1e6;
                detail[i] = line;
                while (r.readLine() != null) { }
            }
            p.waitFor();
        }

        double[] sorted = total.clone();
        Arrays.sort(sorted);
        System.out.printf("%s cold: JVM start to first result: median %.1f ms (min %.1f, max %.1f, n=%d)%n",
            variant, sorted[runs / 2], sorted[0], sorted[runs - 1], runs);
        for (int i = 0; i < runs; i++) System.out.printf("  run %d: %.1f ms, %s%n", i + 1, total[i], detail[i]);
    }

    static void coldChild(String variant, Path fixtures) throws Exception {
        long t0 = System.nanoTime();
        if (variant.equals("none")) {
            System.out.println("first-result (empty JVM)");
            return;
        }

        String source = Files.readString(fixtures.resolve("scripts/eligibility.lua"));
        // Every variant parses replies with Jackson; its first use costs the same everywhere, so time it apart.
        HostResult.from(Json.MAPPER.readTree("{\"ok\":true,\"value\":[1,{\"a\":0.5}],\"output\":\"\"}"));
        long tj = System.nanoTime();
        MoonSharpHost host = open(variant);
        long t1 = System.nanoTime();
        host.load("c", "eligibility", source).valueOrThrow();
        long t2 = System.nanoTime();
        host.invokeJson("c", "eligibility", "eligible", ARGS).valueOrThrow();
        long t3 = System.nanoTime();
        System.out.printf("first-result Jackson %.1f ms, open %.1f ms, load %.1f ms, first call %.1f ms%n",
            (tj - t0) / 1e6, (t1 - tj) / 1e6, (t2 - t1) / 1e6, (t3 - t2) / 1e6);
        System.out.flush();
        host.close();
    }

    static MoonSharpHost open(String variant) {
        return switch (variant) {
            case "worker" -> MoonSharpWorker.startDll(System.getProperty("moonsharp.host.dll"));
            case "coreclr" -> InProcessMoonSharp.coreClr(Path.of(System.getProperty("moonsharp.jni.lib")),
                Path.of(System.getProperty("moonsharp.native.dll")), null);
            case "aot" -> InProcessMoonSharp.nativeAot(Path.of(System.getProperty("moonsharp.jni.lib")),
                Path.of(System.getProperty("moonsharp.aot.lib")));
            default -> throw new IllegalArgumentException("variant " + variant);
        };
    }

    // -------------------------------------------------------------------------------------------- hot

    static void hot(String variant, Path fixtures) throws Exception {
        String source = Files.readString(fixtures.resolve("scripts/eligibility.lua"));
        System.out.println("java " + System.getProperty("java.version") + ", " + Runtime.getRuntime().availableProcessors()
            + " cores, variant " + variant);
        memory("before loading MoonSharp", List.of());

        List<MoonSharpHost> workers = new ArrayList<>();
        MoonSharpHost host = open(variant);
        workers.add(host);
        host.load("c", "eligibility", source).valueOrThrow();

        for (int i = 0; i < WARMUP; i++) host.invokeJson("c", "eligibility", "eligible", ARGS).valueOrThrow();

        // Pass 1 follows WorkerBenchmark exactly. .NET tiered compilation may still be promoting methods
        // during it, so pass 2 repeats it immediately to show the settled state.
        latency(host, "pass 1");
        latency(host, "pass 2");

        if (host instanceof InProcessMoonSharp ip) breakdown(ip);
        memory("after 1 context, " + (WARMUP + CALLS) + " calls", workers);

        // In process every thread gets its own context; with workers every thread gets its own process.
        List<MoonSharpHost> perThread = new ArrayList<>();
        for (int threads : new int[] { 1, 2, 4, 8 }) {
            while (perThread.size() < threads) {
                int t = perThread.size();
                MoonSharpHost h = variant.equals("worker") && t > 0 ? open(variant) : host;
                if (!workers.contains(h)) workers.add(h);
                h.load("t" + t, "eligibility", source).valueOrThrow();
                perThread.add(h);
            }

            throughput(threads, t -> perThread.get(t), WARMUP);
            double[] rate = new double[3];
            for (int r = 0; r < rate.length; r++)
                rate[r] = THROUGHPUT_CALLS / throughput(threads, t -> perThread.get(t), THROUGHPUT_CALLS);
            Arrays.sort(rate);
            System.out.printf("throughput, %d thread(s), one %s per thread: %,.0f calls/s (median of 3; min %,.0f, max %,.0f)%n",
                threads, variant.equals("worker") ? "worker process" : "context", rate[1], rate[0], rate[2]);
        }
        memory("after throughput (8 contexts)", workers);

        for (MoonSharpHost w : workers) w.close();
    }

    static void latency(MoonSharpHost host, String label) {
        long[] lat = new long[CALLS];
        double[] script = new double[CALLS];
        for (int i = 0; i < CALLS; i++) {
            long t0 = System.nanoTime();
            HostResult r = host.invokeJson("c", "eligibility", "eligible", ARGS);
            lat[i] = System.nanoTime() - t0;
            script[i] = r.timing().path("runMs").asDouble() * 1000.0;
        }
        System.out.println("hot call, sequential, invokeJson -> HostResult, " + label + " (n=" + CALLS + "): " + percentiles(lat));
        Arrays.sort(script);
        System.out.printf("  inside ScriptHost.Invoke (parse arguments, run): p50 %.1f us, p99 %.1f us%n",
            script[CALLS / 2], script[CALLS * 99 / 100]);
    }

    // ----------------------------------------------------------------------------------------- memory

    /**
     * Resident memory with a fixed Java heap (run with -Xms64m -Xmx64m), so the variants differ only in
     * what MoonSharp adds: the .NET runtime in this process, or worker processes beside it.
     */
    static void memory(String variant, Path fixtures) throws Exception {
        String source = Files.readString(fixtures.resolve("scripts/eligibility.lua"));
        HostResult.from(Json.MAPPER.readTree("{\"ok\":true,\"value\":[1,{\"a\":0.5}],\"output\":\"\"}"));
        memory("JVM with Jackson, before MoonSharp", List.of());
        if (variant.equals("none")) return;

        List<MoonSharpHost> hosts = new ArrayList<>();
        hosts.add(open(variant));
        memory("after open", hosts);

        for (int contexts : new int[] { 1, 8 }) {
            while (hosts.size() < contexts && variant.equals("worker")) hosts.add(open(variant));
            for (int t = 0; t < contexts; t++) {
                MoonSharpHost h = hosts.get(variant.equals("worker") ? t : 0);
                h.load("t" + t, "eligibility", source).valueOrThrow();
                for (int i = 0; i < 10_000; i++) h.invokeJson("t" + t, "eligibility", "eligible", ARGS).valueOrThrow();
            }
            memory(contexts + " context(s), 10,000 calls each", hosts);
        }

        for (MoonSharpHost h : hosts) h.close();
    }

    /** Where a hot in-process call spends its time, by removing one layer at a time. */
    static void breakdown(InProcessMoonSharp host) {
        long ctx = host.context("c");
        byte[] module = "eligibility".getBytes(StandardCharsets.UTF_8);
        byte[] function = "eligible".getBytes(StandardCharsets.UTF_8);
        byte[] args = ARGS.getBytes(StandardCharsets.UTF_8);
        byte[] reply = null;

        // Native call with pre-encoded arguments: no Java string encoding, no Jackson.
        for (int i = 0; i < WARMUP; i++) reply = NativeBridge.call(ctx, NativeBridge.OP_INVOKE, module, function, args);
        long[] raw = new long[CALLS];
        for (int i = 0; i < CALLS; i++) {
            long t0 = System.nanoTime();
            reply = NativeBridge.call(ctx, NativeBridge.OP_INVOKE, module, function, args);
            raw[i] = System.nanoTime() - t0;
        }

        // The boundary alone: JNI, the managed transition, UTF-8 decode and encode, alloc and free; no Lua.
        for (int i = 0; i < WARMUP; i++) NativeBridge.call(0, NativeBridge.OP_ECHO, args, null, null);
        long[] echo = new long[CALLS];
        for (int i = 0; i < CALLS; i++) {
            long t0 = System.nanoTime();
            NativeBridge.call(0, NativeBridge.OP_ECHO, args, null, null);
            echo[i] = System.nanoTime() - t0;
        }

        // Java-side JSON: encode the arguments and parse the reply into a HostResult.
        long[] java = new long[CALLS];
        for (int i = 0; i < WARMUP + CALLS; i++) {
            long t0 = System.nanoTime();
            ARGS.getBytes(StandardCharsets.UTF_8);
            try {
                HostResult.from(Json.MAPPER.readTree(reply));
            } catch (IOException e) {
                throw new IllegalStateException(e);
            }
            if (i >= WARMUP) java[i - WARMUP] = System.nanoTime() - t0;
        }

        System.out.println("  native call only, pre-encoded bytes in and out: " + percentiles(raw));
        System.out.println("  boundary only (echo of the arguments, no Lua): " + percentiles(echo));
        System.out.println("  Java JSON only (encode arguments, Jackson-parse reply into HostResult): " + percentiles(java));
        System.out.println("  reply: " + new String(reply, StandardCharsets.UTF_8));
    }

    static double throughput(int threads, IntFunction<MoonSharpHost> hostFor, int calls) throws Exception {
        int perThread = calls / threads;
        CyclicBarrier start = new CyclicBarrier(threads + 1);
        List<Thread> ts = new ArrayList<>();
        Throwable[] failure = new Throwable[1];

        for (int t = 0; t < threads; t++) {
            MoonSharpHost h = hostFor.apply(t);
            String context = "t" + t;
            Thread th = new Thread(() -> {
                try {
                    start.await();
                    for (int i = 0; i < perThread; i++) h.invokeJson(context, "eligibility", "eligible", ARGS).valueOrThrow();
                } catch (Throwable e) {
                    failure[0] = e;
                }
            });
            th.start();
            ts.add(th);
        }

        start.await();
        long t0 = System.nanoTime();
        for (Thread th : ts) th.join();
        double secs = (System.nanoTime() - t0) / 1e9;
        if (failure[0] != null) throw new IllegalStateException(failure[0]);
        return secs;
    }

    static String percentiles(long[] ns) {
        long[] s = ns.clone();
        Arrays.sort(s);
        int n = s.length;
        return String.format("p50 %.1f us, p95 %.1f us, p99 %.1f us, max %.1f us",
            s[n / 2] / 1e3, s[n * 95 / 100] / 1e3, s[n * 99 / 100] / 1e3, s[n - 1] / 1e3);
    }

    /** Peak (VmHWM) and current (VmRSS) resident memory of this JVM and of any worker processes. */
    static void memory(String when, List<MoonSharpHost> hosts) throws IOException {
        long[] self = status(Path.of("/proc/self/status"));
        StringBuilder sb = new StringBuilder(String.format("memory %s: JVM VmHWM %d MB, VmRSS %d MB", when, self[0] / 1024, self[1] / 1024));
        long hwm = 0, rss = 0;
        int n = 0;
        for (MoonSharpHost h : hosts) {
            if (h instanceof MoonSharpWorker w) {
                long[] child = status(Path.of("/proc/" + w.pid() + "/status"));
                hwm += child[0];
                rss += child[1];
                n++;
            }
        }
        if (n > 0) sb.append(String.format("; %d worker process(es) VmHWM %d MB, VmRSS %d MB (sum)", n, hwm / 1024, rss / 1024));
        System.out.println(sb);
    }

    static long[] status(Path file) throws IOException {
        long hwm = 0, rss = 0;
        for (String line : Files.readAllLines(file)) {
            if (line.startsWith("VmHWM:")) hwm = kb(line);
            if (line.startsWith("VmRSS:")) rss = kb(line);
        }
        return new long[] { hwm, rss };
    }

    static long kb(String line) {
        return Long.parseLong(line.replaceAll("[^0-9]", ""));
    }
}
