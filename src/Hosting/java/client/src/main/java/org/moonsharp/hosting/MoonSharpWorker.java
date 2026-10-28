package org.moonsharp.hosting;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.concurrent.TimeUnit;

/**
 * One persistent .NET process running the MoonSharp interpreter ({@code moonsharp-host worker}).
 * Requests and replies are single lines of JSON on stdin/stdout. Calls are serialized: use
 * {@link MoonSharpPool} to run scripts in parallel.
 */
public final class MoonSharpWorker implements MoonSharpHost {
    private final Process process;
    private final BufferedWriter in;
    private final BufferedReader out;
    private long nextId = 1;

    private MoonSharpWorker(Process process) {
        this.process = process;
        this.in = new BufferedWriter(new OutputStreamWriter(process.getOutputStream(), StandardCharsets.UTF_8));
        this.out = new BufferedReader(new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8));
    }

    /** Starts a worker with an explicit command, for example {@code ["dotnet", "/opt/moonsharp/moonsharp-host.dll", "worker"]}. */
    public static MoonSharpWorker start(List<String> command) {
        try {
            Process p = new ProcessBuilder(command).redirectError(ProcessBuilder.Redirect.INHERIT).start();
            return new MoonSharpWorker(p);
        } catch (IOException e) {
            throw new WorkerException("cannot start MoonSharp worker: " + command, e);
        }
    }

    /** Starts {@code dotnet <hostDll> worker}. */
    public static MoonSharpWorker startDll(String hostDll) {
        return start(List.of("dotnet", hostDll, "worker"));
    }

    public String version() {
        return request(op("version", null)).value().asText();
    }

    public void create(String context, ContextOptions options) {
        HostResult r = request(op("create", context).put("int64", options.int64())
            .put("instructionLimit", options.instructionLimit()).put("timeLimitMs", options.timeLimitMs()));
        if (!r.ok()) throw new WorkerException("MoonSharp worker could not create context " + context + ": " + r.error(), null);
    }

    public HostResult load(String context, String moduleId, String source) {
        return request(op("load", context).put("module", moduleId).put("source", source));
    }

    public boolean unload(String context, String moduleId) {
        return request(op("unload", context).put("module", moduleId)).value().asBoolean();
    }

    /** Calls a module function. Arguments are serialized with {@link Json#MAPPER}; BigInteger and BigDecimal stay exact. */
    public HostResult invoke(String context, String moduleId, String function, Object... args) {
        ObjectNode r = op("invoke", context).put("module", moduleId).put("function", function);
        r.set("args", Json.MAPPER.valueToTree(args));
        return request(r);
    }

    /** Calls a module function with arguments given as JSON array text. */
    public HostResult invokeJson(String context, String moduleId, String function, String argsJson) {
        return request(op("invoke", context).put("module", moduleId).put("function", function).put("argsJson", argsJson));
    }

    public HostResult run(String context, String chunk, String source, String inputJson) {
        ObjectNode r = op("run", context).put("chunk", chunk).put("source", source);
        if (inputJson != null) r.put("inputJson", inputJson);
        return request(r);
    }

    public HostResult validate(String chunk, String source) {
        return request(op("validate", "__validate").put("chunk", chunk).put("source", source));
    }

    public boolean dispose(String context) {
        return request(op("dispose", context)).value().asBoolean();
    }

    public boolean isAlive() {
        return process.isAlive();
    }

    /** The worker's process id, for monitoring (memory, CPU). */
    public long pid() {
        return process.pid();
    }

    private ObjectNode op(String op, String context) {
        ObjectNode r = Json.MAPPER.createObjectNode().put("op", op);
        if (context != null) r.put("context", context);
        return r;
    }

    private synchronized HostResult request(ObjectNode request) {
        long id = nextId++;
        request.put("id", id);
        String reply;

        try {
            in.write(Json.MAPPER.writeValueAsString(request));
            in.write('\n');
            in.flush();
            reply = out.readLine();
        } catch (IOException e) {
            throw new WorkerException("MoonSharp worker I/O failed", e);
        }

        if (reply == null)
            throw new WorkerException("MoonSharp worker exited" + exitCode(), null);

        try {
            JsonNode node = Json.MAPPER.readTree(reply);
            if (node.path("id").asLong() != id)
                throw new WorkerException("MoonSharp worker answered request " + node.path("id") + ", expected " + id, null);
            return HostResult.from(node.get("result"));
        } catch (JsonProcessingException e) {
            throw new WorkerException("MoonSharp worker sent invalid JSON: " + reply, e);
        }
    }

    private String exitCode() {
        try {
            return process.waitFor(1, TimeUnit.SECONDS) ? " with code " + process.exitValue() : "";
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            return "";
        }
    }

    @Override
    public void close() {
        try {
            in.close();
            if (!process.waitFor(5, TimeUnit.SECONDS))
                process.destroyForcibly();
        } catch (IOException | InterruptedException e) {
            process.destroyForcibly();
            if (e instanceof InterruptedException) Thread.currentThread().interrupt();
        }
    }
}
