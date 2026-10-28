package org.moonsharp.hosting;

import com.fasterxml.jackson.databind.JsonNode;

import java.util.ArrayList;
import java.util.List;

/**
 * The result envelope every MoonSharp host returns (see docs/hosting.md). Numbers in {@link #value()} keep
 * their exact JSON text: integers are BigInteger-backed and decimals BigDecimal-backed.
 */
public record HostResult(boolean ok, JsonNode value, HostError error, String output, JsonNode timing) {

    /** A Lua syntax or runtime error, or a host-side problem with input or output values. */
    public record HostError(String kind, String message, String chunk, Integer line, List<String> stack) { }

    static HostResult from(JsonNode envelope) {
        JsonNode e = envelope.get("error");
        HostError error = null;

        if (e != null && !e.isNull()) {
            List<String> stack = new ArrayList<>();
            e.path("stack").forEach(s -> stack.add(s.asText()));
            error = new HostError(
                e.path("kind").asText(),
                e.path("message").asText(),
                e.path("chunk").isNull() ? null : e.path("chunk").asText(),
                e.path("line").isNumber() ? e.path("line").asInt() : null,
                stack);
        }

        return new HostResult(envelope.path("ok").asBoolean(), envelope.get("value"), error,
            envelope.path("output").asText(""), envelope.get("timing"));
    }

    /** Returns the value, or throws {@link ScriptException} for an error result. */
    public JsonNode valueOrThrow() {
        if (!ok) throw new ScriptException(error);
        return value;
    }
}
