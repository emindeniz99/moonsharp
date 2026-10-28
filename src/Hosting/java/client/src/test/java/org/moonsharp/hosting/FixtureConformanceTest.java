package org.moonsharp.hosting;

import com.fasterxml.jackson.databind.JsonNode;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.TestFactory;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Runs the shared fixture suite through the real MoonSharp interpreter in a .NET worker process.
 * The same suite runs in the .NET CLI and the browser, so passing here means Java sees the same values.
 */
class FixtureConformanceTest {
    static MoonSharpWorker worker;

    @BeforeAll
    static void start() {
        worker = MoonSharpWorker.startDll(Fixtures.hostDll());
    }

    @AfterAll
    static void stop() {
        if (worker != null) worker.close();
    }

    @TestFactory
    List<DynamicTest> logicSuite() throws Exception {
        Path suitePath = Fixtures.fixtures().resolve("logic.suite.json");
        JsonNode suite = Json.MAPPER.readTree(Files.readString(suitePath));
        String context = "fixtures";
        List<DynamicTest> tests = new ArrayList<>();

        for (Iterator<Map.Entry<String, JsonNode>> it = suite.get("modules").fields(); it.hasNext(); ) {
            Map.Entry<String, JsonNode> m = it.next();
            String source = Files.readString(suitePath.getParent().resolve(m.getValue().asText()));
            HostResult loaded = worker.load(context, m.getKey(), source);
            assertTrue(loaded.ok(), () -> "load " + m.getKey() + ": " + loaded.error());
        }

        for (JsonNode c : suite.get("cases")) {
            tests.add(DynamicTest.dynamicTest(c.get("name").asText(), () -> {
                String op = c.path("op").asText("invoke");
                String chunk = c.path("chunk").asText(c.get("name").asText());
                HostResult r = switch (op) {
                    case "invoke" -> worker.invokeJson(context, c.get("module").asText(), c.get("function").asText(), Fixtures.argsJson(c));
                    case "validate" -> worker.validate(chunk, c.get("source").asText());
                    case "run" -> worker.run(context, chunk, c.get("source").asText(),
                        c.has("input") ? Json.MAPPER.writeValueAsString(c.get("input")) : null);
                    default -> throw new IllegalArgumentException("unknown op " + op);
                };
                assertNull(Fixtures.check(c, r));
            }));
        }

        return tests;
    }
}
