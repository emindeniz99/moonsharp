package org.moonsharp.sample;

import com.fasterxml.jackson.core.JsonProcessingException;
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
    public ResponseEntity<String> invoke(@PathVariable("module") String module, @PathVariable("function") String function, @RequestBody String argsJson)
            throws JsonProcessingException {
        // Request and response stay JSON text, written by the client's exact-number mapper, so large
        // integers are never rounded and the sample does not depend on Spring's own Jackson version.
        HostResult r = pool.invokeJson(module, function, argsJson);
        String body = Json.MAPPER.writeValueAsString(r);
        return r.ok() ? ResponseEntity.ok(body) : ResponseEntity.unprocessableContent().body(body);
    }
}
