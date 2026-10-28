package org.moonsharp.hosting;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.TestFactory;
import org.junit.jupiter.api.TestInstance;
import org.opentest4j.TestAbortedException;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.Assertions.fail;

/**
 * Runs every fixture suite through the real MoonSharp interpreter, once per Java transport (the
 * subclasses). The same corpus runs in the .NET CLI and the browser, so passing here means Java sees the
 * same values whichever way it reaches MoonSharp.
 *
 * <p>Results are checked against {@code known-failures/<host>.json} the way the .NET runner does it
 * (KnownFailures in FixtureRunner.cs): a new failure, a changed detail, a listed failure that passes now
 * and a listed case that no longer exists each fail.
 */
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
abstract class FixtureConformanceTest {
    MoonSharpHost host;
    private final Map<String, String> actual = new TreeMap<>();
    private Map<String, String> known;
    private int cases, passed, knownFailed, newFailed, changed, fixed, stale;

    /** Opens the transport under test, once per test class. */
    abstract MoonSharpHost open();

    /** The name of the known-failures file, for example java-worker. */
    abstract String hostName();

    @AfterAll
    void stop() throws Exception {
        if (host != null) host.close();
        if (cases == 0) return;

        System.out.printf("%s: %d cases, %d passed, %d known failures; %d new, %d changed, %d fixed, %d stale%n",
            hostName(), cases, passed, knownFailed, newFailed, changed, fixed, stale);
        if (newFailed + changed + fixed + stale > 0)
            System.out.println("The known failures changed. If that is intended, write this to " + knownFile() + ":\n"
                + Json.MAPPER.writerWithDefaultPrettyPrinter().writeValueAsString(actual).replace("\r\n", "\n"));
    }

    @TestFactory
    List<DynamicTest> corpus() throws Exception {
        known = readKnown();
        List<DynamicTest> tests = new ArrayList<>();
        Set<String> suites = new HashSet<>(), names = new HashSet<>();

        // A transport that is not built is reported as skipped cases, not as an empty class.
        String skip = null;
        try {
            host = open();
        } catch (TestAbortedException e) {
            skip = e.getMessage();
        }

        List<Path> suiteFiles;
        try (Stream<Path> files = Files.list(Fixtures.fixtures())) {
            suiteFiles = files.filter(p -> p.getFileName().toString().endsWith(".suite.json")).sorted().toList();
        }

        for (Path suitePath : suiteFiles) {
            JsonNode suite = Json.MAPPER.readTree(Files.readString(suitePath));
            String name = suite.get("suite").asText();
            suites.add(name);

            if (skip == null) {
                host.create(name, suite.path("context").path("int64").asBoolean(false));
                for (Iterator<Map.Entry<String, JsonNode>> it = suite.path("modules").fields(); it.hasNext(); ) {
                    Map.Entry<String, JsonNode> m = it.next();
                    String source = Files.readString(suitePath.getParent().resolve(m.getValue().asText()));
                    HostResult loaded = host.load(name, m.getKey(), source);
                    assertTrue(loaded.ok(), () -> "load " + m.getKey() + ": " + loaded.error());
                }
            }

            for (JsonNode c : suite.get("cases")) {
                String key = name + " / " + c.get("name").asText();
                names.add(key);
                String skipped = skip;
                tests.add(DynamicTest.dynamicTest(key, () -> {
                    Assumptions.assumeTrue(skipped == null, skipped);
                    record(key, Fixtures.check(c, execute(name, c)));
                }));
            }
        }

        for (String key : known.keySet()) {
            if (!suites.contains(key.split(" / ", 2)[0])) actual.put(key, known.get(key));
            else if (!names.contains(key))
                tests.add(DynamicTest.dynamicTest("stale " + key, () -> {
                    stale++;
                    fail("listed in " + knownFile() + ", but there is no such case");
                }));
        }

        return tests;
    }

    private HostResult execute(String context, JsonNode c) throws Exception {
        String op = c.path("op").asText("invoke");
        String chunk = c.path("chunk").asText(c.get("name").asText());
        return switch (op) {
            case "invoke" -> host.invokeJson(context, c.get("module").asText(), c.get("function").asText(), Fixtures.argsJson(c));
            case "validate" -> host.validate(chunk, c.get("source").asText());
            case "run" -> host.run(context, chunk, c.get("source").asText(),
                c.hasNonNull("inputJson") ? c.get("inputJson").asText()
                    : c.has("input") ? Json.MAPPER.writeValueAsString(c.get("input")) : null);
            default -> throw new IllegalArgumentException("unknown op " + op);
        };
    }

    private void record(String key, String detail) {
        cases++;
        String expected = known.get(key);
        if (detail != null) actual.put(key, detail);

        if (detail == null && expected == null) {
            passed++;
        } else if (detail != null && detail.equals(expected)) {
            knownFailed++;
            System.out.println("known " + key + ": " + detail);
        } else if (detail == null) {
            fixed++;
            fail("passes now, remove it from " + knownFile());
        } else if (expected == null) {
            newFailed++;
            fail(detail);
        } else {
            changed++;
            fail("changed: " + detail + " (known: " + expected + ")");
        }
    }

    private Path knownFile() {
        return Fixtures.fixtures().resolve("known-failures").resolve(hostName() + ".json");
    }

    private Map<String, String> readKnown() throws Exception {
        Map<String, String> map = new TreeMap<>();
        if (Files.isRegularFile(knownFile())) {
            ObjectNode o = (ObjectNode) Json.MAPPER.readTree(Files.readString(knownFile()));
            o.fields().forEachRemaining(e -> map.put(e.getKey(), e.getValue().asText()));
        }
        return map;
    }
}
