package org.moonsharp.sample;

import com.fasterxml.jackson.databind.JsonNode;
import org.junit.jupiter.api.Test;
import org.moonsharp.hosting.Json;
import org.springframework.beans.factory.annotation.Value;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.test.context.DynamicPropertyRegistry;
import org.springframework.test.context.DynamicPropertySource;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;

import static org.junit.jupiter.api.Assertions.assertEquals;

/** Starts the real service with two MoonSharp workers and calls the shared fixture scripts over HTTP. */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
class ScriptControllerTest {

    @DynamicPropertySource
    static void moonsharp(DynamicPropertyRegistry registry) {
        registry.add("moonsharp.command", () -> "dotnet," + System.getProperty("moonsharp.host.dll") + ",worker");
        registry.add("moonsharp.scripts-dir", () -> System.getProperty("moonsharp.fixtures") + "/scripts");
    }

    @Value("${local.server.port}")
    int port;

    final HttpClient http = HttpClient.newHttpClient();

    JsonNode post(String path, String body, int expectedStatus) throws Exception {
        HttpRequest request = HttpRequest.newBuilder(URI.create("http://127.0.0.1:" + port + path))
            .header("Content-Type", "application/json")
            .POST(HttpRequest.BodyPublishers.ofString(body))
            .build();
        HttpResponse<String> r = http.send(request, HttpResponse.BodyHandlers.ofString());
        assertEquals(expectedStatus, r.statusCode(), r.body());
        return Json.MAPPER.readTree(r.body());
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
