package org.moonsharp.sample;

import com.fasterxml.jackson.databind.JsonNode;
import org.moonsharp.hosting.HostResult;
import org.moonsharp.hosting.Json;
import org.moonsharp.hosting.MoonSharpPool;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RestController;

/**
 * POST /scripts/{module}/{function} with a JSON array of arguments. Responds with the host envelope:
 * 200 and {"ok":true,"value":...} on success, 422 with {"ok":false,"error":{...}} on a script error.
 */
@RestController
public class ScriptController {
    private final MoonSharpPool pool;

    public ScriptController(MoonSharpPool pool) {
        this.pool = pool;
    }

    @PostMapping(path = "/scripts/{module}/{function}", consumes = "application/json", produces = "application/json")
    public ResponseEntity<JsonNode> invoke(@PathVariable("module") String module, @PathVariable("function") String function, @RequestBody String argsJson) {
        // The body is passed on as text, so large integers reach the host unrounded.
        HostResult r = pool.invokeJson(module, function, argsJson);
        JsonNode body = Json.MAPPER.valueToTree(r);
        return r.ok() ? ResponseEntity.ok(body) : ResponseEntity.unprocessableEntity().body(body);
    }
}
