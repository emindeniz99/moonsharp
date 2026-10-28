package org.moonsharp.sample;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.boot.context.properties.ConfigurationPropertiesScan;

/** A Spring Boot service that evaluates trusted Lua logic with the MoonSharp interpreter. */
@SpringBootApplication
@ConfigurationPropertiesScan
public class ScriptApplication {
    public static void main(String[] args) {
        SpringApplication.run(ScriptApplication.class, args);
    }
}
