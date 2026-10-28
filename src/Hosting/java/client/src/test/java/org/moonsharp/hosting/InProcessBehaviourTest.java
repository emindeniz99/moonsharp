package org.moonsharp.hosting;

import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.TestInstance;

import java.math.BigInteger;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

import static org.junit.jupiter.api.Assertions.*;

/**
 * What the in-process transports must guarantee beyond the fixtures: the same behaviour as the worker
 * (reload, isolation, exact numbers), plus what only matters in process: strings cross the JNI boundary
 * intact, .NET exceptions never reach the JVM, and concurrent calls on one context do not corrupt it.
 */
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
abstract class InProcessBehaviourTest {
    InProcessMoonSharp host;

    abstract InProcessMoonSharp open();

    // Opened before the first test rather than in @BeforeAll, so that a missing native build marks the
    // tests as skipped in the report instead of hiding the class.
    @BeforeEach
    void start() {
        if (host == null) host = open();
    }

    @AfterAll
    void stop() {
        if (host != null) host.close();
    }

    @Test
    void reloadingAModuleReplacesItsFunctions() {
        assertTrue(host.load("reload", "rules", "return { limit = function() return 5 end }").ok());
        assertEquals(5, host.invoke("reload", "rules", "limit").valueOrThrow().asInt());
        assertTrue(host.load("reload", "rules", "return { limit = function() return 7 end }").ok());
        assertEquals(7, host.invoke("reload", "rules", "limit").valueOrThrow().asInt());
    }

    @Test
    void contextsDoNotShareGlobals() {
        host.load("a", "m", "counter = 41; return { get = function() return counter end }");
        host.load("b", "m", "return { get = function() return counter end }");
        assertEquals(41, host.invoke("a", "m", "get").valueOrThrow().asInt());
        assertTrue(host.invoke("b", "m", "get").valueOrThrow().isNull());
    }

    @Test
    void bigIntegerBeyond2pow53IsRejectedNotRounded() {
        host.load("big", "m", "return { echo = function(x) return x end }");
        HostResult r = host.invoke("big", "m", "echo", new BigInteger("9007199254740993"));
        assertFalse(r.ok());
        assertEquals("input", r.error().kind());
    }

    // JNI's own string functions use modified UTF-8, which encodes characters outside the BMP differently
    // from real UTF-8. The bridge passes byte arrays; this catches a regression to GetStringUTFChars.
    @Test
    void textOutsideTheBmpCrossesTheBoundaryIntact() {
        String text = "Grüße, İstanbul 😀 \u0000 end";
        host.load("utf", "m", "return { echo = function(s) return s, #s end, len = function(s) return #s end }");
        assertEquals(text, host.invoke("utf", "m", "echo", text).valueOrThrow().asText());
        // MoonSharp strings are UTF-16: the emoji counts as two code units (docs/hosting.md, "Strings").
        assertEquals(text.length(), host.invoke("utf", "m", "len", text).valueOrThrow().asInt());
        assertEquals("😀", host.run("utf", "lit", "return '😀'", null).valueOrThrow().asText());
    }

    // A .NET exception that is not a Lua error would end the JVM if it crossed the native boundary.
    @Test
    void dotnetExceptionsBecomeHostErrors() {
        HostResult r = host.run("exc", "rep", "return string.rep('x', 2^31)", null);
        assertFalse(r.ok());
        assertEquals("host", r.error().kind());
        assertTrue(r.error().message().startsWith("System.ArgumentOutOfRangeException"), r.error().message());
        assertEquals(2, host.run("exc", "after", "return 1 + 1", null).valueOrThrow().asInt());
    }

    @Test
    void scriptErrorsAndUnknownFunctionsAreEnvelopes() {
        host.load("err", "m", "return { fail = function() error('nope') end }");
        for (int i = 0; i < 100; i++) assertEquals("runtime", host.invoke("err", "m", "fail").error().kind());
        assertEquals("host", host.invoke("err", "m", "missing").error().kind());
        assertEquals("host", host.invoke("err", "nomodule", "f").error().kind());
        assertEquals("syntax", host.validate("broken.lua", "return (").error().kind());
    }

    // A disposed context is gone: the same name later gets a fresh Lua state without the old modules.
    @Test
    void disposeDropsTheContext() {
        host.load("gone", "m", "return { f = function() return 1 end }");
        assertTrue(host.dispose("gone"));
        assertFalse(host.dispose("gone"));
        HostResult r = host.invoke("gone", "m", "f");
        assertEquals("host", r.error().kind());
        assertTrue(r.error().message().contains("not loaded"), r.error().message());
    }

