package org.moonsharp.hosting;

import com.fasterxml.jackson.databind.DeserializationFeature;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.JsonNodeFactory;

/** The JSON mapper used for the worker protocol. It never turns numbers into doubles. */
public final class Json {
    public static final ObjectMapper MAPPER = new ObjectMapper()
        .enable(DeserializationFeature.USE_BIG_DECIMAL_FOR_FLOATS)
        .enable(DeserializationFeature.USE_BIG_INTEGER_FOR_INTS)
        .setNodeFactory(JsonNodeFactory.withExactBigDecimals(true));

    private Json() { }
}
