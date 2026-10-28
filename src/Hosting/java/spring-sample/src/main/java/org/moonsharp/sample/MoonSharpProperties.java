package org.moonsharp.sample;

import org.springframework.boot.context.properties.ConfigurationProperties;

import java.util.List;

/**
 * @param command    the worker command, for example [dotnet, /opt/moonsharp/moonsharp-host.dll, worker]
 * @param workers    number of worker processes (parallel calls)
 * @param scriptsDir directory of *.lua modules loaded at startup; the file name is the module id
 */
@ConfigurationProperties("moonsharp")
public record MoonSharpProperties(List<String> command, int workers, String scriptsDir) { }
