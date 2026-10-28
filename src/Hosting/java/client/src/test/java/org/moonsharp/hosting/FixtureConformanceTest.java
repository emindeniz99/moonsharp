package org.moonsharp.hosting;

import com.fasterxml.jackson.databind.JsonNode;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.Assumptions;
import org.junit.jupiter.api.DynamicTest;
import org.junit.jupiter.api.TestInstance;
import org.junit.jupiter.api.TestFactory;
import org.opentest4j.TestAbortedException;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Runs the shared fixture suite through the real MoonSharp interpreter, once per Java transport (the
 * subclasses). The same suite runs in the .NET CLI and the browser, so passing here means Java sees the
 * same values whichever way it reaches MoonSharp.
 */
@TestInstance(TestInstance.Lifecycle.PER_CLASS)
abstract class FixtureConformanceTest {
    MoonSharpHost host;

    /** Opens the transport under test, once per test class. */
    abstract MoonSharpHost open();

    @AfterAll
    void stop() {
        if (host != null) host.close();
    }

    @TestFactory
    List<DynamicTest> logicSuite() throws Exception {
        Path suitePath = Fixtures.fixtures().resolve("logic.suite.json");
        JsonNode suite = Json.MAPPER.readTree(Files.readString(suitePath));
        String context = "fixtures";
        List<DynamicTest> tests = new ArrayList<>();

        // A transport that is not built is reported as 24 skipped cases, not as an empty class.
        String skip = null;
        try {
            host = open();
        } catch (TestAbortedException e) {
            skip = e.getMessage();
        }

        for (Iterator<Map.Entry<String, JsonNode>> it = suite.get("modules").fields(); skip == null && it.hasNext(); ) {
            Map.Entry<String, JsonNode> m = it.next();
            String source = Files.readString(suitePath.getParent().resolve(m.getValue().asText()));
            HostResult loaded = host.load(context, m.getKey(), source);
            assertTrue(loaded.ok(), () -> "load " + m.getKey() + ": " + loaded.error());
        }

        for (JsonNode c : suite.get("cases")) {
            String skipped = skip;
            tests.add(DynamicTest.dynamicTest(c.get("name").asText(), () -> {
                Assumptions.assumeTrue(skipped == null, skipped);
                String op = c.path("op").asText("invoke");
                String chunk = c.path("chunk").asText(c.get("name").asText());
                HostResult r = switch (op) {
                    case "invoke" -> host.invokeJson(context, c.get("module").asText(), c.get("function").asText(), Fixtures.argsJson(c));
                    case "validate" -> host.validate(chunk, c.get("source").asText());
                    case "run" -> host.run(context, chunk, c.get("source").asText(),
                        c.has("input") ? Json.MAPPER.writeValueAsString(c.get("input")) : null);
                    default -> throw new IllegalArgumentException("unknown op " + op);
                };
                assertNull(Fixtures.check(c, r));
            }));
        }

        return tests;
    }
}
