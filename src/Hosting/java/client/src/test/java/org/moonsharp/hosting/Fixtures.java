package org.moonsharp.hosting;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Iterator;
import java.util.Map;

/** Test helpers shared by the Java tests: locating the worker and comparing fixture results. */
final class Fixtures {
    private Fixtures() { }

    static String hostDll() {
        String dll = System.getProperty("moonsharp.host.dll");
        if (dll == null || !Files.isRegularFile(Path.of(dll)))
            throw new IllegalStateException("moonsharp-host.dll not found at " + dll
                + ". Build it first: dotnet build -c Release src/Hosting/MoonSharp.Hosting.Cli");
        return dll;
    }

    static Path fixtures() {
        return Path.of(System.getProperty("moonsharp.fixtures"));
    }

    static String argsJson(JsonNode c) throws JsonProcessingException {
        if (c.hasNonNull("argsJson")) return c.get("argsJson").asText();
        return c.has("args") ? Json.MAPPER.writeValueAsString(c.get("args")) : "[]";
    }

    /** Same rules as the .NET and browser runners. Returns null on a match. */
    static String check(JsonNode c, HostResult r) {
        JsonNode expectError = c.get("expectError");

        if (expectError == null) {
            if (!r.ok()) return "unexpected error: " + r.error();
            return diff(c.get("expect"), r.value(), "$");
        }

        if (r.ok()) return "expected an error, got " + r.value();

        if (expectError.has("kind") && !expectError.get("kind").asText().equals(r.error().kind()))
            return "error kind: expected " + expectError.get("kind").asText() + ", got " + r.error().kind() + " (" + r.error().message() + ")";

        if (expectError.has("messageContains") && !r.error().message().contains(expectError.get("messageContains").asText()))
            return "error message: expected to contain \"" + expectError.get("messageContains").asText() + "\", got \"" + r.error().message() + "\"";

        if (expectError.has("line") && !Integer.valueOf(expectError.get("line").asInt()).equals(r.error().line()))
            return "error line: expected " + expectError.get("line") + ", got " + r.error().line();

        return null;
    }

    static String diff(JsonNode expected, JsonNode actual, String path) {
        boolean eNull = expected == null || expected.isNull(), aNull = actual == null || actual.isNull();
        if (eNull || aNull) return eNull && aNull ? null : path + ": expected " + expected + ", got " + actual;

        if (expected.isNumber() && actual.isNumber())
            return expected.decimalValue().compareTo(actual.decimalValue()) == 0 ? null : path + ": expected " + expected + ", got " + actual;

        if (expected.getNodeType() != actual.getNodeType())
            return path + ": expected " + expected + ", got " + actual;

        if (expected.isArray()) {
            if (expected.size() != actual.size()) return path + ": expected " + expected.size() + " items, got " + actual.size() + " (" + actual + ")";
            for (int i = 0; i < expected.size(); i++) {
                String d = diff(expected.get(i), actual.get(i), path + "[" + i + "]");
                if (d != null) return d;
            }
            return null;
        }

        if (expected.isObject()) {
            for (Iterator<Map.Entry<String, JsonNode>> it = expected.fields(); it.hasNext(); ) {
                Map.Entry<String, JsonNode> e = it.next();
                if (!actual.has(e.getKey())) return path + "." + e.getKey() + ": missing (expected " + e.getValue() + ")";
                String d = diff(e.getValue(), actual.get(e.getKey()), path + "." + e.getKey());
                if (d != null) return d;
            }
            for (Iterator<String> it = actual.fieldNames(); it.hasNext(); ) {
                String k = it.next();
                if (!expected.has(k)) return path + "." + k + ": unexpected key (value " + actual.get(k) + ")";
            }
            return null;
        }

        return expected.equals(actual) ? null : path + ": expected " + expected + ", got " + actual;
    }
}