    // create() replaces a context of the same name, and only a context created with int64 has the module.
    @Test
    void createReplacesTheContextWithTheChosenModules() {
        host.load("opts", "m", "return { f = function() return 1 end }");
        host.create("opts", true);
        assertEquals("host", host.invoke("opts", "m", "f").error().kind());
        assertEquals("9223372036854775807", host.run("opts", "max", "return tostring(int64.max)", null).valueOrThrow().asText());
        host.create("opts", false);
        assertEquals("runtime", host.run("opts", "max", "return tostring(int64.max)", null).error().kind());
    }

    // In process, a script that never returns would hold the Java thread forever: Thread.interrupt() does not
    // reach .NET code. The limits stop it with an error envelope, and the context stays usable.
    @Test
    void limitsStopAScriptThatNeverReturns() {
        host.create("limit", ContextOptions.DEFAULT.withInstructionLimit(100_000));
        HostResult r = host.run("limit", "loop.lua", "while true do end", null);
        assertEquals("interrupted", r.error().kind());
        assertEquals("instruction limit of 100000 exceeded", r.error().message());
        assertEquals(2, host.run("limit", "after", "return 1 + 1", null).valueOrThrow().asInt());

        host.create("time", ContextOptions.DEFAULT.withTimeLimitMs(50));
        long t0 = System.nanoTime();
        r = host.run("time", "loop.lua", "local ok = pcall(function() while true do end end)", null);
        assertEquals("interrupted", r.error().kind());
        assertEquals("time limit of 50 ms exceeded", r.error().message());
        assertTrue(System.nanoTime() - t0 < 5_000_000_000L, "stopped within 5 s");
    }

    // Recursion through a CLR callback used to overflow the .NET stack, which ended the JVM. The stack guard
    // must turn it into a Lua error even on a Java thread with a small stack (in-process calls run on the
    // caller's stack; the default is 1 MB).
    @Test
    void recursionThroughACallbackIsAnErrorOnASmallJavaStack() throws Exception {
        HostResult[] r = new HostResult[2];
        Thread t = new Thread(null, () -> {
            r[0] = host.run("guard", "gsub.lua", "local function f(s) return (string.gsub(s, '.', f)) end return f('x')", null);
            r[1] = host.run("guard", "after", "return 1 + 1", null);
        }, "small-stack", 256 * 1024);
        t.start();
        t.join();
        assertEquals("runtime", r[0].error().kind());
        assertTrue(r[0].error().message().endsWith("C stack overflow"), r[0].error().message());
        assertEquals(2, r[1].valueOrThrow().asInt());
    }

    // One context per thread runs in parallel and stays isolated.
    @Test
    void threadsWithTheirOwnContextsRunInParallel() throws Exception {
        ExecutorService exec = Executors.newFixedThreadPool(8);
        List<Future<Integer>> results = new ArrayList<>();
        for (int t = 0; t < 8; t++) {
            String ctx = "par" + t;
            int base = t * 1000;
            results.add(exec.submit(() -> {
                host.load(ctx, "sum", "return { add = function(a, b) return a + b end }").valueOrThrow();
                int last = 0;
                for (int i = 0; i < 1000; i++) last = host.invoke(ctx, "sum", "add", base, i).valueOrThrow().asInt();
                return last;
            }));
        }
        for (int t = 0; t < 8; t++) assertEquals(t * 1000 + 999, results.get(t).get());
        exec.shutdown();
    }

    // A ScriptHost is not thread-safe; the per-context lock must serialize calls on a shared context.
    // Without it MoonSharp rejects the second caller ("Cannot enter the same MoonSharp processor from two
    // different threads") and calls fail at random.
    @Test
    void callsOnOneContextFromManyThreadsAreSerialized() throws Exception {
        host.load("shared", "counter", "local n = 0; return { inc = function() n = n + 1; return n end, get = function() return n end }");
        ExecutorService exec = Executors.newFixedThreadPool(8);
        List<Future<?>> fs = new ArrayList<>();
        for (int t = 0; t < 8; t++)
            fs.add(exec.submit(() -> {
                for (int i = 0; i < 2000; i++) host.invoke("shared", "counter", "inc").valueOrThrow();
                return null;
            }));
        for (Future<?> f : fs) f.get();
        exec.shutdown();
        assertEquals(16000, host.invoke("shared", "counter", "get").valueOrThrow().asInt());
    }
}
