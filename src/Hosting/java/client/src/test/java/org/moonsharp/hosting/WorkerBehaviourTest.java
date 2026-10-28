package org.moonsharp.hosting;

import org.junit.jupiter.api.Test;

import java.math.BigInteger;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.ArrayList;

import static org.junit.jupiter.api.Assertions.*;

class WorkerBehaviourTest {

    // A script update must take effect on the next call, without restarting the worker.
    @Test
    void reloadingAModuleReplacesItsFunctions() {
        try (MoonSharpWorker w = MoonSharpWorker.startDll(Fixtures.hostDll())) {
            assertTrue(w.load("c", "rules", "return { limit = function() return 5 end }").ok());
            assertEquals(5, w.invoke("c", "rules", "limit").valueOrThrow().asInt());

            assertTrue(w.load("c", "rules", "return { limit = function() return 7 end }").ok());
            assertEquals(7, w.invoke("c", "rules", "limit").valueOrThrow().asInt());
        }
    }

    // Contexts are independent Lua states: globals set in one are invisible in another.
    @Test
    void contextsDoNotShareGlobals() {
        try (MoonSharpWorker w = MoonSharpWorker.startDll(Fixtures.hostDll())) {
            w.load("a", "m", "counter = 41; return { get = function() return counter end }");
            w.load("b", "m", "return { get = function() return counter end }");
            assertEquals(41, w.invoke("a", "m", "get").valueOrThrow().asInt());
            assertTrue(w.invoke("b", "m", "get").valueOrThrow().isNull());
        }
    }

    // Java BigInteger arguments travel as exact JSON integers, so the host can reject what a double would round.
    @Test
    void bigIntegerBeyond2pow53IsRejectedNotRounded() {
        try (MoonSharpWorker w = MoonSharpWorker.startDll(Fixtures.hostDll())) {
            w.load("c", "m", "return { echo = function(x) return x end }");
            assertEquals(new BigInteger("9007199254740992"),
                w.invoke("c", "m", "echo", new BigInteger("9007199254740992")).valueOrThrow().bigIntegerValue());

            HostResult r = w.invoke("c", "m", "echo", new BigInteger("9007199254740993"));
            assertFalse(r.ok());
            assertEquals("input", r.error().kind());
        }
    }

    // create() replaces a context of the same name, and only a context created with int64 has the module.
    @Test
    void createReplacesTheContextWithTheChosenModules() {
        try (MoonSharpWorker w = MoonSharpWorker.startDll(Fixtures.hostDll())) {
            w.load("c", "m", "return { f = function() return 1 end }");
            w.create("c", true);
            assertEquals("host", w.invoke("c", "m", "f").error().kind());
            assertEquals("9223372036854775807", w.run("c", "max", "return tostring(int64.max)", null).valueOrThrow().asText());
            w.create("c", false);
            assertEquals("runtime", w.run("c", "max", "return tostring(int64.max)", null).error().kind());
        }
    }

    @Test
    void scriptErrorsDoNotKillTheWorker() {
        try (MoonSharpWorker w = MoonSharpWorker.startDll(Fixtures.hostDll())) {
            w.load("c", "m", "return { fail = function() error('nope') end, ok = function() return true end }");
            for (int i = 0; i < 100; i++)
                assertEquals("runtime", w.invoke("c", "m", "fail").error().kind());
            assertTrue(w.invoke("c", "m", "ok").valueOrThrow().asBoolean());
            assertTrue(w.isAlive());
        }
    }

    @Test
    void poolRunsCallsInParallelAndLoadsModulesEverywhere() throws Exception {
        try (MoonSharpPool pool = new MoonSharpPool(3, () -> MoonSharpWorker.startDll(Fixtures.hostDll()))) {
            assertTrue(pool.load("sum", "return { add = function(a, b) return a + b end }").ok());
            ExecutorService exec = Executors.newFixedThreadPool(6);
            List<Future<Integer>> results = new ArrayList<>();

            for (int i = 0; i < 300; i++) {
                int n = i;
                results.add(exec.submit(() -> pool.invoke("sum", "add", n, 1).valueOrThrow().asInt()));
            }

            for (int i = 0; i < results.size(); i++)
                assertEquals(i + 1, results.get(i).get());

            exec.shutdown();
        }
    }

    // The pool creates every worker's context with its options, including workers that replace dead ones.
    @Test
    void poolContextsGetTheLimits() {
        ContextOptions limit = ContextOptions.DEFAULT.withInstructionLimit(100_000);
        try (MoonSharpPool pool = new MoonSharpPool(2, () -> MoonSharpWorker.startDll(Fixtures.hostDll()), limit)) {
            assertTrue(pool.load("loop", "return { spin = function() while true do end end, ok = function() return 1 end }").ok());
            for (int i = 0; i < 4; i++) {
                HostResult r = pool.invoke("loop", "spin");
                assertEquals("interrupted", r.error().kind());
                assertEquals("instruction limit of 100000 exceeded", r.error().message());
                assertEquals(1, pool.invoke("loop", "ok").valueOrThrow().asInt());
            }
        }
    }

    @Test
    void mapsAndListsConvertToLuaTables() {
        try (MoonSharpWorker w = MoonSharpWorker.startDll(Fixtures.hostDll())) {
            w.load("c", "m", "return { count = function(t) return #t.items + t.extra end }");
            assertEquals(5, w.invoke("c", "m", "count", Map.of("items", List.of(1, 2, 3), "extra", 2)).valueOrThrow().asInt());
        }
    }
}
