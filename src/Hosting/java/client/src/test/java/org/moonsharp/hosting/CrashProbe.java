package org.moonsharp.hosting;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.TimeUnit;

/**
 * Shows what happens to the JVM when a script misbehaves, per transport. Not a unit test: several probes
 * end the process on purpose, so every probe runs in a child JVM and the parent reports how it ended.
 *
 * <pre>
 *   java -cp &lt;test classpath&gt; -Dmoonsharp.*=... org.moonsharp.hosting.CrashProbe &lt;worker|coreclr|aot&gt; [fixtures dir]
 * </pre>
 */
public final class CrashProbe {
    static final String[] PROBES = {
        // A .NET exception that is not a Lua error: string.rep asks for a string larger than .NET allows.
        "dotnet-exception",
        // Lua recursion without a tail call: MoonSharp's own VM stack fills up.
        "lua-recursion",
        // Recursion through a CLR callback (string.gsub calls back into Lua): the .NET thread stack fills up.
        "clr-recursion",
        // The same on a Java thread with an 8 MB stack instead of the default 1 MB (in-process calls run on the
        // calling Java thread's stack).
        "clr-recursion-8mb",
        // A script that never returns, then Thread.interrupt() on the calling thread.
        "interrupt",
        // SIGTERM to the JVM: do Java shutdown hooks still run once .NET is loaded?
        "sigterm",
        // SIGINT (Ctrl-C): the same question.
        "sigint",
        // SIGQUIT (kill -3): does the JVM still print a thread dump?
        "sigquit",
        // A Java NullPointerException after .NET is loaded: the JVM turns SIGSEGV into NPEs, .NET also handles SIGSEGV.
        "java-npe",
    };

    public static void main(String[] a) throws Exception {
        if (a.length > 2) {
            child(a[0], a[2]);
            return;
        }
        for (String probe : PROBES) parent(a[0], probe);
    }

    static void parent(String variant, String probe) throws Exception {
        List<String> command = new ArrayList<>();
        command.add(Path.of(System.getProperty("java.home"), "bin", "java").toString());
        command.add("-cp");
        command.add(System.getProperty("java.class.path"));
        for (String p : new String[] { "moonsharp.host.dll", "moonsharp.jni.lib", "moonsharp.native.dll", "moonsharp.aot.lib" })
            if (System.getProperty(p) != null) command.add("-D" + p + "=" + System.getProperty(p));
        command.add(CrashProbe.class.getName());
        command.add(variant);
        command.add("-");
        command.add(probe);

        long t0 = System.nanoTime();
        Process p = new ProcessBuilder(command).redirectErrorStream(true).start();
        List<String> lines = new ArrayList<>();
        Thread reader = new Thread(() -> {
            try (BufferedReader r = new BufferedReader(new InputStreamReader(p.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = r.readLine()) != null) {
                    synchronized (lines) {
                        lines.add(line);
                    }
                    if (line.equals("ready") && probe.equals("sigterm")) new ProcessBuilder("kill", "-TERM", "" + p.pid()).start();
                    if (line.equals("ready") && probe.equals("sigint")) new ProcessBuilder("kill", "-INT", "" + p.pid()).start();
                    if (line.equals("ready") && probe.equals("sigquit")) {
                        new ProcessBuilder("kill", "-QUIT", "" + p.pid()).start();
                    }
                }
            } catch (Exception e) {
                // the child is gone
            }
        });
        reader.start();

        boolean exited = p.waitFor(probe.equals("sigquit") ? 4 : 30, TimeUnit.SECONDS);
        if (!exited) {
            p.destroyForcibly();
            p.waitFor();
        }
        reader.join(2000);

        System.out.printf("== %s / %s: %s after %.1f s%n", variant, probe,
            exited ? "exit code " + p.exitValue() : "still running, killed", (System.nanoTime() - t0) / 1e9);
        synchronized (lines) {
            int shown = 0;
            for (String line : lines) {
                if (line.startsWith("Picked up JAVA_TOOL_OPTIONS")) continue;
                if (probe.equals("sigquit") && !line.startsWith("Full thread dump") && !line.equals("ready") && !line.startsWith("probe")) continue;
                if (shown++ < 12) System.out.println("   | " + (line.length() > 220 ? line.substring(0, 220) + "..." : line));
            }
            if (shown > 12) System.out.println("   | ... " + (shown - 12) + " more lines");
        }
    }

    static void child(String variant, String probe) throws Exception {
        MoonSharpHost host = HostBenchmark.open(variant);
        host.load("c", "ok", "return { ping = function() return 'pong' end }").valueOrThrow();

        switch (probe) {
            case "dotnet-exception" -> {
                print(host.run("c", "rep", "return string.rep('x', 2^31)", null));
                print(host.invoke("c", "ok", "ping"));
            }
            case "lua-recursion" -> {
                print(host.run("c", "rec", "local function f(n) return 1 + f(n + 1) end return f(1)", null));
                print(host.invoke("c", "ok", "ping"));
            }
            case "clr-recursion" -> {
                print(host.run("c", "gsub", "local function f(s) return (string.gsub(s, '.', f)) end return f('x')", null));
                print(host.invoke("c", "ok", "ping"));
            }
            case "clr-recursion-8mb" -> {
                Thread t = new Thread(null, () -> {
                    print(host.run("c", "gsub", "local function f(s) return (string.gsub(s, '.', f)) end return f('x')", null));
                    print(host.invoke("c", "ok", "ping"));
                }, "big-stack", 8L << 20);
                t.start();
                t.join();
            }
            case "interrupt" -> {
                Thread t = new Thread(() -> print(host.run("c", "loop", "while true do end", null)), "script");
                t.setDaemon(true);
                t.start();
                Thread.sleep(500);
                t.interrupt();
                long t0 = System.nanoTime();
                t.join(3000);
                System.out.printf("probe: after interrupt + 3 s join: thread %s, state %s, interrupted flag %s%n",
                    t.isAlive() ? "still running" : "finished", t.getState(), t.isInterrupted());
                if (host instanceof MoonSharpWorker w) {
                    w.close();
                    t.join(3000);
                    System.out.printf("probe: after closing the worker: thread %s%n", t.isAlive() ? "still running" : "finished");
                }
                System.out.printf("probe: calling System.exit(0) %.1f s after the interrupt%n", (System.nanoTime() - t0) / 1e9);
                System.exit(0);
            }
            case "sigterm", "sigint" -> {
                Runtime.getRuntime().addShutdownHook(new Thread(() -> System.out.println("probe: shutdown hook ran")));
                System.out.println("ready");
                System.out.flush();
                Thread.sleep(20_000);
                System.out.println("probe: the signal did not stop the JVM");
            }
            case "sigquit" -> {
                System.out.println("ready");
                System.out.flush();
                Thread.sleep(3_000);
            }
            case "java-npe" -> {
                int caught = 0;
                for (int i = 0; i < 100_000; i++) {
                    try {
                        npe(i % 2 == 0 ? null : "x");
                    } catch (NullPointerException e) {
                        caught++;
                    }
                }
                System.out.println("probe: caught " + caught + " NullPointerExceptions");
                print(host.invoke("c", "ok", "ping"));
            }
            default -> throw new IllegalArgumentException(probe);
        }
        host.close();
    }

    static int npe(String s) {
        return s.length();
    }

    static void print(HostResult r) {
        System.out.println("probe: " + (r.ok() ? "ok " + r.value() : r.error().kind() + ": " + r.error().message()));
        System.out.flush();
    }
}
