package org.moonsharp.sample;

import org.moonsharp.hosting.HostResult;
import org.moonsharp.hosting.MoonSharpPool;
import org.moonsharp.hosting.MoonSharpWorker;
import org.springframework.context.annotation.Bean;
import org.springframework.context.annotation.Configuration;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
import java.util.stream.Stream;

@Configuration
public class MoonSharpConfiguration {

    @Bean(destroyMethod = "close")
    public MoonSharpPool moonSharpPool(MoonSharpProperties props) throws IOException {
        MoonSharpPool pool = new MoonSharpPool(Math.max(1, props.workers()), () -> MoonSharpWorker.start(props.command()));

        if (props.scriptsDir() != null) {
            List<Path> scripts;
            try (Stream<Path> files = Files.list(Path.of(props.scriptsDir()))) {
                scripts = files.filter(p -> p.toString().endsWith(".lua")).sorted().toList();
            }

            for (Path script : scripts) {
                String id = script.getFileName().toString().replaceFirst("\\.lua$", "");
                HostResult r = pool.load(id, Files.readString(script));
                if (!r.ok()) {
                    pool.close();
                    throw new IllegalStateException("cannot load " + script + ": " + r.error());
                }
            }
        }

        return pool;
    }
}
