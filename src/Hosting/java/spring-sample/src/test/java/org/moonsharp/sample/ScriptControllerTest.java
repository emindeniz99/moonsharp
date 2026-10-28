package org.moonsharp.sample;

import com.fasterxml.jackson.databind.JsonNode;
import org.junit.jupiter.api.Test;
import org.moonsharp.hosting.Json;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.client.TestRestTemplate;
import org.springframework.http.HttpEntity;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;

import static org.junit.jupiter.api.Assertions.assertEquals;

/** Starts the real service with two MoonSharp workers and calls the shared fixture scripts over HTTP. */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class ScriptControllerTest {

    @DynamicPropertySource
    static void moonsharp(DynamicPropertyRegistry registry) {
        registry.add("moonsharp.command", () -> "dotnet," + System.getProperty("moonsharp.host.dll") + ",worker");
        registry.add("moonsharp.scripts-dir", () -> System.getProperty("moonsharp.fixtures") + "/scripts");
    }

    @Autowired
    TestRestTemplate http;

    JsonNode post(String path, String body, int expectedStatus) throws Exception {
        HttpHeaders headers = new HttpHeaders();
        headers.setContentType(MediaType.APPLICATION_JSON);
        ResponseEntity<String> r = http.postForEntity(path, new HttpEntity<>(body, headers), String.class);
        assertEquals(expectedStatus, r.getStatusCode().value(), r.getBody());
        return Json.MAPPER.readTree(r.getBody());
    }

    @Test
    void eligibilityOverHttp() throws Exception {
        JsonNode r = post("/scripts/eligibility/eligible", "[{\"level\":12,\"country\":\"DE\",\"flags\":{\"beta\":true}},\"offers\"]", 200);
        assertEquals(true, r.get("value").asBoolean());
    }

    @Test
    void offersUseRequireAcrossModules() throws Exception {
        JsonNode r = post("/scripts/offers/select_offers",
            "[{\"level\":15,\"country\":\"DE\",\"flags\":{\"beta\":true}},[{\"id\":\"a\",\"price\":2.5,\"priority\":1},{\"id\":\"b\",\"price\":1.25,\"priority\":2}],1]", 200);
        assertEquals("[{\"id\":\"b\",\"price\":1.25}]", r.get("value").toString());
    }

    @Test
    void scriptErrorIs422WithLine() throws Exception {
        JsonNode r = post("/scripts/config/fail", "[\"no price\"]", 422);
        assertEquals("runtime", r.at("/error/kind").asText());
        assertEquals(34, r.at("/error/line").asInt());
    }

    @Test
    void integerBeyond2pow53IsRejected() throws Exception {
        JsonNode r = post("/scripts/config/echo", "[9007199254740993]", 422);
        assertEquals("input", r.at("/error/kind").asText());
    }
}
